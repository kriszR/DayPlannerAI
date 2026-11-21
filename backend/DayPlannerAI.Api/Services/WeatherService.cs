
using System.Net.Http.Json;

using DayPlannerAI.Api.Data;
using DayPlannerAI.Api.DTOs;
using DayPlannerAI.Api.Models;

using Microsoft.EntityFrameworkCore;

namespace DayPlannerAI.Api.Services;

public class WeatherService
{
    private readonly ApplicationDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;

    public WeatherService(
        ApplicationDbContext context,
        IHttpClientFactory httpClientFactory)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<WeatherForecastDto> GetForecastAsync(
        int days,
        CancellationToken cancellationToken = default)
    {
        var profile = await _context.UserProfiles
            .FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);

        if (profile == null)
        {
            throw new InvalidOperationException(
                "A felhasználói profil nem található.");
        }

        if (string.IsNullOrWhiteSpace(profile.HomeLocation))
        {
            throw new InvalidOperationException(
                "Először állítsd be az otthoni településedet a Beállítások oldalon.");
        }

        var location = await GetCoordinatesAsync(
            profile.HomeLocation,
            cancellationToken);

        var forecast = await GetWeatherDataAsync(
            location.Latitude,
            location.Longitude,
            "auto",
            days,
            cancellationToken);

        if (forecast.Current == null || forecast.Daily == null)
        {
            throw new HttpRequestException(
                "Az időjárási szolgáltatás hiányos adatokat adott vissza.");
        }

        var current = forecast.Current;
        var daily = forecast.Daily;

        var dailyForecasts = Enumerable.Range(0, daily.Time.Count)
            .Select(index => new DailyWeatherDto
            {
                Date = daily.Time[index],
                WeatherCode = daily.WeatherCode[index],
                TemperatureMax = daily.TemperatureMax[index],
                TemperatureMin = daily.TemperatureMin[index],
                PrecipitationProbabilityMax =
                    GetValueOrDefault(daily.PrecipitationProbabilityMax, index),
                Sunrise = GetValueOrDefault(daily.Sunrise, index),
                Sunset = GetValueOrDefault(daily.Sunset, index)
            })
            .ToList();

        return new WeatherForecastDto
        {
            Location = location.Name,
            Timezone = forecast.Timezone,
            Current = new CurrentWeatherDto
            {
                Time = current.Time,
                Temperature = current.Temperature,
                ApparentTemperature = current.ApparentTemperature,
                RelativeHumidity = current.RelativeHumidity,
                Precipitation = current.Precipitation,
                WeatherCode = current.WeatherCode,
                WindSpeed = current.WindSpeed,
                IsDay = current.IsDay == 1
            },
            Daily = dailyForecasts
        };
    }

    private async Task<GeocodingResult> GetCoordinatesAsync(
        string locationName,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();

        var encodedLocation = Uri.EscapeDataString(locationName);

        var url =
            "https://geocoding-api.open-meteo.com/v1/search" +
            $"?name={encodedLocation}" +
            "&count=1" +
            "&language=hu" +
            "&format=json";

        var response = await client.GetFromJsonAsync<GeocodingResponse>(
            url,
            cancellationToken);

        var result = response?.Results?.FirstOrDefault();

        if (result == null)
        {
            throw new InvalidOperationException(
                $"A megadott település nem található: {locationName}");
        }

        return result;
    }


    private async Task<OpenMeteoForecastResponse> GetWeatherDataAsync(
        double latitude,
        double longitude,
        string timezone,
        int days,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();

        var url =
            "https://api.open-meteo.com/v1/forecast" +
            $"?latitude={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            $"&longitude={longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
            "&current=temperature_2m,apparent_temperature,relative_humidity_2m,precipitation,weather_code,wind_speed_10m,is_day" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,sunrise,sunset" +
            $"&forecast_days={days}" +
            "&temperature_unit=celsius" +
            "&wind_speed_unit=kmh" +
            $"&timezone={Uri.EscapeDataString(timezone)}";

        using var response = await client.GetAsync(
            url,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Open-Meteo returned {(int)response.StatusCode} " +
                $"{response.StatusCode}. Response: {responseBody}");
        }

        var forecast =
            System.Text.Json.JsonSerializer.Deserialize<OpenMeteoForecastResponse>(
                responseBody,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        return forecast ?? throw new HttpRequestException(
            "Open-Meteo returned an empty response.");
    }


    private static T? GetValueOrDefault<T>(
        IReadOnlyList<T> values,
        int index)
    {
        return index >= 0 && index < values.Count
            ? values[index]
            : default;
    }
}
