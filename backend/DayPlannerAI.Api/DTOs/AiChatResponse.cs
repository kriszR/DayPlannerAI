using DayPlannerAI.Api.Models;

namespace DayPlannerAI.Api.DTOs;

public class AiChatResponse
{
    public string Answer { get; set; } = string.Empty;

    public string ConversationId { get; set; } = string.Empty;

    public TravelPlan? TravelPlan { get; set; }
}