import type { CalendarEvent } from "../api/calendarApi";

const ONLINE_EVENT_PATTERN =
  /zoom(?:\.us|\.com)?|google\s*meet|meet\.google\.com|microsoft\s*teams|teams\s*meeting|teams\.microsoft\.com|webex|gotomeeting|whereby(?:\.com)?|\bonline\b|webinar|webin[aá]r(?:ium)?|vide[oó]konferencia|virtu[aá]lis\s*esem[eé]ny|\bvirtual\b|\bt[aá]voli\b/i;

/** Detects online calendar events consistently across the dashboard and travel planner. */
export function isOnlineCalendarEvent(event: CalendarEvent): boolean {
  const searchableText = [event.summary, event.location, event.description]
    .filter(Boolean)
    .join(" ");

  return ONLINE_EVENT_PATTERN.test(searchableText);
}
