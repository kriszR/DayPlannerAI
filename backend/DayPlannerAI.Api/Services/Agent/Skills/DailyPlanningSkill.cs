using DayPlannerAI.Api.Services.Tools;

namespace DayPlannerAI.Api.Services.Agent.Skills;

public sealed class DailyPlanningSkill : IAgentSkill
{
    private readonly GetCalendarEventsTool _calendarTool;
    private readonly GetWeatherForecastTool _weatherTool;

    private readonly GetUserProfileTool _profileTool;

    public DailyPlanningSkill(
        GetCalendarEventsTool calendarTool,
        GetWeatherForecastTool weatherTool,
        GetUserProfileTool profileTool)
    {
        _calendarTool = calendarTool;
        _weatherTool = weatherTool;
        _profileTool = profileTool;
    }

    public string Name => "DailyPlanningSkill";

    public string Description =>
        "Személyre szabott napi tervezés a naptár és az időjárás alapján.";


    public string Instructions => """
    A képesség célja, hogy a felhasználó kérésének megfelelően
    személyre szabott napi tervet készítsen.

    ELÉRHETŐ ESZKÖZÖK:

    - get_calendar_events: naptári események lekérdezése.
    - get_weather_forecast: időjárás-előrejelzés lekérdezése.
    - get_user_profile: felhasználói profil lekérdezése.

    MŰKÖDÉS:

    - Mai vagy konkrét napi program lekérdezésekor használd
      a get_calendar_events eszközt.
    - Teljes napi terv készítésekor kérd le a naptárat,
      az időjárást és a felhasználói profilt.
    - Mindhárom eszköznek ugyanazt a tervezett dátumot add át.
    - Az időjárást mindig a tervezett napra kérd le.
    - A válaszban kizárólag a ténylegesen visszakapott adatokat használd.

    NAPTÁR:

    - Az események summary mezőjét pontosan őrizd meg.
    - A kezdési és befejezési időpontokat a backend által
      visszaadott értékek alapján jelenítsd meg.
    - A timeRangeValid és durationMinutes mezőket tekintsd
      mérvadónak az időtartam értelmezésekor.
    - Érvényes időintervallum esetén ne állítsd, hogy az esemény
      időtartama negatív.
    - A címben szereplő időpontot ne értelmezd át
      eseménykezdési időpontként.
    - Ne találj ki hiányzó eseményeket vagy helyszíneket.
    - A naptár üres időszakait ne tekintsd automatikusan szabadidőnek.

    FELKÉSZÜLÉS ÉS UTAZÁS:

    - A preparationMinutes értéket a felhasználói profilból vedd át.
    - Ha az érték null, kérdezd meg a felhasználót.
    - Ne találj ki felkészülési vagy utazási időt.
    - Utazási tervet csak ismert indulási hely és célállomás alapján készíts.
    - Ha az esemény helyszíne nem ismert, ne tervezz hozzá útvonalat.

    IDŐJÁRÁS:

    - Csak a tervezett dátumhoz tartozó előrejelzést használd.
    - Ellenőrizd, hogy a requestedDate megegyezik-e a tervezett dátummal.
    - Ha nincs megfelelő előrejelzés, ezt jelezd.

    
    VÁLASZ:

    - Mindig magyarul válaszolj.
    - Először ismertesd a naptári eseményeket.
    - Ezután az időjárást.
    - Végül adj rövid, gyakorlatias napi javaslatot.
    - Különítsd el a tényleges eseményeket és a saját javaslataidat.
    - Hiányzó adatokból ne következtess tényként bemutatott információkra.
    - Ha fontos adat hiányzik, kérdezz rá.
    - A válasz legyen tömör, teljes és időrendi sorrendű.
    - Az üres naptár nem jelenti azt, hogy a felhasználó egész nap szabad.
    Csak azt állítsd, hogy nincs bejegyzett naptári eseménye.
    - A csapadék valószínűsége nem azonos a csapadék intenzitásával.
    - A preparationMinutes megnevezése mindig felkészülési idő.
    """;


    public IReadOnlyList<IAgentTool> Tools =>
    [
        _calendarTool,
        _weatherTool,
        _profileTool
    ];
}