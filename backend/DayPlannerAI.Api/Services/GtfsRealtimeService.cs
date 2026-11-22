using DayPlannerAI.Api.DTOs.Realtime;
using Microsoft.Extensions.Caching.Memory;

namespace DayPlannerAI.Api.Services;

public sealed class GtfsRealtimeService
{
    private readonly MavPluszRealtimeClient _client;
    private readonly IMemoryCache _cache;

    public GtfsRealtimeService(
        MavPluszRealtimeClient client,
        IMemoryCache cache)
    {
        _client = client;
        _cache = cache;
    }

    public async Task<GtfsRealtimeTripDto?> GetTripRealtimeAsync(
        string tripId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
    {
        var cacheKey =
            $"mavplusz:{tripId}:{serviceDate:yyyy-MM-dd}";

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow =
                    TimeSpan.FromSeconds(60);

                return await _client.GetTripRealtimeAsync(
                    tripId,
                    serviceDate,
                    cancellationToken);
            });
    }
}