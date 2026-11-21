using System.ComponentModel.DataAnnotations;

namespace DayPlannerAI.Api.DTOs;

public class AiChatRequest
{
    [StringLength(64)]
    public string? ConversationId { get; set; }

    [StringLength(2000, MinimumLength = 1)]
    public string? Message { get; set; }


    [MaxLength(12)]
    public List<AiChatMessageRequest> Messages { get; set; } = [];
}