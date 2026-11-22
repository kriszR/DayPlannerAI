namespace DayPlannerAI.Api.DTOs;

public sealed class GtfsStopSearchResultDto
{
    public string StopId { get; set; } = string.Empty;
    public string StopName { get; set; } = string.Empty;
    public string? ParentStation { get; set; }
    public int? LocationType { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}