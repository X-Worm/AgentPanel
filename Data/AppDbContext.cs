using Microsoft.EntityFrameworkCore;
using AgentControlPanel.Models;

namespace AgentControlPanel.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Agent> Agents { get; set; }
    public DbSet<Skill> Skills { get; set; }
    public DbSet<KnowledgeDocument> KnowledgeDocuments { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable the pgvector extension and fix the embedding column dimension
        // to match the Voyage output (voyage-3-large @ 1024 dims).
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.Entity<KnowledgeDocument>()
            .Property(d => d.Embedding)
            .HasColumnType("vector(1024)");
    }
}
