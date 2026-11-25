import { useState } from "react";
import { Bus, RefreshCw, AlertCircle } from "lucide-react";

import type { CalendarEvent } from "../api/calendarApi";
import { type GtfsJourney, type GtfsDirectRoute } from "../api/aiApi";
import { searchBusJourneys } from "../api/travelApi";
import { isOnlineCalendarEvent } from "../utils/calendarEventUtils";

import TravelRouteCards from "./TravelRouteCards";
import "./EventTravelPlanner.css";

interface EventTravelPlannerProps {
  event: CalendarEvent;
  departureLocation: string | null;
}

interface TravelSearchResult {
  answer: string;
  routes: GtfsDirectRoute[];
  journeys: GtfsJourney[];
}

interface DepartureRecommendation {
  departureTime: string;
  departureStop: string;
  arrivalTime: string;
  arrivalStop: string;
  durationMinutes: number;
  transferCount: number;
  arrivalMarginMinutes: number;
  isDirect: boolean;
}

function getServiceDateTime(serviceDate: string, time: string): Date | null {
  const dateParts = serviceDate.split("-").map(Number);
  const timeMatch = time.match(/^(\d{1,2}):(\d{2})(?::(\d{2}))?$/);

  if (dateParts.length !== 3 || !timeMatch || dateParts.some(Number.isNaN)) {
    return null;
  }

  const [year, month, day] = dateParts;
  const hours = Number(timeMatch[1]);
  const minutes = Number(timeMatch[2]);
  const seconds = Number(timeMatch[3] ?? 0);

  if (minutes > 59 || seconds > 59) {
    return null;
  }

  // GTFS times can exceed 24:00 for trips continuing after midnight.
  const date = new Date(year, month - 1, day, 0, 0, 0, 0);
  date.setSeconds(hours * 3600 + minutes * 60 + seconds);
  return Number.isNaN(date.getTime()) ? null : date;
}

function getDepartureRecommendation(
  result: TravelSearchResult,
  eventStart: Date | null,
): DepartureRecommendation | null {
  if (!eventStart || Number.isNaN(eventStart.getTime())) {
    return null;
  }

  const options: Array<{
    departure: Date;
    arrival: Date;
    departureTime: string;
    departureStop: string;
    arrivalTime: string;
    arrivalStop: string;
    durationMinutes: number;
    transferCount: number;
    isDirect: boolean;
  }> = [];

  for (const route of result.routes) {
    const departure = getServiceDateTime(route.serviceDate, route.departureTime);
    const arrival = getServiceDateTime(route.serviceDate, route.arrivalTime);

    if (departure && arrival && departure < eventStart && arrival < eventStart) {
      options.push({
        departure,
        arrival,
        departureTime: route.departureTime,
        departureStop: route.departureStop,
        arrivalTime: route.arrivalTime,
        arrivalStop: route.arrivalStop,
        durationMinutes: route.durationMinutes,
        transferCount: 0,
        isDirect: true,
      });
    }
  }

  for (const journey of result.journeys) {
    const departure = getServiceDateTime(journey.serviceDate, journey.departureTime);
    const arrival = getServiceDateTime(journey.serviceDate, journey.arrivalTime);

    if (departure && arrival && departure < eventStart && arrival < eventStart) {
      options.push({
        departure,
        arrival,
        departureTime: journey.departureTime,
        departureStop: journey.departureStop,
        arrivalTime: journey.arrivalTime,
        arrivalStop: journey.arrivalStop,
        durationMinutes: journey.totalDurationMinutes,
        transferCount: journey.transferCount,
        isDirect: false,
      });
    }
  }

  if (options.length === 0) {
    return null;
  }

  // Prefer the latest departure that still leaves at least 10 minutes before the event.
  const withBuffer = options.filter(
    (option) => eventStart.getTime() - option.arrival.getTime() >= 10 * 60_000,
  );
  const candidates = withBuffer.length > 0 ? withBuffer : options;
  candidates.sort((a, b) => b.departure.getTime() - a.departure.getTime());

  const chosen = candidates[0];
  return {
    departureTime: chosen.departureTime,
    departureStop: chosen.departureStop,
    arrivalTime: chosen.arrivalTime,
    arrivalStop: chosen.arrivalStop,
    durationMinutes: chosen.durationMinutes,
    transferCount: chosen.transferCount,
    arrivalMarginMinutes: Math.floor(
      (eventStart.getTime() - chosen.arrival.getTime()) / 60_000,
    ),
    isDirect: chosen.isDirect,
  };
}
function getDestinationCity(location: string): string {
  const normalized = location.trim();

  // Google Calendar locations can contain both city and street address.
  // Example: "Veszprém, Egyetem utca 4"
  const commaIndex = normalized.indexOf(",");

  if (commaIndex === -1) {
    return normalized;
  }

  return normalized.substring(0, commaIndex).trim();
}
function getLocalDateTime(value: string) {
  const date = new Date(value);

  if (Number.isNaN(date.getTime())) {
    return null;
  }

  const pad = (number: number) => String(number).padStart(2, "0");

  return {
    date: `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`,
    time: `${pad(date.getHours())}:${pad(date.getMinutes())}`,
  };
}

