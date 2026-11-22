using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DayPlannerAI.Api.DTOs;

namespace DayPlannerAI.Api.Services.Agent;

public sealed class OllamaAgentService
{
    private const int MaxToolRounds = 4;

    private readonly HttpClient _httpClient;
    private readonly AgentSkillRegistry _registry;
    private readonly ILogger<OllamaAgentService> _logger;
    private readonly string _model;

    private readonly JsonSerializerOptions _jsonOptions =
        new(JsonSerializerDefaults.Web);

    public OllamaAgentService(
        HttpClient httpClient,
        IConfiguration configuration,
        AgentSkillRegistry registry,
        ILogger<OllamaAgentService> logger)
    {
        _httpClient = httpClient;
        _registry = registry;
        _logger = logger;

        var baseUrl = configuration["Ollama:BaseUrl"]
            ?? "http://localhost:11434";

        _httpClient.BaseAddress = new Uri(
            $"{baseUrl.TrimEnd('/')}/");

        _httpClient.Timeout = TimeSpan.FromMinutes(3);

        _model = configuration["Ollama:Model"]
            ?? "qwen3.5:9b";
    }

    public async Task<AgentChatResult> ChatAsync(
    IReadOnlyList<AiChatMessageRequest> conversation,
    string? selectedTripContext = null,
    CancellationToken cancellationToken = default)
    {
        if (conversation.Count == 0)
            throw new ArgumentException("Conversation cannot be empty.");

        var messages = new List<JsonObject>

    {
        new()
        {
            ["role"] = "system",
            ["content"] = BuildSystemPrompt(selectedTripContext)
        }
    };
        var executedToolResults = new List<AgentToolExecutionResult>();

        foreach (var message in conversation.TakeLast(6))
        {
            if (message.Role is not ("user" or "assistant"))
                continue;

            if (string.IsNullOrWhiteSpace(message.Content))
                continue;

            messages.Add(new JsonObject
            {
                ["role"] = message.Role,
                ["content"] = message.Content
            });
        }

        // Allow multiple tool execution and model response rounds.
        for (var round = 0; round < MaxToolRounds; round++)
        {
            using var document = await SendChatRequestAsync(
                messages,
                includeTools: true,
                cancellationToken);

            if (!document.RootElement.TryGetProperty(
                    "message",
                    out var assistantMessage))
            {
                throw new InvalidOperationException(
                    "Ollama response does not contain a message.");
            }

            // Preserve the complete assistant response, including tool_calls.
            var assistantNode = JsonNode.Parse(
                assistantMessage.GetRawText()) as JsonObject;

            if (assistantNode is null)
                throw new InvalidOperationException(
                    "Invalid Ollama assistant message.");

            messages.Add(assistantNode);

            if (!assistantMessage.TryGetProperty(
                    "tool_calls",
                    out var toolCalls) ||
                toolCalls.ValueKind != JsonValueKind.Array ||
                toolCalls.GetArrayLength() == 0)
            {
                return new AgentChatResult(
    GetMessageContent(assistantMessage),
    executedToolResults);
            }

            // Execute every requested tool sequentially.
            var directAnswers = new List<string>();

            var allToolsCanReturnDirectly = true;

            foreach (var toolCall in toolCalls.EnumerateArray())
            {
                var toolResult = await ExecuteToolCallAsync(
                    toolCall,
                    cancellationToken);

                var functionName = GetToolName(toolCall);
                executedToolResults.Add(
    new AgentToolExecutionResult(
        functionName,
        toolResult));

                if (_registry.TryGetTool(functionName, out var tool))
                {
                    var directAnswer = tool.BuildUserResponse(toolResult);

                    if (!string.IsNullOrWhiteSpace(directAnswer))
                    {
                        directAnswers.Add(directAnswer);
                    }
                    else
                    {
                        allToolsCanReturnDirectly = false;
                    }
                }
                else
                {
                    allToolsCanReturnDirectly = false;
                }
                var modelToolResult = BuildModelFacingToolResult(
                    functionName,
                    toolResult);
                _logger.LogInformation(
                "Tool {ToolName}: original result {OriginalLength} chars, model result {ModelLength} chars.",
                functionName,
                toolResult.Length,
                modelToolResult.Length);
                messages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_name"] = functionName,
                    ["content"] = modelToolResult
                });
            }

