using System.Text.Json.Nodes;

namespace DayPlannerAI.Api.Services.Agent;

public sealed class AgentSkillRegistry
{
    private readonly List<IAgentSkill> _skills;
    private readonly Dictionary<string, IAgentTool> _tools;

    public AgentSkillRegistry(IEnumerable<IAgentSkill> skills)
    {
        _skills = skills.ToList();

        _tools = _skills
            .SelectMany(skill => skill.Tools)
            .ToDictionary(
                tool => tool.Name,
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IAgentSkill> Skills => _skills;

    public IReadOnlyCollection<IAgentTool> Tools => _tools.Values;

    public bool TryGetTool(string name, out IAgentTool tool)
    {
        return _tools.TryGetValue(name, out tool!);
    }

    public string BuildSkillInstructions()
    {
        return string.Join(
            "\n\n",
            _skills.Select(skill =>
                $"Skill: {skill.Name}\n" +
                $"Description: {skill.Description}\n" +
                skill.Instructions));
    }

    public List<object> GetOllamaToolDefinitions()
    {
        return _tools.Values.Select(tool => (object)new
        {
            type = "function",
            function = new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = tool.Parameters
            }
        }).ToList();
    }
}