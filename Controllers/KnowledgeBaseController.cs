using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using AgentControlPanel.Models;
using AgentControlPanel.Services;

namespace AgentControlPanel.Controllers;

public class KnowledgeBaseController : Controller
{
    private readonly IKnowledgeBaseService _kb;
    private readonly ILogger<KnowledgeBaseController> _logger;

    public KnowledgeBaseController(IKnowledgeBaseService kb, ILogger<KnowledgeBaseController> logger)
    {
        _kb = kb;
        _logger = logger;
    }

    // GET: KnowledgeBase
    public async Task<IActionResult> Index()
    {
        var docs = await _kb.ListAsync();
        return View(docs);
    }

    // GET: KnowledgeBase/Create
    public IActionResult Create() => View(new KnowledgeDocument());

    // POST: KnowledgeBase/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("llm")]
    public async Task<IActionResult> Create([Bind("Title,Content")] KnowledgeDocument doc)
    {
        if (!ModelState.IsValid) return View(doc);

        try
        {
            await _kb.AddAsync(doc.Title, doc.Content);
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add knowledge document.");
            // Surfaces "Voyage API key not configured" and other embedding errors.
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(doc);
        }
    }

    // POST: KnowledgeBase/Delete/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        await _kb.DeleteAsync(id);
        return RedirectToAction(nameof(Index));
    }
}
