using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AgentControlPanel.Data;
using AgentControlPanel.Models;
using AgentControlPanel.Services;

namespace AgentControlPanel.Controllers;

public class SkillController : Controller
{
    private readonly AppDbContext _context;
    private readonly IBedrockService _bedrockService;
    private readonly ISkillParser _skillParser;
    private readonly ILogger<SkillController> _logger;

    public SkillController(AppDbContext context, IBedrockService bedrockService, ISkillParser skillParser, ILogger<SkillController> logger)
    {
        _context = context;
        _bedrockService = bedrockService;
        _skillParser = skillParser;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> GenerateWithAI([FromBody] string description)
    {
        if (string.IsNullOrEmpty(description)) return BadRequest("Description is required.");

        var prompt = $@"Generate an AI skill based on this description: {description}.

Return ONLY a valid JSON object with three properties:

1. 'name': a short, descriptive skill name following the agentskills.io naming rules:
   - Lowercase letters, numbers, and hyphens only (e.g. 'npi-registry-lookup')
   - Max 64 characters
   - Must not start or end with a hyphen
   - Must not contain consecutive hyphens

2. 'skillMd': the full content of a SKILL.md file that follows the agentskills.io specification exactly.
   It MUST start with YAML frontmatter delimited by '---' lines containing:
   - name: (same as the 'name' property above)
   - description: a clear description of what the skill does and when to use it (max 1024 chars, third person)
   Then a markdown body with instructions for the agent: quick start, usage examples, edge cases, workflows.
   Keep the body under 500 lines and be concise — assume Claude is knowledgeable.
   Do NOT invent custom YAML fields like 'config:'. Only use the standard fields: name, description, license, compatibility, metadata, allowed-tools.

3. 'scripts': a JSON array of script objects. Each object must have:
   - 'name' (string, e.g. 'run.py')
   - 'language' (string: 'python', 'bash', or 'js')
   - 'code' (string, the complete executable script)
   - 'parameters' (array of parameter objects, each with 'name', 'description', 'type' (string/number/boolean), and 'required' (true/false))
   Parameters define the inputs the script expects as environment variables.

Example of a valid SKILL.md in the 'skillMd' field:
---
name: weather-lookup
description: Fetches current weather data for a given city. Use when the user asks about weather conditions or forecasts.
---

# Weather Lookup

## Quick Start
This skill retrieves weather data using the OpenWeatherMap API.

## Usage
Call the `weather_lookup__run` tool with the city name to get current conditions.

Do not include any extra text, markdown formatting blocks (like ```json), or explanations outside the JSON object.";

        var jsonResponse = await _bedrockService.InvokeClaudeAsync(prompt);

        if (jsonResponse.StartsWith("Error: "))
        {
            return BadRequest($"AWS Bedrock returned an error: {jsonResponse}");
        }

        var cleanedJson = jsonResponse.Trim();
        if (cleanedJson.StartsWith("```json"))
            cleanedJson = cleanedJson.Substring("```json".Length);
        else if (cleanedJson.StartsWith("```"))
            cleanedJson = cleanedJson.Substring("```".Length);

        if (cleanedJson.EndsWith("```"))
            cleanedJson = cleanedJson.Substring(0, cleanedJson.Length - "```".Length);
        cleanedJson = cleanedJson.Trim();

        try
        {
            var content = System.Text.Json.JsonDocument.Parse(cleanedJson);
            var root = content.RootElement;
            var generatedName = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;

            if (string.IsNullOrWhiteSpace(generatedName))
                return BadRequest("AI did not generate a valid skill name.");

            var nameValidation = _skillParser.ValidateName(generatedName);
            if (!nameValidation.IsValid)
            {
                _logger.LogWarning("AI-generated name '{Name}' failed validation: {Errors}",
                    generatedName, string.Join("; ", nameValidation.Errors));
                return BadRequest($"Generated skill name is invalid: {string.Join("; ", nameValidation.Errors)}");
            }

            var skillMd = root.GetProperty("skillMd").GetString() ?? "";

            var parseResult = _skillParser.Parse(skillMd);
            if (!parseResult.IsValid)
            {
                _logger.LogWarning("AI-generated SKILL.md failed validation: {Errors}",
                    string.Join("; ", parseResult.Errors));
                return BadRequest($"Generated SKILL.md is invalid: {string.Join("; ", parseResult.Errors)}");
            }

            var scriptsArray = root.GetProperty("scripts");
            var scriptsJson = scriptsArray.GetRawText();

            _logger.LogInformation("Successfully generated skill. Name: {Name}, Scripts count: {Count}",
                generatedName, scriptsArray.GetArrayLength());

            var skill = new Skill
            {
                Name = generatedName,
                SkillMd = skillMd,
                ScriptsJson = scriptsJson,
                IsAIGenerated = true
            };

            _context.Skills.Add(skill);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse JSON response from Claude. Raw JSON Length: {Length}", cleanedJson.Length);
            return BadRequest($"Failed to parse JSON response from Claude: {ex.Message}");
        }
    }

    // GET: Skill
    public async Task<IActionResult> Index()
    {
        return View(await _context.Skills.ToListAsync());
    }

    // GET: Skill/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();
        var skill = await _context.Skills.FirstOrDefaultAsync(m => m.Id == id);
        if (skill == null) return NotFound();
        return View(skill);
    }

    // GET: Skill/Create
    public IActionResult Create()
    {
        return View();
    }

    // POST: Skill/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Id,Name,SkillMd,ScriptsJson,IsAIGenerated")] Skill skill)
    {
        if (ModelState.IsValid)
        {
            var nameValidation = _skillParser.ValidateName(skill.Name);
            if (!nameValidation.IsValid)
            {
                foreach (var error in nameValidation.Errors)
                    ModelState.AddModelError("Name", error);
                return View(skill);
            }

            if (!string.IsNullOrWhiteSpace(skill.SkillMd))
            {
                var parseResult = _skillParser.Parse(skill.SkillMd);
                if (!parseResult.IsValid)
                {
                    foreach (var error in parseResult.Errors)
                        ModelState.AddModelError("SkillMd", error);
                    return View(skill);
                }
            }

            _context.Add(skill);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        return View(skill);
    }

    // GET: Skill/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();
        var skill = await _context.Skills.FindAsync(id);
        if (skill == null) return NotFound();
        return View(skill);
    }

    // POST: Skill/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,Name,SkillMd,ScriptsJson,IsAIGenerated")] Skill skill)
    {
        if (id != skill.Id) return NotFound();
        if (ModelState.IsValid)
        {
            var nameValidation = _skillParser.ValidateName(skill.Name);
            if (!nameValidation.IsValid)
            {
                foreach (var error in nameValidation.Errors)
                    ModelState.AddModelError("Name", error);
                return View(skill);
            }

            if (!string.IsNullOrWhiteSpace(skill.SkillMd))
            {
                var parseResult = _skillParser.Parse(skill.SkillMd);
                if (!parseResult.IsValid)
                {
                    foreach (var error in parseResult.Errors)
                        ModelState.AddModelError("SkillMd", error);
                    return View(skill);
                }
            }

            try
            {
                _context.Update(skill);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SkillExists(skill.Id)) return NotFound();
                else throw;
            }
            return RedirectToAction(nameof(Index));
        }
        return View(skill);
    }

    // GET: Skill/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();
        var skill = await _context.Skills.FirstOrDefaultAsync(m => m.Id == id);
        if (skill == null) return NotFound();
        return View(skill);
    }

    // POST: Skill/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var skill = await _context.Skills.FindAsync(id);
        if (skill != null) _context.Skills.Remove(skill);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private bool SkillExists(int id)
    {
        return _context.Skills.Any(e => e.Id == id);
    }
}
