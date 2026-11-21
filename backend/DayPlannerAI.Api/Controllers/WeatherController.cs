
using DayPlannerAI.Api.DTOs;
using DayPlannerAI.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace DayPlannerAI.Api.Controllers;

[ApiController]
[Route("api/weather")]
public class WeatherController : ControllerBase
{
    private readonly WeatherService _weatherService;
    private readonly ILogger<WeatherController> _logger;

    public WeatherController(
        WeatherService weatherService,
        ILogger<WeatherController> logger)
    {
        _weatherService = weatherService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<WeatherForecastDto>> GetForecast(
        [FromQuery] int days = 7,
        CancellationToken cancellationToken = default)
    {
        if (days < 1 || days > 16)
        {
            return BadRequest(new
            {
                Message = "Az előrejelzés hossza 1 és 16 nap között lehet."
            });
        }

        try
        {
            var forecast = await _weatherService.GetForecastAsync(
                days,
                cancellationToken);

            return Ok(forecast);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new
            {
                Message = exception.Message
            });
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "Weather API request failed.");

            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                Message = "Az időjárási szolgáltatás jelenleg nem elérhető."
            });
        }
    }
}
