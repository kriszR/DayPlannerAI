export function getServiceDateTime(
  serviceDate: string,
  time: string,
): Date | null {
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

  const date = new Date(year, month - 1, day, 0, 0, 0, 0);
  // GTFS service times can exceed 24:00 for trips continuing after midnight.
  date.setSeconds(hours * 3600 + minutes * 60 + seconds);
  return Number.isNaN(date.getTime()) ? null : date;
}

export function getDestinationCity(location: string): string {
  const normalized = location.trim();
  // Calendar locations commonly put the city before the street address.
  const commaIndex = normalized.indexOf(",");

  return commaIndex === -1
    ? normalized
    : normalized.substring(0, commaIndex).trim();
}
