import { useState } from "react";
import type { GtfsJourney } from "../api/aiApi";
import VehicleTracking from "./VehicleTracking";
import "./TravelJourneyCards.css";

interface TravelJourneyCardsProps {
  journeys: GtfsJourney[];
}

function getWaitingMinutes(arrivalTime: string, departureTime: string): number {
  const toSeconds = (value: string) => {
    const parts = value.split(":").map(Number);

    if (parts.length < 2 || parts.some(Number.isNaN)) {
      return 0;
    }

    return parts[0] * 3600 + parts[1] * 60 + (parts[2] ?? 0);
  };

  return Math.max(
    0,
    Math.floor((toSeconds(departureTime) - toSeconds(arrivalTime)) / 60),
  );
}

export default function TravelJourneyCards({
  journeys,
}: TravelJourneyCardsProps) {
  const [trackingTripId, setTrackingTripId] = useState<string | null>(null);

  if (journeys.length === 0) {
    return null;
  }

  return (
    <section className="travel-journeys">
      <div className="travel-journeys__header">
        <h3>Átszállásos útvonalak</h3>
        <span>{journeys.length} lehetőség</span>
      </div>

      <div className="travel-journeys__list">
        {journeys.map((journey, journeyIndex) => (
          <article
            className="travel-journey-card"
            key={`${journey.serviceDate}-${journey.legs.map((x) => x.tripId).join("-")}-${journeyIndex}`}
          >
            <div className="travel-journey-card__header">
              <div>
                <span className="travel-journey-card__label">
                  Indulás – érkezés
                </span>
                <strong>
                  {journey.departureTime} → {journey.arrivalTime}
                </strong>
              </div>

              <div className="travel-journey-card__badges">
                <span>{journey.transferCount} átszállás</span>
                <span>{journey.totalDurationMinutes} perc</span>
              </div>
            </div>

            <div className="travel-journey-card__legs">
              {journey.legs.map((leg, legIndex) => (
                <div key={`${leg.tripId}-${legIndex}`}>
                  <div className="travel-journey-leg">
                    <div className="travel-journey-leg__icon">
                      <span>BUSZ</span>
                    </div>

                    <div className="travel-journey-leg__content">
                      <strong>
                        {leg.routeShortName ?? leg.routeLongName ?? "Buszjárat"}
                      </strong>

                      {leg.tripHeadsign && <small>{leg.tripHeadsign}</small>}

                      <div className="travel-journey-leg__times">
                        <div>
                          <strong>{leg.departureTime}</strong>
                          <span>{leg.departureStop}</span>
                          {leg.departurePlatform && (
                            <small>Kocsiállás: {leg.departurePlatform}</small>
                          )}
                        </div>

                        <div>
                          <strong>{leg.arrivalTime}</strong>
                          <span>{leg.arrivalStop}</span>
                          {leg.arrivalPlatform && (
                            <small>Kocsiállás: {leg.arrivalPlatform}</small>
                          )}
                        </div>
                      </div>

                      <small>Menetidő: {leg.durationMinutes} perc</small>
                      {leg.isInProgress && (
                        <>
                          <small className="travel-journey-leg__live-status">
                            Menetrend szerint jelenleg úton van.
                          </small>
                          <button
                            className="travel-journey-leg__tracking-button"
                            onClick={() =>
                              setTrackingTripId(
                                trackingTripId === leg.tripId
                                  ? null
                                  : leg.tripId,
                              )
                            }
                            type="button"
                          >
                            {trackingTripId === leg.tripId
                              ? "Követés bezárása"
                              : "Élő GPS és késések"}
                          </button>
                          {trackingTripId === leg.tripId && (
                            <VehicleTracking
                              tripId={leg.tripId}
                              serviceDate={journey.serviceDate}
                            />
                          )}
                        </>
                      )}
                    </div>
                  </div>

                  {legIndex < journey.legs.length - 1 && (
                    <div className="travel-journey-transfer">
                      <span className="travel-journey-transfer__line" />
                      <div>
                        <strong>Átszállás</strong>
                        <span>
                          {leg.arrivalStop}
                          {" · "}
                          {getWaitingMinutes(
                            leg.arrivalTime,
                            journey.legs[legIndex + 1].departureTime,
                          )}{" "}
                          perc várakozás
                        </span>
                      </div>
                    </div>
                  )}
                </div>
              ))}
            </div>
          </article>
        ))}
      </div>
    </section>
  );
}
