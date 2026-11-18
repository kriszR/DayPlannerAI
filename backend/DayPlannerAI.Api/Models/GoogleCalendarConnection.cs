
namespace DayPlannerAI.Api.Models;

public class GoogleCalendarConnection
{
    public int Id { get; set; }

    public string ProtectedRefreshToken { get; set; } = string.Empty;

    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
