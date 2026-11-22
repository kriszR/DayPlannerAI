using DayPlannerAI.Api.DTOs;
using DayPlannerAI.Api.Services.Agent;
using Microsoft.AspNetCore.Mvc;

namespace DayPlannerAI.Api.Controllers;

[ApiController]
[Route("api/agent")]
public sealed class AgentController : ControllerBase
{
    private readonly OllamaAgentService _agentService;
    private readonly ILogger<AgentController> _logger;

    public AgentController(
        OllamaAgentService agentService,
        ILogger<AgentController> logger)
    {
        _agentService = agentService;
        _logger = logger;
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat(
        [FromBody] AgentChatRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(request.TripId) &&
    request.ServiceDate is null)
            {
                return BadRequest(new
                {
                    error = "Service date is required when a trip is selected."
                });
            }

            if (string.IsNullOrWhiteSpace(request.TripId) &&
                request.ServiceDate is not null)
            {
                return BadRequest(new
                {
                    error = "Trip ID is required when a service date is provided."
                });
            }

            string? selectedTripContext = null;

            if (!string.IsNullOrWhiteSpace(request.TripId))
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(
                        request.TripId,
                        @"^[a-zA-Z0-9:_-]+$"))
                {
                    return BadRequest(new
                    {
                        error = "Invalid trip ID format."
                    });
                }

                selectedTripContext =
                    $"GTFS trip ID: {request.TripId}\n" +
                    $"Service date: {request.ServiceDate.Value:yyyy-MM-dd}";
            }

            var conversation = new List<AiChatMessageRequest>
                {
                    new()
                    {
                        Role = "user",
                        Content = request.Message
                    }
                };

            var result = await _agentService.ChatAsync(
                conversation,
                selectedTripContext,
                cancellationToken);

            return Ok(new
            {
                answer = result.Answer
            });
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "Agent request failed.");

            return StatusCode(
                StatusCodes.Status502BadGateway,
                new
                {
                    error = "The AI service is currently unavailable."
                });
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError(
                exception,
                "Agent response processing failed.");

            return StatusCode(
                StatusCodes.Status502BadGateway,
                new
                {
                    error = "The AI response could not be processed."
                });
        }
    }
}