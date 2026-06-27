using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentControlPanel.Models;
using AgentControlPanel.Services.Llm;
using System.Diagnostics;

namespace AgentControlPanel.Services;

public interface IConversationService
{
    Task<ConversationResponse> ProcessMessageAsync(Agent agent, List<LlmMessage> history, string userMessage);
}

public class ConversationResponse
{
    public List<LlmMessage> UpdatedHistory { get; set; } = new();
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
    private const string ReadSkillToolName = "read_skill";
    private const string SearchKnowledgeBaseToolName = "search_knowledge_base";

    private readonly ILlmProvider _llm;
    private readonly LlmOptions _llmOptions;
    private readonly ISkillParser _skillParser;
    private readonly IKnowledgeBaseService _knowledgeBase;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(
        ILlmProvider llm,
        LlmOptions llmOptions,
        ISkillParser skillParser,
        IKnowledgeBaseService knowledgeBase,
        ILogger<ConversationService> logger)
    {
        _llm = llm;
        _llmOptions = llmOptions;
        _skillParser = skillParser;
        _knowledgeBase = knowledgeBase;
        _logger = logger;
    }

    public async Task<ConversationResponse> ProcessMessageAsync(Agent agent, List<LlmMessage> history, string userMessage)
    {
        var response = new ConversationResponse();
        var modelId = _llmOptions.ResolveModel(agent.Model);

        _logger.LogInformation(
            "ProcessMessageAsync start. AgentId={AgentId}, AgentName={AgentName}, Model={Model}, Provider={Provider}, IncomingHistoryCount={HistoryCount}, UserMessageLength={MessageLength}",
            agent.Id, agent.Name, modelId, _llm.ProviderName, history.Count, userMessage?.Length ?? 0);

        history.Add(new LlmMessage
        {
            Role = LlmRole.User,
            Content = new List<LlmContentBlock> { new() { Text = userMessage } }
        });

        var systemPrompt = BuildSystemPrompt(agent);
        var tools = BuildTools(agent);

        foreach (var skill in agent.Skills)
        {
            response.ActivityLog.Add(new ActivityLogEntry
            {
                Type = "info",
                Message = $"Skill discovered: {skill.Name} (metadata loaded, full content available via read_skill)"
            });
        }

        bool continueLoop = true;
        int maxIterations = 10;
        int currentIteration = 0;

        while (continueLoop && currentIteration < maxIterations)
        {
            currentIteration++;
            _logger.LogDebug(
                "Conversation loop iteration {Iteration}/{MaxIterations}. HistoryCount={HistoryCount}",
                currentIteration, maxIterations, history.Count);
            response.ActivityLog.Add(new ActivityLogEntry { Message = $"[Iteration {currentIteration}/{maxIterations}] Claude is thinking..." });

            var llmResponse = await _llm.ConverseAsync(modelId, history, systemPrompt, tools);

            var assistantMessage = new LlmMessage { Role = LlmRole.Assistant, Content = llmResponse.Content };
            history.Add(assistantMessage);
            _logger.LogDebug("Received assistant message. ContentBlocks={ContentBlocks}", assistantMessage.Content.Count);

            var toolCalls = assistantMessage.Content.Where(c => c.ToolUse != null).ToList();
            _logger.LogDebug("Assistant tool calls detected: {ToolCallCount}", toolCalls.Count);

            if (!toolCalls.Any())
            {
                response.FinalResponse = assistantMessage.Content
                    .FirstOrDefault(c => !string.IsNullOrEmpty(c.Text))?.Text ?? "";
                _logger.LogInformation("Conversation finished. FinalResponseLength={ResponseLength}", response.FinalResponse.Length);
                continueLoop = false;
            }
            else
            {
                var toolResultBlocks = new List<LlmContentBlock>();

                foreach (var call in toolCalls)
                {
                    var toolUse = call.ToolUse!;
                    var inputJson = string.IsNullOrWhiteSpace(toolUse.InputJson) ? "{}" : toolUse.InputJson;

                    response.ActivityLog.Add(new ActivityLogEntry
                    {
                        Type = "tool_call",
                        Message = $"Tool Call: {toolUse.Name} with args: {inputJson}"
                    });

                    var (result, isError) = await HandleToolCallAsync(agent, toolUse.Name, inputJson, response);
                    _logger.LogInformation(
                        "Tool call handled. ToolName={ToolName}, ToolUseId={ToolUseId}, IsError={IsError}, ResultLength={ResultLength}",
                        toolUse.Name, toolUse.Id, isError, result.Length);

                    response.ActivityLog.Add(new ActivityLogEntry
                    {
                        Type = isError ? "error" : "tool_result",
                        Message = isError
                            ? $"Tool Error: {result}"
                            : $"Tool Result received ({result.Length} chars)"
                    });

                    toolResultBlocks.Add(new LlmContentBlock
                    {
                        ToolResult = new LlmToolResult
                        {
                            ToolUseId = toolUse.Id,
                            Text = result,
                            IsError = isError
                        }
                    });
                }

                history.Add(new LlmMessage
                {
                    Role = LlmRole.User,
                    Content = toolResultBlocks
                });
            }
        }

        _logger.LogInformation(
            "ProcessMessageAsync end. FinalHistoryCount={HistoryCount}, ActivityLogCount={ActivityCount}",
            history.Count, response.ActivityLog.Count);
        response.UpdatedHistory = history;
        return response;
    }

