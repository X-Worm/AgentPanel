using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace AgentControlPanel.Services;

public interface IBedrockService
{
    Task<string> InvokeClaudeAsync(string prompt);
    Task<ConverseResponse> ConverseAsync(List<Message> messages, string systemPrompt = "", List<Tool>? tools = null);
}

public class BedrockService : IBedrockService
{
    private readonly IAmazonBedrockRuntime _bedrockRuntime;
    private readonly string _modelId = "us.anthropic.claude-sonnet-4-6"; // Using the latest Claude 4.6 Sonnet for cutting-edge performance and tool use
    private readonly ILogger<BedrockService> _logger;

    public BedrockService(IAmazonBedrockRuntime bedrockRuntime, ILogger<BedrockService> logger)
    {
        _bedrockRuntime = bedrockRuntime;
        _logger = logger;
    }

    public async Task<string> InvokeClaudeAsync(string prompt)
    {
        var messages = new List<Message>
        {
            new Message { Role = ConversationRole.User, Content = new List<ContentBlock> { new ContentBlock { Text = prompt } } }
        };

        var response = await ConverseAsync(messages);
        return response.Output.Message.Content[0].Text;
    }

    public async Task<ConverseResponse> ConverseAsync(List<Message> messages, string systemPrompt = "", List<Tool>? tools = null)
    {
        _logger.LogInformation(
            "Bedrock ConverseAsync start. ModelId={ModelId}, MessagesCount={MessagesCount}, HasSystemPrompt={HasSystemPrompt}, ToolsCount={ToolsCount}",
            _modelId,
            messages.Count,
            !string.IsNullOrEmpty(systemPrompt),
            tools?.Count ?? 0);

        var request = new ConverseRequest
        {
            ModelId = _modelId,
            Messages = messages,
            InferenceConfig = new InferenceConfiguration
            {
                MaxTokens = 4096,
                Temperature = 0.5f
            }
        };

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            request.System = new List<SystemContentBlock>
            {
                new SystemContentBlock { Text = systemPrompt }
            };
        }

        if (tools != null && tools.Any())
        {
            request.ToolConfig = new ToolConfiguration
            {
                Tools = tools
            };
        }

        try
        {
            var response = await _bedrockRuntime.ConverseAsync(request);
            _logger.LogInformation(
                "Bedrock ConverseAsync success. StopReason={StopReason}, OutputContentCount={OutputContentCount}",
                response.StopReason?.Value ?? "unknown",
                response.Output?.Message?.Content?.Count ?? 0);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bedrock ConverseAsync failed.");
            // Fallback error response handled by the caller or wrapped
            throw new Exception($"Bedrock Converse Error: {ex.Message}", ex);
        }
    }
}
