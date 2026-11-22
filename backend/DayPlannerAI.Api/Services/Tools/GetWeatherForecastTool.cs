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
        "Lekéri a felhasználó lakóhelyére beállított időjárás-előrejelzést. " +
        "Napi programok és szabadtéri tevékenységek tervezésekor használd.";

    public JsonObject Parameters => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["days"] = new JsonObject
            {
                ["type"] = "integer",
                ["description"] =
                    "Az előrejelzés napjainak száma. Alapértelmezés: 1."
            }
        },
        ["additionalProperties"] = false
    };

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var days = 1;

        if (arguments.TryGetProperty("days", out var daysElement) &&
            daysElement.TryGetInt32(out var parsedDays))
        {
            days = Math.Clamp(parsedDays, 1, 7);
        }

        var response = await _apiClient.GetJsonAsync(
            $"/api/weather?days={days}",
            cancellationToken);

        using var document = JsonDocument.Parse(response);

        return JsonSerializer.Serialize(new
        {
            success = true,
            days,
            forecast = document.RootElement
        });
    }
}