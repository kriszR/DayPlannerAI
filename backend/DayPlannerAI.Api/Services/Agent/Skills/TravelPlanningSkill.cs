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
        Ez a képesség autóbuszos utazások megtervezésére szolgál.

        Feladataid:
        - Értelmezd a felhasználó természetes nyelvű utazási kérését.
        - Azonosítsd az indulási és céltelepülést.
        - Értsd meg, hogy a felhasználó mikor akar indulni,
          vagy mikorra akar megérkezni.
        - Használd a keresési Toolt a tényleges járatok lekéréséhez.

        Keresési szabályok:

        1. Ha a felhasználó azt mondja, hogy most akar indulni,
           a searchMode legyen departAfter, a departureTime pedig "now".

        2. Ha a felhasználó indulási időpontot ad meg,
           a searchMode legyen departAfter.

        3. Ha a felhasználó érkezési határidőt ad meg,
           a searchMode legyen arriveBy.

        4. A mai nap, holnap és holnapután értelmezéséhez
           használd a rendszerben megadott aktuális dátumot.

        5. Használd a korábbi beszélgetés releváns adatait,
           ha a felhasználó nem adott meg új értéket.

        6. Ha a felhasználó új indulási települést nevez meg,
           az új helyszínt használd a korábbi helyett.

        7. Ha nem világos az indulási hely vagy a cél,
           kérdezz vissza. Ne találj ki településeket.

        8. Mindig a tényleges Tool-eredmények alapján válaszolj.

        9. Ha nincs megfelelő közvetlen járat,
           jelezd, hogy átszállásos keresést is végeztél,
           ha a Tool ezt valóban megtette.
         10.    Ha a felhasználó GPS-pozíciót, aktuális járműhelyzetet
         vagy késési adatokat is kér:
         - Először keresd meg a megfelelő járatokat.
         - A keresési eredményből vedd át a pontos tripId és serviceDate értékeket.
         - Ezután hívd meg a get_trip_realtime eszközt.
         - A keresési és valós idejű eredményeket együtt értelmezd.
         - Ne állítsd, hogy egy busz már úton van, ha nincs hozzá
         megfelelő valós idejű információd.

        Minden válasz magyar nyelvű legyen.
        """;

   public IReadOnlyList<IAgentTool> Tools =>
   [
       _searchBusJourneysTool
   ];
}