import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { ArrowRight, Bus, CalendarDays, Clock3, MapPin, Search } from "lucide-react";

import { getProfile } from "../api/profileApi";
import { searchBusJourneys, type BusSearchResponse } from "../api/travelApi";
import TravelRouteCards from "../components/TravelRouteCards";
import TravelJourneyCards from "../components/TravelJourneyCards";
import "./Travel.css";

function getLocalDateValue(date = new Date()): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function getTomorrowDateValue(): string {
  const tomorrow = new Date();
  tomorrow.setDate(tomorrow.getDate() + 1);
  return getLocalDateValue(tomorrow);
}

export default function Travel() {
  const [origin, setOrigin] = useState("");
  const [destination, setDestination] = useState("");
  const [travelDate, setTravelDate] = useState(getTomorrowDateValue);
  const [time, setTime] = useState("08:00");
  const [searchMode, setSearchMode] = useState<"arriveBy" | "departAfter">("arriveBy");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<BusSearchResponse | null>(null);

  useEffect(() => {
    let cancelled = false;
    getProfile()
      .then((profile) => {
        if (!cancelled && profile.homeLocation) {
          setOrigin((current) => current.trim() ? current : profile.homeLocation!);
        }
      })
      .catch((profileError) => {
        console.warn("Could not load home location:", profileError);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const handleSearch = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!origin.trim() || !destination.trim() || !travelDate || !time) {
      setError("Add meg az indulási helyet, a célállomást, a dátumot és az időt.");
      return;
    }

    setLoading(true);
    setError(null);
    setResult(null);
    try {
      const response = await searchBusJourneys({
        departureLocation: origin.trim(),
        destination: destination.trim(),
        travelDate,
        searchMode,
        ...(searchMode === "arriveBy" ? { arrivalTime: time } : { departureTime: time }),
      });
      setResult(response);
      if (!response.success) {
        setError("A menetrendi keresés nem adott használható eredményt. Ellenőrizd a településneveket és a dátumot.");
      }
    } catch (searchError) {
      setError(
        searchError instanceof Error
          ? searchError.message
          : "Nem sikerült buszjáratokat keresni. Ellenőrizd, hogy a backend fut-e.",
      );
    } finally {
      setLoading(false);
    }
  };

  const resultCount = result?.resultCount ?? 0;

  return (
    <div className="page travel-page">
      <div className="page-heading">
        <div>
          <span className="page-eyebrow">KÖZLEKEDÉS</span>
          <h1>Utazás</h1>
          <p>Keress közvetlen és átszállásos buszjáratokat.</p>
        </div>
      </div>

      <section className="travel-search-panel">
        <div className="travel-search-panel__title">
          <div className="travel-search-panel__icon"><Bus size={21} /></div>
          <div>
            <h2>Útvonal keresése</h2>
            <p>Add meg az utazás adatait a menetrend lekéréséhez.</p>
          </div>
        </div>

        <form className="travel-search-form" onSubmit={handleSearch}>
          <div className="travel-search-form__locations">
            <label>
              <span><MapPin size={15} /> Indulás</span>
              <input
                value={origin}
                onChange={(event) => setOrigin(event.target.value)}
                placeholder="Pl. Csetény"
                autoComplete="address-level2"
                required
              />
            </label>
            <div className="travel-search-form__arrow"><ArrowRight size={18} /></div>
            <label>
              <span><MapPin size={15} /> Úti cél</span>
              <input
                value={destination}
                onChange={(event) => setDestination(event.target.value)}
                placeholder="Pl. Veszprém"
                autoComplete="address-level2"
                required
              />
            </label>
          </div>

          <div className="travel-search-form__details">
            <label>
              <span><CalendarDays size={15} /> Dátum</span>
              <input
                type="date"
                value={travelDate}
                onChange={(event) => setTravelDate(event.target.value)}
                min={getLocalDateValue()}
                required
              />
            </label>
            <label>
              <span><Clock3 size={15} /> {searchMode === "arriveBy" ? "Érkezés legkésőbb" : "Indulás ettől"}</span>
              <input
                type="time"
                value={time}
                onChange={(event) => setTime(event.target.value)}
                required
              />
            </label>
            <label>
              <span>Időzítés</span>
              <select
                value={searchMode}
                onChange={(event) => setSearchMode(event.target.value as "arriveBy" | "departAfter")}
              >
                <option value="arriveBy">Érkezz meg eddig</option>
                <option value="departAfter">Indulj ezután</option>
              </select>
            </label>
          </div>

          <button className="travel-search-form__submit" type="submit" disabled={loading}>
            <Search size={18} /> {loading ? "Járatok keresése…" : "Járatok keresése"}
          </button>
        </form>
      </section>

      {error && <div className="travel-search-message travel-search-message--error" role="alert">{error}</div>}

      {result && result.success && (
        <section className="travel-search-results" aria-live="polite">
          <div className="travel-search-results__heading">
            <div>
              <h2>{origin} <ArrowRight size={16} /> {destination}</h2>
              <p>{travelDate} · {searchMode === "arriveBy" ? `Érkezés ${time}-ig` : `Indulás ${time}-tól`}</p>
            </div>
            <span>{resultCount} találat</span>
          </div>
          {resultCount === 0 ? (
            <div className="travel-search-message">Erre az időpontra nem találtunk járatot. Próbálj másik időpontot.</div>
          ) : (
            <>
              <TravelRouteCards routes={result.availableRoutes ?? []} />
              <TravelJourneyCards journeys={result.availableJourneys ?? []} />
            </>
          )}
        </section>
      )}
    </div>
  );
}
