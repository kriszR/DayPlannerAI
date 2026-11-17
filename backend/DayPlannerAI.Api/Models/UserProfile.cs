using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DayPlannerAI.Api.Models
{
    public class UserProfile
    {
        public int Id { get; set; }

        public string DisplayName { get; set; } = string.Empty;

        public string? HomeLocation { get; set; }

        public string TimeZone { get; set; } = "Europe/Budapest";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}