import { useState } from "react";
import { Bus, RefreshCw, AlertCircle } from "lucide-react";

import type { CalendarEvent } from "../api/calendarApi";
import { type GtfsJourney, type GtfsDirectRoute } from "../api/aiApi";
import { searchBusJourneys } from "../api/travelApi";

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

  if (event.isAllDay) {
    return null;
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
