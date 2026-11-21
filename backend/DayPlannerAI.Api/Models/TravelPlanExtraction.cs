using System.Text.Json.Serialization;

namespace DayPlannerAI.Api.Models;

public sealed class TravelPlanExtraction
{
    [JsonPropertyName("departureLocation")]
    public string DepartureLocation { get; set; } = string.Empty;

    [JsonPropertyName("destination")]
    public string Destination { get; set; } = string.Empty;

    [JsonPropertyName("travelDate")]
    public string TravelDate { get; set; } = string.Empty;

    [JsonPropertyName("arrivalTime")]
    public string ArrivalTime { get; set; } = string.Empty;

    [JsonPropertyName("transportMode")]
    public string TransportMode { get; set; } = "unknown";
}