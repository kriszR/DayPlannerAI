using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DayPlannerAI.Api.DTOs;
using DayPlannerAI.Api.Models;
using DayPlannerAI.Api.Services.Agent;

namespace DayPlannerAI.Api.Services.Tools;

public sealed class SearchBusJourneysTool : IAgentTool
{
    private readonly GtfsRouteSearchService _routeSearchService;

    public SearchBusJourneysTool(
        GtfsRouteSearchService routeSearchService)
    {
        _routeSearchService = routeSearchService;
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
                    "Indulási idő HH:mm formátumban, vagy 'now' az azonnali induláshoz."
            },
            ["arrivalTime"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] =
                    "Kívánt érkezési idő HH:mm formátumban."
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

        if (searchMode == "departAfter")
        {
            var departureTimeText = GetOptionalString(
                arguments, "departureTime");

            TimeOnly departureTime;

            if (string.Equals(
                    departureTimeText,
                    "now",
                    StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(departureTimeText))
            {
                var localNow = GetBudapestNow();

                travelPlan.TravelDate =
                    DateOnly.FromDateTime(localNow.DateTime);

                departureTime = TimeOnly.FromDateTime(localNow.DateTime);
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

            travelPlan.DepartureTime = departureTime;

            routes = await _routeSearchService.SearchDirectDeparturesAsync(
                departureLocation,
                destination,
                travelPlan.TravelDate.Value,
                departureTime,
                limit: 5,
                cancellationToken: cancellationToken);
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


            if (travelDate == today)
            {
                routes = routes
                    .Where(route => IsDepartureStillAvailable(
                        route.DepartureTime,
                        currentTime))
                    .ToList();
            }

            if (routes.Count == 0)
            {
                journeys = await _routeSearchService
                    .SearchOneTransferRoutesAsync(
                        departureLocation,
                        destination,
                        travelDate,
                        arrivalTime,
                        limit: 5,
                        cancellationToken: cancellationToken);

                if (travelDate == today)
                {
                    journeys = journeys
                        .Where(journey => IsDepartureStillAvailable(
                            journey.DepartureTime,
                            currentTime))
                        .ToList();
                }
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
                transferSearchPerformed =
                    searchMode == "arriveBy" && routes.Count == 0
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
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

    private static bool IsDepartureStillAvailable(
    string departureTime,
    TimeOnly currentTime)
    {
        return TimeOnly.TryParse(
            departureTime,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedTime)
            && parsedTime > currentTime;
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