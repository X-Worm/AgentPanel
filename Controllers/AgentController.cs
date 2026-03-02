using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AgentControlPanel.Data;
using AgentControlPanel.Models;
using AgentControlPanel.Services;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime.Documents;

namespace AgentControlPanel.Controllers;

public class AgentController : Controller
{
    private readonly AppDbContext _context;
    private readonly IBedrockService _bedrockService;
    private readonly IConversationService _conversationService;
    private readonly ILogger<AgentController> _logger;

    public AgentController(
        AppDbContext context,
        IBedrockService bedrockService,
        IConversationService conversationService,
        ILogger<AgentController> logger)
    {
        _context = context;
        _bedrockService = bedrockService;
        _conversationService = conversationService;
        _logger = logger;
    }

    // GET: Agent
    public async Task<IActionResult> Index()
    {
        return View(await _context.Agents.ToListAsync());
    }

    // GET: Agent/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();
        var agent = await _context.Agents
            .Include(a => a.Skills)
            .Include(a => a.KnowledgeBases)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (agent == null) return NotFound();
        return View(agent);
    }

    // GET: Agent/Create
    public async Task<IActionResult> Create()
    {
        ViewBag.Skills = await _context.Skills.ToListAsync();
        return View();
    }

    // POST: Agent/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Id,Name,Description,SystemPrompt")] Agent agent, int[] selectedSkills)
    {
        if (ModelState.IsValid)
        {
            if (selectedSkills != null && selectedSkills.Length > 0)
            {
                agent.Skills = await _context.Skills.Where(s => selectedSkills.Contains(s.Id)).ToListAsync();
            }

            _context.Add(agent);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        ViewBag.Skills = await _context.Skills.ToListAsync();
        return View(agent);
    }

    // GET: Agent/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();
        var agent = await _context.Agents.Include(a => a.Skills).FirstOrDefaultAsync(a => a.Id == id);
        if (agent == null) return NotFound();
        
        ViewBag.Skills = await _context.Skills.ToListAsync();
        return View(agent);
    }

    // POST: Agent/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Description,SystemPrompt")] Agent agent, int[] selectedSkills)
    {
        if (id != agent.Id) return NotFound();
        
        if (ModelState.IsValid)
        {
            try
            {
                var existingAgent = await _context.Agents.Include(a => a.Skills).FirstOrDefaultAsync(a => a.Id == id);
                if (existingAgent == null) return NotFound();

                existingAgent.Name = agent.Name;
                existingAgent.Description = agent.Description;
                existingAgent.SystemPrompt = agent.SystemPrompt;

                existingAgent.Skills.Clear();
                if (selectedSkills != null && selectedSkills.Length > 0)
                {
                    existingAgent.Skills = await _context.Skills.Where(s => selectedSkills.Contains(s.Id)).ToListAsync();
                }

                _context.Update(existingAgent);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!AgentExists(agent.Id)) return NotFound();
                else throw;
            }
            return RedirectToAction(nameof(Index));
        }
        ViewBag.Skills = await _context.Skills.ToListAsync();
        return View(agent);
    }

    // GET: Agent/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();
        var agent = await _context.Agents.FirstOrDefaultAsync(m => m.Id == id);
        if (agent == null) return NotFound();
        return View(agent);
    }

    // POST: Agent/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var agent = await _context.Agents.FindAsync(id);
        if (agent != null) _context.Agents.Remove(agent);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // GET: Agent/Test/5
    public async Task<IActionResult> Test(int? id)
    {
        if (id == null) return NotFound();
        var agent = await _context.Agents.Include(a => a.Skills).FirstOrDefaultAsync(a => a.Id == id);
        if (agent == null) return NotFound();
        return View(agent);
    }

    [HttpPost]
    public async Task<IActionResult> SendMessage([FromBody] ChatRequest request)
    {
        _logger.LogInformation(
            "SendMessage start. AgentId={AgentId}, MessageLength={MessageLength}, HistoryCount={HistoryCount}",
            request.AgentId,
            request.Message?.Length ?? 0,
            request.History?.Count ?? 0);

        var agent = await _context.Agents.Include(a => a.Skills).FirstOrDefaultAsync(a => a.Id == request.AgentId);
        if (agent == null) return NotFound();

        var history = request.History != null
            ? request.History.Select(MapDtoToMessage).ToList()
            : new List<Message>();

        _logger.LogDebug("Mapped incoming history to Bedrock model. HistoryCount={HistoryCount}", history.Count);

        var result = await _conversationService.ProcessMessageAsync(agent, history, request.Message ?? string.Empty);
        var safeHistory = result.UpdatedHistory.Select(MapMessageToDto).ToList();

        _logger.LogInformation(
            "SendMessage completed. UpdatedHistoryCount={UpdatedHistoryCount}, ActivityLogCount={ActivityLogCount}, ResponseLength={ResponseLength}",
            safeHistory.Count,
            result.ActivityLog.Count,
            result.FinalResponse?.Length ?? 0);

        return Json(new { 
            history = safeHistory,
            activityLog = result.ActivityLog, 
            response = result.FinalResponse 
        });
    }

    private bool AgentExists(int id)
    {
        return _context.Agents.Any(e => e.Id == id);
    }

    [HttpPost]
    public async Task<IActionResult> GenerateSystemPrompt([FromBody] GeneratePromptRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Name))
            return BadRequest("Agent Name is required");

        string prompt = $@"You are an expert AI system prompt engineer. Your task is to write an optimal, highly detailed System Prompt for a new AI Agent.

