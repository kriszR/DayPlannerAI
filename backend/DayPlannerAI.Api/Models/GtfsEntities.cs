namespace DayPlannerAI.Api.Models;

public sealed class GtfsAgency
{
    public string AgencyId { get; set; } = string.Empty;
    public string AgencyName { get; set; } = string.Empty;
    public string AgencyUrl { get; set; } = string.Empty;
    public string AgencyTimezone { get; set; } = "Europe/Budapest";
    public string? AgencyLang { get; set; }
    public string? AgencyPhone { get; set; }
}

public sealed class GtfsStop
{
    public string StopId { get; set; } = string.Empty;
    public string StopName { get; set; } = string.Empty;
    public double? StopLat { get; set; }
    public double? StopLon { get; set; }
    public int? LocationType { get; set; }
    public string? ParentStation { get; set; }
    public string? PlatformCode { get; set; }
}

public sealed class GtfsRoute
{
    public string RouteId { get; set; } = string.Empty;
    public string? AgencyId { get; set; }
    public string? RouteShortName { get; set; }
    public string? RouteLongName { get; set; }
    public int RouteType { get; set; }
    public string? RouteUrl { get; set; }
    public string? RouteColor { get; set; }
    public string? RouteTextColor { get; set; }
    public string? RouteNetwork { get; set; }
}

public sealed class GtfsTrip
{
    public string TripId { get; set; } = string.Empty;
    public string RouteId { get; set; } = string.Empty;
    public string ServiceId { get; set; } = string.Empty;
    public string? TripShortName { get; set; }
    public string? TripHeadsign { get; set; }
    public int? DirectionId { get; set; }
    public string? ShapeId { get; set; }
    public int? WheelchairAccessible { get; set; }
    public string? TripRealtimeId { get; set; }
}

public sealed class GtfsStopTime
{
    public string TripId { get; set; } = string.Empty;
    public string StopId { get; set; } = string.Empty;
    public int? ArrivalSeconds { get; set; }
    public int? DepartureSeconds { get; set; }
    public int StopSequence { get; set; }
    public int? PickupType { get; set; }
    public int? DropOffType { get; set; }
    public double? ShapeDistTraveled { get; set; }
}

public sealed class GtfsCalendar
{
    public string ServiceId { get; set; } = string.Empty;
    public bool Monday { get; set; }
    public bool Tuesday { get; set; }
    public bool Wednesday { get; set; }
    public bool Thursday { get; set; }
    public bool Friday { get; set; }
    public bool Saturday { get; set; }
    public bool Sunday { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? ServiceDesc { get; set; }
}

public sealed class GtfsCalendarDate
{
    public string ServiceId { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public int ExceptionType { get; set; }
}

public sealed class GtfsFeedInfo
{
    public int Id { get; set; } = 1;
    public string? FeedId { get; set; }
    public string? PublisherName { get; set; }
    public string? PublisherUrl { get; set; }
    public string? FeedLanguage { get; set; }
    public DateOnly? FeedStartDate { get; set; }
    public DateOnly? FeedEndDate { get; set; }
    public string? FeedVersion { get; set; }
}