export default function EventTravelPlanner({
  event,
  departureLocation,
}: EventTravelPlannerProps) {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<TravelSearchResult | null>(null);

  const eventDateTime =
    event.start && !event.isAllDay ? getLocalDateTime(event.start) : null;

  const eventStartDate = event.start && !event.isAllDay ? new Date(event.start) : null;
  const canSearch =
    Boolean(event.location?.trim()) &&
    Boolean(departureLocation?.trim()) &&
    Boolean(eventDateTime);

  const searchRoutes = async () => {
    if (!canSearch || !event.location || !departureLocation || !eventDateTime) {
      return;
    }

    setLoading(true);
    setError(null);
    setResult(null);

    try {
      const destinationCity = getDestinationCity(event.location);

      if (!destinationCity) {
        throw new Error("Nem sikerült meghatározni a céltelepülést.");
      }

      const response = await searchBusJourneys({
        departureLocation,
        destination: destinationCity,
        travelDate: eventDateTime.date,
        searchMode: "arriveBy",
        arrivalTime: eventDateTime.time,
      });

      setResult({
        answer:
          response.resultCount === 0
            ? "Nem található megfelelő járat az érkezési határidő előtt."
            : "",
        routes: response.availableRoutes ?? [],
        journeys: response.availableJourneys ?? [],
      });
    } catch (err) {
      console.error("Event travel search failed:", err);

      setError(
        err instanceof Error
          ? err.message
          : "Nem sikerült buszjáratokat keresni.",
      );
    } finally {
      setLoading(false);
    }
  };

  const recommendation = result
    ? getDepartureRecommendation(result, eventStartDate)
    : null;

  if (event.isAllDay) {
    return null;
  }

  if (isOnlineCalendarEvent(event)) {
    return (
      <div className="event-travel-note">
        <AlertCircle size={15} />
        Online esemény – nincs szükség buszútvonal-keresésre.
      </div>
    );
  }

  if (!event.location?.trim()) {
    return (
      <div className="event-travel-note">
        <MapPinIcon />
        Az eseményhez nincs helyszín megadva, ezért útvonal nem tervezhető.
      </div>
    );
  }

  if (!departureLocation?.trim()) {
    return (
      <div className="event-travel-note">
        <AlertCircle size={15} />
        Az indulási hely nincs beállítva a felhasználói profilban.
      </div>
    );
  }

  if (!eventDateTime) {
    return (
      <div className="event-travel-note">
        Nem sikerült meghatározni az esemény időpontját.
      </div>
    );
  }

  return (
    <div className="event-travel-planner">
      <button
        type="button"
        className="event-travel-planner__button"
        onClick={() => void searchRoutes()}
        disabled={loading}
      >
        {loading ? (
          <RefreshCw size={16} className="weather-spin" />
        ) : (
          <Bus size={16} />
        )}

        {loading ? "Járatok keresése..." : "Buszjáratok keresése"}
      </button>

      {loading && (
        <p className="event-travel-planner__status">
          A DayPlannerAI megkeresi az eseményhez megfelelő járatokat. Ez
          eltarthat néhány másodpercig.
        </p>
      )}

      {error && (
        <div className="event-travel-planner__error">
          <AlertCircle size={16} />
          {error}
        </div>
      )}

      {result && (
        <div className="event-travel-planner__result">
          {recommendation && (
            <div className="event-travel-planner__recommendation">
              <div className="event-travel-planner__recommendation-heading">
                <strong>Ajánlott járat</strong>
                <span>
                  {recommendation.isDirect
                    ? "Közvetlen járat"
                    : `${recommendation.transferCount} átszállás`}
                </span>
              </div>
              <div className="event-travel-planner__recommendation-times">
                <div>
                  <small>Indulás a megállóból</small>
                  <strong>{recommendation.departureTime}</strong>
                  <span>{recommendation.departureStop}</span>
                </div>
                <div>
                  <small>Érkezés</small>
                  <strong>{recommendation.arrivalTime}</strong>
                  <span>{recommendation.arrivalStop}</span>
                </div>
              </div>
              <p>
                {recommendation.durationMinutes} perc utazás · {recommendation.arrivalMarginMinutes} perc az eseményig.
                {recommendation.arrivalMarginMinutes < 10
                  ? " Kevés érkezési tartalék – érdemes korábbi járatot választani."
                  : " A kiválasztás legalább 10 perc érkezési tartalékot részesít előnyben."}
              </p>
              <p className="event-travel-planner__recommendation-note">
                Ez a megállóból induló járat ideje; az otthon és a megálló közötti gyaloglási időt még nem számolja.
              </p>
            </div>
          )}
          {result.routes.length > 0 && (
            <TravelRouteCards routes={result.routes} />
          )}

          {result.journeys.length > 0 && (
            <div className="event-travel-planner__journeys">
              <h4>Átszállásos lehetőségek</h4>

              {result.journeys.map((journey, journeyIndex) => (
                <article
                  className="event-travel-planner__journey"
                  key={`${journey.departureTime}-${journey.arrivalTime}-${journeyIndex}`}
                >
                  <div className="event-travel-planner__journey-header">
                    <strong>
                      {journey.departureTime} – {journey.arrivalTime}
                    </strong>

                    <span>
                      {journey.totalDurationMinutes} perc ·{" "}
                      {journey.transferCount} átszállás
                    </span>
                  </div>

                  {journey.legs.map((leg, legIndex) => (
                    <div
                      className="event-travel-planner__leg"
                      key={`${leg.tripId}-${legIndex}`}
                    >
                      <strong>
                        {leg.routeShortName ?? leg.routeLongName ?? "Buszjárat"}
                      </strong>

                      <span>
                        {leg.departureTime} · {leg.departureStop}
                      </span>

                      <span>
                        {leg.arrivalTime} · {leg.arrivalStop}
                      </span>
                    </div>
                  ))}
                </article>
              ))}
            </div>
          )}

          {result.routes.length === 0 && result.journeys.length === 0 && (
            <div className="event-travel-planner__answer">{result.answer}</div>
          )}
        </div>
      )}
    </div>
  );
}

function MapPinIcon() {
  return <AlertCircle size={15} />;
}
