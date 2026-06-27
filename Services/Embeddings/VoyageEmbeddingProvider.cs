using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentControlPanel.Services.Embeddings;

/// <summary>
/// IEmbeddingProvider backed by the Voyage AI embeddings REST API
/// (POST https://api.voyageai.com/v1/embeddings).
/// </summary>
public class VoyageEmbeddingProvider : IEmbeddingProvider
{
    private const string EmbeddingsEndpoint = "https://api.voyageai.com/v1/embeddings";

    private readonly HttpClient _http;
    private readonly VoyageOptions _options;
    private readonly ILogger<VoyageEmbeddingProvider> _logger;
    private readonly string? _apiKey;

    public int OutputDimension => _options.OutputDimension;

    public VoyageEmbeddingProvider(HttpClient http, VoyageOptions options, ILogger<VoyageEmbeddingProvider> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;

        // Empty ApiKey => fall back to the VOYAGE_API_KEY environment variable.
        _apiKey = string.IsNullOrWhiteSpace(options.ApiKey)
            ? Environment.GetEnvironmentVariable("VOYAGE_API_KEY")
            : options.ApiKey;
    }

    public async Task<float[]> EmbedAsync(string text, EmbeddingInputType inputType, CancellationToken ct = default)
    {
        var result = await EmbedBatchAsync(new[] { text }, inputType, ct);
        return result[0];
    }

    public async Task<List<float[]>> EmbedBatchAsync(IEnumerable<string> texts, EmbeddingInputType inputType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException(
                "Voyage API key not configured. Set Voyage:ApiKey in appsettings.json or the VOYAGE_API_KEY environment variable.");

        var requestBody = new VoyageRequest
        {
            Input = texts.ToList(),
            Model = _options.Model,
            InputType = inputType == EmbeddingInputType.Query ? "query" : "document",
            OutputDimension = _options.OutputDimension
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, EmbeddingsEndpoint)
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await _http.SendAsync(request, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Voyage embeddings request failed: {Status} {Body}", response.StatusCode, payload);
            throw new InvalidOperationException($"Voyage API error ({(int)response.StatusCode}): {payload}");
        }

        var parsed = JsonSerializer.Deserialize<VoyageResponse>(payload);
        if (parsed?.Data == null || parsed.Data.Count == 0)
            throw new InvalidOperationException("Voyage API returned an unexpected response (no embedding data).");

        return parsed.Data.OrderBy(d => d.Index).Select(d => d.Embedding).ToList();
    }

    private sealed class VoyageRequest
    {
        [JsonPropertyName("input")] public List<string> Input { get; set; } = new();
        [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
        [JsonPropertyName("input_type")] public string InputType { get; set; } = "document";
        [JsonPropertyName("output_dimension")] public int OutputDimension { get; set; }
    }

    private sealed class VoyageResponse
    {
        [JsonPropertyName("data")] public List<VoyageEmbeddingData>? Data { get; set; }
    }

    private sealed class VoyageEmbeddingData
    {
        [JsonPropertyName("embedding")] public float[] Embedding { get; set; } = Array.Empty<float>();
        [JsonPropertyName("index")] public int Index { get; set; }
    }
}
