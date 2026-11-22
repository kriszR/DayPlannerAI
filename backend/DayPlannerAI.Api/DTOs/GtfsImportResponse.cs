namespace DayPlannerAI.Api.DTOs;

public sealed class GtfsImportResponse
{
    public int Agencies { get; set; }
    public int Stops { get; set; }
    public int Routes { get; set; }
    public int Trips { get; set; }
    public int StopTimes { get; set; }
    public int Calendars { get; set; }
    public int CalendarDates { get; set; }
    public int FeedInfoRecords { get; set; }

    public string? FeedVersion { get; set; }
    public DateOnly? FeedStartDate { get; set; }
    public DateOnly? FeedEndDate { get; set; }
}