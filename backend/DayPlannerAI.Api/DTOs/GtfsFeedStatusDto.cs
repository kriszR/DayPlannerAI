namespace DayPlannerAI.Api.DTOs;

public sealed class GtfsFeedStatusDto
{
    public int AgencyCount { get; set; }
    public int StopCount { get; set; }
    public int RouteCount { get; set; }
    public int TripCount { get; set; }
    public int StopTimeCount { get; set; }
    public int CalendarCount { get; set; }
    public int CalendarDateCount { get; set; }

    public string? FeedVersion { get; set; }
    public DateOnly? FeedStartDate { get; set; }
    public DateOnly? FeedEndDate { get; set; }

    public List<GtfsRouteTypeCountDto> RouteTypes { get; set; } = [];
}

public sealed class GtfsRouteTypeCountDto
{
    public int RouteType { get; set; }
    public int Count { get; set; }
}