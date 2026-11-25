using DayPlannerAI.Api.Services.Tools;

namespace DayPlannerAI.Api.Services.Agent.Skills;

public sealed class DailyPlanningSkill : IAgentSkill
{
    private readonly GetCalendarEventsTool _calendarTool;
    private readonly GetWeatherForecastTool _weatherTool;
    private readonly GetUserProfileTool _profileTool;
    private readonly SearchBusJourneysTool _busJourneysTool;

    public DailyPlanningSkill(
        GetCalendarEventsTool calendarTool,
        GetWeatherForecastTool weatherTool,
        GetUserProfileTool profileTool,
        SearchBusJourneysTool busJourneysTool)
    {
        _calendarTool = calendarTool;
        _weatherTool = weatherTool;
        _profileTool = profileTool;
        _busJourneysTool = busJourneysTool;
    }

    public string Name => "DailyPlanningSkill";

    public string Description =>
        "Személyre szabott napi tervezés a Google Calendar, az időjárás, " +
        "a felhasználói profil és a GTFS buszmenetrend alapján.";

    public string Instructions => """
        A képesség célja, hogy a felhasználó kérésének megfelelően
        személyre szabott, időrendi napi tervet készítsen.

        ELÉRHETŐ ESZKÖZÖK:
        - get_calendar_events: Google Calendar események lekérdezése.
        - get_weather_forecast: időjárás-előrejelzés lekérdezése.
        - get_user_profile: lakóhely, időzóna és felkészülési idő lekérdezése.
        - search_bus_journeys: tényleges GTFS buszjáratok keresése.

        ADATLEKÉRÉS:
        - Teljes napi terv készítésekor kérd le a naptárat, az időjárást és a felhasználói profilt.
        - Mindhárom lekérdezésnél ugyanazt a tervezett dátumot használd.
        - A mai/holnapi napot az aktív rendszerpromptban megadott Europe/Budapest dátum alapján határozd meg.
        - Ha a felhasználó csak a naptári programját kéri, ne adj hozzá kitalált eseményeket vagy útvonalakat.

        NAPTÁR:
        - Az események summary mezőjét pontosan őrizd meg.
        - A helyi kezdési és befejezési időpontokhoz a localStart és localEnd mezőket használd.
        - A timeRangeValid és durationMinutes mezőket tekintsd mérvadónak.
        - Egész napos eseményhez ne keress buszjáratot.
        - Online vagy virtuális eseményhez (például Zoom, Google Meet, Microsoft Teams, Webex, webinar/webinár, online meeting vagy videókonferencia) ne keress buszjáratot akkor sem, ha a location mezőben link vagy helyszöveg szerepel.
        - Online vagy virtuális eseménynél ne számíts buszhoz kapcsolódó felkészülési időt; az esemény kezdési időpontját és online jellegét közöld.
        - Érvénytelen vagy hiányos időintervallumú eseményhez ne tervezz időzített utazást.
        - A címben szereplő időpontot ne értelmezd át eseménykezdési időpontként.
        - A naptár üres időszakait ne tekintsd automatikusan szabadidőnek.

        UTAZÁS AZ ESEMÉNYEKHEZ:
        - A homeLocation mezőt használd indulási településként.
        - Ha a homeLocation hiányzik, ne találj ki indulási helyet; kérdezz vissza.
        - Ha az esemény location mezője hiányzik vagy nem azonosítható településként/helyszínként, ne indíts útvonal-keresést.
        - Csak fizikai, személyes megjelenést igénylő, időzített eseményhez hívd meg a search_bus_journeys eszközt, ha az indulási hely és a célhely ismert.
        - Online/virtuális eseménynél hagyd ki a search_bus_journeys eszközt; ne keress buszt és ne adj buszinduláshoz kötött felkészülési időt.
        - A keresés travelDate értéke az esemény localStart dátuma legyen.
        - searchMode = "arriveBy", arrivalTime = az esemény localStart időpontja.
        - A dátumot és az időpontot ne kerekítsd és ne módosítsd önkényesen.
        - Ha a keresőeszköz közvetlen vagy átszállásos találatokat ad, kizárólag azokat mutasd be.
        - Ha nincs találat, mondd el, hogy a menetrendi keresés nem talált megfelelő járatot.
        - Ne becsülj gyaloglási, helyi közlekedési vagy megállóhoz jutási időt.
        - A preparationMinutes a busz indulása előtti felkészülési idő. A felkészülés javasolt kezdete a busz indulási ideje mínusz preparationMinutes.
        - A felkészülési idő nem utazási idő, és nem bizonyítja, hogy a felhasználó időben eléri a megállót.
        - Ha a preparationMinutes null, ne találj ki értéket; jelezd, hogy a felkészülési idő nincs beállítva.
        - Ne állítsd, hogy a felhasználó biztosan odaér, ha a megállóhoz jutás ideje nem ismert.

        IDŐJÁRÁS:
        - Csak a tervezett dátumhoz tartozó előrejelzést használd.
        - Ellenőrizd, hogy a requestedDate megegyezik-e a tervezett dátummal.
        - Ha nincs megfelelő előrejelzés, ezt jelezd.
        - Az időjárás alapján adj rövid, releváns öltözködési vagy közlekedési javaslatot, de ne állíts olyat, amit az előrejelzés nem támaszt alá.

        VÁLASZ:
        - Mindig magyarul válaszolj.
        - Először ismertesd a naptári eseményeket időrendben.
        - Az eseményekhez mutasd be a ténylegesen megtalált buszjáratokat és az átszállásokat.
        - A felkészülési idő kezdetét csak akkor számítsd ki, ha a profilban van ilyen érték és van tényleges járatindulás.
        - Ezután ismertesd az időjárást.
        - Végül adj rövid, gyakorlatias napi javaslatot.
        - Különítsd el a tényleges naptári/menetrendi adatokat és a saját javaslataidat.
        - Hiányzó adatokból ne következtess tényként bemutatott információkra.
        - Ha fontos adat hiányzik, csak a szükséges adatot kérdezd meg.
        - A válasz legyen tömör, teljes és időrendi sorrendű.
        - A preparationMinutes megnevezése mindig felkészülési idő.
        """;

    public IReadOnlyList<IAgentTool> Tools =>
    [
        _calendarTool,
        _weatherTool,
        _profileTool,
        _busJourneysTool
    ];
}
