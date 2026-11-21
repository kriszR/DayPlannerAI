using System.Text.Json;
using DayPlannerAI.Api.DTOs;
using DayPlannerAI.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DayPlannerAI.Api.Controllers;

[ApiController]
[Route("api/ai")]
public class AiController : ControllerBase
{
    private readonly OllamaChatService _chatService;
    private readonly ILogger<AiController> _logger;

    private readonly ConversationManager _conversationManager;
    private readonly ConversationStore _conversationStore;

    public AiController(
        OllamaChatService chatService,
        ILogger<AiController> logger,
        ConversationManager conversationManager,
        ConversationStore conversationStore)
    {
        _chatService = chatService;
        _logger = logger;
        _conversationManager = conversationManager;
        _conversationStore = conversationStore;
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
            if (_conversationManager.IsTravelConversation(conversation))
            {
                var extraction = await _chatService.ExtractTravelPlanAsync(
                    conversation,
                    cancellationToken);

                var previousPlan = _conversationStore.GetTravelPlan(
                    conversationId);

                var travelPlan = _conversationManager.BuildTravelPlan(
                    previousPlan,
                    extraction,
                    conversation);

                _conversationStore.SaveTravelPlan(
                    conversationId,
                    travelPlan);

                var missingInformation =
                    _conversationManager.GetMissingTravelInformation(travelPlan);

                var latestUserMessage = conversation
                    .Last(message => message.Role == "user")
                    .Content;

                var travelAnswer = await _chatService.GenerateTravelResponseAsync(
                    travelPlan,
                    missingInformation,
                    latestUserMessage,
                    cancellationToken);

                return Ok(new AiChatResponse
                {
                    Answer = travelAnswer,
                    ConversationId = conversationId,
                    TravelPlan = travelPlan
                });
            }

            var answer = await _chatService.SendMessageAsync(
                conversation,
                cancellationToken);

            return Ok(new AiChatResponse
            {
                Answer = answer,
                ConversationId = conversationId
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