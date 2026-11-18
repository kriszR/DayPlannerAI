
using DayPlannerAI.Api.DTOs;
using DayPlannerAI.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace DayPlannerAI.Api.Controllers;

[ApiController]
[Route("api/google-calendar")]
public class CalendarEventsController : ControllerBase
{
    private readonly GoogleCalendarService _calendarService;

    public CalendarEventsController(
        GoogleCalendarService calendarService)
    {
        _calendarService = calendarService;
    }

    [HttpGet("events")]
    public async Task<ActionResult<IReadOnlyList<CalendarEventDto>>>
        GetEvents(
            [FromQuery] int days = 7,
            CancellationToken cancellationToken = default)
    {
        if (days < 1 || days > 30)
        {
            return BadRequest(new
            {
                Message = "A napok száma 1 és 30 között lehet."
            });
        }

        try
        {
            var events = await _calendarService.GetUpcomingEventsAsync(
                days,
                cancellationToken);

            return Ok(events);
        }
        catch (InvalidOperationException)
        {
            return Conflict(new
            {
                Message = "Először csatlakoztasd a Google-naptáradat."
            });
        }
    }
}
