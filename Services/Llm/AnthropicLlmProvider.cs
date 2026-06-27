using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace AgentControlPanel.Services.Llm;

/// <summary>
/// ILlmProvider backed by the official Anthropic C# SDK (direct api.anthropic.com).
/// This is the default provider and the primary demonstration of the Claude SDK.
/// </summary>
public class AnthropicLlmProvider : ILlmProvider
{
    private readonly AnthropicClient _client;
    private readonly int _maxTokens;
    private readonly ILogger<AnthropicLlmProvider> _logger;

    public string ProviderName => "Anthropic";

    public AnthropicLlmProvider(LlmOptions options, ILogger<AnthropicLlmProvider> logger)
    {
        _logger = logger;
        _maxTokens = options.MaxTokens;

        // Empty ApiKey => the SDK reads ANTHROPIC_API_KEY from the environment.
        _client = string.IsNullOrWhiteSpace(options.Anthropic.ApiKey)
            ? new AnthropicClient()
            : new AnthropicClient { ApiKey = options.Anthropic.ApiKey };
    }

    public async Task<string> InvokeAsync(string modelId, string prompt, CancellationToken cancellationToken = default)
    {
        var response = await ConverseAsync(
            modelId,
            new List<LlmMessage>
            {
                new() { Role = LlmRole.User, Content = new() { new() { Text = prompt } } }
            },
            cancellationToken: cancellationToken);

        return string.Concat(response.Content.Where(c => c.Text != null).Select(c => c.Text));
    }

    public async Task<LlmResponse> ConverseAsync(
        string modelId,
        List<LlmMessage> messages,
        string? systemPrompt = null,
        List<LlmTool>? tools = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Anthropic ConverseAsync start. Model={Model}, Messages={Messages}, HasSystem={HasSystem}, Tools={Tools}",
            modelId, messages.Count, !string.IsNullOrEmpty(systemPrompt), tools?.Count ?? 0);

        List<ToolUnion>? toolUnions = null;
        if (tools is { Count: > 0 })
        {
            toolUnions = new List<ToolUnion>();
            foreach (var tool in tools)
                toolUnions.Add(ToTool(tool));
        }

        var parameters = new MessageCreateParams
        {
            Model = modelId,
            MaxTokens = _maxTokens,
            Messages = messages.Select(ToMessageParam).ToList(),
        };

        // Only set System / Tools when we actually have values. This SDK version
        // serializes a null assignment as "system": null / "tools": null, which the
        // API rejects ("system: Input should be a valid array"); omitting them is correct.
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            parameters = parameters with { System = systemPrompt };

        if (toolUnions is not null)
            parameters = parameters with { Tools = toolUnions };

        try
        {
            var message = await _client.Messages.Create(parameters);

            var response = new LlmResponse { StopReason = message.StopReason?.ToString() ?? string.Empty };

            foreach (var block in message.Content)
            {
                if (block.TryPickText(out TextBlock? text) && text != null)
                {
                    response.Content.Add(new LlmContentBlock { Text = text.Text });
                }
                else if (block.TryPickToolUse(out ToolUseBlock? toolUse) && toolUse != null)
                {
                    response.Content.Add(new LlmContentBlock
                    {
                        ToolUse = new LlmToolUse
                        {
                            Id = toolUse.ID,
                            Name = toolUse.Name,
                            InputJson = JsonSerializer.Serialize(toolUse.Input)
                        }
                    });
                }
            }

            _logger.LogInformation(
                "Anthropic ConverseAsync success. StopReason={StopReason}, Blocks={Blocks}",
                response.StopReason, response.Content.Count);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Anthropic ConverseAsync failed for model {Model}.", modelId);
            throw new Exception($"Anthropic API error: {ex.Message}", ex);
        }
    }

    private static MessageParam ToMessageParam(LlmMessage message)
    {
        var blocks = new List<ContentBlockParam>();

        foreach (var block in message.Content)
        {
            if (block.Text != null)
            {
                blocks.Add(new TextBlockParam { Text = block.Text });
            }
            else if (block.ToolUse != null)
            {
                blocks.Add(new ToolUseBlockParam
                {
                    ID = block.ToolUse.Id,
                    Name = block.ToolUse.Name,
                    Input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(block.ToolUse.InputJson)
                            ?? new Dictionary<string, JsonElement>()
                });
            }
            else if (block.ToolResult != null)
            {
                blocks.Add(new ToolResultBlockParam
                {
                    ToolUseID = block.ToolResult.ToolUseId,
                    Content = block.ToolResult.Text,
                    IsError = block.ToolResult.IsError
                });
            }
        }

        return new MessageParam
        {
            Role = message.Role == LlmRole.Assistant ? Role.Assistant : Role.User,
            Content = blocks
        };
    }

    private static Tool ToTool(LlmTool tool) => new()
    {
        Name = tool.Name,
        Description = tool.Description,
        InputSchema = new()
        {
            Properties = tool.Properties.ToDictionary(
                kv => kv.Key,
                kv => JsonSerializer.SerializeToElement(kv.Value)),
            Required = tool.Required
        }
    };
}
