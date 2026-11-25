namespace DayPlannerAI.Api.DTOs;

public sealed class GtfsJourneyDto
{
    public string DepartureStop { get; set; } = string.Empty;
    public string ArrivalStop { get; set; } = string.Empty;

    public string DepartureTime { get; set; } = string.Empty;
    public string ArrivalTime { get; set; } = string.Empty;

    public int TotalDurationMinutes { get; set; }
    public int TransferCount { get; set; }

    public DateOnly ServiceDate { get; set; }

    public List<GtfsJourneyLegDto> Legs { get; set; } = [];
}

public sealed class GtfsJourneyLegDto
{
    public string TripId { get; set; } = string.Empty;
    public string RouteId { get; set; } = string.Empty;

    public string? RouteShortName { get; set; }
    public string? RouteLongName { get; set; }
    public string? TripHeadsign { get; set; }

    public string DepartureStop { get; set; } = string.Empty;
    public string? DeparturePlatform { get; set; }
    public string DepartureTime { get; set; } = string.Empty;

    public string ArrivalStop { get; set; } = string.Empty;
    public string? ArrivalPlatform { get; set; }
    public string ArrivalTime { get; set; } = string.Empty;

    public int DurationMinutes { get; set; }
    public bool IsInProgress { get; set; }
}