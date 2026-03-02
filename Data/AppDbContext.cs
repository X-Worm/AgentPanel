using Microsoft.EntityFrameworkCore;
using AgentControlPanel.Models;

namespace AgentControlPanel.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Agent> Agents { get; set; }
    public DbSet<Skill> Skills { get; set; }
    public DbSet<KnowledgeBase> KnowledgeBases { get; set; }
    public DbSet<MCPConfig> MCPConfigs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Additional configuration if needed
    }
}
