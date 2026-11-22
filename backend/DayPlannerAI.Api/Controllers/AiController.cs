using System.Text.Json;
using DayPlannerAI.Api.DTOs;
using DayPlannerAI.Api.Models;
using DayPlannerAI.Api.Services;
using DayPlannerAI.Api.Services.Agent;
using Microsoft.AspNetCore.Mvc;

namespace DayPlannerAI.Api.Controllers;

[ApiController]
[Route("api/ai")]
public class AiController : ControllerBase
{
    private readonly ILogger<AiController> _logger;
    private readonly ConversationStore _conversationStore;
    private readonly OllamaAgentService _agentService;

    public AiController(    
        ILogger<AiController> logger,
        ConversationStore conversationStore,
        OllamaAgentService agentService)
    {
        _logger = logger;
        _conversationStore = conversationStore;
        _agentService = agentService;
    }

    [HttpPost("chat")]
    public async Task<ActionResult<AiChatResponse>> Chat(
     [FromBody] AiChatRequest request,
     CancellationToken cancellationToken)
    {
        var conversation = request.Messages is { Count: > 0 } suppliedMessages
            ? suppliedMessages
            : !string.IsNullOrWhiteSpace(request.Message)
                ? new List<AiChatMessageRequest>
                {
                new()
                {
                    Role = "user",
                    Content = request.Message
                }
                }
                : [];

        if (conversation.Count == 0)
        {
            return BadRequest(new
            {
                message = "A beszélgetés nem lehet üres."
            });
        }

        if (!string.IsNullOrWhiteSpace(request.ConversationId) &&
            !Guid.TryParse(request.ConversationId, out _))
        {
            return BadRequest(new
            {
                message = "Érvénytelen beszélgetésazonosító."
            });
        }

        var conversationId = Guid.TryParse(
            request.ConversationId,
            out var parsedId)
                ? parsedId.ToString("N")
                : Guid.NewGuid().ToString("N");

        if (conversation.Count > 12)
        {
            return BadRequest(new
            {
                message = "Legfeljebb 12 üzenet küldhető egyszerre."
            });
        }

        if (conversation.Any(message =>
            message.Role is not ("user" or "assistant") ||
            string.IsNullOrWhiteSpace(message.Content) ||
            message.Content.Length > 2000))
        {
            return BadRequest(new
            {
                message = "A beszélgetés egyik üzenete érvénytelen."
            });
        }

        if (conversation[^1].Role != "user")
        {
            return BadRequest(new
            {
                message = "A beszélgetés utolsó üzenetének felhasználói üzenetnek kell lennie."
            });
        }

        try
        {
            var agentResult = await _agentService.ChatAsync(
            conversation,
            cancellationToken: cancellationToken);

            TravelPlan? travelPlan = null;

            var availableRoutes = new List<GtfsDirectRouteDto>();
            var availableJourneys = new List<GtfsJourneyDto>();

            // Extract structured travel data from the executed search tool.
            var searchResult = agentResult.ToolResults
                .LastOrDefault(x => x.ToolName == "search_bus_journeys");

            if (searchResult is not null)
            {
                using var document = JsonDocument.Parse(
                    searchResult.ResultJson);

                var root = document.RootElement;

                if (root.TryGetProperty("success", out var success) &&
                    success.ValueKind == JsonValueKind.True)
                {
                    var options = new JsonSerializerOptions(
                        JsonSerializerDefaults.Web);

                    if (root.TryGetProperty("travelPlan", out var planElement))
                    {
                        travelPlan = planElement.Deserialize<TravelPlan>(options);
                    }

                    if (root.TryGetProperty("availableRoutes", out var routesElement))
                    {
                        availableRoutes =
                            routesElement.Deserialize<List<GtfsDirectRouteDto>>(options)
                            ?? [];
                    }

                    if (root.TryGetProperty("availableJourneys", out var journeysElement))
                    {
                        availableJourneys =
                            journeysElement.Deserialize<List<GtfsJourneyDto>>(options)
                            ?? [];
                    }
                }
            }

            var response = new AiChatResponse
            {
                Answer = agentResult.Answer,
                ConversationId = conversationId,
                TravelPlan = travelPlan,
                AvailableRoutes = availableRoutes,
                AvailableJourneys = availableJourneys
            };

            return Ok(response);
        }
        catch (ArgumentException exception)
        {
            _logger.LogWarning(
                exception,
                "Invalid travel search parameters.");

            return BadRequest(new
            {
                message = exception.Message
            });
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(
                exception,
                "Travel search could not be completed.");

            return UnprocessableEntity(new
            {
                message = exception.Message
            });
        }
        catch (JsonException exception)
        {
            _logger.LogError(
                exception,
                "Failed to parse the structured travel plan.");

            return StatusCode(
                StatusCodes.Status502BadGateway,
                new
                {
                    message = "Az utazási adatok feldolgozása nem sikerült."
                });
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "Failed to communicate with the local Ollama service.");

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    message = "A helyi AI-szolgáltatás jelenleg nem érhető el."
                });
        }
        catch (TaskCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                exception,
                "The local AI service request timed out.");

            return StatusCode(
                StatusCodes.Status504GatewayTimeout,
                new
                {
                    message = "Az AI-válasz generálása túllépte az időkorlátot."
                });
        }
    }

    [HttpDelete("conversation/{conversationId}")]
    public IActionResult ClearConversation(string conversationId)
    {
        if (!Guid.TryParse(conversationId, out var parsedId))
        {
            return BadRequest(new
            {
                message = "Érvénytelen beszélgetésazonosító."
            });
        }

        _conversationStore.Clear(parsedId.ToString("N"));

        return NoContent();
    }
}