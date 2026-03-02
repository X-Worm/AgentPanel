using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using AgentControlPanel.Models;
using System.Diagnostics;
using Amazon.Runtime.Documents;

namespace AgentControlPanel.Services;

public interface IConversationService
{
    Task<ConversationResponse> ProcessMessageAsync(Agent agent, List<Message> history, string userMessage);
}

public class ConversationResponse
{
    public List<Message> UpdatedHistory { get; set; } = new();
    public List<ActivityLogEntry> ActivityLog { get; set; } = new();
    public string FinalResponse { get; set; } = string.Empty;
}

public class ActivityLogEntry
{
    public string Type { get; set; } = "info";
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class ConversationService : IConversationService
{
    private readonly IBedrockService _bedrockService;
    private readonly ISkillParser _skillParser;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(IBedrockService bedrockService, ISkillParser skillParser, ILogger<ConversationService> logger)
    {
        _bedrockService = bedrockService;
        _skillParser = skillParser;
        _logger = logger;
    }

    public async Task<ConversationResponse> ProcessMessageAsync(Agent agent, List<Message> history, string userMessage)
    {
        var response = new ConversationResponse();
        _logger.LogInformation(
            "ProcessMessageAsync start. AgentId={AgentId}, AgentName={AgentName}, IncomingHistoryCount={HistoryCount}, UserMessageLength={MessageLength}",
            agent.Id, agent.Name, history.Count, userMessage?.Length ?? 0);

        history.Add(new Message
        {
            Role = ConversationRole.User,
            Content = new List<ContentBlock> { new ContentBlock { Text = userMessage } }
        });

        var systemPrompt = BuildSystemPrompt(agent);
        var tools = MapScriptsToTools(agent.Skills);

        foreach (var skill in agent.Skills)
        {
            var scripts = DeserializeScripts(skill.ScriptsJson);
            if (scripts.Any())
            {
                response.ActivityLog.Add(new ActivityLogEntry
                {
                    Type = "info",
                    Message = $"Skill loaded: {skill.Name} ({scripts.Count} script tool(s) registered)"
                });
            }
            else
            {
                response.ActivityLog.Add(new ActivityLogEntry
                {
                    Type = "info",
                    Message = $"Skill loaded: {skill.Name} (instruction-only, injected into system prompt)"
                });
            }
        }

        bool continueLoop = true;
        int maxIterations = 5;
        int currentIteration = 0;

        while (continueLoop && currentIteration < maxIterations)
        {
            currentIteration++;
            _logger.LogDebug(
                "Conversation loop iteration {Iteration}/{MaxIterations}. HistoryCount={HistoryCount}",
                currentIteration, maxIterations, history.Count);
            response.ActivityLog.Add(new ActivityLogEntry { Message = "Claude is thinking..." });

            var bedrockResponse = await _bedrockService.ConverseAsync(history, systemPrompt, tools);
            var assistantMessage = bedrockResponse.Output.Message;
            history.Add(assistantMessage);
            _logger.LogDebug("Received assistant message. ContentBlocks={ContentBlocks}", assistantMessage.Content?.Count ?? 0);

            var toolCalls = (assistantMessage.Content ?? new List<ContentBlock>())
                .Where(c => c.ToolUse != null)
                .ToList();
            _logger.LogDebug("Assistant tool calls detected: {ToolCallCount}", toolCalls.Count);

            if (!toolCalls.Any())
            {
                response.FinalResponse = (assistantMessage.Content ?? new List<ContentBlock>())
                    .FirstOrDefault(c => !string.IsNullOrEmpty(c.Text))?.Text ?? "";
                _logger.LogInformation("Conversation finished. FinalResponseLength={ResponseLength}", response.FinalResponse.Length);
                continueLoop = false;
            }
            else
            {
                var toolResultBlocks = new List<ContentBlock>();

                foreach (var call in toolCalls)
                {
                    var toolUse = call.ToolUse;
                    object? standardInput = ConvertDocumentToStandard(toolUse.Input);
                    string inputJson = standardInput != null ? JsonSerializer.Serialize(standardInput) : "{}";
                    _logger.LogInformation(
                        "Executing tool call. ToolName={ToolName}, ToolUseId={ToolUseId}, InputJsonLength={InputJsonLength}",
                        toolUse.Name, toolUse.ToolUseId, inputJson.Length);

                    response.ActivityLog.Add(new ActivityLogEntry
                    {
                        Type = "tool_call",
                        Message = $"Tool Call: {toolUse.Name} with args: {inputJson}"
                    });

                    var (result, status) = await ExecuteSkillScriptAsync(agent, toolUse.Name, inputJson);
                    _logger.LogInformation(
                        "Tool execution finished. ToolName={ToolName}, ToolUseId={ToolUseId}, Status={Status}, ResultLength={ResultLength}",
                        toolUse.Name, toolUse.ToolUseId, status, result.Length);

                    response.ActivityLog.Add(new ActivityLogEntry
                    {
                        Type = status == ToolResultStatus.Success ? "tool_result" : "error",
                        Message = status == ToolResultStatus.Success
                            ? $"Tool Result received ({result.Length} chars)"
                            : $"Tool Error: {result}"
                    });

                    toolResultBlocks.Add(new ContentBlock
                    {
                        ToolResult = new ToolResultBlock
                        {
                            ToolUseId = toolUse.ToolUseId,
                            Content = new List<ToolResultContentBlock> { new ToolResultContentBlock { Text = result } },
                            Status = status
                        }
                    });
                }

                history.Add(new Message
                {
                    Role = ConversationRole.User,
                    Content = toolResultBlocks
                });
                _logger.LogDebug(
                    "Added tool result message to history. ToolResultBlocks={ToolResultBlocks}, HistoryCount={HistoryCount}",
                    toolResultBlocks.Count, history.Count);
            }
        }

        _logger.LogInformation(
            "ProcessMessageAsync end. FinalHistoryCount={HistoryCount}, ActivityLogCount={ActivityCount}",
            history.Count, response.ActivityLog.Count);
        response.UpdatedHistory = history;
        return response;
    }

    /// <summary>
    /// Builds the full system prompt by combining the agent's own system prompt
    /// with skill instructions extracted from SKILL.md bodies (progressive disclosure).
    /// This ensures Claude actually reads and understands the skill instructions.
    /// </summary>
    private string BuildSystemPrompt(Agent agent)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(agent.SystemPrompt))
            sb.AppendLine(agent.SystemPrompt);

