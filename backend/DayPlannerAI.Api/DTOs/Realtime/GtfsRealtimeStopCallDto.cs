namespace DayPlannerAI.Api.DTOs.Realtime;

public sealed class GtfsRealtimeStopCallDto
{
    public string StopId { get; set; } = string.Empty;

    public string StopName { get; set; } = string.Empty;

    public int? ArrivalDelaySeconds { get; set; }

    public int? DepartureDelaySeconds { get; set; }

    public bool Realtime { get; set; }

    public string? RealtimeState { get; set; }
}