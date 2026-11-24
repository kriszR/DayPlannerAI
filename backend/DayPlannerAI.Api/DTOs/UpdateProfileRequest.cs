using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace DayPlannerAI.Api.DTOs
{
    public class UpdateProfileRequest
    {
        [Required]
        [StringLength(100, MinimumLength = 2)]
        public string DisplayName { get; set; } = string.Empty;

        [StringLength(200)]
        public string? HomeLocation { get; set; }

        [Required]
        [StringLength(100)]
        public string TimeZone { get; set; } = "Europe/Budapest";

        [Range(0, 180)]
        public int? PreparationMinutes { get; set; }
    }
}