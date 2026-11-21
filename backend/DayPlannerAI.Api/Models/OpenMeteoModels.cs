
using System.Text.Json.Serialization;

namespace DayPlannerAI.Api.Models;

public class GeocodingResponse
{
    [JsonPropertyName("results")]
    public List<GeocodingResult>? Results { get; set; }
}

public class GeocodingResult
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }
}

public class OpenMeteoForecastResponse
{
    [JsonPropertyName("timezone")]
    public string Timezone { get; set; } = string.Empty;

    [JsonPropertyName("current")]
    public OpenMeteoCurrent? Current { get; set; }

    [JsonPropertyName("daily")]
    public OpenMeteoDaily? Daily { get; set; }
}

public class OpenMeteoCurrent
{
    [JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;

    [JsonPropertyName("temperature_2m")]
    public double Temperature { get; set; }

    [JsonPropertyName("apparent_temperature")]
    public double ApparentTemperature { get; set; }

    [JsonPropertyName("relative_humidity_2m")]
    public int RelativeHumidity { get; set; }

    [JsonPropertyName("precipitation")]
    public double Precipitation { get; set; }

    [JsonPropertyName("weather_code")]
    public int WeatherCode { get; set; }

    [JsonPropertyName("wind_speed_10m")]
    public double WindSpeed { get; set; }

    [JsonPropertyName("is_day")]
    public int IsDay { get; set; }
}

public class OpenMeteoDaily
{
    [JsonPropertyName("time")]
    public List<string> Time { get; set; } = [];

    [JsonPropertyName("weather_code")]
    public List<int> WeatherCode { get; set; } = [];

    [JsonPropertyName("temperature_2m_max")]
    public List<double> TemperatureMax { get; set; } = [];

    [JsonPropertyName("temperature_2m_min")]
    public List<double> TemperatureMin { get; set; } = [];

    [JsonPropertyName("precipitation_probability_max")]
    public List<int?> PrecipitationProbabilityMax { get; set; } = [];

    [JsonPropertyName("sunrise")]
    public List<string> Sunrise { get; set; } = [];

    [JsonPropertyName("sunset")]
    public List<string> Sunset { get; set; } = [];
}
