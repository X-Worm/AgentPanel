using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AgentControlPanel.Data;
using AgentControlPanel.Models;

namespace AgentControlPanel.Controllers;

public class MCPController : Controller
{
    private readonly AppDbContext _context;

    public MCPController(AppDbContext context)
    {
        _context = context;
    }

    // GET: MCP
    public async Task<IActionResult> Index()
    {
        return View(await _context.MCPConfigs.ToListAsync());
    }

    // GET: MCP/Create
    public IActionResult Create()
    {
        return View();
    }

    // POST: MCP/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Id,Name,Endpoint,ConfigurationJson")] MCPConfig mcpConfig)
    {
        if (ModelState.IsValid)
        {
            _context.Add(mcpConfig);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        return View(mcpConfig);
    }

    // GET: MCP/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();
        var mcp = await _context.MCPConfigs.FirstOrDefaultAsync(m => m.Id == id);
        if (mcp == null) return NotFound();
        return View(mcp);
    }

    // POST: MCP/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var mcp = await _context.MCPConfigs.FindAsync(id);
        if (mcp != null) _context.MCPConfigs.Remove(mcp);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
