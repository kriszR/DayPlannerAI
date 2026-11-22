using DayPlannerAI.Api.Data;
using DayPlannerAI.Api.DTOs.Realtime;
using DayPlannerAI.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DayPlannerAI.Api.Controllers;

[ApiController]
[Route("api/realtime")]
public sealed class RealtimeController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly GtfsRealtimeService _realtimeService;
    private readonly ILogger<RealtimeController> _logger;

    public RealtimeController(
        ApplicationDbContext dbContext,
        GtfsRealtimeService realtimeService,
        ILogger<RealtimeController> logger)
    {
        _dbContext = dbContext;
        _realtimeService = realtimeService;
        _logger = logger;
    }

    [HttpGet("trips/{tripId}")]
    [ProducesResponseType(typeof(GtfsRealtimeTripDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<GtfsRealtimeTripDto>> GetTripRealtime(
        string tripId,
        [FromQuery] DateOnly serviceDate,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tripId))
            return BadRequest("Trip ID is required.");

        if (serviceDate == default)
            return BadRequest("Service date is required.");

        // Only allow trips that exist in the locally imported GTFS feed.
        var tripExists = await _dbContext.GtfsTrips
            .AsNoTracking()
            .AnyAsync(
                x => x.TripId == tripId,
                cancellationToken);

        if (!tripExists)
            return NotFound("The requested trip does not exist in the GTFS database.");

        try
        {
            var result = await _realtimeService.GetTripRealtimeAsync(
                tripId,
                serviceDate,
                cancellationToken);

            if (result is null)
                return NotFound("The realtime provider did not return this trip.");

            return Ok(result);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(
                exception,
                "MÁVPlusz realtime request failed for trip {TripId}.",
                tripId);

            return StatusCode(
                StatusCodes.Status502BadGateway,
                new ProblemDetails
                {
                    Title = "Realtime provider unavailable",
                    Detail = "The MÁVPlusz realtime service could not be reached."
                });
        }
        catch (System.Text.Json.JsonException exception)
        {
            _logger.LogError(
                exception,
                "Invalid realtime response for trip {TripId}.",
                tripId);

            return StatusCode(
                StatusCodes.Status502BadGateway,
                new ProblemDetails
                {
                    Title = "Invalid realtime response",
                    Detail = "The realtime provider returned an unexpected response."
                });
        }
    }
}