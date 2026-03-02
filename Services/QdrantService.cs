using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace AgentControlPanel.Services;

public interface IQdrantService
{
    Task CreateCollectionAsync(string collectionName);
    Task DeleteCollectionAsync(string collectionName);
}

public class QdrantService : IQdrantService
{
    private readonly QdrantClient _client;

    public QdrantService(IConfiguration configuration)
    {
        var url = configuration["Qdrant:Url"] ?? "http://localhost:6334";
        _client = new QdrantClient(new Uri(url));
    }

    public async Task CreateCollectionAsync(string collectionName)
    {
        await _client.CreateCollectionAsync(collectionName, new VectorParams { Size = 1536, Distance = Distance.Cosine });
    }

    public async Task DeleteCollectionAsync(string collectionName)
    {
        await _client.DeleteCollectionAsync(collectionName);
    }
}
