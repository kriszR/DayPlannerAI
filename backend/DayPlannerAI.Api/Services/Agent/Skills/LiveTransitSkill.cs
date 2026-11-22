using DayPlannerAI.Api.Services.Tools;

namespace DayPlannerAI.Api.Services.Agent.Skills;

public sealed class LiveTransitSkill : IAgentSkill
{
    private readonly GetTripRealtimeTool _getTripRealtimeTool;

    public LiveTransitSkill(GetTripRealtimeTool getTripRealtimeTool)
    {
        _getTripRealtimeTool = getTripRealtimeTool;
    }

    public string Name => "LiveTransitSkill";

    public string Description =>
    "Magyarországi autóbuszjáratok valós idejű követése, " +
    "GPS-pozíciók és késési információk lekérése.";

    public string Instructions => """
    Ez a képesség a magyarországi autóbuszjáratok valós idejű követésére szolgál.

    Feladataid:
    - Járművek GPS-pozíciójának lekérése.
    - Megállónkénti késési információk lekérése.
    - Járatok aktuális állapotának ellenőrzése.

    Tool használati szabályok:

    1. Ha a felhasználó megad egy pontos járatazonosítót,
       használd a get_trip_realtime eszközt.

    2. Ha a felhasználó nem ad meg járatazonosítót,
       de az előző keresési eredményben szerepelnek járatok,
       használd az ott kapott pontos tripId értéket.

    3. A serviceDate értékét szintén a keresési eredményből vedd át.

    4. Ha a felhasználó azt kérdezi, hogy hol jár most egy busz,
       mindenképpen próbáld meg lekérni a valós idejű adatokat.

    5. Egy keresési eredményből elsőként a legközelebbi
       releváns járatot vizsgáld meg. Ne kérj le automatikusan
       minden járathoz GPS-adatot.

    6. Soha ne használd egy másik busz tripId értékét.

    7. Ha nincs GPS-adat, ezt egyértelműen közöld.

    8. Különböztesd meg a menetrendi és valós idejű adatokat.

    Minden felhasználói válasz magyar nyelvű legyen.
    """;

    public IReadOnlyList<IAgentTool> Tools =>
    [
        _getTripRealtimeTool
    ];
}