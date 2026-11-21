using DayPlannerAI.Api.Models;
using Microsoft.Extensions.Caching.Memory;

namespace DayPlannerAI.Api.Services;

public sealed class ConversationStore
{
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan Expiration =
        TimeSpan.FromHours(24);

    public ConversationStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public TravelPlan? GetTravelPlan(string conversationId)
    {
        return _cache.TryGetValue<TravelPlan>(
            GetKey(conversationId),
            out var plan) && plan is not null
                ? Clone(plan)
                : null;
    }

    public void SaveTravelPlan(
        string conversationId,
        TravelPlan plan)
    {
        _cache.Set(
            GetKey(conversationId),
            Clone(plan),
            new MemoryCacheEntryOptions
            {
                SlidingExpiration = Expiration
            });
    }

    public void Clear(string conversationId)
    {
        _cache.Remove(GetKey(conversationId));
    }

    private static string GetKey(string conversationId)
    {
        return $"travel-plan:{conversationId}";
    }

    private static TravelPlan Clone(TravelPlan plan)
    {
        return new TravelPlan
        {
            DepartureLocation = plan.DepartureLocation,
            Destination = plan.Destination,
            TravelDate = plan.TravelDate,
            ArrivalTime = plan.ArrivalTime,
            TransportMode = plan.TransportMode
        };
    }
}