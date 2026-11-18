
namespace DayPlannerAI.Api.DTOs;

public class CalendarEventDto
{
    public string Id { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public string? Description { get; set; }

    public string? Location { get; set; }

    public string? Start { get; set; }

    public string? End { get; set; }

    public bool IsAllDay { get; set; }

    public string? HtmlLink { get; set; }
}
