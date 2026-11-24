using System.Text.Json;
using System.Text.Json.Nodes;
using DayPlannerAI.Api.Services.Agent;

namespace DayPlannerAI.Api.Services.Tools;

public sealed class GetUserProfileTool : IAgentTool
{
    private readonly InternalApiClient _apiClient;

    public GetUserProfileTool(InternalApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public string Name => "get_user_profile";

    public string Description =>
        "Lekéri a felhasználói profilt, a lakóhelyet, az időzónát " +
        "és a beállított felkészülési időt. Napi tervezéskor használd.";

    public JsonObject Parameters => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
        ["additionalProperties"] = false
    };

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var response = await _apiClient.GetJsonAsync(
            "/api/profile",
            cancellationToken);

        using var document = JsonDocument.Parse(response);

        var profile = document.RootElement;

        if (profile.TryGetProperty("profile", out var nestedProfile))
        {
            profile = nestedProfile;
        }
        else if (profile.TryGetProperty("data", out var nestedData) &&
                 nestedData.ValueKind == JsonValueKind.Object)
        {
            profile = nestedData;
        }

        var result = new
        {
            success = true,
            displayName = GetString(profile, "displayName"),
            homeLocation = GetString(profile, "homeLocation"),
            timeZone = GetString(profile, "timeZone"),
            preparationMinutes = GetNullableInt(
                profile,
                "preparationMinutes")
        };

        return JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
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

    private static int? GetNullableInt(
        JsonElement element,
        string property)
    {
        return element.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out var result)
            ? result
            : null;
    }
}