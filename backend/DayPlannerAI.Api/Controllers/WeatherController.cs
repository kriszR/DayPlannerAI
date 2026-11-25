
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
    private readonly OllamaChatService _ollamaChatService;

    public WeatherController(
        WeatherService weatherService,
        OllamaChatService ollamaChatService,
        ILogger<WeatherController> logger)
    {
        _weatherService = weatherService;
        _ollamaChatService = ollamaChatService;
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

    [HttpPost("clothing-advice")]
    public async Task<IActionResult> GetClothingAdvice(
        [FromBody] WeatherForecastDto? forecast,
        CancellationToken cancellationToken)
    {
        if (forecast is null ||
            forecast.Current is null ||
            forecast.Daily is null ||
            forecast.Daily.Count == 0)
        {
            return BadRequest(new
            {
                Message = "Az aktuális időjárás és a napi előrejelzés megadása kötelező."
            });
        }

        try
        {
            var advice = await _ollamaChatService.GenerateClothingAdviceAsync(
                forecast,
                cancellationToken);

            return Ok(new { advice });
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "Clothing advice generation failed.");

            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                Message = "Az AI-szolgáltatás jelenleg nem érhető el."
            });
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError(
                exception,
                "Clothing advice generation returned no usable response.");

            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                Message = "Az AI nem tudott öltözködési javaslatot készíteni."
            });
        }
        catch (TaskCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                exception,
                "Clothing advice generation timed out.");

            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                Message = "Az AI-javaslat készítése túllépte az időkorlátot."
            });
        }
    }
}
