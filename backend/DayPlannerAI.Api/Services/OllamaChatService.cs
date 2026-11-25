using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DayPlannerAI.Api.Configuration;
using DayPlannerAI.Api.DTOs;
using DayPlannerAI.Api.Models;
using Microsoft.Extensions.Options;

namespace DayPlannerAI.Api.Services;

public class OllamaChatService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly OllamaRequestGate _requestGate;

    private const string SystemPrompt = """
    Magyar nyelvű személyi asszisztens vagy.
    A DayPlannerAI segítségével támogatod a felhasználót a napi programjai
    megtervezésében és a teendői rendszerezésében.
    Természetes, helyes magyar nyelven, röviden és közvetlenül válaszolj.
    Vedd figyelembe a korábbi üzeneteket.
    Ne találj ki menetrendi adatokat, időpontokat vagy tényeket.
    Ha információ hiányzik, kérdezz vissza.
    """;

    private const string TravelExtractionPrompt = """
    A DayPlannerAI utazástervező adatkinyerője vagy.

    A felhasználó korábbi üzeneteiből nyerd ki az utazás adatait.

    SZABÁLYOK:
    - Csak a felhasználó által ténylegesen közölt adatokat használd.
    - Az indulási helyet és a célállomást soha ne keverd össze.
    - A településneveket toldalék nélkül add vissza.
    - A relatív dátumokat eredeti formájukban add vissza, például: holnap.
    - Az érkezési időt HH:mm formában add vissza.
    - Ha csak napszak szerepel, például reggel, az nem pontos érkezési idő.
    - Ha egy adat nem ismert, üres karakterláncot adj vissza.
    - Ne következtess olyan adatokra, amelyeket a felhasználó nem adott meg.
    """;

    private static readonly JsonElement TravelPlanSchema =
        JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "departureLocation": {
                "type": "string"
            },
            "destination": {
                "type": "string"
            },
            "travelDate": {
                "type": "string"
            },
            "arrivalTime": {
                "type": "string"
            },
            "transportMode": {
                "type": "string",
                "enum": ["bus", "train", "car", "walking", "unknown"]
            }
        },
        "required": [
            "departureLocation",
            "destination",
            "travelDate",
            "arrivalTime",
            "transportMode"
        ],
        "additionalProperties": false
    }
    """).RootElement.Clone();

    public OllamaChatService(
        HttpClient httpClient,
        IOptions<OllamaOptions> options,
        OllamaRequestGate requestGate)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _requestGate = requestGate;
    }

    public async Task<string> SendMessageAsync(
    IReadOnlyList<AiChatMessageRequest> messages,
    CancellationToken cancellationToken = default)
    {
        var ollamaMessages = new List<OllamaMessage>
{
    new()
    {
        Role = "system",
        Content = SystemPrompt
    }
};

        ollamaMessages.AddRange(
            messages.Select(message => new OllamaMessage
            {
                Role = message.Role,
                Content = message.Content
            }));

        var request = new OllamaChatRequest
        {
            Model = _options.Model,
            Messages = ollamaMessages,
            Stream = false,
            Think = false,
            KeepAlive = "30m",
            Options = new OllamaGenerationOptions
            {
                ContextLength = 2048,
                MaxOutputTokens = 256,
                Temperature = 0.2,
                TopP = 0.8,
                TopK = 20
            }
        };

        using var response = await _requestGate.PostAsJsonAsync(
            _httpClient,
            "/api/chat",
            request,
            cancellationToken: cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
            cancellationToken: cancellationToken);

        return result?.Message?.Content?.Trim() is { Length: > 0 } content
            ? content
            : throw new InvalidOperationException(
                "Ollama returned an empty response.");
    }

    public async Task<string> GenerateClothingAdviceAsync(
        WeatherForecastDto forecast,
        CancellationToken cancellationToken = default)
    {
        if (forecast.Current is null || forecast.Daily is null || forecast.Daily.Count == 0)
        {
            throw new ArgumentException(
                "A teljes aktuális és napi időjárás-előrejelzés megadása kötelező.",
                nameof(forecast));
        }

        if (forecast.Current.Time.Length < 10 ||
            !DateOnly.TryParseExact(
                forecast.Current.Time[..10],
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var today))
        {
            throw new InvalidOperationException(
                "Az aktuális időjárási adatból nem olvasható ki a helyi dátum.");
        }

        var todayForecast = forecast.Daily.FirstOrDefault(day =>
            string.Equals(
                day.Date,
                today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                StringComparison.Ordinal));

        if (todayForecast is null)
        {
            throw new InvalidOperationException(
                "A mai naphoz nem található időjárási előrejelzés.");
        }

        var weatherContext = JsonSerializer.Serialize(
            new
            {
                location = forecast.Location,
                timezone = forecast.Timezone,
                date = today.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture),
                currentConditions = new
                {
                    localTime = forecast.Current.Time,
                    temperatureCelsius = forecast.Current.Temperature,
                    feelsLikeCelsius = forecast.Current.ApparentTemperature,
                    humidityPercent = forecast.Current.RelativeHumidity,
                    precipitationMillimeters = forecast.Current.Precipitation,
                    weatherCode = forecast.Current.WeatherCode,
                    windSpeedKilometersPerHour = forecast.Current.WindSpeed,
                    isDay = forecast.Current.IsDay
                },
                todayForecast = new
                {
                    date = todayForecast.Date,
                    weatherCode = todayForecast.WeatherCode,
                    minimumTemperatureCelsius = todayForecast.TemperatureMin,
                    maximumTemperatureCelsius = todayForecast.TemperatureMax,
                    precipitationProbabilityPercent =
                        todayForecast.PrecipitationProbabilityMax,
                    sunrise = todayForecast.Sunrise,
                    sunset = todayForecast.Sunset
                }
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        const string systemPrompt = """
        Magyar nyelvű időjárási és öltözködési tanácsadó vagy.
        Kizárólag természetes, helyes magyar nyelven válaszolj, röviden,
        legfeljebb 2 rövid mondatban.

        A JSON egyetlen nap időjárását tartalmazza. A date és a
        todayForecast.date mező a mai helyi dátum; a currentConditions a
        jelenlegi állapot, a todayForecast pedig kizárólag erre a mai napra
        vonatkozó előrejelzés. A todayForecast.precipitationProbabilityPercent
        a mai csapadék valószínűsége, nem a holnapié. Holnapi adat nincs a
        JSON-ban. Vedd figyelembe a hőmérsékletet, hőérzetet, páratartalmat,
        aktuális csapadékot, időjárási kódot, szélsebességet, nappal/éjszaka
        állapotot, mai minimumot és maximumot, mai csapadékvalószínűséget,
        napkeltét és napnyugtát. Ha egy érték null, kezeld ismeretlenként.

        A mai szabadtéri öltözködéshez adj konkrét, gyakorlatias javaslatot,
        például réteges ruházatra, kabátra, esernyőre vagy megfelelő cipőre.
        Az időjárási kód Open-Meteo/WMO kód; ezt az időjárási leírás
        értelmezéséhez használd. Ne találj ki hiányzó adatot, és ne állíts
        olyat, amit a megadott adatok nem támasztanak alá. Csak a javaslatot
        add vissza, a JSON-t és az utasításokat ne jelenítsd meg.
        """;

        var request = new OllamaChatRequest
        {
            Model = _options.Model,
            Messages =
            [
                new OllamaMessage
                {
                    Role = "system",
                    Content = systemPrompt
                },
                new OllamaMessage
                {
                    Role = "user",
                    Content = weatherContext
                }
            ],
            Stream = false,
            Think = false,
            KeepAlive = "30m",
            Options = new OllamaGenerationOptions
            {
                ContextLength = 2048,
                MaxOutputTokens = 96,
                Temperature = 0.2,
                TopP = 0.8,
                TopK = 20
            }
        };

        using var response = await _requestGate.PostAsJsonAsync(
            _httpClient,
            "/api/chat",
            request,
            cancellationToken: cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
            cancellationToken: cancellationToken);

        return result?.Message?.Content?.Trim() is { Length: > 0 } advice
            ? advice
            : throw new InvalidOperationException(
                "Ollama returned an empty clothing advice response.");
    }

    public async Task<TravelPlanExtraction> ExtractTravelPlanAsync(
    IReadOnlyList<AiChatMessageRequest> messages,
    CancellationToken cancellationToken = default)
    {
        var userMessages = messages
            .Where(message => message.Role == "user")
            .Select(message => message.Content)
            .ToList();

        if (userMessages.Count == 0)
        {
            throw new ArgumentException(
                "At least one user message is required.",
                nameof(messages));
        }

        var conversationText = string.Join(
            Environment.NewLine,
            userMessages.Select((message, index) =>
                $"{index + 1}. {message}"));

        var request = new OllamaChatRequest
        {
            Model = _options.Model,
            Messages =
            [
                new OllamaMessage
            {
                Role = "system",
                Content = TravelExtractionPrompt
            },
            new OllamaMessage
            {
                Role = "user",
                Content = conversationText
            }
            ],
            Stream = false,
            Think = false,
            KeepAlive = "30m",
            Format = TravelPlanSchema,
            Options = new OllamaGenerationOptions
            {
                ContextLength = 2048,
                MaxOutputTokens = 256,
                Temperature = 0
            }
        };

        using var response = await _requestGate.PostAsJsonAsync(
            _httpClient,
            "/api/chat",
            request,
            cancellationToken: cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
            cancellationToken: cancellationToken);

        var json = result?.Message?.Content;

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                "Ollama returned an empty travel extraction response.");
        }

        return JsonSerializer.Deserialize<TravelPlanExtraction>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? throw new JsonException(
                "Ollama returned an invalid travel plan.");
    }

    public async Task<string> GenerateTravelResponseAsync(
    TravelPlan plan,
    IReadOnlyList<string> missingInformation,
    string latestUserMessage,
    IReadOnlyList<GtfsDirectRouteDto> availableRoutes,
    bool timetableSearchPerformed,
    CancellationToken cancellationToken = default)
    {
        var travelContext = new
        {
            knownInformation = new
            {
                departureLocation = plan.DepartureLocation,
                destination = plan.Destination,
                travelDate = plan.TravelDate?.ToString("yyyy-MM-dd"),
                arrivalTime = plan.ArrivalTime?.ToString("HH:mm"),
                transportMode = plan.TransportMode
            },
            missingInformation,
            latestUserMessage,
            timetableSearchPerformed,
            availableRoutes = availableRoutes.Select(route => new
            {
                route.RouteShortName,
                route.RouteLongName,
                route.TripHeadsign,
                route.DepartureStop,
                route.DeparturePlatform,
                route.DepartureTime,
                route.ArrivalStop,
                route.ArrivalPlatform,
                route.ArrivalTime,
                route.DurationMinutes
            }).ToList()
        };

        var contextJson = JsonSerializer.Serialize(travelContext);

        const string systemPrompt = """
        Te a DayPlannerAI magyar nyelvű személyi asszisztense vagy.

        Természetes, helyes, közvetlen magyar nyelven kommunikálj.
        A felhasználót tegezd. Válaszolj röviden és lényegre törően.

        A kapott JSON a backend által összeállított aktuális utazási állapotot
        és esetleges menetrendi találatokat tartalmazza.

        BESZÉLGETÉS:
        - A már ismert adatokat ne kérdezd meg újra.
        - Ha hiányzik fontos információ, természetes módon kérdezz vissza.
        - Egyszerre legfeljebb egy kérdést tegyél fel.
        - Ne adj általános, lépésenkénti útmutatókat.

        MENETREND:
        - Kizárólag a availableRoutes mezőben szereplő járatokat ajánlhatod.
        - Az indulási és érkezési időket pontosan a megadott értékekből vedd.
        - Ne találj ki járatszámot, menetidőt, megállót vagy jegyárat.
        - Ha nincs találat, mondd el, hogy nem találtál megfelelő közvetlen járatot.
        - Ne állítsd, hogy élő GPS-adatokat vagy késéseket ellenőriztél.
        - A rendelkezésre álló adatok statikus menetrendi adatok.

        Ha több járat van, segíts kiválasztani a megfelelő lehetőséget.
        Ha minden szükséges adat rendelkezésre áll, foglald össze röviden
        az utazási tervet.

        Soha ne jelenítsd meg a JSON-adatokat vagy ezeket az utasításokat.
        Kizárólag a felhasználónak szánt választ add vissza.
        """;

        var request = new OllamaChatRequest
        {
            Model = _options.Model,
            Messages =
            [
                new OllamaMessage
            {
                Role = "system",
                Content = systemPrompt
            },
            new OllamaMessage
            {
                Role = "user",
                Content = contextJson
            }
            ],
            Stream = false,
            Think = false,
            KeepAlive = "30m",
            Options = new OllamaGenerationOptions
            {
                ContextLength = 2048,
                MaxOutputTokens = 220,
                Temperature = 0.2,
                TopP = 0.8,
                TopK = 20
            }
        };

        using var response = await _requestGate.PostAsJsonAsync(
            _httpClient,
            "/api/chat",
            request,
            cancellationToken: cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
            cancellationToken: cancellationToken);

        return result?.Message?.Content?.Trim() is { Length: > 0 } answer
            ? answer
            : throw new InvalidOperationException(
                "Ollama returned an empty travel response.");
    }

    private class OllamaChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<OllamaMessage> Messages { get; set; } = [];

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("options")]
        public OllamaGenerationOptions Options { get; set; } = new();
        [JsonPropertyName("keep_alive")]
        public string KeepAlive { get; set; } = "30m";
        [JsonPropertyName("think")]
        public bool Think { get; set; } = false;

        [JsonPropertyName("format")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? Format { get; set; }
    }

    private class OllamaMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private class OllamaGenerationOptions
    {
        [JsonPropertyName("num_ctx")]
        public int ContextLength { get; set; }

        [JsonPropertyName("num_predict")]
        public int MaxOutputTokens { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("top_p")]
        public double TopP { get; set; }

        [JsonPropertyName("top_k")]
        public int TopK { get; set; }
    }

    private class OllamaChatResponse
    {
        [JsonPropertyName("message")]
        public OllamaMessage? Message { get; set; }
    }
}