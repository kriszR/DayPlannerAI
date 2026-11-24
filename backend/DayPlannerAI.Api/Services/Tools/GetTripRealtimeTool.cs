using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DayPlannerAI.Api.Data;
using DayPlannerAI.Api.Services;
using DayPlannerAI.Api.Services.Agent;
using Microsoft.EntityFrameworkCore;

namespace DayPlannerAI.Api.Services.Tools;

public sealed class GetTripRealtimeTool : IAgentTool
{
    private readonly ApplicationDbContext _dbContext;
    private readonly GtfsRealtimeService _realtimeService;

    public bool CanShortCircuit => true;

    public GetTripRealtimeTool(
        ApplicationDbContext dbContext,
        GtfsRealtimeService realtimeService)
    {
        _dbContext = dbContext;
        _realtimeService = realtimeService;
    }

    public string Name => "get_trip_realtime";

    public string Description =>
    "Valós idejű GPS-pozíció és késési információ lekérése " +
    "egy konkrét magyarországi buszjárathoz. A tripId és " +
    "serviceDate értéket korábbi menetrendkeresési eredményből is átveheted.";

    public JsonObject Parameters => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["tripId"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] =
                    "The exact GTFS trip ID, for example hkir_7300_318-7."
            },
            ["serviceDate"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Service date in YYYY-MM-DD format."
            }
        },
        ["required"] = new JsonArray
        {
            JsonValue.Create("tripId"),
            JsonValue.Create("serviceDate")
        },
        ["additionalProperties"] = false
    };

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Tool arguments must be a JSON object.");

        var tripId = arguments.TryGetProperty("tripId", out var tripElement)
            ? tripElement.GetString()
            : null;

        var dateText = arguments.TryGetProperty("serviceDate", out var dateElement)
            ? dateElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(tripId))
            throw new ArgumentException("Trip ID is required.");

        if (!DateOnly.TryParseExact(
                dateText,
                "yyyy-MM-dd",
                out var serviceDate))
        {
            throw new ArgumentException(
                "Service date must use YYYY-MM-DD format.");
        }

        // Accept both local GTFS IDs and MÁVPlusz-prefixed IDs.
        const string prefix = "hkir:";

        var localTripId = tripId.StartsWith(
            prefix,
            StringComparison.OrdinalIgnoreCase)
            ? tripId[prefix.Length..]
            : tripId;

        // Only allow trips that exist in the imported GTFS database.
        var tripExists = await _dbContext.GtfsTrips
            .AsNoTracking()
            .AnyAsync(
                x => x.TripId == localTripId,
                cancellationToken);

        if (!tripExists)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "The requested trip was not found in the GTFS database."
            });
        }

        var result = await _realtimeService.GetTripRealtimeAsync(
            localTripId,
            serviceDate,
            cancellationToken);

        if (result is null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "No realtime information is available for this trip."
            });
        }

        return JsonSerializer.Serialize(
            new
            {
                success = true,
                data = result
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public string? BuildUserResponse(string toolResult)
    {
        using var document = JsonDocument.Parse(toolResult);

        var root = document.RootElement;

        if (!root.TryGetProperty("success", out var success) ||
            success.ValueKind != JsonValueKind.True)
        {
            return null;
        }

        var data = root.GetProperty("data");

        var result = new StringBuilder();

        var routeName = GetJsonString(data, "routeShortName")
            ?? GetJsonString(data, "routeLongName")
            ?? "Ismeretlen";

        result.AppendLine($"A {routeName} járat valós idejű adatai:");
        result.AppendLine();

        result.AppendLine(
            $"Úticél: {GetJsonString(data, "tripHeadsign") ?? "Nem ismert"}");

        var vehicle = data.GetProperty("vehiclePosition");

        if (vehicle.ValueKind == JsonValueKind.Object)
        {
            var vehicleId = GetJsonString(vehicle, "vehicleId");

            var latitude = GetJsonDouble(vehicle, "latitude");
            var longitude = GetJsonDouble(vehicle, "longitude");

            var speed = GetJsonNullableDouble(
                vehicle,
                "speedKilometersPerHour");

            var heading = GetJsonNullableDouble(vehicle, "heading");

            var lastUpdated = GetJsonString(
                vehicle,
                "lastUpdatedUtc");

            var isFresh = GetJsonBoolean(vehicle, "isFresh");

            result.AppendLine($"Jármű: {vehicleId ?? "Ismeretlen"}");

            if (isFresh)
            {
                result.AppendLine(
                    $"GPS-koordináták: {latitude:F6}, {longitude:F6}");

                result.AppendLine(
                    $"Sebesség: {(speed.HasValue ? $"{speed.Value:F1} km/h" : "nincs adat")}");

                if (heading.HasValue)
                {
                    result.AppendLine(
                        $"Haladási irány: {heading.Value:F0}°");
                }

                result.AppendLine(
                    $"Utolsó GPS-frissítés: {lastUpdated}");

                result.AppendLine(
                    "A GPS-pozíció frissnek minősül.");
            }
            else
            {
                result.AppendLine(
                    $"A jármű utolsó ismert GPS-pozíciója elavult: {lastUpdated}");
            }
        }
        else
        {
            result.AppendLine(
                "Ehhez a járathoz jelenleg nem érhető el GPS-pozíció.");
        }

        result.AppendLine();

        result.AppendLine("Megállónkénti valós idejű késések:");

        var stopCalls = data.GetProperty("stopCalls")
            .EnumerateArray()
            .Where(call => GetJsonBoolean(call, "realtime"))
            .ToList();

        foreach (var stop in stopCalls)
        {
            var stopName = GetJsonString(stop, "stopName")
                ?? "Ismeretlen megálló";

            var arrivalDelay = GetJsonNullableInt(
                stop,
                "arrivalDelaySeconds");

            var departureDelay = GetJsonNullableInt(
                stop,
                "departureDelaySeconds");

            result.AppendLine($"- {stopName}");

            if (arrivalDelay.HasValue)
            {
                result.AppendLine(
                    $"  Érkezés: {FormatDelay(arrivalDelay.Value)}");
            }

            if (departureDelay.HasValue)
            {
                result.AppendLine(
                    $"  Indulás: {FormatDelay(departureDelay.Value)}");
            }
        }

        if (stopCalls.Count == 0)
        {
            result.AppendLine(
                "Nem érkezett valós idejű megállóadat.");
        }

        result.AppendLine();
        result.AppendLine(
            "Az adatok a MÁVPlusz valós idejű szolgáltatásából származnak.");

        return result.ToString().Trim();
    }

    private static string FormatDelay(int seconds)
    {
        if (seconds == 0)
            return "menetrend szerint";

        var absoluteSeconds = Math.Abs((long)seconds);

        var minutes = absoluteSeconds / 60;
        var remainingSeconds = absoluteSeconds % 60;

        var duration = minutes > 0
            ? $"{minutes} perc {remainingSeconds} másodperc"
            : $"{remainingSeconds} másodperc";

        return seconds > 0
            ? $"+{duration} késés"
            : $"{duration} korábbi érkezés";
    }

    private static string? GetJsonString(
        JsonElement element,
        string property)
    {
        return element.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static double GetJsonDouble(
        JsonElement element,
        string property)
    {
        return GetJsonNullableDouble(element, property) ?? 0;
    }

    private static double? GetJsonNullableDouble(
        JsonElement element,
        string property)
    {
        return element.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetDouble(out var result)
            ? result
            : null;
    }

    private static int? GetJsonNullableInt(
        JsonElement element,
        string property)
    {
        return element.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out var result)
            ? result
            : null;
    }

    private static bool GetJsonBoolean(
        JsonElement element,
        string property)
    {
        return element.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.True;
    }
}