        if (agent.Skills.Count == 0)
            return sb.ToString();

        sb.AppendLine();
        sb.AppendLine("## Available Skills");
        sb.AppendLine();
        sb.AppendLine("You have the following skills available. Some skills provide executable scripts you can call as tools. Others are instruction-only and provide knowledge and guidelines you should follow when relevant.");
        sb.AppendLine();

        foreach (var skill in agent.Skills)
        {
            var parseResult = _skillParser.Parse(skill.SkillMd);

            if (!parseResult.IsValid)
            {
                _logger.LogWarning(
                    "Skill '{SkillName}' (Id={SkillId}) has invalid SKILL.md: {Errors}",
                    skill.Name, skill.Id, string.Join("; ", parseResult.Errors));
                sb.AppendLine($"### Skill: {skill.Name}");
                sb.AppendLine();
                sb.AppendLine("*This skill has configuration issues and may not work correctly.*");
                sb.AppendLine();

                if (!string.IsNullOrWhiteSpace(parseResult.Body))
                    sb.AppendLine(parseResult.Body);

                sb.AppendLine();
                continue;
            }

            sb.AppendLine($"### Skill: {parseResult.Frontmatter.Name}");
            sb.AppendLine();
            sb.AppendLine($"**Description:** {parseResult.Frontmatter.Description}");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(parseResult.Body))
            {
                sb.AppendLine(parseResult.Body);
                sb.AppendLine();
            }

