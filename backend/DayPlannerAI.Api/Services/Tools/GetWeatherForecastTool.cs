
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DayPlannerAI.Api.Services.Agent;

namespace DayPlannerAI.Api.Services.Tools;

public sealed class GetWeatherForecastTool : IAgentTool
{
    private readonly InternalApiClient _apiClient;

    public GetWeatherForecastTool(InternalApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public string Name => "get_weather_forecast";

    public string Description =>
        "Lekéri a felhasználó lakóhelyére vonatkozó időjárás-előrejelzést " +
        "egy konkrét dátumra. Napi tervezéskor mindig a kért dátumot add meg.";

    public JsonObject Parameters => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["date"] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "date",
                ["description"] =
                    "A kért előrejelzés dátuma YYYY-MM-DD formátumban. " +
                    "Például: 2026-11-23."
            }
        },
        ["required"] = new JsonArray(JsonValue.Create("date")),
        ["additionalProperties"] = false
    };

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        // Validate the requested date.
        if (!arguments.TryGetProperty("date", out var dateElement) ||
            dateElement.ValueKind != JsonValueKind.String ||
            !DateOnly.TryParseExact(
                dateElement.GetString(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var requestedDate))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Érvényes dátum szükséges YYYY-MM-DD formátumban."
            });
        }

        // Use the user's configured timezone.
        var timeZone = await GetUserTimeZoneAsync(cancellationToken);

        var localNow = TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            timeZone);

        var today = DateOnly.FromDateTime(localNow.DateTime);

        var daysAhead = requestedDate.DayNumber - today.DayNumber;

        // The weather endpoint supports forecasts for today
        // and up to six additional days.
        if (daysAhead < 0 || daysAhead > 6)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                requestedDate = requestedDate.ToString("yyyy-MM-dd"),
                error =
                    "A kért dátum kívül esik a jelenleg elérhető " +
                    "előrejelzési időszakon. A mai naptól számított " +
                    "következő hét napra érhető el előrejelzés."
            });
        }

        // The endpoint counts today as the first forecast day.
        var days = daysAhead + 1;

        var response = await _apiClient.GetJsonAsync(
            $"/api/weather?days={days}",
            cancellationToken);

        using var document = JsonDocument.Parse(response);

        var forecast = document.RootElement;

        if (!forecast.TryGetProperty("daily", out var dailyForecast) ||
            dailyForecast.ValueKind != JsonValueKind.Array)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                requestedDate = requestedDate.ToString("yyyy-MM-dd"),
                error = "Az időjárási szolgáltatás nem adott napi előrejelzést."
            });
        }

        var requestedDateString = requestedDate.ToString("yyyy-MM-dd");

        // Select only the forecast belonging to the requested date.
        foreach (var day in dailyForecast.EnumerateArray())
        {
            if (day.TryGetProperty("date", out var date) &&
                date.GetString() == requestedDateString)
            {
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    requestedDate = requestedDateString,
                    forecast = new
                    {
                        location = forecast.TryGetProperty(
                            "location", out var location)
                                ? location.GetString()
                                : null,

                        timezone = forecast.TryGetProperty(
                            "timezone", out var timezoneElement)
                                ? timezoneElement.GetString()
                                : null,

                        daily = day.Clone()
                    }
                });
            }
        }

        // Never substitute today's weather for a missing future forecast.
        return JsonSerializer.Serialize(new
        {
            success = false,
            requestedDate = requestedDateString,
            error = "A kért dátumhoz nem található időjárási előrejelzés."
        });
    }

    private async Task<TimeZoneInfo> GetUserTimeZoneAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _apiClient.GetJsonAsync(
                "/api/profile",
                cancellationToken);

            using var document = JsonDocument.Parse(response);

            if (document.RootElement.TryGetProperty(
                    "timeZone", out var timeZoneElement) &&
                timeZoneElement.ValueKind == JsonValueKind.String)
            {
                var timeZoneId = timeZoneElement.GetString();

                if (!string.IsNullOrWhiteSpace(timeZoneId))
                {
                    try
                    {
                        return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                    }
                    catch (TimeZoneNotFoundException)
                    {
                        // Use the default timezone if the configured one is unavailable.
                    }
                    catch (InvalidTimeZoneException)
                    {
                        // Use the default timezone if the configured one is invalid.
                    }
                }
            }
        }
        catch (HttpRequestException)
        {
            // Fall back to the application default timezone.
        }
        catch (JsonException)
        {
            // Fall back to the application default timezone.
        }

        return TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");
    }
}
