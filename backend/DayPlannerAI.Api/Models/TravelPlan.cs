namespace DayPlannerAI.Api.Models;

public sealed class TravelPlan
{
    public string? DepartureLocation { get; set; }

    public string? Destination { get; set; }

    public DateOnly? TravelDate { get; set; }

    public TimeOnly? ArrivalTime { get; set; }

    public string TransportMode { get; set; } = "unknown";
}