import { useMemo } from "react";
import {
  CalendarDays,
  Clock,
  MapPin,
  ExternalLink,
  Sparkles,
} from "lucide-react";

import EventTravelPlanner from "./EventTravelPlanner";
import type { CalendarEvent } from "../api/calendarApi";
import { isOnlineCalendarEvent } from "../utils/calendarEventUtils";
import "./DailyTimeline.css";

interface DailyTimelineProps {
  events: CalendarEvent[];
  preparationMinutes: number;
  departureLocation: string | null;
}

type TimelineItem =
  | {
      id: string;
      kind: "preparation";
      start: Date;
      end: Date;
    }
  | {
      id: string;
      kind: "event";
      event: CalendarEvent;
      start: Date | null;
    };

function formatTime(date: Date): string {
  return new Intl.DateTimeFormat("hu-HU", {
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

function getDateKey(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");

  return `${year}-${month}-${day}`;
}

function DailyTimeline({
  events,
  preparationMinutes,
  departureLocation,
}: DailyTimelineProps) {
  const timelineItems = useMemo(() => {
    const items: TimelineItem[] = events
      .filter((event) => Boolean(event.start))
      .map((event) => ({
        id: `event-${event.id}`,
        kind: "event" as const,
        event,
        start: event.isAllDay ? null : new Date(event.start!),
      }));

    const firstTimedEvent = events
      .filter((event) => event.start && !event.isAllDay && !isOnlineCalendarEvent(event))
      .map((event) => ({
        event,
        start: new Date(event.start!),
      }))
      .filter(({ start }) => !Number.isNaN(start.getTime()))
      .sort((a, b) => a.start.getTime() - b.start.getTime())[0];

    const minutes = Math.max(
      0,
      Math.min(180, Math.floor(preparationMinutes || 0)),
    );

    if (
      firstTimedEvent &&
      minutes > 0 &&
      !events.some((event) => event.isAllDay)
    ) {
      const end = firstTimedEvent.start;
      const start = new Date(end.getTime() - minutes * 60_000);

      // Only show preparation blocks that start on the selected day.
      if (getDateKey(start) === getDateKey(end)) {
        items.push({
          id: "preparation",
          kind: "preparation",
          start,
          end,
        });
      }
    }

    return items.sort((a, b) => {
      if (a.kind === "event" && a.event.isAllDay) return -1;
      if (b.kind === "event" && b.event.isAllDay) return 1;

      const aTime = a.kind === "preparation" ? a.start : a.start;
      const bTime = b.kind === "preparation" ? b.start : b.start;

      return (aTime?.getTime() ?? 0) - (bTime?.getTime() ?? 0);
    });
  }, [events, preparationMinutes]);

  if (events.length === 0) {
    return (
      <section className="section">
        <div className="section-heading">
          <h2>Mai időbeosztás</h2>
          <span>DayPlannerAI</span>
        </div>

        <div className="daily-timeline-empty">
          <CalendarDays size={24} />
          <div>
            <strong>Ma nincs bejegyzett naptári eseményed.</strong>
            <p>
              Amint program kerül a Google Naptáradba, itt jelenik meg a napi
              idővonalon.
            </p>
          </div>
        </div>
      </section>
    );
  }

  return (
    <section className="section">
      <div className="section-heading">
        <div>
          <h2>Mai időbeosztás</h2>
          <span>{events.length} naptári esemény</span>
        </div>
      </div>

      <div className="daily-timeline">
        {timelineItems.map((item) => {
          if (item.kind === "preparation") {
            return (
              <article
                className="timeline-item timeline-preparation"
                key={item.id}
              >
                <div className="timeline-time">
                  <strong>{formatTime(item.start)}</strong>
                  <span>{formatTime(item.end)}</span>
                </div>

                <div className="timeline-track">
                  <div className="timeline-marker preparation-marker">
                    <Sparkles size={17} />
                  </div>
                </div>

                <div className="timeline-card preparation-card">
                  <div className="timeline-card-heading">
                    <h3>Felkészülés</h3>
                    <span className="timeline-tag">
                      {preparationMinutes} perc
                    </span>
                  </div>

                  <p>
                    A beállított felkészülési idő az első napi programod előtt.
                  </p>
                </div>
              </article>
            );
          }

          const event = item.event;
          const startText = event.isAllDay
            ? "Egész nap"
            : item.start
              ? formatTime(item.start)
              : "Időpont nélkül";

          const endDate =
            event.end && !event.isAllDay ? new Date(event.end) : null;

          const validEnd =
            endDate &&
            item.start &&
            !Number.isNaN(endDate.getTime()) &&
            endDate >= item.start;

          return (
            <article className="timeline-item" key={item.id}>
              <div className="timeline-time">
                <strong>{startText}</strong>
                {validEnd && <span>{formatTime(endDate!)}</span>}
              </div>

              <div className="timeline-track">
                <div className="timeline-marker event-marker">
                  <CalendarDays size={17} />
                </div>
              </div>

              <div className="timeline-card">
                <div className="timeline-card-heading">
                  <h3>{event.summary || "Névtelen esemény"}</h3>

                  {validEnd && (
                    <span className="timeline-tag">
                      {Math.round(
                        (endDate!.getTime() - item.start!.getTime()) / 60_000,
                      )}{" "}
                      perc
                    </span>
                  )}
                </div>

                {event.location && (
                  <p className="timeline-location">
                    <MapPin size={14} />
                    {event.location}
                  </p>
                )}

                {event.htmlLink && (
                  <a
                    href={event.htmlLink}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="timeline-link"
                  >
                    Esemény megnyitása
                    <ExternalLink size={14} />
                  </a>
                )}

                <EventTravelPlanner
                  event={event}
                  departureLocation={departureLocation}
                />
              </div>
            </article>
          );
        })}
      </div>

      <div className="timeline-footer">
        <Clock size={15} />
        <span>
          Az idővonal a Google Naptár eseményeiből és a profilod felkészülési
          idejéből készül.
        </span>
      </div>
    </section>
  );
}

export default DailyTimeline;
