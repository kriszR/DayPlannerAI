
import { apiGet } from "./http";

export interface CalendarEvent {
    id: string;
    summary: string | null;
    description: string | null;
    location: string | null;
    start: string | null;
    end: string | null;
    isAllDay: boolean;
    htmlLink: string | null;
}

export async function getCalendarEvents(
    days: number = 7
): Promise<CalendarEvent[]> {
    return apiGet<CalendarEvent[]>(
        `/google-calendar/events?days=${days}`
    );
}
