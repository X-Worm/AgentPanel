using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using AgentControlPanel.Data;
using AgentControlPanel.Models;
using AgentControlPanel.Services.Embeddings;

namespace AgentControlPanel.Services;

public interface IKnowledgeBaseService
{
    Task<List<KnowledgeDocument>> ListAsync(CancellationToken ct = default);
    Task<KnowledgeDocument> AddAsync(string title, string content, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Embeds the query and returns the most similar knowledge documents,
    /// ordered by ascending cosine distance (closest first).
    /// </summary>
    Task<List<KnowledgeDocument>> SearchAsync(string query, int topK = 5, CancellationToken ct = default);
}

public class KnowledgeBaseService : IKnowledgeBaseService
{
    private readonly AppDbContext _db;
    private readonly IEmbeddingProvider _embeddings;
    private readonly ILogger<KnowledgeBaseService> _logger;

    public KnowledgeBaseService(AppDbContext db, IEmbeddingProvider embeddings, ILogger<KnowledgeBaseService> logger)
    {
        _db = db;
        _embeddings = embeddings;
        _logger = logger;
    }

    public async Task<List<KnowledgeDocument>> ListAsync(CancellationToken ct = default) =>
        await _db.KnowledgeDocuments.OrderByDescending(d => d.CreatedAt).ToListAsync(ct);

    public async Task<KnowledgeDocument> AddAsync(string title, string content, CancellationToken ct = default)
    {
        // One embedding per entry over the combined Title + Content (per the
        // "just Title + Text" requirement). Chunking is a future enhancement.
        var text = $"{title}\n\n{content}";
        var embedding = await _embeddings.EmbedAsync(text, EmbeddingInputType.Document, ct);

        var doc = new KnowledgeDocument
        {
            Title = title,
            Content = content,
            Embedding = new Vector(embedding)
        };

        _db.KnowledgeDocuments.Add(doc);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Knowledge document added. Id={Id}, Title={Title}", doc.Id, doc.Title);
        return doc;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var doc = await _db.KnowledgeDocuments.FindAsync(new object?[] { id }, ct);
        if (doc != null)
        {
            _db.KnowledgeDocuments.Remove(doc);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<List<KnowledgeDocument>> SearchAsync(string query, int topK = 5, CancellationToken ct = default)
    {
        if (topK < 1) topK = 1;

        var queryEmbedding = await _embeddings.EmbedAsync(query, EmbeddingInputType.Query, ct);
        var queryVector = new Vector(queryEmbedding);

        // Translates to a pgvector "<=>" (cosine distance) ORDER BY in SQL.
        return await _db.KnowledgeDocuments
            .Where(d => d.Embedding != null)
            .OrderBy(d => d.Embedding!.CosineDistance(queryVector))
            .Take(topK)
            .ToListAsync(ct);
    }
}
