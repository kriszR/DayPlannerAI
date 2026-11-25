using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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

        _httpClient.Timeout = TimeSpan.FromMinutes(6);

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

        var activeContext = DetermineActiveContext(conversation);
        var isDailyPlanning = activeContext.SkillNames.Contains(
            "DailyPlanningSkill",
            StringComparer.OrdinalIgnoreCase);
        DateOnly? targetDailyPlanDate = isDailyPlanning ? GetDailyPlanDate(conversation) : null;
        var messages = new List<JsonObject>
        {
            new()
            {
                ["role"] = "system",
                ["content"] = BuildSystemPrompt(
                    selectedTripContext,
                    activeContext.SkillNames,
                    activeContext.IsTravelRequest,
                    isDailyPlanning,
                    targetDailyPlanDate)
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

        var modelToolNames = activeContext.ToolNames;

        if (isDailyPlanning)
        {
            await AddDailyPlanToolResultsAsync(
                conversation,
                messages,
                executedToolResults,
                cancellationToken);

            // Daily planning is a server-orchestrated workflow:
            // all data (including per-event bus searches) is gathered first,
            // then the model synthesizes the plan without selecting tools.
            using var dailyPlanDocument = await SendChatRequestAsync(
                messages,
                includeTools: false,
                requestStage: "daily-plan synthesis",
                cancellationToken);

            if (dailyPlanDocument.RootElement.TryGetProperty(
                    "message",
                    out var dailyPlanMessage))
            {
                var dailyPlanAnswer = GetMessageContent(dailyPlanMessage);
                if (!string.IsNullOrWhiteSpace(dailyPlanAnswer))
                {
                    return new AgentChatResult(
                        dailyPlanAnswer,
                        executedToolResults);
                }
            }

            return new AgentChatResult(
                "A napi terv adatainak lekérése sikerült, de a terv összeállítása nem sikerült.",
                executedToolResults);
        }

        // Allow multiple tool execution and model response rounds.
        // If the model skips a required bus search, retry once with only the
        // bus-search tool available instead of silently returning an answer.
        var forceBusSearch = false;

        for (var round = 0; round < MaxToolRounds; round++)
        {
            var isForcedBusSearchRound = forceBusSearch;
            IReadOnlyCollection<string>? availableToolNames = forceBusSearch
                ? new[] { "search_bus_journeys" }
                : modelToolNames;

            using var document = await SendChatRequestAsync(
                messages,
                includeTools: true,
                requestStage: forceBusSearch
                    ? $"forced bus-search retry round {round + 1}"
                    : $"tool-selection round {round + 1}",
                cancellationToken,
                availableToolNames);

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

            var hasToolCalls = assistantMessage.TryGetProperty(
                    "tool_calls",
                    out var toolCalls) &&
                toolCalls.ValueKind == JsonValueKind.Array &&
                toolCalls.GetArrayLength() > 0;

            _logger.LogInformation(
                "Ollama decision. TravelRequest={TravelRequest}, Round={Round}, " +
                "ToolCallCount={ToolCallCount}, ResponseCharacters={ResponseCharacters}.",
                activeContext.IsTravelRequest,
                round + 1,
                hasToolCalls ? toolCalls.GetArrayLength() : 0,
                GetMessageContent(assistantMessage).Length);

            if (!hasToolCalls)
            {
                var busSearchWasExecuted = executedToolResults.Any(x =>
                    string.Equals(
                        x.ToolName,
                        "search_bus_journeys",
                        StringComparison.OrdinalIgnoreCase));

                var busSearchIsRequired = activeContext.IsTravelRequest;

                var responseContent = GetMessageContent(assistantMessage);

                if (busSearchIsRequired &&
                    !busSearchWasExecuted &&
                    IsClarificationQuestion(responseContent))
                {
                    return new AgentChatResult(
                        responseContent,
                        executedToolResults);
                }

                if (busSearchIsRequired &&
                    !busSearchWasExecuted &&
                    !forceBusSearch &&
                    round < MaxToolRounds - 1)
                {
                    _logger.LogWarning(
                        "A required bus search was skipped. Retrying with only the bus-search tool enabled.");

                    messages.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = "A menetrendi keresés még nem történt meg. " +
                            "Most kizárólag a search_bus_journeys eszközt használd. " +
                            "Az indulási helyet, célállomást, dátumot és időzítési módot " +
                            "a felhasználó korábbi üzeneteiből, a profilt és naptárat " +
                            "pedig a korábban visszakapott eszközeredményekből vedd át. " +
                            "Ne találj ki adatot. Ha valamelyik kötelező adat hiányzik, " +
                            "beleértve a konkrét indulási időt vagy érkezési határidőt, " +
                            "ne indíts keresést, hanem kérdezz vissza egyetlen konkrét kérdéssel. " +
                            "Ha minden adat rendelkezésre áll, hívd meg most az eszközt."
                    });

                    forceBusSearch = true;
                    continue;
                }

                if (busSearchIsRequired && !busSearchWasExecuted)
                {
                    _logger.LogWarning(
                        "A required bus search was not executed after the forced retry.");
                }

                // A travel answer containing timetable claims is only allowed
                // after the bus-search tool has actually run. If the model
                // skips the tool, fail closed instead of inventing a journey.
                if (busSearchIsRequired &&
                    !executedToolResults.Any(x => string.Equals(
                        x.ToolName,
                        "search_bus_journeys",
                        StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrWhiteSpace(responseContent) &&
                    !IsClarificationQuestion(responseContent))
                {
                    _logger.LogWarning(
                        "Travel response blocked because search_bus_journeys was not executed.");

                    responseContent =
                        "Nem tudok ellenőrzött buszjáratot megjeleníteni, mert a menetrendi keresés nem futott le. " +
                        "Kérlek, ismételd meg az utazási kérést.";
                }

                return new AgentChatResult(
                    responseContent,
                    executedToolResults);
            }

            // Execute every requested tool sequentially.
            var directAnswers = new List<string>();

            var allToolsCanReturnDirectly = true;
            var allToolsCanShortCircuit = true;

            foreach (var toolCall in toolCalls.EnumerateArray())
            {
                var functionName = GetToolName(toolCall);
                if (activeContext.IsTravelRequest &&
                    string.Equals(
                        functionName,
                        "search_bus_journeys",
                        StringComparison.OrdinalIgnoreCase) &&
                    !HasExplicitTravelTime(conversation))
                {
                    return new AgentChatResult(
                        "Mikor szeretnél indulni?",
                        executedToolResults);
                }

                var toolResult = await ExecuteToolCallAsync(
                    toolCall,
                    cancellationToken,
                    activeContext.IsTravelRequest ? conversation : null);

                executedToolResults.Add(
    new AgentToolExecutionResult(
        functionName,
        toolResult));

                if (_registry.TryGetTool(functionName, out var tool))
                {
                    allToolsCanShortCircuit &= tool.CanShortCircuit;

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
                    allToolsCanShortCircuit = false;
                }
                var modelToolResult = BuildModelFacingToolResult(
                    functionName,
                    toolResult);

                if (functionName == "get_weather_forecast")
                {
                    _logger.LogWarning(
                        "WEATHER TOOL RESULT: {ToolResult}",
                        toolResult);
                }
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

            // A standalone travel search can return its deterministic answer
            // directly. Daily planning must continue so the model can combine
            // calendar, weather, profile, and bus-search results.
            if (isDailyPlanning)
            {
                allToolsCanReturnDirectly = false;
            }

            if (isForcedBusSearchRound && isDailyPlanning)
                forceBusSearch = false;

            // Skip another LLM generation when every tool has produced
            // a complete, deterministic user-facing answer.
            if (allToolsCanReturnDirectly &&
    allToolsCanShortCircuit &&
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
            requestStage: "final response",
            cancellationToken);

        if (finalDocument.RootElement.TryGetProperty(
                "message",
                out var finalMessage))
        {
            var content = GetMessageContent(finalMessage);

            if (!string.IsNullOrWhiteSpace(content))
            {
                if (activeContext.IsTravelRequest &&
                    !executedToolResults.Any(x => string.Equals(
                        x.ToolName,
                        "search_bus_journeys",
                        StringComparison.OrdinalIgnoreCase)) &&
                    !IsClarificationQuestion(content))
                {
                    _logger.LogWarning(
                        "Final travel response blocked because search_bus_journeys was not executed.");

                    content =
                        "Nem tudok ellenőrzött buszjáratot megjeleníteni, mert a menetrendi keresés nem futott le. " +
                        "Kérlek, ismételd meg az utazási kérést.";
                }

                return new AgentChatResult(content, executedToolResults);
            }
        }

        return new AgentChatResult(
    "A lekérdezést elvégeztem, de nem sikerült választ összeállítanom.",
    executedToolResults);
    }

    private async Task AddDailyPlanToolResultsAsync(
        IReadOnlyList<AiChatMessageRequest> conversation,
        List<JsonObject> messages,
        List<AgentToolExecutionResult> executedToolResults,
        CancellationToken cancellationToken)
    {
        var date = GetDailyPlanDate(conversation);

        var profileArguments = new JsonObject();
        var profileResult = await ExecuteToolAsync(
            "get_user_profile",
            profileArguments,
            cancellationToken);

        var profileTimeZone = GetProfileTimeZone(profileResult);
        var calendarArguments = new JsonObject
        {
            ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(profileTimeZone))
            calendarArguments["timeZone"] = profileTimeZone;

        var weatherArguments = new JsonObject
        {
            ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        // Run independent data requests separately. A failed integration must
        // not prevent the other data sources or the daily-plan synthesis.
        var calendarTask = ExecuteToolAsync(
            "get_calendar_events",
            calendarArguments,
            cancellationToken);

        var weatherTask = ExecuteToolAsync(
            "get_weather_forecast",
            weatherArguments,
            cancellationToken);

        await Task.WhenAll(calendarTask, weatherTask);

        var calendarResult = await calendarTask;
        var weatherResult = await weatherTask;

        var prefetchedResults = new List<(string ToolName, JsonObject Arguments, string ResultJson)>
        {
            ("get_user_profile", profileArguments, profileResult),
            ("get_calendar_events", calendarArguments, calendarResult),
            ("get_weather_forecast", weatherArguments, weatherResult)
        };

        AddPrefetchedToolResultsToMessages(
            prefetchedResults,
            messages,
            executedToolResults);

        // Bus journeys are searched deterministically by the backend for each
        // valid physical calendar event. The LLM does not need to choose tools.
        var busSearches = BuildDailyPlanBusSearches(
            profileResult,
            calendarResult);

        foreach (var busSearch in busSearches)
        {
            var busResult = await ExecuteToolAsync(
                "search_bus_journeys",
                busSearch.Arguments,
                cancellationToken);

            AddPrefetchedToolResultsToMessages(
                [
                    ("search_bus_journeys", busSearch.Arguments, busResult)
                ],
                messages,
                executedToolResults);

            _logger.LogInformation(
                "Daily-plan bus search completed. Event={EventSummary}, " +
                "Destination={Destination}, Date={TravelDate}, Arrival={ArrivalTime}.",
                busSearch.EventSummary,
                busSearch.Arguments["destination"]?.GetValue<string>(),
                busSearch.Arguments["travelDate"]?.GetValue<string>(),
                busSearch.Arguments["arrivalTime"]?.GetValue<string>());
        }
    }

    private void AddPrefetchedToolResultsToMessages(
        IEnumerable<(string ToolName, JsonObject Arguments, string ResultJson)> results,
        List<JsonObject> messages,
        List<AgentToolExecutionResult> executedToolResults)
    {
        foreach (var result in results)
        {
            messages.Add(new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = string.Empty,
                ["tool_calls"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = result.ToolName,
                            ["arguments"] = JsonNode.Parse(
                                result.Arguments.ToJsonString())
                        }
                    }
                }
            });

            executedToolResults.Add(new AgentToolExecutionResult(
                result.ToolName,
                result.ResultJson));

            messages.Add(new JsonObject
            {
                ["role"] = "tool",
                ["tool_name"] = result.ToolName,
                ["content"] = BuildModelFacingToolResult(
                    result.ToolName,
                    result.ResultJson)
            });

            _logger.LogInformation(
                "Daily-plan data tool completed. Tool={ToolName}, ResultCharacters={ResultCharacters}.",
                result.ToolName,
                result.ResultJson.Length);
        }
    }

    private static List<DailyPlanBusSearch> BuildDailyPlanBusSearches(
        string profileResult,
        string calendarResult)
    {
        var searches = new List<DailyPlanBusSearch>();

        try
        {
            using var profileDocument = JsonDocument.Parse(profileResult);
            var profile = profileDocument.RootElement;

            if (!profile.TryGetProperty("success", out var profileSuccess) ||
                profileSuccess.ValueKind != JsonValueKind.True)
            {
                return searches;
            }

            var homeLocation = GetString(profile, "homeLocation");
            if (string.IsNullOrWhiteSpace(homeLocation))
                return searches;

            using var calendarDocument = JsonDocument.Parse(calendarResult);
            var calendar = calendarDocument.RootElement;

            if (!calendar.TryGetProperty("success", out var calendarSuccess) ||
                calendarSuccess.ValueKind != JsonValueKind.True ||
                !calendar.TryGetProperty("events", out var events) ||
                events.ValueKind != JsonValueKind.Array)
            {
                return searches;
            }

            foreach (var calendarEvent in events.EnumerateArray())
            {
                if (calendarEvent.TryGetProperty("isAllDay", out var allDay) &&
                    allDay.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                if (!calendarEvent.TryGetProperty("timeRangeValid", out var validRange) ||
                    validRange.ValueKind != JsonValueKind.True)
                {
                    continue;
                }

                var destination = GetString(calendarEvent, "location");
                var localStart = GetString(calendarEvent, "localStart");
                var summary = GetString(calendarEvent, "summary") ?? "Naptári esemény";

                if (string.IsNullOrWhiteSpace(destination) ||
                    string.IsNullOrWhiteSpace(localStart) ||
                    IsVirtualEvent(summary, destination))
                {
                    continue;
                }

                if (!DateTime.TryParseExact(
                        localStart,
                        "yyyy-MM-dd'T'HH:mm",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var eventStart))
                {
                    continue;
                }

                var eventDate = DateOnly.FromDateTime(eventStart);
                var arrivalTime = TimeOnly.FromDateTime(eventStart);

                var arguments = new JsonObject
                {
                    ["departureLocation"] = homeLocation,
                    ["destination"] = destination,
                    ["travelDate"] = eventDate.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture),
                    ["searchMode"] = "arriveBy",
                    ["arrivalTime"] = arrivalTime.ToString(
                        "HH:mm",
                        CultureInfo.InvariantCulture)
                };

                searches.Add(new DailyPlanBusSearch(summary, arguments));
            }
        }
        catch (JsonException)
        {
            // Keep the planner functional even when a data provider returns
            // malformed JSON. The failed source remains in the model context.
        }

        return searches;
    }

    private static bool IsVirtualEvent(string summary, string location)
    {
        var text = $"{summary} {location}";
        return ContainsAny(
            text,
            "online", "webinar", "webinár", "zoom", "google meet",
            "microsoft teams", "teams meeting", "webex",
            "videókonferencia", "video conference", "virtuális");
    }

    private sealed record DailyPlanBusSearch(
        string EventSummary,
        JsonObject Arguments);

    private async Task<string> ExecuteToolAsync(
        string toolName,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        var toolCall = new JsonObject
        {
            ["function"] = new JsonObject
            {
                ["name"] = toolName,
                ["arguments"] = JsonNode.Parse(arguments.ToJsonString())
            }
        };

        using var document = JsonDocument.Parse(toolCall.ToJsonString());

        return await ExecuteToolCallAsync(
            document.RootElement,
            cancellationToken);
    }

    private static DateOnly GetDailyPlanDate(
        IReadOnlyList<AiChatMessageRequest> conversation)
    {
        var userMessages = string.Join(
            " ",
            conversation
                .Where(message => string.Equals(
                    message.Role,
                    "user",
                    StringComparison.OrdinalIgnoreCase))
                .Select(message => message.Content ?? string.Empty));

        var dates = Regex.Matches(userMessages, @"\b\d{4}-\d{2}-\d{2}\b");
        if (dates.Count > 0)
        {
            var requestedDate = dates[dates.Count - 1].Value;
            if (DateOnly.TryParseExact(
                    requestedDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedDate))
            {
                return parsedDate;
            }

            throw new ArgumentException(
                "The daily plan date must use YYYY-MM-DD format.");
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows()
                ? "Central Europe Standard Time"
                : "Europe/Budapest");

        return DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime);
    }

    private static string? GetProfileTimeZone(string profileResult)
    {
        using var document = JsonDocument.Parse(profileResult);
        var root = document.RootElement;

        return root.TryGetProperty("success", out var success) &&
               success.ValueKind == JsonValueKind.True
            ? GetString(root, "timeZone")
            : null;
    }

    private async Task<string> ExecuteToolCallAsync(
        JsonElement toolCall,
        CancellationToken cancellationToken,
        IReadOnlyList<AiChatMessageRequest>? conversation = null)
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
            var argumentsJson = arguments.GetRawText();
            if (conversation is not null &&
                string.Equals(
                    name,
                    "search_bus_journeys",
                    StringComparison.OrdinalIgnoreCase) &&
                ResolveRelativeTravelDate(conversation) is DateOnly requestedDate)
            {
                var normalizedArguments = JsonNode.Parse(argumentsJson) as JsonObject
                    ?? throw new JsonException(
                        "Bus-search arguments must be a JSON object.");
                var normalizedDateText = requestedDate.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);
                var modelDateText = normalizedArguments["travelDate"]?.GetValue<string>();

                if (!string.Equals(
                        modelDateText,
                        normalizedDateText,
                        StringComparison.Ordinal))
                {
                    _logger.LogWarning(
                        "Relative travel date corrected. ModelDate={ModelDate}, " +
                        "ResolvedDate={ResolvedDate}.",
                        modelDateText,
                        normalizedDateText);
                    normalizedArguments["travelDate"] = normalizedDateText;
                }

                argumentsJson = normalizedArguments.ToJsonString(_jsonOptions);
            }

            using var argumentsDocument = JsonDocument.Parse(
                argumentsJson);

            _logger.LogInformation(
                "Executing AI tool: {ToolName}",
                name);

            if (string.Equals(
                    name,
                    "search_bus_journeys",
                    StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Ollama bus-search arguments: {Arguments}",
                    arguments.GetRawText());
            }

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
        string requestStage,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? toolNames = null)
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
                ["temperature"] = 0.15,
                ["top_p"] = 0.8,
                ["top_k"] = 20,
                ["repeat_penalty"] = 1.1,
                ["num_ctx"] = 6144,
                ["num_predict"] = 650
            }
        };

        if (includeTools)
        {
            request["tools"] = JsonSerializer.SerializeToNode(
                _registry.GetOllamaToolDefinitions(toolNames),
                _jsonOptions);
        }

        var promptCharacters = messages.Sum(
            message => message.ToJsonString(_jsonOptions).Length);
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Sending Ollama request. Stage={Stage}, Model={Model}, " +
            "IncludeTools={IncludeTools}, MessageCount={MessageCount}, " +
            "PromptCharacters={PromptCharacters}, ToolCount={ToolCount}.",
            requestStage,
            _model,
            includeTools,
            messages.Count,
            promptCharacters,
            includeTools
                ? (toolNames is null
                    ? _registry.Tools.Count
                    : _registry.Tools.Count(tool => toolNames.Contains(
                        tool.Name,
                        StringComparer.OrdinalIgnoreCase)))
                : 0);

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.PostAsJsonAsync(
                "api/chat",
                request,
                _jsonOptions,
                cancellationToken);
        }
        catch (TaskCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();

            _logger.LogError(
                exception,
                "Ollama request timed out. Stage={Stage}, Model={Model}, " +
                "ElapsedSeconds={ElapsedSeconds}, MessageCount={MessageCount}, " +
                "PromptCharacters={PromptCharacters}.",
                requestStage,
                _model,
                stopwatch.Elapsed.TotalSeconds,
                messages.Count,
                promptCharacters);

            throw;
        }

        using (response)
        {
            var responseBody = await response.Content.ReadAsStringAsync(
                cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "Ollama request completed. Stage={Stage}, StatusCode={StatusCode}, " +
                "ElapsedSeconds={ElapsedSeconds}, ResponseCharacters={ResponseCharacters}.",
                requestStage,
                (int)response.StatusCode,
                stopwatch.Elapsed.TotalSeconds,
                responseBody.Length);

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
            var root = document.RootElement;

            var content = root
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;

            var doneReason = root.TryGetProperty("done_reason", out var reason)
                ? reason.ToString()
                : "not provided";

            var evalCount = root.TryGetProperty("eval_count", out var eval)
                ? eval.ToString()
                : "not provided";

            var promptEvalCount = root.TryGetProperty(
                "prompt_eval_count",
                out var promptEval)
                    ? promptEval.ToString()
                    : "missing";

            var totalDurationNs = GetLong(root, "total_duration");
            var loadDurationNs = GetLong(root, "load_duration");
            var promptEvalDurationNs = GetLong(root, "prompt_eval_duration");
            var evalDurationNs = GetLong(root, "eval_duration");

            var numPredict = request["options"]?["num_predict"]?.ToString();
            var numCtx = request["options"]?["num_ctx"]?.ToString();

            _logger.LogInformation(
                "OLLAMA DIAGNOSTICS: Stage={Stage}, DoneReason={DoneReason}, " +
                "PromptEvalCount={PromptEvalCount}, EvalCount={EvalCount}, " +
                "NumPredict={NumPredict}, NumCtx={NumCtx}, ContentLength={ContentLength}, " +
                "TotalMs={TotalMs}, LoadMs={LoadMs}, PromptEvalMs={PromptEvalMs}, EvalMs={EvalMs}.",
                requestStage,
                doneReason,
                promptEvalCount,
                evalCount,
                numPredict,
                numCtx,
                content.Length,
                ToMilliseconds(totalDurationNs),
                ToMilliseconds(loadDurationNs),
                ToMilliseconds(promptEvalDurationNs),
                ToMilliseconds(evalDurationNs));

            if (root.TryGetProperty("error", out var error))
            {
                var message = error.GetString()
                    ?? "Unknown Ollama error.";

                document.Dispose();

                throw new HttpRequestException(
                    $"Ollama API error: {message}");
            }

            return document;
        }
    }

    private static long? GetLong(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) &&
               value.TryGetInt64(out var result)
            ? result
            : null;
    }

    private static double? ToMilliseconds(long? nanoseconds)
    {
        return nanoseconds.HasValue
            ? nanoseconds.Value / 1_000_000d
            : null;
    }

    private string BuildSystemPrompt(
         string? selectedTripContext,
         IReadOnlyCollection<string> activeSkillNames,
         bool isTravelRequest,
         bool dailyPlanDataPrefetched,
         DateOnly? targetDailyPlanDate = null)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows()
                ? "Central Europe Standard Time"
                : "Europe/Budapest");

        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timezone);
        var huCulture = CultureInfo.GetCultureInfo("hu-HU");
        var formattedToday = now.ToString("yyyy. MMMM d. (dddd)", huCulture);
        var formattedTargetDate = targetDailyPlanDate.HasValue
            ? targetDailyPlanDate.Value.ToDateTime(TimeOnly.MinValue).ToString("yyyy. MMMM d. (dddd)", huCulture)
            : formattedToday;

        var skillInstructions = _registry.BuildSkillInstructions(activeSkillNames);

        var travelRules = isTravelRequest
            ? """
              UTAZÁSI SZABÁLYOK:
                - A teljes beszélgetésből őrizd meg az előzőleg megadott indulási és céltelepülést.
                - A legutóbbi felhasználói pontosítás csak az általa módosított adatot írja felül; a korábbi indulási helyet, dátumot és időzítési szándékot őrizd meg.
                - Ha a felhasználó csak a célállomást módosítja (például: "inkább Balatonfüredre"), tartsd meg a korábbi indulási helyet és a korábbi időzítési szándékot.
                - "Most szeretnék indulni", "azonnal indulnék" vagy "minél előbb" = searchMode "departAfter", departureTime "now", travelDate a mai dátum.
                - A "now" értéket soha ne helyettesítsd korábbi üzenetből származó fix idővel.
                - A "10 órára kell odaérnem" vagy "legkésőbb 10-re legyek ott" érkezési határidő: searchMode "arriveBy", arrivalTime "10:00". Ilyenkor ne kérdezz indulási időt.
                - Ha az indulási hely, a célállomás és az utazási dátum ismert, de nincs konkrét indulási idő vagy érkezési határidő, ne indíts keresést; kérdezd meg: "Mikor szeretnél indulni?"
                - Ha minden kötelező adat ismert, minden utazásiStructured kérésnél kötelező meghívni a search_bus_journeys eszközt, még akkor is, ha csak a célállomás változott.
                - Tilos azt írnod, hogy "keresem a járatokat" vagy "várj", ha nem hívtad meg az eszközt.
                - Ha egy szükséges adat hiányzik, csak azt az egy adatot kérdezd meg.
                - Ne találj ki járatot vagy menetrendi adatot.
                - Járatszámot, indulási/érkezési időt, átszállást, menetidőt vagy útvonalat kizárólag a search_bus_journeys eszköz tényleges eredményéből közölhetsz.
                - Ha az eszköz nem futott le, ne adj meg útvonalat vagy időpontot; kérj pontosítást, vagy jelezd, hogy a keresés nem futott le.
              """
            : string.Empty;

        var calendarRules = activeSkillNames.Contains(
            "DailyPlanningSkill",
            StringComparer.OrdinalIgnoreCase)
            ? $"""
              NAPTÁRI ÉS NAPI TERVEZÉSI SZABÁLYOK:
                - A tervezett nap pontos dátuma és magyar napneve: {formattedTargetDate}. Ezt használd a címben, ne számold át a hét napját!
                - A szerver a napi terv generálása előtt automatikusan lekérte a Google Calendar eseményeit, az időjárást és a felhasználói profilt. Az eredmények az előzményben találhatók.
                - A szerver minden érvényes, személyes megjelenést igénylő, időzített naptári eseményhez automatikusan elvégezte a buszkeresést, ha ismert a homeLocation és az esemény helyszíne.
                - Ne próbáld újra lekérni a naptárat, időjárást, profilt vagy buszjáratokat; ezek eredményeit használd.
                - Google Calendar eseményt kizárólag a get_calendar_events eszköz eredményéből jeleníthetsz meg.
                - A homeLocation az indulási település, a preparationMinutes a felhasználó felkészülési ideje.
                - A buszkereséshez az esemény localStart dátumát és időpontját használtuk, searchMode = "arriveBy" értékkel.
                - Ne keress és ne javasolj buszt egész napos, online/virtuális, helyszín nélküli vagy érvénytelen időintervallumú eseményhez.
                - A busz indulási idejét, érkezési idejét, járatszámát és átszállásait kizárólag a tényleges keresési eredményből vedd át.
                - A preparationMinutes értéket a busz indulása előtti felkészülési időként kezeld. A felkészülés kezdete = járat indulása mínusz preparationMinutes.
                - Ne becsülj gyaloglási vagy megállóhoz jutási időt; ha ilyen adat nincs, jelezd, hogy ez nincs beleszámítva.
                - Ha egy buszkeresés hibázott vagy nem talált járatot, az eseményt és a napi terv többi részét akkor is jelenítsd meg.
                - Ne találj ki eseményt, időpontot, időjárást vagy menetrendi adatot.
                - A naptár üres időszaka nem bizonyítja, hogy a felhasználó szabad.
              """
            : string.Empty;

        var prefetchedDataRules = dailyPlanDataPrefetched
            ? """
              CSAK OLVASHATÓ RIPORT MÓD:
              - Ez a válasz egy statikus napi összefoglaló kártyán jelenik meg, ahol a felhasználó NEM tud visszakérdezni vagy üzenetet írni.
              - SZIGORÚAN TILOS kérdést feltenni a válasz végén, és TILOS interakcióra hívni a felhasználót (pl. "Ha szeretnél valamit beütemezni, írd meg...").
              - Üres naptár esetén NE találj ki napi sávokat (pl. Reggel / Délután / Este órarendet).
              - Mindig helyes, természetes magyar ragozást használj (pl. "reggelizz", "olvass", "beszélgess" – soha ne használj torz igealakokat).
              """
            : string.Empty;

        var generalInteractionRule = dailyPlanDataPrefetched
            ? "- Ez egy csak olvasható összefoglaló, ne kérdezz vissza."
            : "- Ha szükséges adat hiányzik, kérdezz vissza.";

        return $"""
        Te a DayPlannerAI személyi AI-asszisztense vagy. Mindig helyes, természetes magyar nyelven válaszolj.

        Aktuális budapesti dátum és idő: {now:yyyy-MM-dd HH:mm:ss} ({formattedToday})
        Időzóna: Europe/Budapest

        Az ebben a kérésben aktív képességek és eszközök:
        {skillInstructions}

        Általános szabályok:
        - Értelmezd a teljes beszélgetési előzményt.
        - A legutóbbi pontosítás felülírja a korábbi értéket.
        - A Tool eredménye az egyetlen hiteles adatforrás.
        - Ne állítsd, hogy végrehajtottál egy keresést, ha nem hívtad meg a megfelelő eszközt.
        {generalInteractionRule}

        {travelRules}
        {calendarRules}
        {prefetchedDataRules}
        {selectedTripContext}
        """;
    }

    private ActiveAgentContext DetermineActiveContext(
        IReadOnlyList<AiChatMessageRequest> conversation)
    {
        // Classify intent from user messages only. Assistant responses may
        // mention calendars, weather, buses, or travel rules and must not
        // activate unrelated skills on their own.
        // Classify the latest user turn as the active intent. Older user turns
        // provide context to the model, but must not keep unrelated skills active.
        var latestUserMessage = conversation.LastOrDefault(message =>
            string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
            ?.Content ?? string.Empty;
        var contextText = latestUserMessage.Trim();

        var isRealtimeRequest = ContainsAny(
            contextText,
            "gps", "valós idő", "hol jár", "késés", "késik", "járműpozíció");

        // Explicit daily-plan requests take precedence over transport words
        // contained in the planner's own instruction text.
        var explicitDailyPlanningIntent = ContainsAny(
            contextText,
            "napi terv", "napi tervez", "tervezd meg a nap",
            "napi beosztás", "daily plan");

        // Travel intent includes natural arrive-at phrasing, even when the
        // user never says bus/train/travel (e.g. "Veszprémben lenni 8-ra").
        var isTravelRequest =
            !explicitDailyPlanningIntent &&
            (HasTravelIntent(contextText) ||
             IsTravelClarificationFollowUp(conversation));

        // A standalone trip request must not activate daily planning.
        var isDailyPlanningRequest = explicitDailyPlanningIntent ||
            (!isTravelRequest && ContainsAny(
                contextText,
                "napomat", "napom", "holnapom", "mai nap",
                "programom", "naptár", "időjárás", "eseményeim",
                "mit csináljak holnap", "mit csináljak ma"));

        if (!isTravelRequest && !isRealtimeRequest && !isDailyPlanningRequest)
        {
            return new ActiveAgentContext(
                _registry.Skills.Select(skill => skill.Name).ToArray(),
                _registry.Tools.Select(tool => tool.Name).ToArray(),
                false);
        }

        var skillNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (isTravelRequest)
        {
            skillNames.Add("TravelPlanningSkill");
            toolNames.Add("search_bus_journeys");
            // The travel skill may need the configured home location when origin is missing.
            toolNames.Add("get_user_profile");
        }

        if (isRealtimeRequest)
        {
            skillNames.Add("LiveTransitSkill");
            toolNames.Add("get_trip_realtime");
        }

        if (isDailyPlanningRequest)
        {
            skillNames.Add("DailyPlanningSkill");
            toolNames.Add("get_calendar_events");
            toolNames.Add("get_weather_forecast");
            toolNames.Add("get_user_profile");
            toolNames.Add("search_bus_journeys");
        }

        return new ActiveAgentContext(
            skillNames.ToArray(),
            toolNames.ToArray(),
            isTravelRequest);
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(
            term,
            StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsClarificationQuestion(string content)
    {
        return content.Contains('?');
    }

    private static bool HasTravelIntent(string value)
    {
        var explicitTravelIntent = ContainsAny(
            value,
            "utaz", "eljut", "jutni", "szeretnék menni", "menni",
            "járat", "busz", "vonat", "menetrend", "indul",
            "érkez", "átszáll", "állomás", "odaér", "odaérni",
            "ott lenni", "-ra érni", "-re érni", "-ra odaér", "-re odaér");
        var arriveAtIntent = Regex.IsMatch(
            value,
            @"(?=.*\b(?:lenni|érni|odaérni)\b)(?=.*\b(?:reggel|délelőtt|délután|este|óra|órára|-ra|-re)\b)(?=.*\d).+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return explicitTravelIntent || arriveAtIntent;
    }

    private static bool IsTravelClarificationFollowUp(
        IReadOnlyList<AiChatMessageRequest> conversation)
    {
        var latestUserIndex = FindLatestUserMessageIndex(conversation);

        if (latestUserIndex < 2 ||
            !string.Equals(
                conversation[latestUserIndex - 1].Role,
                "assistant",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var assistantQuestion = conversation[latestUserIndex - 1].Content ?? string.Empty;

        return conversation
                   .Take(latestUserIndex - 1)
                   .Any(message =>
                       string.Equals(
                           message.Role,
                           "user",
                           StringComparison.OrdinalIgnoreCase) &&
                       HasTravelIntent(message.Content ?? string.Empty)) &&
               ContainsAny(
                   assistantQuestion,
                   "honnan", "indulási hely", "indulási település",
                   "hova szeretnél", "hová szeretnél", "célállomás",
                   "mikor szeretnél indulni", "mikorra kell odaérni",
                   "mikor kell odaérned", "milyen időpontban");
    }

    private static int FindLatestUserMessageIndex(
        IReadOnlyList<AiChatMessageRequest> conversation)
    {
        for (var index = conversation.Count - 1; index >= 0; index--)
        {
            if (string.Equals(
                    conversation[index].Role,
                    "user",
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static string GetPendingTravelUserText(
        IReadOnlyList<AiChatMessageRequest> conversation)
    {
        var latestUserIndex = FindLatestUserMessageIndex(conversation);
        if (latestUserIndex < 0)
            return string.Empty;

        var latestUserText = conversation[latestUserIndex].Content ?? string.Empty;
        if (Regex.IsMatch(
                latestUserText,
                @"\b(?:holnapután|holnap|ma)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            !IsTravelClarificationFollowUp(conversation))
        {
            return latestUserText;
        }

        var priorTravelRequestIndex = -1;
        for (var index = latestUserIndex - 2; index >= 0; index--)
        {
            if (string.Equals(
                    conversation[index].Role,
                    "user",
                    StringComparison.OrdinalIgnoreCase) &&
                HasTravelIntent(conversation[index].Content ?? string.Empty))
            {
                priorTravelRequestIndex = index;
                break;
            }
        }

        if (priorTravelRequestIndex < 0)
            return latestUserText;

        return string.Join(
            " ",
            conversation
                .Skip(priorTravelRequestIndex)
                .Take(latestUserIndex - priorTravelRequestIndex + 1)
                .Where(message => string.Equals(
                    message.Role,
                    "user",
                    StringComparison.OrdinalIgnoreCase))
                .Select(message => message.Content ?? string.Empty));
    }

    private static bool HasExplicitTravelTime(
        IReadOnlyList<AiChatMessageRequest> conversation)
    {
        var travelText = GetPendingTravelUserText(conversation);
        return Regex.IsMatch(
                   travelText,
                   @"\b(?:[01]?\d|2[0-3]):[0-5]\d\b|\b\d{1,2}\s*ór(?:a|á)(?:kor|ra|re|ig)?\b|\b\d{1,2}-(?:kor|ra|re|ig)\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
               ContainsAny(
                   travelText,
                   "most", "azonnal", "minél előbb", "rögtön");
    }

    private static DateOnly? ResolveRelativeTravelDate(
        IReadOnlyList<AiChatMessageRequest> conversation)
    {
        var matches = Regex.Matches(
            GetPendingTravelUserText(conversation),
            @"\b(holnapután|holnap|ma)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (matches.Count == 0)
            return null;

        var timezone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows()
                ? "Central Europe Standard Time"
                : "Europe/Budapest");
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timezone).DateTime);
        var relativeDate = matches[matches.Count - 1].Groups[1].Value;

        if (string.Equals(relativeDate, "ma", StringComparison.OrdinalIgnoreCase))
            return today;
        if (string.Equals(relativeDate, "holnap", StringComparison.OrdinalIgnoreCase))
            return today.AddDays(1);
        if (string.Equals(relativeDate, "holnapután", StringComparison.OrdinalIgnoreCase))
            return today.AddDays(2);

        return null;
    }

    private sealed record ActiveAgentContext(
        IReadOnlyCollection<string> SkillNames,
        IReadOnlyCollection<string> ToolNames,
        bool IsTravelRequest);

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

            var success =
                r.TryGetProperty("success", out var successElement) &&
                successElement.ValueKind == JsonValueKind.True;

            var result = new JsonObject
            {
                ["success"] = success,
                ["source"] = "Google Calendar",
                ["requestedDate"] = GetString(r, "requestedDate"),
                ["timeZone"] = GetString(r, "timeZone")
            };

            if (!success)
            {
                result["error"] = GetString(r, "error")
                    ?? "A naptári adatok lekérdezése sikertelen.";

                return result.ToJsonString();
            }

            if (!r.TryGetProperty("events", out var events) ||
                events.ValueKind != JsonValueKind.Array)
            {
                result["success"] = false;
                result["error"] =
                    "A naptári szolgáltatás nem adott vissza eseménylistát.";

                return result.ToJsonString();
            }

            var compactEvents = new JsonArray();

            foreach (var item in events.EnumerateArray())
            {
                compactEvents.Add(new JsonObject
                {
                    ["summary"] = GetString(item, "summary"),
                    ["start"] = GetString(item, "start"),
                    ["end"] = GetString(item, "end"),
                    ["localStart"] = GetString(item, "localStart"),
                    ["localEnd"] = GetString(item, "localEnd"),

                    ["timeRangeValid"] =
                        item.TryGetProperty(
                            "timeRangeValid",
                            out var timeRange) &&
                        timeRange.ValueKind == JsonValueKind.True,

                    ["durationMinutes"] =
                        GetInt(item, "durationMinutes"),

                    ["location"] = GetString(item, "location"),

                    ["isAllDay"] =
                        item.TryGetProperty(
                            "isAllDay",
                            out var allDay) &&
                        allDay.ValueKind == JsonValueKind.True
                });
            }

            result["eventCount"] = compactEvents.Count;
            result["events"] = compactEvents;

            result["instruction"] =
                "A naptári események a Google Calendar tényleges adatai. " +
                "Kizárólag ezeket az eseményeket jelenítheted meg. " +
                "A summary mezőt pontosan őrizd meg. " +
                "A localStart és localEnd mezőket használd a helyi időpontok " +
                "megjelenítéséhez. " +
                "Ha a timeRangeValid értéke true, az időintervallum érvényes, " +
                "és a durationMinutes mező tartalmazza az időtartamot percekben. " +
                "Ilyenkor tilos negatív időtartamot állítani. " +
                "Ha az időintervallum nem érvényes, ne következtess " +
                "az esemény időtartamára. " +
                "Ne találj ki eseményeket, helyszíneket vagy időpontokat.";

            return result.ToJsonString();
        }

        if (string.Equals(
        toolName,
        "get_user_profile",
        StringComparison.OrdinalIgnoreCase))
        {
            using var profileDoc = JsonDocument.Parse(toolResult);

            var profileRoot = profileDoc.RootElement;

            return new JsonObject
            {
                ["success"] = profileRoot.GetProperty("success").GetBoolean(),
                ["displayName"] = GetString(profileRoot, "displayName"),
                ["homeLocation"] = GetString(profileRoot, "homeLocation"),
                ["timeZone"] = GetString(profileRoot, "timeZone"),
                ["preparationMinutes"] =
                    profileRoot.TryGetProperty("preparationMinutes", out var minutes) &&
                    minutes.ValueKind == JsonValueKind.Number
                        ? minutes.GetInt32()
                        : (int?)null
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

        var busSearchSucceeded =
            root.TryGetProperty("success", out var busSuccess) &&
            busSuccess.ValueKind == JsonValueKind.True;

        var compact = new JsonObject
        {
            ["success"] = busSearchSucceeded
        };

        if (!busSearchSucceeded)
        {
            compact["error"] = GetString(root, "error")
                ?? "A bus timetable search failed.";
            return compact.ToJsonString();
        }

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
                    ["durationMinutes"] = GetInt(route, "durationMinutes"),
                    ["isInProgress"] = GetBoolean(route, "isInProgress")
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
                            ["arrivalTime"] = GetString(leg, "arrivalTime"),
                            ["isInProgress"] = GetBoolean(leg, "isInProgress")
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

    private static bool GetBoolean(
        JsonElement element,
        string property)
    {
        return element.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.True;
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