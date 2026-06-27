namespace AgentControlPanel.Services.Embeddings;

/// <summary>
/// Bound from the "Voyage" configuration section.
/// </summary>
public class VoyageOptions
{
    /// <summary>
    /// API key for api.voyageai.com. If empty, the provider falls back to the
    /// VOYAGE_API_KEY environment variable (mirrors the Anthropic key handling).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Voyage embedding model to use.</summary>
    public string Model { get; set; } = "voyage-4-lite";

    /// <summary>
    /// Output vector dimension. Must match the pgvector column dimension
    /// (vector(1024)). The voyage-4 family supports 256, 512, 1024, 2048.
    /// </summary>
    public int OutputDimension { get; set; } = 1024;
}