            var scripts = DeserializeScripts(skill.ScriptsJson);
            if (scripts.Any())
            {
                sb.AppendLine("**Available script tools for this skill:**");
                var toolPrefix = _skillParser.SanitizeNameForToolUse(parseResult.Frontmatter.Name);
                foreach (var script in scripts)
                {
                    var scriptToolName = $"{toolPrefix}__{SanitizeScriptName(script.Name)}";
                    sb.AppendLine($"- `{scriptToolName}`: {script.Name} ({script.Language})");

                    if (script.Parameters.Any())
                    {
                        foreach (var p in script.Parameters)
                        {
                            var reqLabel = p.Required ? "required" : "optional";
                            sb.AppendLine($"  - `{p.Name}` ({p.Type}, {reqLabel}): {p.Description}");
                        }
                    }
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("*This is an instruction-only skill with no executable scripts. Follow the instructions above when relevant.*");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Maps each script within each skill to a Bedrock Tool definition.
    /// Tool name format: {sanitized_skill_name}__{sanitized_script_name}
    /// Parameters come from the script's Parameters list, with a fallback
    /// generic "input" parameter for backward compatibility.
    /// </summary>
    private List<Tool> MapScriptsToTools(List<Skill> skills)
    {
        var tools = new List<Tool>();

        foreach (var skill in skills)
        {
            var parseResult = _skillParser.Parse(skill.SkillMd);
            var skillToolName = parseResult.IsValid
                ? _skillParser.SanitizeNameForToolUse(parseResult.Frontmatter.Name)
                : SanitizeLegacyName(skill.Name);

            var description = parseResult.IsValid
                ? parseResult.Frontmatter.Description
                : skill.Name;

            var scripts = DeserializeScripts(skill.ScriptsJson);
            if (!scripts.Any())
            {
                _logger.LogDebug("Skill '{SkillName}' (Id={SkillId}) is instruction-only (no scripts). Its instructions are included in the system prompt.", skill.Name, skill.Id);
                continue;
            }

            foreach (var script in scripts)
            {
                try
                {
                    var scriptName = SanitizeScriptName(script.Name);
                    var toolName = $"{skillToolName}__{scriptName}";
                    var toolDescription = scripts.Count == 1
                        ? description
                        : $"{description} - {script.Name}";

                    var properties = new Dictionary<string, object>();
                    var requiredFields = new List<string>();

                    if (script.Parameters.Any())
                    {
                        foreach (var param in script.Parameters)
                        {
                            properties[param.Name] = new
                            {
                                type = MapParameterType(param.Type),
                                description = string.IsNullOrEmpty(param.Description) ? param.Name : param.Description
                            };
                            if (param.Required) requiredFields.Add(param.Name);
                        }
                    }
                    else
                    {
                        properties["input"] = new
                        {
                            type = "string",
                            description = "Input for the script"
                        };
                    }

                    tools.Add(new Tool
                    {
                        ToolSpec = new ToolSpecification
                        {
                            Name = toolName,
                            Description = toolDescription,
                            InputSchema = new ToolInputSchema
                            {
                                Json = Document.FromObject(new
                                {
                                    type = "object",
                                    properties = properties,
                                    required = requiredFields
                                })
                            }
                        }
                    });

                    _logger.LogDebug("Mapped script to tool: {ToolName} from skill '{SkillName}'", toolName, skill.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to map script '{ScriptName}' from skill '{SkillName}' (Id={SkillId}) to tool.",
                        script.Name, skill.Name, skill.Id);
                }
            }
        }

        _logger.LogDebug("Mapped scripts to tools. SkillsCount={SkillsCount}, ToolsCount={ToolsCount}", skills.Count, tools.Count);
        return tools;
    }

    /// <summary>
    /// Finds and executes the correct script by matching the Bedrock tool name
    /// back to skill + script. Returns the output and result status.
    /// </summary>
    private async Task<(string Result, ToolResultStatus Status)> ExecuteSkillScriptAsync(Agent agent, string toolName, string inputJson)
    {
        foreach (var skill in agent.Skills)
        {
            var parseResult = _skillParser.Parse(skill.SkillMd);
            var skillToolName = parseResult.IsValid
                ? _skillParser.SanitizeNameForToolUse(parseResult.Frontmatter.Name)
                : SanitizeLegacyName(skill.Name);

            if (!toolName.StartsWith(skillToolName + "__"))
                continue;

            var scriptSuffix = toolName.Substring(skillToolName.Length + 2);
            var scripts = DeserializeScripts(skill.ScriptsJson);
            var script = scripts.FirstOrDefault(s => SanitizeScriptName(s.Name) == scriptSuffix);

            if (script == null)
            {
                _logger.LogWarning("Script not found for tool '{ToolName}' in skill '{SkillName}'.", toolName, skill.Name);
                continue;
            }

            return await RunScriptAsync(script, inputJson);
        }

        _logger.LogError("No matching skill/script found for tool '{ToolName}'.", toolName);
        return ($"Error: No matching skill or script found for tool '{toolName}'.", ToolResultStatus.Error);
    }

    private async Task<(string Result, ToolResultStatus Status)> RunScriptAsync(AgentSkillScript script, string inputJson)
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"skill_{Guid.NewGuid()}{GetExtension(script.Language)}");
        await File.WriteAllTextAsync(tempFile, script.Code);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = GetExecutable(script.Language),
                Arguments = tempFile,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var inputs = JsonDocument.Parse(inputJson);
            foreach (var prop in inputs.RootElement.EnumerateObject())
            {
                startInfo.EnvironmentVariables[prop.Name] = prop.Value.ToString();
            }

            using var process = Process.Start(startInfo);
            if (process == null)
                return ("Error: Failed to start process.", ToolResultStatus.Error);

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Script '{ScriptName}' exited with code {ExitCode}. Stderr: {Stderr}",
                    script.Name, process.ExitCode, error);
                return ($"Script exited with code {process.ExitCode}:\n{error}", ToolResultStatus.Error);
            }

            return (output, ToolResultStatus.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception running script '{ScriptName}'.", script.Name);
            return ($"Exception during execution: {ex.Message}", ToolResultStatus.Error);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private List<AgentSkillScript> DeserializeScripts(string scriptsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<AgentSkillScript>>(
                scriptsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            ) ?? new List<AgentSkillScript>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize ScriptsJson.");
            return new List<AgentSkillScript>();
        }
    }

    private static string SanitizeScriptName(string scriptName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(scriptName);
        return Regex.Replace(nameWithoutExt, @"[^a-zA-Z0-9]", "_").ToLower();
    }

    private static string SanitizeLegacyName(string name)
    {
        return Regex.Replace(name, @"[^a-zA-Z0-9]", "_").ToLower();
    }

    private static string MapParameterType(string type) => type.ToLower() switch
    {
        "number" or "integer" or "int" or "float" or "double" => "number",
        "boolean" or "bool" => "boolean",
        _ => "string"
    };

    private static string GetExtension(string language) => language.ToLower() switch
    {
        "python" => ".py",
        "js" or "javascript" => ".js",
        "bash" or "sh" => ".sh",
        _ => ".txt"
    };

    private static string GetExecutable(string language) => language.ToLower() switch
    {
        "python" => "python",
        "js" or "javascript" => "node",
        "bash" or "sh" => "bash",
        _ => throw new NotSupportedException($"Unsupported script language: {language}")
    };

    private object? ConvertDocumentToStandard(Document doc)
    {
        if (doc.IsNull()) return null;
        if (doc.IsString()) return doc.AsString();
        if (doc.IsDouble()) return doc.AsDouble();
        if (doc.IsBool()) return doc.AsBool();
        if (doc.IsInt()) return doc.AsInt();
        if (doc.IsLong()) return doc.AsLong();

        if (doc.IsList())
            return doc.AsList().Select(ConvertDocumentToStandard).ToList();

        if (doc.IsDictionary())
        {
            var dict = new Dictionary<string, object?>();
            foreach (var kvp in doc.AsDictionary())
                dict[kvp.Key] = ConvertDocumentToStandard(kvp.Value);
            return dict;
        }

        return null;
    }
}