Agent Name: {request.Name}
Agent Description: {request.Description}

Write the system prompt directly to the agent. Establish its persona, primary goals, strict instructions, and operational constraints based on the provided name and description. Only output the raw text of the final system prompt. Do not include introductory or concluding commentary.";

        var systemPrompt = await _bedrockService.InvokeClaudeAsync(prompt);
        return Json(new { prompt = systemPrompt.Trim() });
    }

    public class ChatRequest
    {
        public int AgentId { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<ChatMessageDto>? History { get; set; }
    }

    public class GeneratePromptRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class ChatMessageDto
    {
        public string Role { get; set; } = "user";
        public List<ChatContentDto> Content { get; set; } = new();
    }

    public class ChatContentDto
    {
        public string? Text { get; set; }
        public ChatToolUseDto? ToolUse { get; set; }
        public ChatToolResultDto? ToolResult { get; set; }
    }

    public class ChatToolUseDto
    {
        public string ToolUseId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public object? Input { get; set; }
    }

    public class ChatToolResultDto
    {
        public string ToolUseId { get; set; } = string.Empty;
        public string Status { get; set; } = "success";
        public string? Text { get; set; }
    }

    private static ChatMessageDto MapMessageToDto(Message message)
    {
        var dto = new ChatMessageDto
        {
            Role = message.Role?.Value ?? "user"
        };

        foreach (var block in message.Content ?? new List<ContentBlock>())
        {
            if (!string.IsNullOrEmpty(block.Text))
            {
                dto.Content.Add(new ChatContentDto { Text = block.Text });
                continue;
            }

            if (block.ToolUse != null)
            {
                dto.Content.Add(new ChatContentDto
                {
                    ToolUse = new ChatToolUseDto
                    {
                        ToolUseId = block.ToolUse.ToolUseId,
                        Name = block.ToolUse.Name,
                        Input = ConvertDocumentToStandard(block.ToolUse.Input)
                    }
                });
                continue;
            }

            if (block.ToolResult != null)
            {
                dto.Content.Add(new ChatContentDto
                {
                    ToolResult = new ChatToolResultDto
                    {
                        ToolUseId = block.ToolResult.ToolUseId,
                        Status = block.ToolResult.Status?.Value ?? "success",
                        Text = block.ToolResult.Content?.FirstOrDefault()?.Text
                    }
                });
            }
        }

        return dto;
    }

    private static Message MapDtoToMessage(ChatMessageDto dto)
    {
        var role = dto.Role?.ToLowerInvariant() == "assistant"
            ? ConversationRole.Assistant
            : ConversationRole.User;

        var content = new List<ContentBlock>();

        foreach (var block in dto.Content ?? new List<ChatContentDto>())
        {
            if (!string.IsNullOrEmpty(block.Text))
            {
                content.Add(new ContentBlock { Text = block.Text });
                continue;
            }

            if (block.ToolUse != null)
            {
                content.Add(new ContentBlock
                {
                    ToolUse = new ToolUseBlock
                    {
                        ToolUseId = block.ToolUse.ToolUseId,
                        Name = block.ToolUse.Name,
                        Input = Document.FromObject(block.ToolUse.Input ?? new Dictionary<string, object?>())
                    }
                });
                continue;
            }

            if (block.ToolResult != null)
            {
                var status = block.ToolResult.Status?.ToLowerInvariant() == "error"
                    ? ToolResultStatus.Error
                    : ToolResultStatus.Success;

                content.Add(new ContentBlock
                {
                    ToolResult = new ToolResultBlock
                    {
                        ToolUseId = block.ToolResult.ToolUseId,
                        Status = status,
                        Content = new List<ToolResultContentBlock>
                        {
                            new ToolResultContentBlock { Text = block.ToolResult.Text ?? string.Empty }
                        }
                    }
                });
            }
        }

        return new Message
        {
            Role = role,
            Content = content
        };
    }

    private static object? ConvertDocumentToStandard(Document doc)
    {
        if (doc.IsNull()) return null;
        if (doc.IsString()) return doc.AsString();
        if (doc.IsDouble()) return doc.AsDouble();
        if (doc.IsBool()) return doc.AsBool();
        if (doc.IsInt()) return doc.AsInt();
        if (doc.IsLong()) return doc.AsLong();

        if (doc.IsList())
        {
            return doc.AsList().Select(ConvertDocumentToStandard).ToList();
        }

        if (doc.IsDictionary())
        {
            var dict = new Dictionary<string, object?>();
            foreach (var kvp in doc.AsDictionary())
            {
                dict[kvp.Key] = ConvertDocumentToStandard(kvp.Value);
            }
            return dict;
        }

        return null;
    }
}
