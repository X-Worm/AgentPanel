using System.ComponentModel.DataAnnotations;

namespace AgentControlPanel.Models;

public class Agent
{
    public int Id { get; set; }
    [Required]
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Relationships
    public List<Skill> Skills { get; set; } = new();
    public List<KnowledgeBase> KnowledgeBases { get; set; } = new();
    public List<MCPConfig> MCPConfigs { get; set; } = new();
}

public class Skill
{
    public int Id { get; set; }
    [Required]
    public string Name { get; set; } = string.Empty;
    public string SkillMd { get; set; } = string.Empty; // Content of SKILL.md
    public string ScriptsJson { get; set; } = "[]"; // JSON array of AgentSkillScript objects
    public bool IsAIGenerated { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Agent> Agents { get; set; } = new();
}

public class ScriptParameter
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
}

public class AgentSkillScript
{
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public List<ScriptParameter> Parameters { get; set; } = new();
}

public class KnowledgeBase
{
    public int Id { get; set; }
    [Required]
    public string Name { get; set; } = string.Empty;
    public string QdrantCollectionName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MCPConfig
{
    public int Id { get; set; }
    [Required]
    public string Name { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string ConfigurationJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
