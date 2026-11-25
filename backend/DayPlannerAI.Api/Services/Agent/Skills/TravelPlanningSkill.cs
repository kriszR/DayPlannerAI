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
        - A "ma", "holnap" és "holnapután" dátumát a rendszerben megadott
          budapesti dátumhoz viszonyítsd.
        - "Most szeretnék indulni" esetén a searchMode = "departAfter",
          a departureTime = "now", az utazás dátuma pedig a rendszerben megadott mai dátum.
        - Konkrét indulási idő esetén searchMode = "departAfter".
        - Konkrét érkezési határidő esetén searchMode = "arriveBy".
          Például: "10 órára kell odaérnem" vagy "legkésőbb 10-re legyek ott"
          esetén arrivalTime = "10:00"; ne kérdezz indulási időt.
        - Ha az indulási hely, a célállomás és az utazási dátum ismert, de
          nincs konkrét indulási idő vagy érkezési határidő, ne indíts keresést;
          kérdezd meg: "Mikor szeretnél indulni?"
        - Az érkezési idő határidő: olyan járatot válassz, amely legkésőbb
          ekkor érkezik. Később érkező járat nem megfelelő, akkor sem,
          ha csak néhány perccel későbbi.
        - A felhasználó felkészülési ideje (például 30 perc) nem az utazási
          idő, nem a busz indulása, és nem az otthonról megállóhoz indulás.
          Ne vond le a felkészülési időt a busz indulásából, és ne állítsd,
          hogy a felhasználónak ekkor kell elindulnia otthonról.
        - Külön kezeld ezeket: felkészülés kezdete, otthonról a megállóhoz
          indulás, busz indulása, célállomásra érkezés. Csak akkor számolj
          otthonról indulási időt, ha az otthon és a megálló közötti menetidő
          ténylegesen rendelkezésre áll. Ha nincs, ezt ne találd ki.
        - Önálló utazási kérésnél ne említs naptáreseményt, online workshopot,
          időjárást vagy más, a kért utazáshoz nem szükséges információt.
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
          Az érkezési határidőt és a járat tényleges érkezését mindig vesd össze.
          Ha nincs határidőn belüli találat, mondd ki egyértelműen; ne ajánlj
          később érkező járatot megfelelő megoldásként.
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