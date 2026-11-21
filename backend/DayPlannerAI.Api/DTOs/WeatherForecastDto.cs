
namespace DayPlannerAI.Api.DTOs;

public class WeatherForecastDto
{
    public string Location { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public CurrentWeatherDto Current { get; set; } = new();
    public List<DailyWeatherDto> Daily { get; set; } = [];
}

public class CurrentWeatherDto
{
    public string Time { get; set; } = string.Empty;
    public double Temperature { get; set; }
    public double ApparentTemperature { get; set; }
    public int RelativeHumidity { get; set; }
    public double Precipitation { get; set; }
    public int WeatherCode { get; set; }
    public double WindSpeed { get; set; }
    public bool IsDay { get; set; }
}

public class DailyWeatherDto
{
    public string Date { get; set; } = string.Empty;
    public int WeatherCode { get; set; }
    public double TemperatureMax { get; set; }
    public double TemperatureMin { get; set; }
    public int? PrecipitationProbabilityMax { get; set; }
    public string? Sunrise { get; set; }
    public string? Sunset { get; set; }
}
