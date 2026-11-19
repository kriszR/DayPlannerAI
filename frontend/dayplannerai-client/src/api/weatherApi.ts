
import { apiGet } from "./http";

export interface CurrentWeather {
    time: string;
    temperature: number;
    apparentTemperature: number;
    relativeHumidity: number;
    precipitation: number;
    weatherCode: number;
    windSpeed: number;
    isDay: boolean;
}

export interface DailyWeather {
    date: string;
    weatherCode: number;
    temperatureMax: number;
    temperatureMin: number;
    precipitationProbabilityMax: number | null;
    sunrise: string | null;
    sunset: string | null;
}

export interface WeatherForecast {
    location: string;
    timezone: string;
    current: CurrentWeather;
    daily: DailyWeather[];
}

export async function getWeatherForecast(
    days: number = 7
): Promise<WeatherForecast> {
    return apiGet<WeatherForecast>(`/weather?days=${days}`);
}
