using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DayPlannerAI.Api.DTOs;
using DayPlannerAI.Api.Models;
using DayPlannerAI.Api.Services.Agent;

namespace DayPlannerAI.Api.Services.Tools;

public sealed class SearchBusJourneysTool : IAgentTool
{
    private readonly GtfsRouteSearchService _routeSearchService;
    private readonly ILogger<SearchBusJourneysTool> _logger;

    public SearchBusJourneysTool(
        GtfsRouteSearchService routeSearchService,
        ILogger<SearchBusJourneysTool> logger)
    {
        _routeSearchService = routeSearchService;
        _logger = logger;
    }

    public string Name => "search_bus_journeys";

    public string Description =>
        "Buszjáratok és átszállásos útvonalak keresése magyarországi " +
        "GTFS-menetrendből. Használd, amikor a felhasználó utazni szeretne, " +
        "járatokat keres, vagy indulási és érkezési időpontot ad meg.";

    public JsonObject Parameters => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["departureLocation"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Indulási település."
            },
            ["destination"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Céltelepülés."
            },
            ["travelDate"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Utazási dátum YYYY-MM-DD formátumban."
            },
            ["searchMode"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray
                {
                    JsonValue.Create("departAfter"),
                    JsonValue.Create("arriveBy")
                },
                ["description"] =
                    "departAfter: indulási időponttól keres. " +
                    "arriveBy: megadott érkezési határidőre keres."
            },
            ["departureTime"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] =
                    "Indulási idő HH:mm formátumban. departAfter kereséshez kötelező, kivéve ha a felhasználó most, azonnal vagy minél előbb szeretne indulni; ilyenkor 'now' értéket adj meg. Ne használj korábbi üzenetből származó fix időpontot."
            },
            ["arrivalTime"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] =
                    "Kívánt érkezési határidő HH:mm formátumban. Például: '10 órára kell odaérnem' esetén searchMode=arriveBy és arrivalTime='10:00'."
            }
        },
        ["required"] = new JsonArray
        {
            JsonValue.Create("departureLocation"),
            JsonValue.Create("destination"),
            JsonValue.Create("travelDate"),
            JsonValue.Create("searchMode")
        },
        ["additionalProperties"] = false
    };

    public bool CanShortCircuit => true;

    public string? BuildUserResponse(string toolResult)
    {
        using var document = JsonDocument.Parse(toolResult);
        var root = document.RootElement;

        if (!root.TryGetProperty("success", out var success) ||
            success.ValueKind != JsonValueKind.True)
        {
            var error = GetJsonString(root, "error");
            return string.IsNullOrWhiteSpace(error)
                ? "A buszjáratok keresése nem sikerült."
                : $"A buszjáratok keresése nem sikerült: {error}";
        }

        var plan = root.GetProperty("travelPlan");
        var departure = GetJsonString(plan, "departureLocation") ?? "az indulási hely";
        var destination = GetJsonString(plan, "destination") ?? "a célállomás";
        var date = GetJsonString(plan, "travelDate") ?? "a megadott napon";

        var routes = root.TryGetProperty("availableRoutes", out var routeArray) &&
                     routeArray.ValueKind == JsonValueKind.Array
            ? routeArray
            : default;

        var journeys = root.TryGetProperty("availableJourneys", out var journeyArray) &&
                       journeyArray.ValueKind == JsonValueKind.Array
            ? journeyArray
            : default;

        var routeCount = routes.ValueKind == JsonValueKind.Array
            ? routes.GetArrayLength()
            : 0;
        var journeyCount = journeys.ValueKind == JsonValueKind.Array
            ? journeys.GetArrayLength()
            : 0;

        if (routeCount == 0 && journeyCount == 0)
        {
            var transferWasChecked =
                root.TryGetProperty("transferSearchPerformed", out var transferElement) &&
                transferElement.ValueKind == JsonValueKind.True;

            return transferWasChecked
                ? $"Nem találtam megfelelő buszjáratot {departure} és {destination} között {date} napjára, közvetlen és átszállásos kereséssel sem."
                : $"Nem találtam közvetlen buszjáratot {departure} és {destination} között {date} napjára. Ebben a keresésben átszállásos útvonalat még nem ellenőriztem.";
        }

        var response = new StringBuilder();

        if (routeCount > 0)
        {
            response.Append($"Találtam {routeCount} közvetlen buszjáratot {departure} és {destination} között {date} napjára.");

            var firstRoute = routes[0];
            var routeName = GetJsonString(firstRoute, "routeShortName")
                ?? GetJsonString(firstRoute, "routeLongName");
            var departureTime = GetJsonString(firstRoute, "departureTime");
            var arrivalTime = GetJsonString(firstRoute, "arrivalTime");
            var duration = firstRoute.TryGetProperty("durationMinutes", out var durationElement) &&
                           durationElement.TryGetInt32(out var durationMinutes)
                ? durationMinutes
                : (int?)null;

            if (!string.IsNullOrWhiteSpace(departureTime) &&
                !string.IsNullOrWhiteSpace(arrivalTime))
            {
                response.Append($" Az első találat");

                if (!string.IsNullOrWhiteSpace(routeName))
                    response.Append($" ({routeName})");

                response.Append($": {departureTime}–{arrivalTime}");

                if (duration.HasValue)
                    response.Append($", {duration.Value} perc");

                response.Append('.');
            }

            response.Append(" A részleteket az alábbi járatkártyákon találod.");
        }
        else
        {
            response.Append($"Találtam {journeyCount} átszállásos útvonalat {departure} és {destination} között {date} napjára.");
            response.Append(" A részleteket az alábbi útvonalkártyákon találod.");
        }

        return response.ToString();
    }

    private static string? GetJsonString(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var departureLocation = GetRequiredString(
            arguments, "departureLocation");

        var destination = GetRequiredString(
            arguments, "destination");

        var travelDateText = GetRequiredString(
            arguments, "travelDate");

        var searchMode = GetRequiredString(
            arguments, "searchMode");

        if (!DateOnly.TryParseExact(
                travelDateText,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var travelDate))
        {
            throw new ArgumentException(
                "Invalid travel date. Expected YYYY-MM-DD.");
        }

        var travelPlan = new TravelPlan
        {
            DepartureLocation = departureLocation,
            Destination = destination,
            TravelDate = travelDate,
            TransportMode = "bus",
            SearchMode = searchMode
        };

        List<GtfsDirectRouteDto> routes = [];
        List<GtfsJourneyDto> journeys = [];
        var transferSearchPerformed = false;

        if (searchMode == "departAfter")
        {
            var departureTimeText = GetOptionalString(
                arguments, "departureTime");

            var localNow = GetBudapestNow();
            var today = DateOnly.FromDateTime(localNow.DateTime);
            TimeOnly departureTime;

            if (string.Equals(
                    departureTimeText,
                    "now",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (travelPlan.TravelDate != today)
                {
                    throw new ArgumentException(
                        "A 'now' indulási idő csak a mai utazási naphoz használható.");
                }

                travelPlan.TravelDate = today;
                departureTime = TimeOnly.FromDateTime(localNow.DateTime);
            }
            else if (string.IsNullOrWhiteSpace(departureTimeText))
            {
                throw new ArgumentException(
                    "A pontos indulási idő megadása szükséges.");
            }
            else if (!TimeOnly.TryParseExact(
                         departureTimeText,
                         "HH:mm",
                         CultureInfo.InvariantCulture,
                         DateTimeStyles.None,
                         out departureTime))
            {
                throw new ArgumentException(
                    "Invalid departure time. Expected HH:mm or now.");
            }

            if (travelPlan.TravelDate < today)
            {
                throw new ArgumentException(
                    "A kiválasztott utazási dátum már elmúlt.");
            }

            // On today's date, never search using an AI-generated time that
            // has already passed. This is a defensive fallback for follow-up
            // messages where the model may accidentally reuse an old time.
            var requestedDepartureTime = departureTime;
            if (travelPlan.TravelDate == today)
            {
                var currentTime = TimeOnly.FromDateTime(localNow.DateTime);
                if (departureTime < currentTime)
                {
                    _logger.LogWarning(
                        "Past departure time corrected. From={DepartureLocation}, To={Destination}, " +
                        "TravelDate={TravelDate}, RequestedDepartureTime={RequestedDepartureTime}, " +
                        "EffectiveDepartureTime={EffectiveDepartureTime}, LocalNow={LocalNow}.",
                        departureLocation,
                        destination,
                        travelPlan.TravelDate,
                        requestedDepartureTime,
                        currentTime,
                        localNow);

                    departureTime = currentTime;
                }
            }

            travelPlan.DepartureTime = departureTime;

            _logger.LogInformation(
                "Bus journey search parameters. From={DepartureLocation}, To={Destination}, " +
                "TravelDate={TravelDate}, SearchMode={SearchMode}, " +
                "RequestedDepartureTime={RequestedDepartureTime}, EffectiveDepartureTime={EffectiveDepartureTime}, " +
                "LocalNow={LocalNow}.",
                departureLocation,
                destination,
                travelPlan.TravelDate,
                searchMode,
                requestedDepartureTime,
                departureTime,
                localNow);

            routes = await _routeSearchService.SearchDirectDeparturesAsync(
                departureLocation,
                destination,
                travelPlan.TravelDate.Value,
                departureTime,
                limit: 5,
                cancellationToken: cancellationToken);

            // If no direct service exists, also look for a valid one-transfer
            // journey that departs after the requested time.
            if (routes.Count == 0)
            {
                transferSearchPerformed = true;
                journeys = await _routeSearchService
                    .SearchOneTransferRoutesAfterAsync(
                        departureLocation,
                        destination,
                        travelPlan.TravelDate.Value,
                        departureTime,
                        limit: 5,
                        cancellationToken: cancellationToken);
            }
        }
        else if (searchMode == "arriveBy")
        {
            var arrivalTimeText = GetRequiredString(
                arguments, "arrivalTime");

            if (!TimeOnly.TryParseExact(
                    arrivalTimeText,
                    "HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var arrivalTime))
            {
                throw new ArgumentException(
                    "Invalid arrival time. Expected HH:mm.");
            }
            var localNow = GetBudapestNow();

            var today = DateOnly.FromDateTime(localNow.DateTime);
            var currentTime = TimeOnly.FromDateTime(localNow.DateTime);

            if (travelDate < today)
            {
                throw new ArgumentException(
                    "A kiválasztott utazási dátum már elmúlt.");
            }

            if (travelDate == today && arrivalTime <= currentTime)
            {
                throw new ArgumentException(
                    "Az esemény kezdési időpontja már elmúlt. " +
                    "Ehhez az eseményhez már nem lehet járatot keresni.");
            }

            travelPlan.ArrivalTime = arrivalTime;

            routes = await _routeSearchService.SearchDirectRoutesAsync(
                departureLocation,
                destination,
                travelDate,
                arrivalTime,
                limit: 5,
                cancellationToken: cancellationToken);

            // Enforce the requested arrival deadline at the tool boundary.
            // Never trust a route-search result that arrives after the deadline.
            routes = routes
                .Where(route => IsAtOrBefore(route.ArrivalTime, arrivalTime))
                .ToList();

            if (routes.Count == 0)
            {
                transferSearchPerformed = true;
                journeys = await _routeSearchService
                    .SearchOneTransferRoutesAsync(
                        departureLocation,
                        destination,
                        travelDate,
                        arrivalTime,
                        limit: 5,
                        cancellationToken: cancellationToken);

                journeys = journeys
                    .Where(journey => IsAtOrBefore(journey.ArrivalTime, arrivalTime))
                    .ToList();
            }
        }
        else
        {
            throw new ArgumentException(
                "Unsupported search mode.");
        }

        return JsonSerializer.Serialize(
            new
            {
                success = true,
                travelPlan,
                availableRoutes = routes,
                availableJourneys = journeys,
                resultCount = routes.Count + journeys.Count,
                directRoutesFound = routes.Count > 0,
                transferSearchPerformed
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }


    private static bool IsAtOrBefore(string? timeText, TimeOnly deadline)
    {
        if (string.IsNullOrWhiteSpace(timeText))
            return false;

        // GTFS may represent times beyond 24:00 (for service after midnight).
        var parts = timeText.Split(':');
        if (parts.Length < 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            hours < 0 || minutes is < 0 or > 59)
        {
            return false;
        }

        var arrivalMinutes = (long)hours * 60 + minutes;
        var deadlineMinutes = deadline.Hour * 60 + deadline.Minute;
        return arrivalMinutes <= deadlineMinutes;
    }

    private static string GetRequiredString(
        JsonElement arguments,
        string property)
    {
        var value = GetOptionalString(arguments, property);

        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException(
                $"Missing required argument: {property}");
    }

    private static string? GetOptionalString(
        JsonElement arguments,
        string property)
    {
        if (!arguments.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static DateTimeOffset GetBudapestNow()
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows()
                ? "Central Europe Standard Time"
                : "Europe/Budapest");

        return TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            timezone);
    }
}