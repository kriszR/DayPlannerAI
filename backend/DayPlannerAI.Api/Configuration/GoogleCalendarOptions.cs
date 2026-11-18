using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DayPlannerAI.Api.Configuration
{
    public class GoogleCalendarOptions
    {
        public const string SectionName = "GoogleCalendar";

        public string ClientId { get; set; } = string.Empty;

        public string ClientSecret { get; set; } = string.Empty;

        public string RedirectUri { get; set; } = string.Empty;
    }
}