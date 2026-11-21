using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DayPlannerAI.Api.Configuration
{
    public class OllamaOptions
    {
        public string BaseUrl { get; set; } = "http://localhost:11434";

        public string Model { get; set; } = "qwen3:4b";
    }
}