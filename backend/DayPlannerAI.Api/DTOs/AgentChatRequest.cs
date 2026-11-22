using System.ComponentModel.DataAnnotations;

namespace DayPlannerAI.Api.DTOs;

public sealed class AgentChatRequest
{
    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public string Message { get; set; } = string.Empty;

    [StringLength(200)]
    public string? TripId { get; set; }

    public DateOnly? ServiceDate { get; set; }
}