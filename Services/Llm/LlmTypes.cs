namespace AgentControlPanel.Services.Llm;

/// <summary>
/// Provider-agnostic conversation types. ConversationService and the controllers
/// work exclusively against these so the underlying LLM provider (direct Anthropic
/// API or AWS Bedrock) can be swapped via configuration without touching call sites.
/// </summary>
public enum LlmRole
{
    User,
    Assistant
}

public class LlmContentBlock
{
    public string? Text { get; set; }
    public LlmToolUse? ToolUse { get; set; }
    public LlmToolResult? ToolResult { get; set; }
}

public class LlmToolUse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>The tool input, as a raw JSON object string.</summary>
    public string InputJson { get; set; } = "{}";
}

public class LlmToolResult
{
    public string ToolUseId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsError { get; set; }
}

public class LlmMessage
{
    public LlmRole Role { get; set; }
    public List<LlmContentBlock> Content { get; set; } = new();
}

/// <summary>
/// A tool definition. <see cref="Properties"/> maps each parameter name to a JSON
/// Schema fragment (e.g. new { type = "string", description = "..." }); providers
/// translate this into their own tool-schema shape.
/// </summary>
public class LlmTool
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> Properties { get; set; } = new();
    public List<string> Required { get; set; } = new();
}

public class LlmResponse
{
    public List<LlmContentBlock> Content { get; set; } = new();
    public string StopReason { get; set; } = string.Empty;
}

/// <summary>
/// Abstraction over an LLM backend. Implemented by AnthropicLlmProvider (direct
/// Claude API) and BedrockLlmProvider (Claude via AWS Bedrock).
/// </summary>
public interface ILlmProvider
{
    /// <summary>Human-readable name of the active provider (for diagnostics/UI).</summary>
    string ProviderName { get; }

    /// <summary>Multi-turn conversation with optional system prompt and tools.</summary>
    Task<LlmResponse> ConverseAsync(
        string modelId,
        List<LlmMessage> messages,
        string? systemPrompt = null,
        List<LlmTool>? tools = null,
        CancellationToken cancellationToken = default);

    /// <summary>Convenience helper for a single prompt → plain text response.</summary>
    Task<string> InvokeAsync(string modelId, string prompt, CancellationToken cancellationToken = default);
}
