using DayPlannerAI.Api.Data;
using DayPlannerAI.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace DayPlannerAI.Api.Services;

public sealed class GtfsQueryService
{
    private readonly ApplicationDbContext _dbContext;

    public GtfsQueryService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<GtfsStopSearchResultDto>> SearchStopsAsync(
        string query,
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        query = query.Trim();

        if (query.Length < 2)
        {
            return [];
        }

        return await _dbContext.GtfsStops
            .AsNoTracking()
            .Where(stop =>
                EF.Functions.ILike(
                    stop.StopName,
                    $"%{query}%"))
            .OrderBy(stop => stop.StopName)
            .ThenBy(stop => stop.LocationType)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(stop => new GtfsStopSearchResultDto
            {
                StopId = stop.StopId,
                StopName = stop.StopName,
                ParentStation = stop.ParentStation,
                LocationType = stop.LocationType,
                Latitude = stop.StopLat,
                Longitude = stop.StopLon
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<GtfsFeedStatusDto> GetFeedStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var feedInfo = await _dbContext.GtfsFeedInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == 1,
                cancellationToken);

        var routeTypes = await _dbContext.GtfsRoutes
            .AsNoTracking()
            .GroupBy(route => route.RouteType)
            .Select(group => new GtfsRouteTypeCountDto
            {
                RouteType = group.Key,
                Count = group.Count()
            })
            .OrderBy(item => item.RouteType)
            .ToListAsync(cancellationToken);

        return new GtfsFeedStatusDto
        {
            AgencyCount = await _dbContext.GtfsAgencies.CountAsync(cancellationToken),
            StopCount = await _dbContext.GtfsStops.CountAsync(cancellationToken),
            RouteCount = await _dbContext.GtfsRoutes.CountAsync(cancellationToken),
            TripCount = await _dbContext.GtfsTrips.CountAsync(cancellationToken),
            StopTimeCount = await _dbContext.GtfsStopTimes.CountAsync(cancellationToken),
            CalendarCount = await _dbContext.GtfsCalendars.CountAsync(cancellationToken),
            CalendarDateCount = await _dbContext.GtfsCalendarDates.CountAsync(cancellationToken),
            FeedVersion = feedInfo?.FeedVersion,
            FeedStartDate = feedInfo?.FeedStartDate,
            FeedEndDate = feedInfo?.FeedEndDate,
            RouteTypes = routeTypes
        };
    }
}