            // Skip another LLM generation when every tool has produced
            // a complete, deterministic user-facing answer.
            if (allToolsCanReturnDirectly &&
                directAnswers.Count == toolCalls.GetArrayLength())
            {
                return new AgentChatResult(
            string.Join(
                Environment.NewLine + Environment.NewLine,
                directAnswers),
            executedToolResults);
            }
        }
        _logger.LogInformation(
            "Sending tool results back to Ollama. Messages: {MessageCount}, Tool result sizes: {ToolResultSizes}",
            messages.Count,
            string.Join(", ", executedToolResults.Select(
                x => $"{x.ToolName}: {x.ResultJson.Length} characters")));
        // Ask the model for a final answer without allowing additional tools.
        using var finalDocument = await SendChatRequestAsync(
            messages,
            includeTools: false,
            cancellationToken);

        if (finalDocument.RootElement.TryGetProperty(
                "message",
                out var finalMessage))
        {
            var content = GetMessageContent(finalMessage);

            if (!string.IsNullOrWhiteSpace(content))
                return new AgentChatResult(
    content,
    executedToolResults);
        }

        return new AgentChatResult(
    "A lekérdezést elvégeztem, de nem sikerült választ összeállítanom.",
    executedToolResults);
    }

    private async Task<string> ExecuteToolCallAsync(
        JsonElement toolCall,
        CancellationToken cancellationToken)
    {
        var function = toolCall.GetProperty("function");

        var name = GetString(function, "name");

        if (string.IsNullOrWhiteSpace(name))
            return CreateToolError("Missing tool name.");

        if (!function.TryGetProperty(
                "arguments",
                out var arguments))
        {
            return CreateToolError("Missing tool arguments.");
        }

        if (arguments.ValueKind != JsonValueKind.Object)
            return CreateToolError("Tool arguments must be a JSON object.");

        if (!_registry.TryGetTool(name, out var tool))
        {
            _logger.LogWarning(
                "Ollama requested unknown tool: {ToolName}",
                name);

            return CreateToolError("Unknown tool.");
        }

        try
        {
            using var argumentsDocument = JsonDocument.Parse(
                arguments.GetRawText());

            _logger.LogInformation(
                "Executing AI tool: {ToolName}",
                name);

            var result = await tool.ExecuteAsync(
                argumentsDocument.RootElement,
                cancellationToken);

            _logger.LogInformation(
                "AI tool completed: {ToolName}",
                name);

            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "AI tool execution failed: {ToolName}",
                name);

            return CreateToolError(
                "The tool execution failed. The requested data could not be retrieved.");
        }
    }

    private async Task<JsonDocument> SendChatRequestAsync(
        List<JsonObject> messages,
        bool includeTools,
        CancellationToken cancellationToken)
    {
        var request = new JsonObject
        {
            ["model"] = _model,
            ["messages"] = JsonSerializer.SerializeToNode(
                messages,
                _jsonOptions),
            ["stream"] = false,
            ["think"] = false,
            ["keep_alive"] = "30m",
            ["options"] = new JsonObject
            {
                ["temperature"] = 0.2,
                ["top_p"] = 0.8,
                ["top_k"] = 20,
                ["num_ctx"] = 4096,
                ["num_predict"] = 256
            }
        };

        if (includeTools)
        {
            request["tools"] = JsonSerializer.SerializeToNode(
                _registry.GetOllamaToolDefinitions(),
                _jsonOptions);
        }

        using var response = await _httpClient.PostAsJsonAsync(
    "api/chat",
    request,
    _jsonOptions,
    cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Ollama returned HTTP {StatusCode}. Response: {ResponseBody}",
                (int)response.StatusCode,
                responseBody);

            throw new HttpRequestException(
                $"Ollama API error ({(int)response.StatusCode}): {responseBody}");
        }

        var document = JsonDocument.Parse(responseBody);

        if (document.RootElement.TryGetProperty(
                "error",
                out var error))
        {
            var message = error.GetString()
                ?? "Unknown Ollama error.";

            document.Dispose();

            throw new HttpRequestException(
                $"Ollama API error: {message}");
        }

        return document;
    }

    private string BuildSystemPrompt(string? selectedTripContext)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows()
                ? "Central Europe Standard Time"
                : "Europe/Budapest");

        var now = TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            timezone);

        return $"""
        Te a DayPlannerAI személyi AI-asszisztense vagy.

        AKTUÁLIS IDŐ:
        {now:yyyy-MM-dd HH:mm:ss}

        IDŐZÓNA:
        Europe/Budapest

        A felhasználóval mindig magyarul kommunikálj.

        Az elérhető képességeid:

        {_registry.BuildSkillInstructions()}

        Beszélgetési szabályok:
        - Értelmezd a teljes beszélgetési előzményt.
        - A legutóbbi, egyértelmű felhasználói pontosítás felülírja a korábbi értéket.
        - Ha a felhasználó most akar indulni, használj aktuális budapesti időpontot.
        - Ha utazást kér, használd a TravelPlanningSkill képességet.
        - Ha valós idejű közlekedési adatot kér, használd a LiveTransitSkill képességet.
        - Ne találj ki járatokat, időpontokat, GPS-adatokat.
        - A Toolok tényleges eredményeire támaszkodj.
        - Ha egy szükséges adat hiányzik, kérdezz vissza.

        Minden felhasználói válasz magyar nyelvű legyen.

        KRITIKUS ADATMEGBÍZHATÓSÁGI SZABÁLYOK:

        - A Toolok eredményei a tények egyetlen hiteles forrásai.
        - Soha ne találj ki Google Calendar eseményeket.
        - Kizárólag a get_calendar_events által visszaadott
        events tömbben szereplő eseményeket jelenítheted meg.
        - Ha az events tömb üres, a naptárban nincs esemény.
        - Ha egy esemény szerepel, nem egészítheted ki más eseményekkel.
        - Nem találhatsz ki helyszínt, időpontot, résztvevőt vagy leírást.
        - A summary, start és end mezőket különálló adatként kezeld.
        - Ha a Tool nem adott vissza egy információt, jelezd, hogy nincs adat.

        {selectedTripContext}
        """;
    }

    private static string BuildModelFacingToolResult(
        string toolName,
        string toolResult)
    {
        if (string.Equals(
        toolName,
        "get_calendar_events",
        StringComparison.OrdinalIgnoreCase))
        {
            using var doc = JsonDocument.Parse(toolResult);

            var r = doc.RootElement;
            var events = r.GetProperty("events");

            var compactEvents = new JsonArray();

            foreach (var item in events.EnumerateArray())
            {
                compactEvents.Add(new JsonObject
                {
                    ["summary"] = GetString(item, "summary"),
                    ["start"] = GetString(item, "start"),
                    ["end"] = GetString(item, "end"),
                    ["location"] = GetString(item, "location"),
                    ["isAllDay"] = item.TryGetProperty(
                        "isAllDay", out var allDay) &&
                        allDay.ValueKind == JsonValueKind.True
                });
            }

            return new JsonObject
            {
                ["success"] = true,
                ["source"] = "Google Calendar",
                ["requestedDate"] = GetString(r, "requestedDate"),
                ["eventCount"] = events.GetArrayLength(),
                ["events"] = compactEvents,
                ["instruction"] =
                    "Only these exact calendar events exist in this result. " +
                    "Do not invent or add events."
            }.ToJsonString();
        }

        if (!string.Equals(
                toolName,
                "search_bus_journeys",
                StringComparison.OrdinalIgnoreCase))
        {
            return toolResult;
        }

        using var document = JsonDocument.Parse(toolResult);

        var root = document.RootElement;

        var compact = new JsonObject
        {
            ["success"] = true
        };

        if (root.TryGetProperty("travelPlan", out var plan))
        {
            compact["travelPlan"] = JsonNode.Parse(
                plan.GetRawText());
        }

        if (root.TryGetProperty("resultCount", out var count))
        {
            compact["resultCount"] = count.GetInt32();
        }

        var compactRoutes = new JsonArray();

        if (root.TryGetProperty(
                "availableRoutes",
                out var routes) &&
            routes.ValueKind == JsonValueKind.Array)
        {
            foreach (var route in routes.EnumerateArray().Take(3))
            {
                compactRoutes.Add(new JsonObject
                {
                    ["tripId"] = GetString(route, "tripId"),
                    ["serviceDate"] = GetString(route, "serviceDate"),

                    ["routeNumber"] = GetString(route, "routeShortName")
        ?? GetString(route, "routeLongName")
        ?? "Ismeretlen",

                    ["departureStop"] = GetString(route, "departureStop"),
                    ["departureTime"] = GetString(route, "departureTime"),
                    ["arrivalStop"] = GetString(route, "arrivalStop"),
                    ["arrivalTime"] = GetString(route, "arrivalTime"),
                    ["durationMinutes"] = GetInt(route, "durationMinutes")
                });
            }
        }

        compact["availableRoutes"] = compactRoutes;

        var compactJourneys = new JsonArray();

        if (root.TryGetProperty(
                "availableJourneys",
                out var journeys) &&
            journeys.ValueKind == JsonValueKind.Array)
        {
            foreach (var journey in journeys.EnumerateArray().Take(2))
            {
                var legs = new JsonArray();

                if (journey.TryGetProperty("legs", out var journeyLegs))
                {
                    foreach (var leg in journeyLegs.EnumerateArray())
                    {
                        legs.Add(new JsonObject
                        {
                            ["tripId"] = GetString(leg, "tripId"),
                            ["routeNumber"] = GetString(leg, "routeShortName"),
                            ["departureStop"] = GetString(leg, "departureStop"),
                            ["departureTime"] = GetString(leg, "departureTime"),
                            ["arrivalStop"] = GetString(leg, "arrivalStop"),
                            ["arrivalTime"] = GetString(leg, "arrivalTime")
                        });
                    }
                }

                compactJourneys.Add(new JsonObject
                {
                    ["departureTime"] = GetString(journey, "departureTime"),
                    ["arrivalTime"] = GetString(journey, "arrivalTime"),
                    ["durationMinutes"] = GetInt(journey, "totalDurationMinutes"),
                    ["transferCount"] = GetInt(journey, "transferCount"),
                    ["legs"] = legs
                });
            }
        }

        compact["availableJourneys"] = compactJourneys;

        compact["instruction"] =
            "A megadott járatok valós menetrendi találatok. " +
            "Ezek alapján válaszolj magyarul. Ne találj ki adatokat.";

        return compact.ToJsonString();
    }

    private static int GetInt(
        JsonElement element,
        string property)
    {
        return element.TryGetProperty(property, out var value) &&
               value.TryGetInt32(out var result)
            ? result
            : 0;
    }

    private static string GetToolName(JsonElement toolCall)
    {
        if (toolCall.TryGetProperty("function", out var function))
            return GetString(function, "name") ?? "unknown";

        return "unknown";
    }

    private static string GetMessageContent(JsonElement message)
    {
        if (message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string? GetString(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static string CreateToolError(string message)
    {
        return JsonSerializer.Serialize(new
        {
            success = false,
            error = message
        });
    }
}