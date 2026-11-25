export function getWeatherDescription(code: number): string {
  const descriptions: Record<number, string> = {
    0: "Derült égbolt",
    1: "Többnyire derült",
    2: "Részben felhős",
    3: "Borult",
    45: "Köd",
    48: "Zúzmarás köd",
    51: "Enyhe szitálás",
    53: "Mérsékelt szitálás",
    55: "Erős szitálás",
    56: "Ónos szitálás",
    57: "Erős ónos szitálás",
    61: "Enyhe eső",
    63: "Mérsékelt eső",
    65: "Erős eső",
    66: "Enyhe ónos eső",
    67: "Erős ónos eső",
    71: "Enyhe havazás",
    73: "Mérsékelt havazás",
    75: "Erős havazás",
    77: "Hószemcsék",
    80: "Enyhe zápor",
    81: "Mérsékelt zápor",
    82: "Heves zápor",
    85: "Enyhe hózápor",
    86: "Erős hózápor",
    95: "Zivatar",
    96: "Zivatar jégesővel",
    99: "Erős zivatar jégesővel",
  };

  return descriptions[code] ?? "Ismeretlen időjárás";
}

export function getWeatherEmoji(code: number): string {
  if (code === 0) return "☀️";
  if (code <= 2) return "🌤️";
  if (code === 3) return "☁️";
  if (code === 45 || code === 48) return "🌫️";
  if (code >= 51 && code <= 67) return "🌧️";
  if (code >= 71 && code <= 77) return "❄️";
  if (code >= 80 && code <= 82) return "🌦️";
  if (code === 85 || code === 86) return "🌨️";
  if (code >= 95) return "⛈️";

  return "🌡️";
}

export function getCurrentWeatherEmoji(code: number, isDay: boolean): string {
  if (code === 0) {
    return isDay ? "☀️" : "🌙";
  }

  if (code === 1 || code === 2) {
    return isDay ? "🌤️" : "☁️🌙";
  }

  if (code === 3) {
    return isDay ? "☁️" : "☁️🌙";
  }

  return getWeatherEmoji(code);
}

export function formatWeatherDate(date: string): string {
  return new Intl.DateTimeFormat("hu-HU", {
    weekday: "short",
    month: "short",
    day: "numeric",
  }).format(new Date(`${date}T12:00:00`));
}
