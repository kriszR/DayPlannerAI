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

        var activeContext = DetermineActiveContext(conversation);
        var isDailyPlanning = activeContext.SkillNames.Contains(
            "DailyPlanningSkill",
            StringComparer.OrdinalIgnoreCase);
        var messages = new List<JsonObject>
        {
            new()
            {
                ["role"] = "system",
                ["content"] = BuildSystemPrompt(
                    selectedTripContext,
                    activeContext.SkillNames,
                    activeContext.IsTravelRequest,
                    isDailyPlanning)
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

        var requiresDailyPlanBusSearch = false;
        var modelToolNames = activeContext.ToolNames;

        if (isDailyPlanning)
        {
            requiresDailyPlanBusSearch = await AddDailyPlanToolResultsAsync(
                conversation,
                messages,
                executedToolResults,
                cancellationToken);

            var dailyDataToolNames = new HashSet<string>(
                [
                    "get_calendar_events",
                    "get_weather_forecast",
                    "get_user_profile"
                ],
                StringComparer.OrdinalIgnoreCase);

            modelToolNames = activeContext.ToolNames
                .Where(name => !dailyDataToolNames.Contains(name))
                .ToArray();
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

                var busSearchIsRequired =
                    activeContext.IsTravelRequest ||
                    requiresDailyPlanBusSearch;

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

                var responseContent = GetMessageContent(assistantMessage);

                // A travel answer containing timetable claims is only allowed
                // after the bus-search tool has actually run. If the model
                // skips the tool, fail closed instead of inventing a journey.
                if (busSearchIsRequired &&
                    !executedToolResults.Any(x => string.Equals(
                        x.ToolName,
                        "search_bus_journeys",
                        StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrWhiteSpace(responseContent) &&
                    (!responseContent.Contains('?') || responseContent.Any(char.IsDigit)))
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
                if ((activeContext.IsTravelRequest ||
                     requiresDailyPlanBusSearch) &&
                    !executedToolResults.Any(x => string.Equals(
                        x.ToolName,
                        "search_bus_journeys",
                        StringComparison.OrdinalIgnoreCase)) &&
                    (!content.Contains('?') || content.Any(char.IsDigit)))
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

    private async Task<bool> AddDailyPlanToolResultsAsync(
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

        var results = new List<(string ToolName, JsonObject Arguments, string ResultJson)>
        {
            ("get_user_profile", profileArguments, profileResult)
        };

        var calendarArguments = new JsonObject
        {
            ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        var timeZone = GetProfileTimeZone(profileResult);
        if (!string.IsNullOrWhiteSpace(timeZone))
            calendarArguments["timeZone"] = timeZone;

        results.Add((
            "get_calendar_events",
            calendarArguments,
            await ExecuteToolAsync(
                "get_calendar_events",
                calendarArguments,
                cancellationToken)));

        var weatherArguments = new JsonObject
        {
            ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        results.Add((
            "get_weather_forecast",
            weatherArguments,
            await ExecuteToolAsync(
                "get_weather_forecast",
                weatherArguments,
                cancellationToken)));

        var toolCalls = new JsonArray();
        foreach (var result in results)
        {
            toolCalls.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = result.ToolName,
                    ["arguments"] = JsonNode.Parse(
                        result.Arguments.ToJsonString())
                }
            });
        }

        messages.Add(new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = string.Empty,
            ["tool_calls"] = toolCalls
        });

        foreach (var result in results)
        {
            executedToolResults.Add(new AgentToolExecutionResult(
                result.ToolName,
                result.ResultJson));

            var modelResult = BuildModelFacingToolResult(
                result.ToolName,
                result.ResultJson);

            messages.Add(new JsonObject
            {
                ["role"] = "tool",
                ["tool_name"] = result.ToolName,
                ["content"] = modelResult
            });

            _logger.LogInformation(
                "Daily-plan data tool completed. Tool={ToolName}, ResultCharacters={ResultCharacters}.",
                result.ToolName,
                result.ResultJson.Length);
        }

        return HasEligibleDailyPlanEvent(profileResult, results[1].ResultJson);
    }

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

    private static bool HasEligibleDailyPlanEvent(
        string profileResult,
        string calendarResult)
    {
        using var profileDocument = JsonDocument.Parse(profileResult);
        var profile = profileDocument.RootElement;

        if (!profile.TryGetProperty("success", out var profileSuccess) ||
            profileSuccess.ValueKind != JsonValueKind.True ||
            string.IsNullOrWhiteSpace(GetString(profile, "homeLocation")))
        {
            return false;
        }

        using var calendarDocument = JsonDocument.Parse(calendarResult);
        var calendar = calendarDocument.RootElement;

        if (!calendar.TryGetProperty("success", out var calendarSuccess) ||
            calendarSuccess.ValueKind != JsonValueKind.True ||
            !calendar.TryGetProperty("events", out var events) ||
            events.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows()
                ? "Central Europe Standard Time"
                : "Europe/Budapest");
        var localNow = TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            timeZone).DateTime;

        return events.EnumerateArray().Any(calendarEvent =>
            !(calendarEvent.TryGetProperty("isAllDay", out var allDay) &&
              allDay.ValueKind == JsonValueKind.True) &&
            calendarEvent.TryGetProperty(
                "timeRangeValid",
                out var validRange) &&
            validRange.ValueKind == JsonValueKind.True &&
            !string.IsNullOrWhiteSpace(GetString(calendarEvent, "location")) &&
            DateTime.TryParseExact(
                GetString(calendarEvent, "localStart"),
                "yyyy-MM-dd'T'HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var eventStart) &&
            eventStart > localNow);
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
                ["temperature"] = 0.2,
                ["top_p"] = 0.8,
                ["top_k"] = 20,
                ["num_ctx"] = 6144,
                // Tool selection may require several arguments and can be
                // truncated when the model has to inspect calendar/profile data.
                ["num_predict"] = 512
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
        bool dailyPlanDataPrefetched)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows()
                ? "Central Europe Standard Time"
                : "Europe/Budapest");

        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timezone);
        var skillInstructions = _registry.BuildSkillInstructions(activeSkillNames);

        var travelRules = isTravelRequest
            ? """
              UTAZÁSI SZABÁLYOK:
              - A teljes beszélgetésből őrizd meg az előzőleg megadott indulási és céltelepülést.
              - A legutóbbi felhasználói pontosítás csak az általa módosított adatot írja felül; a korábbi indulási helyet, dátumot és időzítési szándékot őrizd meg.
              - Ha a felhasználó csak a célállomást módosítja (például: "inkább Balatonfüredre"), tartsd meg a korábbi indulási helyet és a korábbi időzítési szándékot.
              - "Most szeretnék indulni", "azonnal indulnék" vagy "minél előbb" = searchMode "departAfter", departureTime "now", travelDate a mai dátum.
              - A "now" értéket soha ne helyettesítsd korábbi üzenetből származó fix idővel.
              - Ha minden kötelező adat ismert, minden utazási kérésnél kötelező meghívni a search_bus_journeys eszközt, még akkor is, ha csak a célállomás változott.
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
            ? """
              NAPTÁRI ÉS NAPI TERVEZÉSI SZABÁLYOK:
              - Google Calendar eseményt kizárólag a get_calendar_events eszköz eredményéből jeleníthetsz meg.
              - Napi terv készítésekor kérd le a felhasználói profilt is; a homeLocation az indulási település, a preparationMinutes a felkészülési idő.
              - Ha egy eseménynek van helyszíne és a homeLocation ismert, keress hozzá buszútvonalat a search_bus_journeys eszközzel.
              - A buszkereséshez az esemény helyi kezdési dátumát és időpontját használd, searchMode = "arriveBy" értékkel.
              - Ne keress útvonalat egész napos eseményhez, hiányzó helyszínhez vagy érvénytelen esemény-időintervallumhoz.
              - A busz indulási idejét, érkezési idejét, járatszámát és átszállásait kizárólag a keresőeszköz eredményéből vedd át.
              - A preparationMinutes értéket a busz indulási ideje előtti felkészülési időként kezeld. A felkészülés kezdete = járat indulása mínusz preparationMinutes.
              - Ne becsülj gyaloglási vagy megállóhoz jutási időt; ha ilyen adat nincs, jelezd, hogy ez nincs beleszámítva.
              - Ha nincs megfelelő buszjárat, ezt egyértelműen jelezd; ne találj ki alternatívát.
              - Ne állítsd, hogy egy útvonalat ellenőriztél, ha nem hívtad meg a search_bus_journeys eszközt.
              - Ne jeleníts meg olyan járatot, amely nem szerepel az eszköz eredményében.
              - Ne találj ki eseményt, időpontot vagy helyszínt.
              - A naptár üres időszaka nem bizonyítja, hogy a felhasználó szabad.
              """
            : string.Empty;

        var prefetchedDataRules = dailyPlanDataPrefetched
            ? """
              A napi tervhez szükséges naptári, időjárási és profiladatokat a szerver már lekérte.
              A hozzájuk tartozó eszközeredmények közvetlenül az előzményben találhatók.
              Ezeket használd; ne állítsd, hogy nem érkezett adat, és ne kérdezd le őket újra.
              """
            : string.Empty;

        return $"""
        Te a DayPlannerAI személyi AI-asszisztense vagy. Mindig magyarul válaszolj.

        Aktuális budapesti dátum és idő: {now:yyyy-MM-dd HH:mm:ss}
        Időzóna: Europe/Budapest

        Az ebben a kérésben aktív képességek és eszközök:
        {skillInstructions}

        Általános szabályok:
        - Értelmezd a teljes beszélgetési előzményt.
        - A legutóbbi pontosítás felülírja a korábbi értéket.
        - A Tool eredménye az egyetlen hiteles adatforrás.
        - Ne állítsd, hogy végrehajtottál egy keresést, ha nem hívtad meg a megfelelő eszközt.
        - Ha szükséges adat hiányzik, kérdezz vissza.

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
        var contextText = string.Join(
            " ",
            conversation
                .Where(message => string.Equals(
                    message.Role,
                    "user",
                    StringComparison.OrdinalIgnoreCase))
                .TakeLast(6)
                .Select(message => message.Content ?? string.Empty));

        var isRealtimeRequest = ContainsAny(
            contextText,
            "gps", "valós idő", "hol jár", "késés", "késik", "járműpozíció");

        var isDailyPlanningRequest = ContainsAny(
            contextText,
            "napi terv", "napi tervez", "tervezd meg", "napomat", "napom", "holnapom",
            "mai nap", "programom", "naptár", "időjárás", "eseményeim",
            "napi beosztás", "mit csináljak holnap", "mit csináljak ma");

        // Daily-plan prompts can mention bus searches conditionally. Do not
        // treat those instructions as a standalone travel request, which
        // would force a bus search even when no calendar event needs one.
        var isTravelRequest = !isDailyPlanningRequest && ContainsAny(
            contextText,
            "utaz", "eljut", "jutni", "szeretnék jutni", "szeretnék menni",
            "menni", "járat", "busz", "vonat", "menetrend", "indul",
            "érkez", "átszáll", "állomás");

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