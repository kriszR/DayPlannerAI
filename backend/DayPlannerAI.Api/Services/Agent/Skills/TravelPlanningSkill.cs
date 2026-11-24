using DayPlannerAI.Api.Services.Tools;

namespace DayPlannerAI.Api.Services.Agent.Skills;

public sealed class TravelPlanningSkill : IAgentSkill
{
   private readonly SearchBusJourneysTool _searchBusJourneysTool;

   public TravelPlanningSkill(
       SearchBusJourneysTool searchBusJourneysTool)
   {
      _searchBusJourneysTool = searchBusJourneysTool;
   }

   public string Name => "TravelPlanningSkill";

   public string Description =>
       "Utazások tervezése és menetrendi járatok keresése.";

   public string Instructions => """
        Utazási kérésnél az alábbi szabályokat kövesd.

        - A teljes beszélgetésből vedd át a korábban megadott célállomást és indulási helyet.
        - A felhasználó legutóbbi pontosítása mindig felülírja a korábbi adatot.
        - "Most szeretnék indulni" esetén a searchMode = "departAfter",
          a departureTime = "now", az utazás dátuma pedig a rendszerben megadott mai dátum.
        - Konkrét indulási idő esetén searchMode = "departAfter".
        - Konkrét érkezési határidő esetén searchMode = "arriveBy".
        - Ha az indulási hely hiányzik, hívd meg a get_user_profile eszközt,
          és a homeLocation mezőből vedd át a települést. Ha nem egyértelmű,
          pontosan ezt a kérdést tedd fel: "Honnan szeretnél indulni?" Ne használd a "Hol szeretnél indulni?" változatot.
        - Ha a célállomás hiányzik, röviden és természetesen kérdezz vissza.
        - Visszakérdezéskor ne magyarázd a belső gondolkodásodat, és ne írd le,
          hogy "meg kell kérdeznem", "mivel nem adtad meg" vagy hasonló fordulatokat.
        - Ha minden szükséges adat rendelkezésre áll, AZONNAL hívd meg a
          search_bus_journeys eszközt. Ne küldj előzetes olyan üzenetet,
          hogy "keresem a járatokat" vagy "kérlek, várj".
        - Ne találj ki települést, dátumot, időpontot vagy járatot.
        - A válaszod kizárólag a keresőeszköz tényleges eredményére épüljön.
        - Ha nincs közvetlen járat, csak akkor állítsd, hogy átszállásos
          keresés is történt, ha az eszköz eredménye ezt jelzi.
        - Valós idejű GPS- vagy késési kérésnél használd a LiveTransitSkill
          eszközt is; a menetrendi adatot különböztesd meg a valós idejű adattól.

        Mindig magyarul válaszolj, tömören.
        """;

   public IReadOnlyList<IAgentTool> Tools =>
   [
       _searchBusJourneysTool
   ];
}