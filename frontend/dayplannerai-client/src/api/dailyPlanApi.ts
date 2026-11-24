const API_BASE_URL = "http://localhost:5131";

interface AgentChatResponse {
  answer?: string;
  message?: string;
  error?: string;
}

export async function generateDailyPlan(
  date: string,
  signal?: AbortSignal,
): Promise<string> {
  const message =
    `Készíts teljes napi tervet erre a dátumra: ${date}. ` +
    `Használd a DailyPlanningSkill képességet. ` +
    `Kérdezd le a Google Naptár eseményeit, ` +
    `az adott nap időjárását és a felhasználói profilt. ` +
    `Vedd figyelembe a profilban beállított felkészülési időt. ` +
    `Ha egy időzített naptári eseménynek van helyszíne, és a profilban ismert az indulási település, ` +
    `keress hozzá tényleges buszjáratot a GTFS-menetrendből az esemény kezdési időpontjáig. ` +
    `A járat indulási idejéből és a felkészülési időből számítsd ki a felkészülés javasolt kezdetét. ` +
    `Ne becsülj gyaloglási időt, és ne találj ki hiányzó útvonalat vagy menetrendi adatot. ` +
    `Csak a ténylegesen visszakapott adatokra támaszkodj. ` +
    `A választ magyarul, teljes egészében add meg.`;

  const response = await fetch(`${API_BASE_URL}/api/agent/chat`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ message }),
    signal,
  });

  const result = (await response.json()) as AgentChatResponse;

  if (!response.ok) {
    throw new Error(
      result.message ??
        result.error ??
        `A napi terv lekérése sikertelen (${response.status}).`,
    );
  }

  if (!result.answer || !result.answer.trim()) {
    throw new Error("Az AI nem adott vissza napi tervet.");
  }

  return result.answer;
}
