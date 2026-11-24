using System.Text.Json;
using System.Text.Json.Nodes;

namespace DayPlannerAI.Api.Services.Agent;

public interface IAgentTool
{
    string Name { get; }

    string Description { get; }

    JsonObject Parameters { get; }

    Task<string> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default);

    string? BuildUserResponse(string toolResult) => null;

    bool CanShortCircuit => false;
}