import { useState } from "react";
import type { GtfsDirectRoute } from "../api/aiApi";
import "./TravelRouteCards.css";
import VehicleTracking from "./VehicleTracking";

interface TravelRouteCardsProps {
  routes: GtfsDirectRoute[];
}

export default function TravelRouteCards({ routes }: TravelRouteCardsProps) {
  const [trackingTripId, setTrackingTripId] = useState<string | null>(null);
  
  if (routes.length === 0) {
    return null;
  }

  return (
    <section className="travel-routes">
      <div className="travel-routes__header">
        <h3>Talált közvetlen buszjáratok</h3>
        <span>{routes.length} járat</span>
      </div>

      <div className="travel-routes__list">
        {routes.map((route) => (
          <article className="travel-route-card" key={route.tripId}>
            <div className="travel-route-card__header">
              <div>
                <span className="travel-route-card__label">Járatszám</span>
                <h4>
                  {route.routeShortName ??
                    route.routeLongName ??
                    "Ismeretlen járat"}
                </h4>
              </div>

              {route.tripHeadsign && (
                <span className="travel-route-card__headsign">
                  {route.tripHeadsign}
                </span>
              )}
            </div>

            <div className="travel-route-card__journey">
              <div className="travel-route-card__stop">
                <strong>{route.departureTime}</strong>
                <span>{route.departureStop}</span>

                {route.departurePlatform && (
                  <small>Kocsiállás: {route.departurePlatform}</small>
                )}
              </div>

              <div className="travel-route-card__duration">
                <span>{route.durationMinutes} perc</span>
                <div className="travel-route-card__line">
                  <span />
                </div>
              </div>

              <div className="travel-route-card__stop travel-route-card__stop--arrival">
                <strong>{route.arrivalTime}</strong>
                <span>{route.arrivalStop}</span>

                {route.arrivalPlatform && (
                  <small>Kocsiállás: {route.arrivalPlatform}</small>
                )}
              </div>
            </div>
            {route.isInProgress && (
              <p className="travel-route-card__live-status">
                Menetrend szerint jelenleg úton van. A GPS-adatokat az élő
                követés gombbal nézheted meg.
              </p>
            )}
            <button
              type="button"
              className="travel-route-card__tracking-button"
              onClick={() =>
                setTrackingTripId(
                  trackingTripId === route.tripId ? null : route.tripId,
                )
              }
            >
              {trackingTripId === route.tripId
                ? "Követés bezárása"
                : "Élő GPS és késések"}
            </button>

            {trackingTripId === route.tripId && (
              <VehicleTracking
                tripId={route.tripId}
                serviceDate={route.serviceDate}
              />
            )}
          </article>
        ))}
      </div>
    </section>
  );
}
