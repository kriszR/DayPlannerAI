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
        Töltsd ki az alábbi pontos magyar nyelvű sablont a szerver által lekérdezett adatokkal.
        SZIGORÚAN TILOS Markdown formázást (#, ##, **, *) használni a válaszban! Csak tiszta szöveget és emojikat írj.
        NE írj hozzá saját gondolatokat, metaforákat, extra tanácsokat vagy záró kérdést!

        NAPI TERV – [Dátum és hét napja a rendszerutasításból]

        📅 NAPTÁRI ESEMÉNYEK ÉS UTAZÁS
        [Ha nincs esemény, írd pontosan ezt: "Erre a napra nincs beütemezett program a naptáradban."]
        [Ha van esemény, sorold fel őket így:
        • [Kezdés]–[Befejezés]: [Esemény neve] ([Helyszín, ha van])
          - Javasolt járat: [Járatszám] ([Indulási megálló] [Indulási idő] → [Érkezési megálló] [Érkezési idő])
          - Felkészülés kezdete: [Indulási idő mínusz preparationMinutes] (felkészülési idő: [preparationMinutes] perc)
          (Ha nincs buszkeresési találat, írd: "Ehhez az eseményhez nem található közvetlen buszjárat.")]

        🌤️ IDŐJÁRÁS ÉS JAVASLATOK
        • Helyszín: [Település]
        • Hőmérséklet: [Min] °C – [Max] °C
        • Csapadék esélye: [Százalék]%
        • Javaslat: [Maximum 2 rövid, tárgyilagos mondat az öltözködésről és a közlekedésről az időjárás alapján.]
        """;

  public IReadOnlyList<IAgentTool> Tools =>
  [
      _calendarTool,
        _weatherTool,
        _profileTool,
        _busJourneysTool
  ];
}