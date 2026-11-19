import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  Activity,
  CalendarDays,
  Clock,
  MapPin,
  Sparkles,
  Droplets,
  Wind,
  Thermometer,
  Sunrise,
  Sunset,
  RefreshCw,
  ExternalLink,
} from "lucide-react";

import { checkApiHealth, getProfile } from "../api/profileApi";
import { getWeatherForecast } from "../api/weatherApi";
import { getCalendarEvents } from "../api/calendarApi";
import type { CalendarEvent } from "../api/calendarApi";
import {
  formatWeatherDate,
  getClothingAdvice,
  getWeatherDescription,
  getWeatherEmoji,
  getCurrentWeatherEmoji,
} from "../utils/weatherUtils";
import WorldClock from "../components/WorldClock";

function Dashboard() {
  const profileQuery = useQuery({
    queryKey: ["profile"],
    queryFn: getProfile,
  });

  const healthQuery = useQuery({
    queryKey: ["api-health"],
    queryFn: checkApiHealth,
  });

  const weatherQuery = useQuery({
    queryKey: ["weather", profileQuery.data?.homeLocation],
    queryFn: () => getWeatherForecast(7),
    enabled: Boolean(profileQuery.data?.homeLocation),
    retry: 1,
    staleTime: 15 * 60 * 1000,
  });

  const calendarQuery = useQuery({
    queryKey: ["calendar-events", 7],
    queryFn: () => getCalendarEvents(7),
    retry: 1,
    staleTime: 60 * 1000,
  });

  const today = new Intl.DateTimeFormat("hu-HU", {
    weekday: "long",
    year: "numeric",
    month: "long",
    day: "numeric",
  }).format(new Date());

  const getLocalDateKey = (date: Date): string => {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, "0");
    const day = String(date.getDate()).padStart(2, "0");

    return `${year}-${month}-${day}`;
  };

  const formatEventTime = (event: CalendarEvent): string => {
    if (!event.start) {
      return "Időpont nélkül";
    }

    if (event.isAllDay) {
      return "Egész nap";
    }

    return new Intl.DateTimeFormat("hu-HU", {
      hour: "2-digit",
      minute: "2-digit",
    }).format(new Date(event.start));
  };

  const todayKey = getLocalDateKey(new Date());

  const todayEvents = useMemo(() => {
    return (calendarQuery.data ?? [])
      .filter((event) => {
        if (!event.start) {
          return false;
        }

        if (event.isAllDay) {
          return event.start === todayKey;
        }

        return getLocalDateKey(new Date(event.start)) === todayKey;
      })
      .sort((a, b) => {
        return new Date(a.start!).getTime() - new Date(b.start!).getTime();
      });
  }, [calendarQuery.data, todayKey]);

  const nextEvent = todayEvents[0];

  const currentWeather = weatherQuery.data?.current;
  const todayForecast = weatherQuery.data?.daily[0];

  return (
    <div className="page">
      <div className="page-heading">
        <div>
          <span className="page-eyebrow">{today}</span>

          <h1>
            {profileQuery.data
              ? `Üdvözöllek, ${profileQuery.data.displayName}!`
              : "Üdvözöllek a DayPlannerAI-ban!"}
          </h1>

          <p>Tervezzük meg együtt a mai napodat!</p>
        </div>

        <div className="system-status">
          <span
            className={`status-dot ${
              healthQuery.isSuccess ? "online" : "offline"
            }`}
          />
          {healthQuery.isLoading
            ? "Kapcsolódás..."
            : healthQuery.isSuccess
              ? "Minden rendszer működik"
              : "A szerver nem elérhető"}
        </div>
      </div>

      <section className="welcome-panel">
        <div>
          <div className="panel-label">
            <Sparkles size={16} />
            SZEMÉLYI AI-ASSZISZTENSED
          </div>

          <h2>A napod, intelligensen megszervezve.</h2>

          <p>
            A személyi asszisztensed segít a napi programod, utazásaid és
            teendőid megtervezésében.
          </p>
        </div>

        <div className="welcome-decoration">
          <Sparkles size={64} />
        </div>
      </section>

      <WorldClock />

      <section className="section">
        <div className="section-heading">
          <h2>Időjárás</h2>
          <span>
            {weatherQuery.data
              ? weatherQuery.data.location
              : "Helyi előrejelzés"}
          </span>
        </div>

        {!profileQuery.isLoading && !profileQuery.data?.homeLocation && (
          <div className="weather-message">
            <MapPin size={22} />
            <div>
              <strong>Nincs beállítva település</strong>
              <p>
                Az időjárás megjelenítéséhez add meg az otthoni településedet a
                Beállításokban.
              </p>
            </div>
          </div>
        )}

        {profileQuery.data?.homeLocation && weatherQuery.isLoading && (
          <div className="weather-message">
            <RefreshCw size={22} className="weather-spin" />
            <div>
              <strong>Időjárási adatok betöltése</strong>
              <p>Kapcsolódás az Open-Meteo szolgáltatáshoz...</p>
            </div>
          </div>
        )}

        {weatherQuery.isError && (
          <div className="weather-message weather-error">
            <Thermometer size={22} />
            <div>
              <strong>Nem sikerült betölteni az időjárást</strong>
              <p>
                {weatherQuery.error instanceof Error
                  ? weatherQuery.error.message
                  : "Ismeretlen hiba történt."}
              </p>
              <button
                className="weather-retry-button"
                onClick={() => weatherQuery.refetch()}
                type="button"
              >
                Újrapróbálás
              </button>
            </div>
          </div>
        )}

        {currentWeather && todayForecast && (
          <div className="weather-dashboard">
            <div className="weather-current-card">
              <div className="weather-current-main">
                <div>
                  <span className="weather-location">
                    {weatherQuery.data?.location}
                  </span>

                  <div className="weather-update-info">
                    Adatok időpontja:{" "}
                    {new Intl.DateTimeFormat("hu-HU", {
                      hour: "2-digit",
                      minute: "2-digit",
                    }).format(new Date(currentWeather.time))}
                  </div>
                  <div className="weather-temperature">
                    {Math.round(currentWeather.temperature)}°
                  </div>
                  <strong>
                    {getWeatherDescription(currentWeather.weatherCode)}
                  </strong>
                </div>

                <span className="weather-main-emoji">
                  {getCurrentWeatherEmoji(
                    currentWeather.weatherCode,
                    currentWeather.isDay,
                  )}
                </span>
              </div>

              <div className="weather-feels-like">
                Hőérzet: {Math.round(currentWeather.apparentTemperature)} °C
              </div>

              <div className="weather-details">
                <div>
                  <Thermometer size={18} />
                  <span>
                    {Math.round(todayForecast.temperatureMin)}° /{" "}
                    {Math.round(todayForecast.temperatureMax)}°
                  </span>
                  <small>Min. / Max.</small>
                </div>

                <div>
                  <Droplets size={18} />
                  <span>{currentWeather.relativeHumidity}%</span>
                  <small>Páratartalom</small>
                </div>

                <div>
                  <Wind size={18} />
                  <span>{Math.round(currentWeather.windSpeed)} km/h</span>
                  <small>Szélsebesség</small>
                </div>

                <div>
                  <Droplets size={18} />
                  <span>
                    {todayForecast.precipitationProbabilityMax !== null
                      ? `${todayForecast.precipitationProbabilityMax}%`
                      : "N/A"}
                  </span>
                  <small>Eső esélye</small>
                </div>
              </div>

              <div className="weather-advice">
                <Sparkles size={18} />
                <div>
                  <strong>Öltözködési javaslat</strong>
                  <p>
                    {getClothingAdvice(
                      todayForecast.temperatureMax,
                      todayForecast.precipitationProbabilityMax,
                    )}
                  </p>
                </div>
              </div>
            </div>

            <div className="weather-forecast-card">
              <h3>7 napos előrejelzés</h3>

              <div className="weather-forecast-list">
                {weatherQuery.data?.daily.map((day, index) => (
                  <div className="weather-forecast-day" key={day.date}>
                    <span className="forecast-date">
                      {index === 0 ? "Ma" : formatWeatherDate(day.date)}
                    </span>

                    <span className="forecast-icon">
                      {getWeatherEmoji(day.weatherCode)}
                    </span>

                    <span className="forecast-description">
                      {getWeatherDescription(day.weatherCode)}
                    </span>

                    <span className="forecast-precipitation">
                      <Droplets size={14} />
                      {day.precipitationProbabilityMax !== null
                        ? `${day.precipitationProbabilityMax}%`
                        : "--"}
                    </span>

                    <span className="forecast-temperature">
                      <strong>{Math.round(day.temperatureMax)}°</strong>
                      <span>{Math.round(day.temperatureMin)}°</span>
                    </span>
                  </div>
                ))}
              </div>
            </div>

            <div className="weather-sunrise-card">
              <div>
                <Sunrise size={20} />
                <span>
                  Napkelte:{" "}
                  {todayForecast.sunrise
                    ? new Intl.DateTimeFormat("hu-HU", {
                        hour: "2-digit",
                        minute: "2-digit",
                      }).format(new Date(todayForecast.sunrise))
                    : "--:--"}
                </span>
              </div>

              <div>
                <Sunset size={20} />
                <span>
                  Napnyugta:{" "}
                  {todayForecast.sunset
                    ? new Intl.DateTimeFormat("hu-HU", {
                        hour: "2-digit",
                        minute: "2-digit",
                      }).format(new Date(todayForecast.sunset))
                    : "--:--"}
                </span>
              </div>
            </div>
          </div>
        )}
      </section>

      <section className="section">
        <div className="section-heading">
          <h2>Mai programok</h2>
          <span>
            {calendarQuery.data
              ? `${todayEvents.length} esemény`
              : "Google Naptár"}
          </span>
        </div>

        {calendarQuery.isLoading && (
          <div className="calendar-message">
            <RefreshCw size={20} className="weather-spin" />
            <span>Naptáresemények betöltése...</span>
          </div>
        )}

        {calendarQuery.isError && (
          <div className="calendar-message calendar-error">
            <CalendarDays size={20} />
            <div>
              <strong>Nem sikerült betölteni a naptárat.</strong>
              <p>
                {calendarQuery.error instanceof Error
                  ? calendarQuery.error.message
                  : "Ismeretlen hiba történt."}
              </p>
              <button
                className="weather-retry-button"
                type="button"
                onClick={() => calendarQuery.refetch()}
              >
                Újrapróbálás
              </button>
            </div>
          </div>
        )}

        {calendarQuery.isSuccess && todayEvents.length === 0 && (
          <div className="calendar-message">
            <CalendarDays size={22} />
            <div>
              <strong>Ma nincs betervezett programod.</strong>
              <p>A Google Naptárban szereplő mai események itt jelennek meg.</p>
            </div>
          </div>
        )}

        {todayEvents.length > 0 && (
          <div className="calendar-event-list">
            {todayEvents.map((event) => (
              <article className="calendar-event-card" key={event.id}>
                <div className="calendar-event-time">
                  <Clock size={18} />
                  <strong>{formatEventTime(event)}</strong>
                </div>

                <div className="calendar-event-content">
                  <h3>{event.summary || "Névtelen esemény"}</h3>

                  {event.location && (
                    <span className="calendar-event-location">
                      <MapPin size={14} />
                      {event.location}
                    </span>
                  )}
                </div>

                {event.htmlLink && (
                  <a
                    href={event.htmlLink}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="calendar-event-link"
                    aria-label="Esemény megnyitása a Google Naptárban"
                  >
                    <ExternalLink size={18} />
                  </a>
                )}
              </article>
            ))}
          </div>
        )}
      </section>

      <section className="section">
        <div className="section-heading">
          <h2>Napi áttekintés</h2>
          <span>A mai nap legfontosabb információi</span>
        </div>

        <div className="overview-grid">
          <article className="overview-card">
            <div className="card-icon purple">
              <CalendarDays size={21} />
            </div>

            <h3>Mai események</h3>
            <strong>
              {calendarQuery.isLoading ? "--" : todayEvents.length}
            </strong>
            <span>
              {calendarQuery.isError
                ? "A naptár nem elérhető"
                : "Mai naptáresemények"}
            </span>
          </article>
          <article className="overview-card">
            <div className="card-icon blue">
              <Clock size={21} />
            </div>

            <h3>Következő esemény</h3>
            <strong>
              {calendarQuery.isLoading
                ? "--:--"
                : nextEvent
                  ? formatEventTime(nextEvent)
                  : "--:--"}
            </strong>
            <span>
              {calendarQuery.isError
                ? "A naptár nem elérhető"
                : nextEvent?.summary || "Nincs több mai esemény"}
            </span>
          </article>

          <article className="overview-card">
            <div className="card-icon orange">
              <MapPin size={21} />
            </div>

            <h3>Kiindulási hely</h3>
            <strong>
              {profileQuery.data?.homeLocation ?? "Nincs beállítva"}
            </strong>
            <span>A helyszínt a Beállításokban adhatod meg</span>
          </article>

          <article className="overview-card">
            <div className="card-icon green">
              <Activity size={21} />
            </div>

            <h3>Rendszerállapot</h3>
            <strong>{healthQuery.isSuccess ? "Online" : "Offline"}</strong>
            <span>
              {healthQuery.isSuccess
                ? "A szerverkapcsolat aktív"
                : "A szerverkapcsolat nem elérhető"}
            </span>
          </article>
        </div>
      </section>

      <section className="section">
        <div className="section-heading">
          <h2>Első lépések</h2>
          <span>Az alkalmazás beállítása</span>
        </div>

        <div className="setup-card">
          <div className="setup-item">
            <span className="setup-check">✓</span>
            <div>
              <strong>Backend csatlakoztatva</strong>
              <p>Az ASP.NET Core API működik.</p>
            </div>
          </div>

          <div className="setup-item">
            <span className="setup-check">✓</span>
            <div>
              <strong>Adatbázis csatlakoztatva</strong>
              <p>A PostgreSQL-adatok elérhetők.</p>
            </div>
          </div>

          <div className="setup-item pending">
            <span className="setup-pending">3</span>
            <div>
              <strong>Google Naptár összekapcsolása</strong>
              <p>A naptáresemények szinkronizálása.</p>
            </div>
          </div>

          <div className="setup-item pending">
            <span className="setup-pending">4</span>
            <div>
              <strong>AI-asszisztens konfigurálása</strong>
              <p>Intelligens napi tervezés engedélyezése.</p>
            </div>
          </div>
        </div>
      </section>
    </div>
  );
}

export default Dashboard;
