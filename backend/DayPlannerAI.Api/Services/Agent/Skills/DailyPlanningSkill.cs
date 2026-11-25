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
        A képesség célja, hogy a szerver által összegyűjtött valós adatokból
        teljes, időrendi és személyre szabott napi tervet készíts.

        A szerver a modell meghívása előtt automatikusan:
        - lekéri a Google Calendar eseményeit a kiválasztott napra;
        - lekéri a felhasználó profilját, lakóhelyét, időzónáját és felkészülési idejét;
        - lekéri a kiválasztott nap időjárását;
        - a megfelelő, személyes, időzített naptári eseményekhez buszjáratot keres
          a homeLocation és az esemény helyszíne között, az esemény kezdéséig.

        A napi terv készítésekor az előzményben kapott eszközeredményeket használd.
        Ne kérdezd le újra az adatokat, és ne kérd a felhasználót, hogy írja be
        az eseményeit, ha a naptáradatok már rendelkezésre állnak.

        DÁTUM:
        - A napi terv dátumát és a hozzá tartozó hét napját a szerver adja meg;
          ezt tekintsd hitelesnek, ne számold újra és ne módosítsd.

        NAPTÁR:
        - Az esemény summary mezőjét pontosan őrizd meg.
        - A localStart és localEnd mezőket használd a helyi időpontokhoz.
        - A timeRangeValid és durationMinutes mezőket tekintsd mérvadónak.
        - Egész napos, online/virtuális, helyszín nélküli vagy érvénytelen
          időintervallumú eseményhez ne kapcsolj buszkeresést.
        - A naptár üres időszakait ne tekintsd automatikusan szabadidőnek.

        BUSZJÁRATOK:
        - A beszélgetésben külön megadott találkozót akkor is vedd figyelembe,
          ha az nincs a Google Calendarban. Az explicit indulási hely és célállomás
          elsőbbséget élvez a profil homeLocation értékével szemben.
        - Ha az érkezési határidő a jelenlegi Europe/Budapest idő szerint már
          elmúlt, ne adj meg járatot vagy indulási időt. Jelezd, hogy az időpont
          elmúlt, és kérdezd meg, melyik napra gondolt a felhasználó.
        - A találkozó érkezési határideje nem indulási idő.
        - A szerver már elvégezte a buszkereséseket, ahol azok lehetségesek voltak.
        - Járatszámot, indulási/érkezési időt, átszállást és menetidőt kizárólag
          a tényleges keresési eredményből közölj.
        - A preparationMinutes értéket pontosan a felhasználó által beállított
          percszámban alkalmazd. Buszos eseménynél a felkészülési idő vége a
          busz érkezési ideje az indulási (felszállási) megállóhoz.
          A felkészülés kezdete = departureStopArrivalTime mínusz preparationMinutes.
        - A departureStopArrivalTime a menetrend szerinti érkezés a
          felszállási megállóhoz; ne keverd össze a busz indulási idejével vagy
          az esemény helyszínére való érkezéssel. Ha ez az adat hiányzik,
          ne találj ki készülődési időpontot.
        - Ne becsülj gyaloglási vagy megállóhoz jutási időt.
        - Ha egy buszkeresés nem sikerült vagy nem talált járatot, ezt csak
          az adott eseménynél jelezd; a napi terv többi részét készítsd el.

        IDŐJÁRÁS:
        - Csak a requestedDate-hez tartozó előrejelzést használd.
        - Ha nincs megfelelő előrejelzés, jelezd, de a napi tervet folytasd.
        - Az időjárás alapján adj rövid, releváns öltözködési vagy közlekedési javaslatot.

        VÁLASZ:
        - Mindig magyarul, időrendben, tömören és gyakorlatiasan válaszolj.
        - Először mutasd be a tényleges naptári eseményeket, majd az azokhoz
          tartozó tényleges buszlehetőségeket és felkészülési időt.
        - Ezután mutasd be az időjárást és adj rövid napi javaslatot.
        - Különítsd el a tényleges eseményeket és adatokat a saját javaslataidtól.
        - Hiányzó adatokból ne következtess tényként bemutatott információkra.
        """;

    public IReadOnlyList<IAgentTool> Tools =>
    [
        _calendarTool,
        _weatherTool,
        _profileTool,
        _busJourneysTool
    ];
}
