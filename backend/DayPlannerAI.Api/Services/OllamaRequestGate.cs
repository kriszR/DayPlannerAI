using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace DayPlannerAI.Api.Services;

public sealed class OllamaRequestGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ILogger<OllamaRequestGate> _logger;

    public OllamaRequestGate(ILogger<OllamaRequestGate> logger)
    {
        _logger = logger;
    }

    public async Task<HttpResponseMessage> PostAsJsonAsync<T>(
        HttpClient client,
        string requestUri,
        T value,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var waitTimer = Stopwatch.StartNew();
        await _semaphore.WaitAsync(cancellationToken);
        waitTimer.Stop();

        if (waitTimer.Elapsed >= TimeSpan.FromSeconds(1))
        {
            _logger.LogInformation(
                "Ollama request waited {WaitSeconds:F1} seconds for the shared model slot.",
                waitTimer.Elapsed.TotalSeconds);
        }

        try
        {
            return await client.PostAsJsonAsync(
                requestUri,
                value,
                options,
                cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
