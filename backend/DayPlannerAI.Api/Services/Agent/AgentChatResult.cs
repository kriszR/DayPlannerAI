namespace DayPlannerAI.Api.Services.Agent;

public sealed record AgentToolExecutionResult(
    string ToolName,
    string ResultJson);

public sealed record AgentChatResult(
    string Answer,
    IReadOnlyList<AgentToolExecutionResult> ToolResults);