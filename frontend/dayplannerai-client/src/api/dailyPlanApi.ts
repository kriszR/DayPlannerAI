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
    `A backend automatikusan lekéri a Google Naptár eseményeit, ` +
    `az adott nap időjárását és a felhasználói profilt, valamint a megfelelő eseményekhez buszjáratot keres. ` +
    `Vedd figyelembe a profilban beállított felkészülési időt. ` +
    `Ha egy időzített, személyes megjelenést igénylő naptári eseménynek van helyszíne, és a profilban ismert az indulási település, ` +
    `a backend tényleges GTFS buszjárat-keresési eredményét használd az esemény kezdési időpontjáig. ` +
    `A buszkeresésnél ne jeleníts meg olyan járatot készülődéshez, amelynek indulási megállóba érkezési ideje már elmúlt. ` +
    `Online vagy virtuális eseményhez (Zoom, Google Meet, Teams, Webex, webinar/webinár, online meeting vagy videókonferencia) ne keress buszjáratot, ` +
    `és ne számolj buszhoz kapcsolódó felkészülési időt akkor sem, ha van location mezője. ` +
    `Buszos eseménynél a felkészülés pontosan a profilban beállított ideig tartson, ` +
    `és a busz departureStopArrivalTime időpontjában, az indulási megállóhoz érkezésekor érjen véget. ` +
    `A kezdete: departureStopArrivalTime mínusz preparationMinutes; ne a busz indulási vagy célállomási érkezési idejéből számolj. ` +
    `Ne becsülj gyaloglási időt, és ne találj ki hiányzó útvonalat vagy menetrendi adatot. ` +
    `Ha valamelyik adatforrás vagy egy buszkeresés hibázik, a többi sikeres adatból akkor is készíts napi tervet. ` +
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
