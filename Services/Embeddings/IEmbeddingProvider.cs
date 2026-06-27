namespace AgentControlPanel.Services.Embeddings;

/// <summary>
/// How the text being embedded will be used. Voyage produces asymmetric
/// embeddings, so stored documents and search queries are embedded differently.
/// </summary>
public enum EmbeddingInputType
{
    Document,
    Query
}

/// <summary>
/// Abstraction over a text embedding provider. The default implementation calls
/// Voyage AI (Anthropic's recommended embeddings partner — the Anthropic SDK has
/// no embeddings API of its own).
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>The dimensionality of the vectors this provider returns.</summary>
    int OutputDimension { get; }

    Task<float[]> EmbedAsync(string text, EmbeddingInputType inputType, CancellationToken ct = default);

    Task<List<float[]>> EmbedBatchAsync(IEnumerable<string> texts, EmbeddingInputType inputType, CancellationToken ct = default);
}
