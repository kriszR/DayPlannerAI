using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using DayPlannerAI.Api.DTOs.Realtime;

namespace DayPlannerAI.Api.Services;

public sealed class MavPluszRealtimeClient
{
    private const string TripQuery = """
        query GetTripRealtime($id: String!, $serviceDay: String!) {
          trip(id: $id, serviceDay: $serviceDay) {
            gtfsId
            tripHeadsign

            route {
              gtfsId
              shortName
              longName
            }

            stoptimes {
              stop {
                gtfsId
                name
              }

              arrivalDelay
              departureDelay
              realtime
              realtimeState
            }
          }
        }
        """;

    private const string RouteQuery = """
        query GetRouteVehicles($id: String!) {
          route(id: $id) {
            gtfsId

            patterns {
              vehiclePositions {
                vehicleId
                lat
                lon
                heading
                speed
                lastUpdated

                trip {
                  gtfsId
                }
              }
            }
          }
        }
        """;

    private readonly HttpClient _httpClient;

    public MavPluszRealtimeClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<GtfsRealtimeTripDto?> GetTripRealtimeAsync(
        string tripId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
    {
        var otpTripId = ToOtpId(tripId);

        using var tripDocument = await SendQueryAsync(
            TripQuery,
            new
            {
                id = otpTripId,
                serviceDay = serviceDate.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture)
            },
            cancellationToken);

        var data = tripDocument.RootElement
            .GetProperty("data");

        var trip = data.GetProperty("trip");

        if (trip.ValueKind == JsonValueKind.Null)
            return null;

        var route = trip.GetProperty("route");

        if (route.ValueKind == JsonValueKind.Null)
            return null;

        var result = new GtfsRealtimeTripDto
        {
            TripId = otpTripId,
            ServiceDate = serviceDate,
            RouteId = GetString(route, "gtfsId") ?? string.Empty,
            RouteShortName = GetString(route, "shortName"),
            RouteLongName = GetString(route, "longName"),
            TripHeadsign = GetString(trip, "tripHeadsign"),
            RetrievedAtUtc = DateTimeOffset.UtcNow
        };

        var stopCalls = trip.GetProperty("stoptimes");

        foreach (var call in stopCalls.EnumerateArray())
        {
            var stop = call.GetProperty("stop");

            var stopCall = new GtfsRealtimeStopCallDto
            {
                StopId = GetString(stop, "gtfsId") ?? string.Empty,
                StopName = GetString(stop, "name") ?? string.Empty,
                ArrivalDelaySeconds = GetNullableInt(call, "arrivalDelay"),
                DepartureDelaySeconds = GetNullableInt(call, "departureDelay"),
                Realtime = GetBoolean(call, "realtime"),
                RealtimeState = GetString(call, "realtimeState")
            };

            result.StopCalls.Add(stopCall);
        }

        result.HasRealtimeStopData = result.StopCalls.Any(
            x => x.Realtime ||
                 x.ArrivalDelaySeconds.HasValue ||
                 x.DepartureDelaySeconds.HasValue);

        // The trip's own pattern can have no vehicle position.
        // Therefore query the route's scheduled patterns as well.
        result.VehiclePosition = await GetVehiclePositionAsync(
            result.RouteId,
            otpTripId,
            cancellationToken);

        return result;
    }

    private async Task<GtfsRealtimeVehicleDto?> GetVehiclePositionAsync(
        string routeId,
        string tripId,
        CancellationToken cancellationToken)
    {
        using var document = await SendQueryAsync(
            RouteQuery,
            new { id = routeId },
            cancellationToken);

        var route = document.RootElement
            .GetProperty("data")
            .GetProperty("route");

        if (route.ValueKind == JsonValueKind.Null)
            return null;

        var patterns = route.GetProperty("patterns");

        var matchingVehicles = new List<GtfsRealtimeVehicleDto>();

        foreach (var pattern in patterns.EnumerateArray())
        {
            if (!pattern.TryGetProperty(
                    "vehiclePositions",
                    out var positions))
                continue;

            foreach (var position in positions.EnumerateArray())
            {
                var vehicleTrip = position.GetProperty("trip");

                var vehicleTripId = GetString(
                    vehicleTrip,
                    "gtfsId");

                // Never associate another trip's vehicle with this trip.
                if (!string.Equals(
                        vehicleTripId,
                        tripId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var timestamp = GetNullableLong(
                    position,
                    "lastUpdated");

                if (!timestamp.HasValue)
                    continue;

                DateTimeOffset lastUpdated;

                try
                {
                    lastUpdated = DateTimeOffset.FromUnixTimeSeconds(
                        timestamp.Value);
                }
                catch (ArgumentOutOfRangeException)
                {
                    continue;
                }

                var age = DateTimeOffset.UtcNow - lastUpdated;

                if (age > TimeSpan.FromMinutes(15) ||
                    age < TimeSpan.FromMinutes(-5))
                {
                    continue;
                }

                matchingVehicles.Add(new GtfsRealtimeVehicleDto
                {
                    VehicleId = GetString(position, "vehicleId") ?? "",
                    TripId = vehicleTripId,
                    Latitude = GetDouble(position, "lat"),
                    Longitude = GetDouble(position, "lon"),
                    Heading = GetNullableDouble(position, "heading"),
                    SpeedMetersPerSecond = GetNullableDouble(
                        position,
                        "speed"),
                    LastUpdatedUtc = lastUpdated,
                    IsFresh = true
                });
            }
        }

        return matchingVehicles
            .OrderByDescending(x => x.LastUpdatedUtc)
            .FirstOrDefault();
    }

    private async Task<JsonDocument> SendQueryAsync(
        string query,
        object variables,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "",
            new
            {
                query,
                variables
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken);

        var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);

        if (document.RootElement.TryGetProperty(
                "errors",
                out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
        {
            var message = GetString(
                errors[0],
                "message") ?? "Unknown GraphQL error.";

            document.Dispose();

            throw new HttpRequestException(
                $"MÁVPlusz GraphQL error: {message}");
        }

        return document;
    }

    private static string ToOtpId(string tripId)
    {
        return tripId.Contains(':')
            ? tripId
            : $"hkir:{tripId}";
    }

    private static string? GetString(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind == JsonValueKind.Null)
            return null;

        return value.ToString();
    }

    private static bool GetBoolean(
        JsonElement element,
        string property)
    {
        return element.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.True;
    }

    private static int? GetNullableInt(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind == JsonValueKind.Null)
            return null;

        if (value.TryGetInt32(out var result))
            return result;

        return null;
    }

    private static long? GetNullableLong(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind == JsonValueKind.Null)
            return null;

        return value.TryGetInt64(out var result)
            ? result
            : null;
    }

    private static double GetDouble(
        JsonElement element,
        string property)
    {
        return GetNullableDouble(element, property) ?? 0;
    }

    private static double? GetNullableDouble(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind == JsonValueKind.Null)
            return null;

        return value.TryGetDouble(out var result)
            ? result
            : null;
    }
}