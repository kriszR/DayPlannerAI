namespace DayPlannerAI.Api.Services.Agent;

public interface IAgentSkill
{
    string Name { get; }

    string Description { get; }

    string Instructions { get; }

    IReadOnlyList<IAgentTool> Tools { get; }
}