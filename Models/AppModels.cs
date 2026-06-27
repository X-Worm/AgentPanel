using System.ComponentModel.DataAnnotations;
using Pgvector;

namespace AgentControlPanel.Models;

public class Agent
{
    public int Id { get; set; }
    [Required]
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// The Claude model this agent runs on (e.g. "claude-opus-4-8").
    /// Empty falls back to the configured default model.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// When true, this agent is given the built-in search_knowledge_base tool
    /// so it can retrieve documents from the knowledge base during a conversation.
    /// </summary>
    public bool KnowledgeBaseEnabled { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Relationships
    public List<Skill> Skills { get; set; } = new();
}

/// <summary>
/// A single knowledge base entry: a Title + Content pair whose combined text is
/// embedded with Voyage and stored as a pgvector for similarity search.
/// </summary>
public class KnowledgeDocument
{
    public int Id { get; set; }
    [Required]
    public string Title { get; set; } = string.Empty;
    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>The Voyage embedding of "Title\n\nContent" (1024 dimensions).</summary>
    public Vector? Embedding { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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

