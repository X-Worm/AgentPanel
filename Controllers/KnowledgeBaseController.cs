using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AgentControlPanel.Data;
using AgentControlPanel.Models;

namespace AgentControlPanel.Controllers;

public class KnowledgeBaseController : Controller
{
    private readonly AppDbContext _context;

    public KnowledgeBaseController(AppDbContext context)
    {
        _context = context;
    }

    // GET: KnowledgeBase
    public async Task<IActionResult> Index()
    {
        return View(await _context.KnowledgeBases.ToListAsync());
    }

    // GET: KnowledgeBase/Create
    public IActionResult Create()
    {
        return View();
    }

    // POST: KnowledgeBase/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Id,Name,QdrantCollectionName")] KnowledgeBase knowledgeBase)
    {
        if (ModelState.IsValid)
        {
            _context.Add(knowledgeBase);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        return View(knowledgeBase);
    }

    // GET: KnowledgeBase/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();
        var kb = await _context.KnowledgeBases.FirstOrDefaultAsync(m => m.Id == id);
        if (kb == null) return NotFound();
        return View(kb);
    }

    // POST: KnowledgeBase/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var kb = await _context.KnowledgeBases.FindAsync(id);
        if (kb != null) _context.KnowledgeBases.Remove(kb);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