    /// <summary>
    /// Phase 1 (Discovery): Builds the system prompt with only skill metadata
    /// (~100 tokens per skill) using the XML format recommended by agentskills.io.
    /// Claude reads full skill content on demand via the read_skill tool.
    /// </summary>
    private string BuildSystemPrompt(Agent agent)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(agent.SystemPrompt))
            sb.AppendLine(agent.SystemPrompt);

        if (agent.Skills.Count == 0)
            return sb.ToString();

        sb.AppendLine();
        sb.AppendLine(@"You have skills available. To use a skill, first call the `read_skill` tool with the skill name to load its full instructions and understand its capabilities. Only then proceed to use its script tools if available.");
        sb.AppendLine();
        sb.AppendLine("<available_skills>");

        foreach (var skill in agent.Skills)
        {
            var parseResult = _skillParser.Parse(skill.SkillMd);
            var name = parseResult.IsValid ? parseResult.Frontmatter.Name : skill.Name;
            var description = parseResult.IsValid ? parseResult.Frontmatter.Description : skill.Name;

            sb.AppendLine("  <skill>");
            sb.AppendLine($"    <name>{EscapeXml(name)}</name>");
            sb.AppendLine($"    <description>{EscapeXml(description)}</description>");
            sb.AppendLine("  </skill>");
        }

        sb.AppendLine("</available_skills>");
        return sb.ToString();
    }

    /// <summary>
    /// Builds all tools: the built-in search_knowledge_base tool (when the agent
    /// has knowledge base access enabled), the read_skill tool for progressive
    /// disclosure, plus individual script tools for each skill's scripts.
    /// </summary>
    private List<LlmTool> BuildTools(Agent agent)
    {
        var tools = new List<LlmTool>();

        if (agent.KnowledgeBaseEnabled)
        {
            tools.Add(BuildSearchKnowledgeBaseTool());
        }

        var skills = agent.Skills;
        if (skills.Any())
        {
            tools.Add(BuildReadSkillTool(skills));
        }

        tools.AddRange(MapScriptsToTools(skills));
        return tools;
    }

    /// <summary>
    /// The built-in knowledge base retrieval tool. Handled natively in C#
    /// (embed query + pgvector similarity search), independent of the
    /// skill/script execution path.
    /// </summary>
    private static LlmTool BuildSearchKnowledgeBaseTool() => new()
    {
        Name = SearchKnowledgeBaseToolName,
        Description = "Search the knowledge base for relevant documented information. Use this whenever the user asks about stored knowledge, internal policies, or facts that may have been added to the knowledge base.",
        Properties = new Dictionary<string, object>
        {
            ["query"] = new
            {
                type = "string",
                description = "What to search the knowledge base for."
            },
            ["max_results"] = new
            {
                type = "number",
                description = "Maximum number of entries to return (default 5)."
            }
        },
        Required = new List<string> { "query" }
    };

    /// <summary>
    /// Creates the read_skill tool that lets Claude load full SKILL.md content
    /// on demand (Phase 2 - Activation per agentskills.io spec).
    /// </summary>
    private LlmTool BuildReadSkillTool(List<Skill> skills)
    {
        var skillNames = skills.Select(s =>
        {
            var parseResult = _skillParser.Parse(s.SkillMd);
            return parseResult.IsValid ? parseResult.Frontmatter.Name : s.Name;
        }).ToList();

        return new LlmTool
        {
            Name = ReadSkillToolName,
            Description = $"Load the full SKILL.md instructions for a skill. Call this before using a skill to understand its capabilities, workflows, and available script tools. Available skills: {string.Join(", ", skillNames)}",
            Properties = new Dictionary<string, object>
            {
                ["skill_name"] = new
                {
                    type = "string",
                    description = $"The name of the skill to read. One of: {string.Join(", ", skillNames)}"
                }
            },
            Required = new List<string> { "skill_name" }
        };
    }

    /// <summary>
    /// Maps each script within each skill to a tool definition.
    /// </summary>
    private List<LlmTool> MapScriptsToTools(List<Skill> skills)
    {
        var tools = new List<LlmTool>();

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
                _logger.LogDebug("Skill '{SkillName}' (Id={SkillId}) is instruction-only (no scripts).", skill.Name, skill.Id);
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

                    tools.Add(new LlmTool
                    {
                        Name = toolName,
                        Description = toolDescription,
                        Properties = properties,
                        Required = requiredFields
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

        return tools;
    }

    /// <summary>
    /// Routes tool calls to either read_skill (returns SKILL.md content)
    /// or script execution.
    /// </summary>
    private async Task<(string Result, bool IsError)> HandleToolCallAsync(
        Agent agent, string toolName, string inputJson, ConversationResponse response)
    {
        if (toolName == SearchKnowledgeBaseToolName)
            return await HandleSearchKnowledgeBaseAsync(inputJson, response);

        if (toolName == ReadSkillToolName)
            return HandleReadSkill(agent, inputJson, response);

        return await ExecuteSkillScriptAsync(agent, toolName, inputJson);
    }

    /// <summary>
    /// Handles the built-in search_knowledge_base tool: embeds the query and
    /// returns the closest knowledge documents via pgvector similarity search.
    /// </summary>
    private async Task<(string Result, bool IsError)> HandleSearchKnowledgeBaseAsync(
        string inputJson, ConversationResponse response)
    {
        string query;
        int maxResults = 5;
        try
        {
            var doc = JsonDocument.Parse(inputJson);
            query = doc.RootElement.GetProperty("query").GetString() ?? "";
            if (doc.RootElement.TryGetProperty("max_results", out var maxProp) &&
                maxProp.ValueKind == JsonValueKind.Number)
            {
                maxResults = Math.Clamp((int)maxProp.GetDouble(), 1, 20);
            }
        }
        catch
        {
            return ("Error: 'query' parameter is required.", true);
        }

        if (string.IsNullOrWhiteSpace(query))
            return ("Error: 'query' must not be empty.", true);

        try
        {
            var results = await _knowledgeBase.SearchAsync(query, maxResults);

            response.ActivityLog.Add(new ActivityLogEntry
            {
                Type = "info",
                Message = $"Knowledge base searched: \"{query}\" → {results.Count} result(s)"
            });

            if (results.Count == 0)
                return ("No matching documents found in the knowledge base.", false);

            var sb = new StringBuilder();
            sb.AppendLine($"Found {results.Count} knowledge base entr{(results.Count == 1 ? "y" : "ies")}:");
            sb.AppendLine();
            int i = 1;
            foreach (var doc in results)
            {
                sb.AppendLine($"### {i}. {doc.Title}");
                sb.AppendLine(doc.Content);
                sb.AppendLine();
                i++;
            }

            return (sb.ToString().TrimEnd(), false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Knowledge base search failed for query '{Query}'.", query);
            // Non-fatal: return the error to the model so it can respond gracefully
            // (e.g. when the Voyage API key is not configured).
            return ($"Knowledge base search failed: {ex.Message}", true);
        }
    }

    /// <summary>
    /// Phase 2 (Activation): Returns the full SKILL.md body + script tool listing
    /// when Claude calls read_skill. This is the progressive disclosure step.
    /// </summary>
    private (string Result, bool IsError) HandleReadSkill(
        Agent agent, string inputJson, ConversationResponse response)
    {
        string requestedName;
        try
        {
            var doc = JsonDocument.Parse(inputJson);
            requestedName = doc.RootElement.GetProperty("skill_name").GetString() ?? "";
        }
        catch
        {
            return ("Error: 'skill_name' parameter is required.", true);
        }

        foreach (var skill in agent.Skills)
        {
            var parseResult = _skillParser.Parse(skill.SkillMd);
            var name = parseResult.IsValid ? parseResult.Frontmatter.Name : skill.Name;

            if (!string.Equals(name, requestedName, StringComparison.OrdinalIgnoreCase))
                continue;

            _logger.LogInformation("Skill activated via read_skill: '{SkillName}' (Id={SkillId})", skill.Name, skill.Id);

            response.ActivityLog.Add(new ActivityLogEntry
            {
                Type = "info",
                Message = $"Skill activated: {name} (full SKILL.md loaded)"
            });

            var sb = new StringBuilder();

            if (parseResult.IsValid && !string.IsNullOrWhiteSpace(parseResult.Body))
            {
                sb.AppendLine(parseResult.Body);
            }
            else if (!string.IsNullOrWhiteSpace(skill.SkillMd))
            {
                sb.AppendLine(skill.SkillMd);
            }

            var scripts = DeserializeScripts(skill.ScriptsJson);
            if (scripts.Any())
            {
                sb.AppendLine();
                sb.AppendLine("## Available script tools");
                sb.AppendLine();
                var toolPrefix = parseResult.IsValid
                    ? _skillParser.SanitizeNameForToolUse(parseResult.Frontmatter.Name)
                    : SanitizeLegacyName(skill.Name);

                foreach (var script in scripts)
                {
                    var scriptToolName = $"{toolPrefix}__{SanitizeScriptName(script.Name)}";
                    sb.AppendLine($"- `{scriptToolName}`: {script.Name} ({script.Language})");

                    foreach (var p in script.Parameters)
                    {
                        var reqLabel = p.Required ? "required" : "optional";
                        sb.AppendLine($"  - `{p.Name}` ({p.Type}, {reqLabel}): {p.Description}");
                    }
                }
            }

            return (sb.ToString(), false);
        }

        var availableNames = agent.Skills.Select(s =>
        {
            var pr = _skillParser.Parse(s.SkillMd);
            return pr.IsValid ? pr.Frontmatter.Name : s.Name;
        });
        return ($"Error: Skill '{requestedName}' not found. Available skills: {string.Join(", ", availableNames)}", true);
    }

    /// <summary>
    /// Phase 3 (Execution): Finds and executes the correct script by matching
    /// the tool name back to skill + script.
    /// </summary>
    private async Task<(string Result, bool IsError)> ExecuteSkillScriptAsync(Agent agent, string toolName, string inputJson)
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
        return ($"Error: No matching skill or script found for tool '{toolName}'.", true);
    }

    private async Task<(string Result, bool IsError)> RunScriptAsync(AgentSkillScript script, string inputJson)
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
                return ("Error: Failed to start process.", true);

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Script '{ScriptName}' exited with code {ExitCode}. Stderr: {Stderr}",
                    script.Name, process.ExitCode, error);
                return ($"Script exited with code {process.ExitCode}:\n{error}", true);
            }

            return (output, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception running script '{ScriptName}'.", script.Name);
            return ($"Exception during execution: {ex.Message}", true);
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

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
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
}
