import { useQuery } from "@tanstack/react-query";
import {
  CalendarDays,
  Clock,
  MapPin,
  ExternalLink,
  RefreshCw,
} from "lucide-react";

import { getCalendarEvents, type CalendarEvent } from "../api/calendarApi";

import DOMPurify from "dompurify";

const DAYS_TO_LOAD = 7;

function formatEventDate(dateValue: string, isAllDay: boolean) {
  if (isAllDay) {
    const [year, month, day] = dateValue.split("-").map(Number);

    return new Intl.DateTimeFormat("hu-HU", {
      year: "numeric",
      month: "long",
      day: "numeric",
    }).format(new Date(year, month - 1, day));
  }

  return new Intl.DateTimeFormat("hu-HU", {
    year: "numeric",
    month: "long",
    day: "numeric",
  }).format(new Date(dateValue));
}

function formatEventTime(dateValue: string | null) {
  if (!dateValue) return "";

  return new Intl.DateTimeFormat("hu-HU", {
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(dateValue));
}

function getEventDateLabel(event: CalendarEvent) {
  if (!event.start) return "Ismeretlen időpont";

  return formatEventDate(event.start, event.isAllDay);
}

function Calendar() {
  const {
    data: events = [],
    isLoading,
    isError,
    error,
    refetch,
    isFetching,
  } = useQuery({
    queryKey: ["calendar-events", DAYS_TO_LOAD],
    queryFn: () => getCalendarEvents(DAYS_TO_LOAD),
    retry: 1,
    staleTime: 60_000,
  });

  return (
    <div className="page">
      <div className="page-heading">
        <div>
          <span className="page-eyebrow">NAPI TERVEZÉS</span>
          <h1>Naptár</h1>
          <p>Kezeld a programjaidat és a közelgő eseményeidet.</p>
        </div>

        <button
          className="calendar-refresh-button"
          onClick={() => refetch()}
          disabled={isFetching}
          type="button"
        >
          <RefreshCw size={16} className={isFetching ? "spin" : ""} />
          Frissítés
        </button>
      </div>

      {isLoading && (
        <div className="empty-state">
          <CalendarDays size={42} />
          <h2>Naptáresemények betöltése</h2>
          <p>Kapcsolódás a Google Naptárhoz...</p>
        </div>
      )}

      {isError && (
        <div className="empty-state calendar-error">
          <CalendarDays size={42} />
          <h2>Nem sikerült betölteni a naptárat</h2>
          <p>
            {error instanceof Error
              ? error.message
              : "Ismeretlen hiba történt."}
          </p>
          <button
            className="calendar-action-button"
            onClick={() => refetch()}
            type="button"
          >
            Újrapróbálás
          </button>
        </div>
      )}

      {!isLoading && !isError && events.length === 0 && (
        <div className="empty-state">
          <CalendarDays size={42} />
          <h2>Nincsenek közelgő események</h2>
          <p>
            A következő {DAYS_TO_LOAD} napra nincs megjeleníthető esemény a
            Google Naptáradban.
          </p>
        </div>
      )}

      {!isLoading && !isError && events.length > 0 && (
        <div className="calendar-content">
          <div className="calendar-summary">
            <div>
              <span className="calendar-summary-label">Közelgő események</span>
              <strong>{events.length}</strong>
            </div>

            <span className="calendar-period">
              Következő {DAYS_TO_LOAD} nap
            </span>
          </div>

          <div className="calendar-event-list">
            {events.map((event) => (
              <article className="calendar-event-card" key={event.id}>
                <div className="calendar-event-icon">
                  <CalendarDays size={20} />
                </div>

                <div className="calendar-event-details">
                  <h2>{event.summary || "Névtelen esemény"}</h2>

                  <div className="calendar-event-meta">
                    <span>
                      <CalendarDays size={15} />
                      {getEventDateLabel(event)}
                    </span>

                    {!event.isAllDay && event.start && (
                      <span>
                        <Clock size={15} />
                        {formatEventTime(event.start)}
                        {event.end && ` – ${formatEventTime(event.end)}`}
                      </span>
                    )}

                    {event.isAllDay && <span>Egész napos</span>}

                    {event.location && (
                      <span>
                        <MapPin size={15} />
                        {event.location}
                      </span>
                    )}
                  </div>

                  {event.description && (
                    <div
                      className="calendar-event-description"
                      dangerouslySetInnerHTML={{
                        __html: DOMPurify.sanitize(event.description),
                      }}
                    />
                  )}
                </div>

                {event.htmlLink && (
                  <a
                    className="calendar-event-link"
                    href={event.htmlLink}
                    target="_blank"
                    rel="noreferrer"
                    aria-label="Esemény megnyitása a Google Naptárban"
                  >
                    <ExternalLink size={18} />
                  </a>
                )}
              </article>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

export default Calendar;
