namespace DayPlannerAI.Api.DTOs.Realtime;

public sealed class GtfsRealtimeTripDto
{
    public string TripId { get; set; } = string.Empty;

    public DateOnly ServiceDate { get; set; }

    public string RouteId { get; set; } = string.Empty;

    public string? RouteShortName { get; set; }

    public string? RouteLongName { get; set; }

    public string? TripHeadsign { get; set; }

    public bool HasRealtimeStopData { get; set; }

    public DateTimeOffset RetrievedAtUtc { get; set; }

    public GtfsRealtimeVehicleDto? VehiclePosition { get; set; }

    public List<GtfsRealtimeStopCallDto> StopCalls { get; set; } = [];
}