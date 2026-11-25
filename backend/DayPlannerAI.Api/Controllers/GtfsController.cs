using DayPlannerAI.Api.DTOs;
using DayPlannerAI.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace DayPlannerAI.Api.Controllers;

[ApiController]
[Route("api/gtfs")]
public sealed class GtfsController : ControllerBase
{
    private readonly GtfsImportService _importService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<GtfsController> _logger;

    private readonly GtfsQueryService _queryService;

    private readonly GtfsRouteSearchService _routeSearchService;

    public GtfsController(
        GtfsImportService importService,
        IWebHostEnvironment environment,
        ILogger<GtfsController> logger,
        GtfsQueryService queryService,
        GtfsRouteSearchService routeSearchService)
    {
        _importService = importService;
        _environment = environment;
        _logger = logger;
        _queryService = queryService;
        _routeSearchService = routeSearchService;
    }

    [HttpPost("import")]
    public async Task<ActionResult<GtfsImportResponse>> Import(
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var zipPath = Path.Combine(
            _environment.ContentRootPath,
            "Data",
            "Gtfs",
            "volanbusz_gtfs.zip");

        if (!System.IO.File.Exists(zipPath))
        {
            return BadRequest(new
            {
                message = "A GTFS ZIP fájl nem található.",
                path = zipPath
            });
        }

        try
        {
            var result = await _importService.ImportAsync(
                zipPath,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "GTFS import endpoint failed.");

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    message = "A menetrendi adatok importálása nem sikerült.",
                    details = exception.Message
                });
        }
    }

    [HttpGet("stops")]
    public async Task<ActionResult<List<GtfsStopSearchResultDto>>> SearchStops(
    [FromQuery] string query,
    [FromQuery] int limit = 30,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            return BadRequest(new
            {
                message = "Legalább két karakteres keresési kifejezés szükséges."
            });
        }

        var results = await _queryService.SearchStopsAsync(
            query,
            limit,
            cancellationToken);

        return Ok(results);
    }

    [HttpGet("status")]
    public async Task<ActionResult<GtfsFeedStatusDto>> GetStatus(
        CancellationToken cancellationToken)
    {
        var result = await _queryService.GetFeedStatusAsync(
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("direct-routes")]
    public async Task<ActionResult<List<GtfsDirectRouteDto>>> SearchDirectRoutes(
    [FromQuery] string from,
    [FromQuery] string to,
    [FromQuery] DateOnly date,
    [FromQuery] TimeOnly arriveBy,
    [FromQuery] int limit = 5,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(from) ||
            string.IsNullOrWhiteSpace(to))
        {
            return BadRequest(new
            {
                message = "Az indulási és célállomás megadása kötelező."
            });
        }

        if (limit is < 1 or > 10)
        {
            return BadRequest(new
            {
                message = "A találatok száma 1 és 10 között lehet."
            });
        }

        try
        {
            var results = await _routeSearchService.SearchDirectRoutesAsync(
                from,
                to,
                date,
                arriveBy,
                limit: limit,
                includeInProgress: true,
                cancellationToken: cancellationToken);

            return Ok(results);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new
            {
                message = exception.Message
            });
        }
        catch (InvalidOperationException exception)
        {
            return NotFound(new
            {
                message = exception.Message
            });
        }
    }
}