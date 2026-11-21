using System.ComponentModel.DataAnnotations;

namespace DayPlannerAI.Api.DTOs;

public class AiChatMessageRequest
{
    [Required]
    [RegularExpression("^(user|assistant)$")]
    public string Role { get; set; } = string.Empty;

    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public string Content { get; set; } = string.Empty;
}