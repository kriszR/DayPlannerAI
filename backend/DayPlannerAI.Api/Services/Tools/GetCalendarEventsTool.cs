using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DayPlannerAI.Api.Services.Agent;

namespace DayPlannerAI.Api.Services.Tools;

public sealed class GetCalendarEventsTool : IAgentTool
{
    private readonly InternalApiClient _apiClient;

    public GetCalendarEventsTool(InternalApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public string Name => "get_calendar_events";

    public string Description =>
        "Lekéri a Google Calendar eseményeit egy pontos naptári napra. " +
        "Napi programtervezéshez használd. A dátumot a budapesti aktuális " +
        "idő alapján határozd meg.";

    public JsonObject Parameters => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["date"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] =
                    "A lekérdezendő naptári nap YYYY-MM-DD formátumban."
            },
            ["timeZone"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] =
                    "A felhasználó időzónája. Alapértelmezés: Europe/Budapest."
            }
        },
        ["required"] = new JsonArray
        {
            JsonValue.Create("date")
        },
        ["additionalProperties"] = false
    };

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var dateText = GetString(arguments, "date");

        if (!DateOnly.TryParseExact(
                dateText,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var requestedDate))
        {
            throw new ArgumentException(
                "A dátum kötelező, formátuma YYYY-MM-DD.");
        }

        var timeZoneId = GetString(arguments, "timeZone")
            ?? "Europe/Budapest";

        var timeZone = ResolveTimeZone(timeZoneId);

        // Fetch a sufficiently broad window, then filter by the exact local date.
        var response = await _apiClient.GetJsonAsync(
            "/api/google-calendar/events?days=7",
            cancellationToken);

        using var document = JsonDocument.Parse(response);

        var eventsElement = FindEventsArray(document.RootElement);

        var dayStart = requestedDate.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);

        var filteredEvents = new JsonArray();

        foreach (var calendarEvent in eventsElement.EnumerateArray())
        {
            var startText = GetString(calendarEvent, "start");
            var endText = GetString(calendarEvent, "end");

            if (string.IsNullOrWhiteSpace(startText))
                continue;

            var start = ParseLocalDateTime(startText, timeZone);

            if (start is null)
                continue;

            var end = string.IsNullOrWhiteSpace(endText)
                ? start.Value
                : ParseLocalDateTime(endText, timeZone) ?? start.Value;

            // Include events that overlap the requested local calendar day.
            var overlapsRequestedDay =
                start.Value < dayEnd &&
                end >= dayStart;

            if (!overlapsRequestedDay)
                continue;

            filteredEvents.Add(
                JsonNode.Parse(calendarEvent.GetRawText()));
        }

        return JsonSerializer.Serialize(
            new
            {
                success = true,
                requestedDate = requestedDate.ToString("yyyy-MM-dd"),
                timeZone = timeZone.Id,
                eventCount = filteredEvents.Count,
                events = filteredEvents
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

        var date = GetString(root, "requestedDate")
            ?? "Ismeretlen dátum";

        var events = root.GetProperty("events");

        var result = new StringBuilder();

        if (events.GetArrayLength() == 0)
        {
            result.AppendLine(
                $"A naptáradban {date} napján nincs bejegyzett esemény.");

            return result.ToString().Trim();
        }

        result.AppendLine(
            $"A naptáradban {date} napján " +
            $"{events.GetArrayLength()} bejegyzés található.");

        result.AppendLine();

        foreach (var calendarEvent in events.EnumerateArray())
        {
            var summary = GetString(calendarEvent, "summary")
                ?? "Névtelen esemény";

            var start = GetString(calendarEvent, "start");
            var end = GetString(calendarEvent, "end");

            var isAllDay =
                calendarEvent.TryGetProperty("isAllDay", out var allDay) &&
                allDay.ValueKind == JsonValueKind.True;

            result.AppendLine($"**{summary}**");

            if (isAllDay)
            {
                result.AppendLine("Egész napos esemény.");
            }
            else
            {
                result.AppendLine(
                    $"Naptári időablak: {FormatEventTime(start)} – {FormatEventTime(end)}");
            }

            var location = GetString(calendarEvent, "location");

            if (!string.IsNullOrWhiteSpace(location))
            {
                result.AppendLine($"Helyszín: {location}");
            }

            result.AppendLine();
        }

        return result.ToString().Trim();
    }

    private static string FormatEventTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Ismeretlen";

        if (DateTimeOffset.TryParse(value, out var dateTime))
            return dateTime.ToString("HH:mm");

        return value;
    }

    private static JsonElement FindEventsArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root;

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "events", "items" })
            {
                if (root.TryGetProperty(propertyName, out var value) &&
                    value.ValueKind == JsonValueKind.Array)
                {
                    return value;
                }
            }

            if (root.TryGetProperty("data", out var data))
                return FindEventsArray(data);
        }

        throw new JsonException(
            "The Google Calendar API response does not contain an event array.");
    }

    private static DateTime? ParseLocalDateTime(
        string value,
        TimeZoneInfo timeZone)
    {
        // Google Calendar all-day events may use YYYY-MM-DD.
        if (DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var allDayDate))
        {
            return allDayDate.ToDateTime(TimeOnly.MinValue);
        }

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var dateTime))
        {
            return null;
        }

        return TimeZoneInfo.ConvertTime(dateTime, timeZone).DateTime;
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            if (timeZoneId == "Europe/Budapest")
            {
                return TimeZoneInfo.FindSystemTimeZoneById(
                    "Central Europe Standard Time");
            }

            throw;
        }
    }

    private static string? GetString(
        JsonElement element,
        string property)
    {
        return element.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

}