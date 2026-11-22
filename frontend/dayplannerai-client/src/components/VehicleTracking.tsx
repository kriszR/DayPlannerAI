import { useCallback, useEffect, useState } from "react";
import {
  MapContainer,
  TileLayer,
  CircleMarker,
  Popup,
  useMap,
} from "react-leaflet";

import { getTripRealtime, type RealtimeTrip } from "../api/realtimeApi";

import "leaflet/dist/leaflet.css";
import "./VehicleTracking.css";
import { askAgentAboutTrip } from "../api/agentApi";

interface VehicleTrackingProps {
  tripId: string;
  serviceDate: string;
}

function RecenterMap({
  latitude,
  longitude,
}: {
  latitude: number;
  longitude: number;
}) {
  const map = useMap();

  useEffect(() => {
    map.setView([latitude, longitude]);
  }, [map, latitude, longitude]);

  return null;
}

function formatTimestamp(value: string): string {
  return new Date(value).toLocaleTimeString("hu-HU", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}

function formatStopDelay(seconds: number | null): string {
  if (seconds === null) return "Nincs valós idejű adat";

  if (seconds === 0) return "Menetrend szerint";

  const minutes = Math.abs(seconds) / 60;

  const delay =
    minutes < 1 ? "kevesebb mint 1 perc" : `${Math.round(minutes)} perc`;

  return seconds > 0 ? `${delay} késés` : `${delay} korábbi érkezés`;
}

export default function VehicleTracking({
  tripId,
  serviceDate,
}: VehicleTrackingProps) {
  const [agentAnswer, setAgentAnswer] = useState<string | null>(null);
  const [agentLoading, setAgentLoading] = useState(false);
  const [agentError, setAgentError] = useState<string | null>(null);
  const [data, setData] = useState<RealtimeTrip | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lastRequest, setLastRequest] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    try {
      const result = await getTripRealtime(tripId, serviceDate);

      setData(result);
      setError(null);
      setLastRequest(new Date().toISOString());
    } catch (err) {
      console.error("Realtime tracking error:", err);
      setError("A valós idejű adatok jelenleg nem érhetők el.");
    } finally {
      setLoading(false);
    }
  }, [tripId, serviceDate]);

  useEffect(() => {
    let active = true;

    const refresh = async () => {
      try {
        const result = await getTripRealtime(tripId, serviceDate);

        if (!active) return;

        setData(result);
        setError(null);
        setLastRequest(new Date().toISOString());
      } catch (err) {
        console.error("Realtime tracking error:", err);

        if (active) {
          setError("A valós idejű adatok jelenleg nem érhetők el.");
        }
      } finally {
        if (active) {
          setLoading(false);
        }
      }
    };

    void refresh();

    const intervalId = window.setInterval(() => {
      void refresh();
    }, 60000);

    return () => {
      active = false;
      window.clearInterval(intervalId);
    };
  }, [tripId, serviceDate]);

  if (loading && !data) {
    return (
      <div className="vehicle-tracking__status">
        Valós idejű adatok betöltése...
      </div>
    );
  }

  if (!data && error) {
    return <div className="vehicle-tracking__error">{error}</div>;
  }

  if (!data) return null;

  const vehicle = data.vehiclePosition;
  const handleAskAgent = async () => {
    setAgentLoading(true);
    setAgentError(null);
    setAgentAnswer(null);

    try {
      const response = await askAgentAboutTrip(
        "Hol jár most ez a busz? Foglald össze a GPS-helyzetét, sebességét és a megállók késéseit.",
        tripId,
        serviceDate,
      );

      setAgentAnswer(response.answer);
    } catch (error) {
      console.error("Agent request failed:", error);
      setAgentError("Az AI-összefoglaló jelenleg nem érhető el.");
    } finally {
      setAgentLoading(false);
    }
  };

  return (
    <section className="vehicle-tracking">
      <div className="vehicle-tracking__header">
        <div>
          <h3>Élő járműkövetés</h3>
          <span>
            {data.routeShortName} · {vehicle?.vehicleId ?? "Járműadat nélkül"}
          </span>
        </div>

        <button
          type="button"
          onClick={() => void loadData()}
          disabled={loading}
        >
          Frissítés
        </button>
      </div>

      {error && (
        <div className="vehicle-tracking__warning">
          {error} A korábban lekért adatokat továbbra is mutatjuk.
        </div>
      )}

      {vehicle?.isFresh ? (
        <>
          <div className="vehicle-tracking__map">
            <MapContainer
              center={[vehicle.latitude, vehicle.longitude]}
              zoom={15}
              scrollWheelZoom
              className="vehicle-tracking__leaflet"
            >
              <TileLayer
                attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
                url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
              />

              <RecenterMap
                latitude={vehicle.latitude}
                longitude={vehicle.longitude}
              />

              <CircleMarker
                center={[vehicle.latitude, vehicle.longitude]}
                radius={10}
                pathOptions={{
                  color: "#ffffff",
                  fillColor: "#2563eb",
                  fillOpacity: 1,
                  weight: 3,
                }}
              >
                <Popup>
                  <strong>{vehicle.vehicleId}</strong>
                  <br />
                  {data.routeShortName} járat
                  <br />
                  {vehicle.speedKilometersPerHour?.toFixed(0) ?? "–"} km/h
                </Popup>
              </CircleMarker>
            </MapContainer>
          </div>

          <div className="vehicle-tracking__stats">
            <div>
              <span>Sebesség</span>
              <strong>
                {vehicle.speedKilometersPerHour?.toFixed(0) ?? "–"} km/h
              </strong>
            </div>

            <div>
              <span>Haladási irány</span>
              <strong>{vehicle.heading?.toFixed(0) ?? "–"}°</strong>
            </div>

            <div>
              <span>GPS-frissítés</span>
              <strong>{formatTimestamp(vehicle.lastUpdatedUtc)}</strong>
            </div>
          </div>
        </>
      ) : (
        <div className="vehicle-tracking__status">
          Jelenleg nincs friss GPS-pozíció ehhez a járathoz.
          {vehicle && (
            <p>
              Utolsó ismert pozíció: {formatTimestamp(vehicle.lastUpdatedUtc)}
            </p>
          )}
        </div>
      )}
      <div className="vehicle-tracking__agent">
        <button type="button" onClick={handleAskAgent} disabled={agentLoading}>
          {agentLoading
            ? "AI elemzi a járatot..."
            : "Kérdezd meg a DayPlannerAI-t"}
        </button>

        {agentError && (
          <p className="vehicle-tracking__agent-error">{agentError}</p>
        )}

        {agentAnswer && (
          <div className="vehicle-tracking__agent-answer">
            <h4>DayPlannerAI összefoglaló</h4>
            <p>{agentAnswer}</p>
          </div>
        )}
      </div>
      <div className="vehicle-tracking__delays">
        <h4>Megállók és késések</h4>

        {data.stopCalls.map((stop, index) => {
          const delay = stop.arrivalDelaySeconds ?? stop.departureDelaySeconds;

          return (
            <div
              key={`${stop.stopId}-${index}`}
              className="vehicle-tracking__stop"
            >
              <span>{stop.stopName}</span>
              <strong>{formatStopDelay(delay)}</strong>
            </div>
          );
        })}

        {data.stopCalls.length === 0 && (
          <p>Nincs elérhető megállóinformáció.</p>
        )}
      </div>

      <div className="vehicle-tracking__footer">
        <span>
          Adatlekérés: {lastRequest ? formatTimestamp(lastRequest) : "–"}
        </span>

        <span>
          {data.hasRealtimeStopData
            ? "Valós idejű menetrendi adatok"
            : "Csak menetrendi adatok"}
        </span>
      </div>
    </section>
  );
}
