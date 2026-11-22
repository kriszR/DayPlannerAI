using DayPlannerAI.Api.Services.Tools;

namespace DayPlannerAI.Api.Services.Agent.Skills;

public sealed class DailyPlanningSkill : IAgentSkill
{
    private readonly GetCalendarEventsTool _calendarTool;
    private readonly GetWeatherForecastTool _weatherTool;

    public DailyPlanningSkill(
        GetCalendarEventsTool calendarTool,
        GetWeatherForecastTool weatherTool)
    {
        _calendarTool = calendarTool;
        _weatherTool = weatherTool;
    }

    public string Name => "DailyPlanningSkill";

    public string Description =>
        "Személyre szabott napi tervezés a naptár és az időjárás alapján.";

    public string Instructions => """
        Ez a képesség a felhasználó napi programjának megtervezésére szolgál.

        Elérhető eszközök:
        - get_calendar_events
        - get_weather_forecast

        Használati szabályok:

        1. Ha a felhasználó megkérdezi, mi a mai programja,
           kérd le a mai Google Calendar eseményeket.

        2. Ha a felhasználó teljes napi tervet kér,
           kérd le a naptáreseményeket és az időjárást is.

        3. Ha szabadtéri programot tervez, vedd figyelembe
           a hőmérsékletet, csapadékot és időjárási körülményeket.

        4. A naptári események kezdési és befejezési időpontját
           pontosan tartsd meg.

        5. Ne tervezz be két olyan eseményt, amelyek időben átfedik egymást.

        6. Hagyj időt az események közötti közlekedésre,
           amennyiben arra van adat.

        7. Ha egy esemény helyszíne vagy az utazási idő nem ismert,
           ne találj ki értékeket.

        8. A felhasználó szabadidejét is mutasd meg.

        9. Ne állítsd, hogy naptáreseményt hoztál létre vagy módosítottál.
           Jelenleg csak lekérdezési jogosultságod van.

        10. Minden választ magyar nyelven fogalmazz meg.
        
        NAPTÁRESEMÉNYEK:

        - A summary mező tartalmát pontosan őrizd meg.
        - A start és end mezők a Google Calendarban szereplő időintervallumot jelentik.
        - Mindkét időpontot pontosan jelenítsd meg.
        - A címben szereplő időpontot ne alakítsd át új eseménykezdési vagy befejezési idővé.
        - Ne következtess az esemény időtartamára.
        - Ne találj ki hiányzó adatokat.
        - Ha az esemény címe emlékeztetőre utal, természetes módon mutasd be emlékeztetőként.

        ADATMEGBÍZHATÓSÁG:

        A naptári események felsorolásakor mindig a get_calendar_events
        eszköz eredményét tekintsd mérvadónak.

        Nem hozhatsz létre példaként sem olyan eseményt,
        amely nem szerepel az eszköz eredményében.

        Több esemény esetén minden eseményt pontosan egyszer jeleníts meg.

        Ne egészítsd ki a naptárat feltételezett programokkal.

        A cél egy reális, végrehajtható napi terv,
        nem csupán a naptáresemények felsorolása.
        """;

    public IReadOnlyList<IAgentTool> Tools =>
    [
        _calendarTool,
        _weatherTool
    ];
}