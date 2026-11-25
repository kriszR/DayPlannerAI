import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import ReactMarkdown from "react-markdown";
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
  Bell,
} from "lucide-react";

import { checkApiHealth, getProfile } from "../api/profileApi";
import { getClothingAdvice, getWeatherForecast } from "../api/weatherApi";
import { getCalendarEvents } from "../api/calendarApi";
import type { CalendarEvent } from "../api/calendarApi";
import { searchBusJourneys } from "../api/travelApi";
import type { BusSearchResponse } from "../api/travelApi";
import {
  formatWeatherDate,
  getWeatherDescription,
  getWeatherEmoji,
  getCurrentWeatherEmoji,
} from "../utils/weatherUtils";
import WorldClock from "../components/WorldClock";
import { generateDailyPlan } from "../api/dailyPlanApi";
import DailyTimeline from "../components/DailyTimeline";
import { isOnlineCalendarEvent } from "../utils/calendarEventUtils";
import {
  getDestinationCity,
  getServiceDateTime,
} from "../utils/travelTimeUtils";

function getBoardingStopArrival(
  response: BusSearchResponse,
  eventStart: Date,
  now: Date,
): Date | null {
  const options: Array<{
    boardingArrival: Date;
    departure: Date;
    destinationArrival: Date;
  }> = [];

  for (const route of response.availableRoutes) {
    const boardingArrival = getServiceDateTime(
      route.serviceDate,
      route.departureStopArrivalTime,
    );
    const departure = getServiceDateTime(route.serviceDate, route.departureTime);
    const destinationArrival = getServiceDateTime(
      route.serviceDate,
      route.arrivalTime,
    );

    if (
      boardingArrival &&
      departure &&
      destinationArrival &&
      boardingArrival > now &&
      destinationArrival < eventStart
    ) {
      options.push({ boardingArrival, departure, destinationArrival });
    }
  }

  for (const journey of response.availableJourneys) {
    const firstLeg = journey.legs[0];
    if (!firstLeg) {
      continue;
    }

    const boardingArrival = getServiceDateTime(
      journey.serviceDate,
      firstLeg.departureStopArrivalTime,
    );
    const departure = getServiceDateTime(
      journey.serviceDate,
      firstLeg.departureTime,
    );
    const destinationArrival = getServiceDateTime(
      journey.serviceDate,
      journey.arrivalTime,
    );

    if (
      boardingArrival &&
      departure &&
      destinationArrival &&
      boardingArrival > now &&
      destinationArrival < eventStart
    ) {
      options.push({ boardingArrival, departure, destinationArrival });
    }
  }

  if (options.length === 0) {
    return null;
  }

  const withBuffer = options.filter(
    (option) => eventStart.getTime() - option.destinationArrival.getTime() >= 10 * 60_000,
  );
  const candidates = withBuffer.length > 0 ? withBuffer : options;
  candidates.sort((a, b) => b.departure.getTime() - a.departure.getTime());

  return candidates[0].boardingArrival;
}

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

  const [currentTime, setCurrentTime] = useState(() => new Date());
  const [notificationPermission, setNotificationPermission] = useState<
    NotificationPermission | "unsupported"
  >(
    typeof window !== "undefined" && "Notification" in window
      ? Notification.permission
      : "unsupported",
  );
  const [notificationMessage, setNotificationMessage] = useState("");

  const enableNotifications = async () => {
    if (!("Notification" in window)) {
      setNotificationPermission("unsupported");
      setNotificationMessage("Ez a böngésző nem támogatja az értesítéseket.");
      return;
    }

    try {
      const permission = await Notification.requestPermission();
      setNotificationPermission(permission);
      setNotificationMessage(
        permission === "granted"
          ? "Értesítések engedélyezve. Az események előtt 5 perccel jelzek."
          : permission === "denied"
            ? "Az értesítések le vannak tiltva a böngészőben."
            : "Az értesítési engedély nem lett megadva.",
      );
    } catch {
      setNotificationMessage("Nem sikerült bekapcsolni az értesítéseket.");
    }
  };

  useEffect(() => {
    const intervalId = window.setInterval(() => {
      setCurrentTime(new Date());
    }, 30_000);

    return () => window.clearInterval(intervalId);
  }, []);

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

  const dailyPlanQuery = useQuery({
    queryKey: [
      "daily-plan",
      todayKey,
      profileQuery.data?.preparationMinutes,
      profileQuery.data?.homeLocation,
    ],
    queryFn: ({ signal }) => generateDailyPlan(todayKey, signal),
    enabled: profileQuery.isSuccess,
    staleTime: 10 * 60 * 1000,
    retry: false,
    refetchOnWindowFocus: false,
  });

  const clothingAdviceQuery = useQuery({
    queryKey: [
      "clothing-advice",
      weatherQuery.data?.location,
      weatherQuery.data?.current.time,
    ],
    queryFn: () => {
      if (!weatherQuery.data) {
        throw new Error("Az időjárási adatok nem érhetők el.");
      }

      return getClothingAdvice(weatherQuery.data);
    },
    enabled: Boolean(
      weatherQuery.data &&
        dailyPlanQuery.isFetched &&
        !dailyPlanQuery.isFetching,
    ),
    staleTime: 15 * 60 * 1000,
    retry: false,
  });

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

  const preparationMinutes = profileQuery.data?.preparationMinutes ?? 30;

  const nextEvent = todayEvents.find((event) => {
    if (event.isAllDay || !event.start) {
      return false;
    }

    return new Date(event.start).getTime() > currentTime.getTime();
  });

  const nextEventIsOnline = nextEvent
    ? isOnlineCalendarEvent(nextEvent)
    : false;
  const nextEventStart = nextEvent?.start
    ? new Date(nextEvent.start)
    : null;
  const nextEventDestination = nextEvent?.location
    ? getDestinationCity(nextEvent.location)
    : undefined;
  const preparationBusQuery = useQuery({
    queryKey: [
      "preparation-bus-search",
      nextEvent?.id,
      nextEvent?.start,
      profileQuery.data?.homeLocation,
      nextEventDestination,
    ],
    queryFn: () => {
      if (
        !nextEventStart ||
        !profileQuery.data?.homeLocation ||
        !nextEventDestination
      ) {
        throw new Error("Az esemény buszkereséséhez hiányzik egy szükséges adat.");
      }

      const pad = (value: number) => String(value).padStart(2, "0");

      return searchBusJourneys({
        departureLocation: profileQuery.data.homeLocation,
        destination: nextEventDestination,
        travelDate: getLocalDateKey(nextEventStart),
        searchMode: "arriveBy",
        arrivalTime: `${pad(nextEventStart.getHours())}:${pad(nextEventStart.getMinutes())}`,
        includeInProgress: false,
      });
    },
    enabled: Boolean(
      nextEventStart &&
        !nextEventIsOnline &&
        profileQuery.data?.homeLocation &&
        nextEventDestination,
    ),
    staleTime: 5 * 60 * 1000,
    retry: false,
    refetchOnWindowFocus: false,
  });

  const boardingStopArrival =
    nextEventStart && preparationBusQuery.data
      ? getBoardingStopArrival(
          preparationBusQuery.data,
          nextEventStart,
          currentTime,
        )
      : null;
  const preparationStart =
    boardingStopArrival && !nextEventIsOnline
      ? new Date(
          boardingStopArrival.getTime() - preparationMinutes * 60_000,
        )
      : null;

  const notificationTime = nextEvent?.start
    ? new Date(new Date(nextEvent.start).getTime() - 5 * 60_000)
    : null;
  const notificationTimeText = notificationTime
    ? new Intl.DateTimeFormat("hu-HU", {
        hour: "2-digit",
        minute: "2-digit",
      }).format(notificationTime)
    : null;

  const preparationStartTime = preparationStart
    ? new Intl.DateTimeFormat("hu-HU", {
        hour: "2-digit",
        minute: "2-digit",
      }).format(preparationStart)
    : null;

  const minutesUntilPreparation = preparationStart
    ? Math.ceil((preparationStart.getTime() - currentTime.getTime()) / 60_000)
    : null;

  const preparationDisplayTime = calendarQuery.isLoading
    ? "--:--"
    : !nextEvent
      ? "--:--"
      : nextEventIsOnline
        ? "Online"
        : preparationBusQuery.isLoading
          ? "..."
          : !preparationStartTime
            ? "--:--"
            : minutesUntilPreparation !== null && minutesUntilPreparation <= 0
              ? "Most"
              : preparationStartTime;

  const preparationDescription = (() => {
    if (calendarQuery.isError) {
      return "A naptár nem elérhető";
    }

    if (!nextEvent) {
      return "Nincs további időzített esemény";
    }

    if (nextEventIsOnline) {
      return `Online esemény – nincs készülődési idő. Értesítés: ${notificationTimeText ?? "--:--"}`;
    }

    if (preparationBusQuery.isLoading) {
      return "Buszjárat keresése a felszálló megálló érkezési idejéhez";
    }

    if (preparationBusQuery.isError) {
      return "A buszkeresés sikertelen; a megállóba érkezési idő nem ismert";
    }

    if (!boardingStopArrival) {
      return profileQuery.data?.homeLocation && nextEventDestination
        ? "Nem található megfelelő, még felszállható buszjárat"
        : "Add meg az indulási helyet és az esemény helyszínét";
    }

    if (minutesUntilPreparation !== null && minutesUntilPreparation <= 0) {
      return `A busz felszálló megállóba érkezéséig ${preparationMinutes} perc`;
    }

    return `${minutesUntilPreparation} perc múlva kezdj készülődni`;
  })();

  useEffect(() => {
    if (notificationPermission !== "granted" || !todayEvents.length) {
      return;
    }

    const checkNotifications = () => {
      const now = Date.now();

      for (const event of todayEvents) {
        if (!event.start || event.isAllDay) continue;

        const eventStart = new Date(event.start).getTime();
        if (Number.isNaN(eventStart)) continue;

        const notifyAt = eventStart - 5 * 60_000;
        if (now < notifyAt || now >= eventStart) continue;

        const notificationKey = `dayplannerai-notification-${getLocalDateKey(new Date(eventStart))}-${event.id}`;
        try {
          if (window.localStorage.getItem(notificationKey)) continue;
          new Notification(event.summary || "Közelgő naptáresemény", {
            body: `${new Intl.DateTimeFormat("hu-HU", {
              hour: "2-digit",
              minute: "2-digit",
            }).format(new Date(eventStart))} – 5 perc múlva kezdődik.`,
            icon: "/favicon.ico",
          });
          window.localStorage.setItem(notificationKey, "sent");
        } catch (error) {
          console.error("Could not show calendar notification:", error);
        }
      }
    };

    checkNotifications();
    const intervalId = window.setInterval(checkNotifications, 15_000);
    return () => window.clearInterval(intervalId);
  }, [notificationPermission, todayEvents]);

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
          <div>
            <h2>Mai napi terv</h2>
            <span>DayPlannerAI személyre szabott napirendje</span>
          </div>

          <button
            className="weather-retry-button"
            type="button"
            onClick={() => dailyPlanQuery.refetch()}
            disabled={
              dailyPlanQuery.isFetching || clothingAdviceQuery.isFetching
            }
          >
            <RefreshCw
              size={16}
              className={dailyPlanQuery.isFetching ? "weather-spin" : ""}
            />
            {dailyPlanQuery.isFetching ? "Tervezés..." : "Újratervezés"}
          </button>
        </div>

        {dailyPlanQuery.isLoading && (
          <div className="weather-message">
            <RefreshCw size={22} className="weather-spin" />
            <div>
              <strong>A DayPlannerAI összeállítja a napodat</strong>
              <p>
                Naptár, időjárás és felhasználói profil lekérdezése... Ez a
                helyi AI miatt eltarthat egy ideig.
              </p>
            </div>
          </div>
        )}

        {dailyPlanQuery.isError && (
          <div className="weather-message weather-error">
            <Sparkles size={22} />
            <div>
              <strong>Nem sikerült elkészíteni a napi tervet</strong>
              <p>
                {dailyPlanQuery.error instanceof Error
                  ? dailyPlanQuery.error.message
                  : "Ismeretlen hiba történt."}
              </p>
            </div>
          </div>
        )}

        {dailyPlanQuery.data && (
          <div className="setup-card daily-plan-card">
            <div className="daily-plan-content">
              <ReactMarkdown>
                {dailyPlanQuery.data}
              </ReactMarkdown>
            </div>
          </div>
        )}
      </section>

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
                    {clothingAdviceQuery.isPending
                      ? dailyPlanQuery.isFetching ||
                        !dailyPlanQuery.isFetched
                        ? "A napi terv készül; az öltözködési javaslat ezután indul."
                        : "Az AI az időjárási adatok alapján javaslatot készít..."
                      : clothingAdviceQuery.isError
                        ? "Nem sikerült AI-javaslatot készíteni az időjárási adatokból."
                        : clothingAdviceQuery.data}
                  </p>
                  {clothingAdviceQuery.isError && (
                    <button
                      className="weather-retry-button"
                      onClick={() => clothingAdviceQuery.refetch()}
                      disabled={dailyPlanQuery.isFetching}
                      type="button"
                    >
                      Újrapróbálás
                    </button>
                  )}
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

      <DailyTimeline
        events={todayEvents}
        preparationMinutes={preparationMinutes}
        departureLocation={profileQuery.data?.homeLocation ?? null}
        preparationEventId={nextEvent?.id ?? null}
        preparationEnd={boardingStopArrival}
      />

      <section className="section">
        <div className="section-heading">
          <h2>Napi áttekintés</h2>
          <span>A mai nap legfontosabb információi</span>
        </div>

        <div className="overview-grid">
          <article className="overview-card">
            <div className="card-icon blue">
              <Bell size={21} />
            </div>
            <h3>Eseményértesítések</h3>
            <strong>
              {notificationPermission === "granted"
                ? "Bekapcsolva"
                : notificationPermission === "denied"
                  ? "Letiltva"
                  : notificationPermission === "unsupported"
                    ? "Nem támogatott"
                    : "Kikapcsolva"}
            </strong>
            <button
              className="weather-retry-button"
              type="button"
              onClick={enableNotifications}
              disabled={
                notificationPermission === "granted" ||
                notificationPermission === "unsupported"
              }
            >
              {notificationPermission === "granted"
                ? "Engedélyezve"
                : "Értesítések engedélyezése"}
            </button>
            <br />
            <span>
              {notificationMessage ||
                "Böngészőértesítés 5 perccel az esemény előtt."}
            </span>
          </article>

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
              <Clock size={21} />
            </div>

            <h3>Készülődés kezdete</h3>
            <strong>{preparationDisplayTime}</strong>
            <span>{preparationDescription}</span>
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
    </div>
  );
}

export default Dashboard;
