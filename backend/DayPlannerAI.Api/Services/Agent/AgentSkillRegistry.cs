using System.Text.Json.Nodes;

namespace DayPlannerAI.Api.Services.Agent;

public sealed class AgentSkillRegistry
{
    private readonly List<IAgentSkill> _skills;
    private readonly Dictionary<string, IAgentTool> _tools;

    public AgentSkillRegistry(IEnumerable<IAgentSkill> skills)
    {
        _skills = skills.ToList();

        _tools = skills
            .SelectMany(skill => skill.Tools)
            .GroupBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IAgentSkill> Skills => _skills;

    public IReadOnlyCollection<IAgentTool> Tools => _tools.Values;

    public bool TryGetTool(string name, out IAgentTool tool)
    {
        return _tools.TryGetValue(name, out tool!);
    }

    public string BuildSkillInstructions(
        IReadOnlyCollection<string>? skillNames = null)
    {
        var selectedSkills = skillNames is null
            ? _skills
            : _skills.Where(skill => skillNames.Contains(
                skill.Name,
                StringComparer.OrdinalIgnoreCase));

        return string.Join(
            "\n\n",
            selectedSkills.Select(skill =>
                $"Skill: {skill.Name}\n" +
                $"Description: {skill.Description}\n" +
                skill.Instructions));
    }

    public List<object> GetOllamaToolDefinitions(
        IReadOnlyCollection<string>? toolNames = null)
    {
        var selectedTools = toolNames is null
            ? _tools.Values
            : _tools.Values.Where(tool => toolNames.Contains(
                tool.Name,
                StringComparer.OrdinalIgnoreCase));

        return selectedTools.Select(tool => (object)new
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