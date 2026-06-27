namespace AgentControlPanel.Services.Llm;

/// <summary>
/// Bound from the "Llm" section of appsettings.json. Controls which provider is
/// used, the list of selectable models, and per-provider settings.
/// </summary>
public class LlmOptions
{
    /// <summary>"Anthropic" (direct Claude API, default) or "Bedrock".</summary>
    public string Provider { get; set; } = "Anthropic";

    /// <summary>Model used when an agent has no explicit model set.</summary>
    public string DefaultModel { get; set; } = "claude-opus-4-8";

    public int MaxTokens { get; set; } = 4096;

    /// <summary>The models offered in the agent model dropdown.</summary>
    public List<LlmModelOption> Models { get; set; } = new();

    public AnthropicOptions Anthropic { get; set; } = new();
    public BedrockOptions Bedrock { get; set; } = new();

    /// <summary>Returns the agent's model, or the configured default when unset.</summary>
    public string ResolveModel(string? agentModel) =>
        string.IsNullOrWhiteSpace(agentModel) ? DefaultModel : agentModel!;
}

public class LlmModelOption
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class AnthropicOptions
{
    /// <summary>
    /// API key for api.anthropic.com. If empty, the SDK falls back to the
    /// ANTHROPIC_API_KEY environment variable.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}

public class BedrockOptions
{
    /// <summary>
    /// Prefix prepended to the bare model id to form a Bedrock inference-profile id
    /// (e.g. "claude-sonnet-4-6" -> "us.anthropic.claude-sonnet-4-6").
    /// </summary>
    public string ModelPrefix { get; set; } = "us.anthropic.";
}
