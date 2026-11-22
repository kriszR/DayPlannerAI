namespace DayPlannerAI.Api.Models;

public sealed class TravelPlan
{
    public string? DepartureLocation { get; set; }
    public string? Destination { get; set; }

    public DateOnly? TravelDate { get; set; }

    public TimeOnly? DepartureTime { get; set; }
    public TimeOnly? ArrivalTime { get; set; }

    // Supported values: "arriveBy", "departAfter"
    public string SearchMode { get; set; } = "arriveBy";

    public string TransportMode { get; set; } = "unknown";
}