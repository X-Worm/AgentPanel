using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime.Documents;

namespace AgentControlPanel.Services.Llm;

/// <summary>
/// ILlmProvider backed by Claude on AWS Bedrock (Converse API). Selected when the
/// "Llm:Provider" config value is "Bedrock". The bare model id is prefixed with
/// the configured Bedrock inference-profile prefix (e.g. "us.anthropic.").
/// </summary>
public class BedrockLlmProvider : ILlmProvider
{
    private readonly IAmazonBedrockRuntime _runtime;
    private readonly LlmOptions _options;
    private readonly ILogger<BedrockLlmProvider> _logger;

    public string ProviderName => "Bedrock";

    public BedrockLlmProvider(IAmazonBedrockRuntime runtime, LlmOptions options, ILogger<BedrockLlmProvider> logger)
    {
        _runtime = runtime;
        _options = options;
        _logger = logger;
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
        var bedrockModelId = ResolveBedrockModelId(modelId);

        _logger.LogInformation(
            "Bedrock ConverseAsync start. Model={Model}, Messages={Messages}, HasSystem={HasSystem}, Tools={Tools}",
            bedrockModelId, messages.Count, !string.IsNullOrEmpty(systemPrompt), tools?.Count ?? 0);

        var request = new ConverseRequest
        {
            ModelId = bedrockModelId,
            Messages = messages.Select(ToBedrockMessage).ToList(),
            InferenceConfig = new InferenceConfiguration
            {
                MaxTokens = _options.MaxTokens,
                Temperature = 0.5f
            }
        };

        if (!string.IsNullOrWhiteSpace(systemPrompt))
            request.System = new List<SystemContentBlock> { new() { Text = systemPrompt } };

        if (tools is { Count: > 0 })
            request.ToolConfig = new ToolConfiguration { Tools = tools.Select(ToBedrockTool).ToList() };

        try
        {
            var bedrockResponse = await _runtime.ConverseAsync(request, cancellationToken);
            var message = bedrockResponse.Output.Message;

            var response = new LlmResponse { StopReason = bedrockResponse.StopReason?.Value ?? string.Empty };

            foreach (var block in message.Content ?? new List<ContentBlock>())
            {
                if (!string.IsNullOrEmpty(block.Text))
                {
                    response.Content.Add(new LlmContentBlock { Text = block.Text });
                }
                else if (block.ToolUse != null)
                {
                    response.Content.Add(new LlmContentBlock
                    {
                        ToolUse = new LlmToolUse
                        {
                            Id = block.ToolUse.ToolUseId,
                            Name = block.ToolUse.Name,
                            InputJson = JsonSerializer.Serialize(DocumentToStandard(block.ToolUse.Input))
                        }
                    });
                }
            }

            _logger.LogInformation(
                "Bedrock ConverseAsync success. StopReason={StopReason}, Blocks={Blocks}",
                response.StopReason, response.Content.Count);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bedrock ConverseAsync failed for model {Model}.", bedrockModelId);
            throw new Exception($"Bedrock Converse error: {ex.Message}", ex);
        }
    }

    private string ResolveBedrockModelId(string modelId) =>
        modelId.Contains('.') ? modelId : _options.Bedrock.ModelPrefix + modelId;

    private static Message ToBedrockMessage(LlmMessage message)
    {
        var content = new List<ContentBlock>();

        foreach (var block in message.Content)
        {
            if (block.Text != null)
            {
                content.Add(new ContentBlock { Text = block.Text });
            }
            else if (block.ToolUse != null)
            {
                content.Add(new ContentBlock
                {
                    ToolUse = new ToolUseBlock
                    {
                        ToolUseId = block.ToolUse.Id,
                        Name = block.ToolUse.Name,
                        Input = Document.FromObject(JsonToStandard(block.ToolUse.InputJson))
                    }
                });
            }
            else if (block.ToolResult != null)
            {
                content.Add(new ContentBlock
                {
                    ToolResult = new ToolResultBlock
                    {
                        ToolUseId = block.ToolResult.ToolUseId,
                        Status = block.ToolResult.IsError ? ToolResultStatus.Error : ToolResultStatus.Success,
                        Content = new List<ToolResultContentBlock>
                        {
                            new() { Text = block.ToolResult.Text }
                        }
                    }
                });
            }
        }

        return new Message
        {
            Role = message.Role == LlmRole.Assistant ? ConversationRole.Assistant : ConversationRole.User,
            Content = content
        };
    }

    private static Tool ToBedrockTool(LlmTool tool) => new()
    {
        ToolSpec = new ToolSpecification
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = new ToolInputSchema
            {
                Json = Document.FromObject(new
                {
                    type = "object",
                    properties = tool.Properties,
                    required = tool.Required
                })
            }
        }
    };

    private static object? JsonToStandard(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonElementToStandard(doc.RootElement);
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    private static object? JsonElementToStandard(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToStandard).ToList(),
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(p => p.Name, p => JsonElementToStandard(p.Value)),
        _ => null
    };

    private static object? DocumentToStandard(Document doc)
    {
        if (doc.IsNull()) return null;
        if (doc.IsString()) return doc.AsString();
        if (doc.IsDouble()) return doc.AsDouble();
        if (doc.IsBool()) return doc.AsBool();
        if (doc.IsInt()) return doc.AsInt();
        if (doc.IsLong()) return doc.AsLong();
        if (doc.IsList()) return doc.AsList().Select(DocumentToStandard).ToList();
        if (doc.IsDictionary())
        {
            var dict = new Dictionary<string, object?>();
            foreach (var kvp in doc.AsDictionary())
                dict[kvp.Key] = DocumentToStandard(kvp.Value);
            return dict;
        }
        return null;
    }
}
