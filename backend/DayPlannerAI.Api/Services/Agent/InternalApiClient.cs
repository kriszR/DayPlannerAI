namespace DayPlannerAI.Api.Services.Agent;

public sealed class InternalApiClient
{
    private readonly HttpClient _httpClient;

    public InternalApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> GetJsonAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            path,
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Internal API returned {(int)response.StatusCode}: {content}");
        }

        return content;
    }
}