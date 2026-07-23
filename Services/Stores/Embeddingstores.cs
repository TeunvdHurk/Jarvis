using System.Net.Http.Json;
using Jarvis.Mind.Api.Models;

namespace Jarvis.Mind.Api.Services.Stores;

/// <summary>
/// Registered until you've generated and stored embeddings. Reports "no embeddings yet"
/// honestly rather than pretending - the assembler falls back to tag/category co-occurrence
/// edges and stats.SimilarityIsSemantic stays false. Swap the DI registration for
/// ChromaEmbeddingStore in Program.cs once you've run the embedding job.
/// </summary>
public sealed class NullEmbeddingStore : IEmbeddingStore
{
    public Task<bool> HasEmbeddingsAsync(CancellationToken ct) => Task.FromResult(false);

    public Task<IReadOnlyDictionary<string, float[]>> GetEmbeddingsAsync(IReadOnlyList<string> memoryIds, CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<string, float[]>>(new Dictionary<string, float[]>());

    public Task<IReadOnlyList<(string Id, double Similarity)>> QueryNeighborsAsync(string memoryId, int topK, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<(string, double)>>(Array.Empty<(string, double)>());
}

/// <summary>
/// Minimal Chroma REST client (v1 `/api/v1/collections/{id}/get` and `/query` shape).
/// Chroma's HTTP API has changed across versions - verify these two routes against
/// whatever version you deploy before relying on this; the interface contract is what
/// the rest of the app depends on, this implementation is the swappable part.
/// </summary>
public sealed class ChromaEmbeddingStore : IEmbeddingStore
{
    private readonly HttpClient _http;
    private readonly string _collection;
    private readonly ILogger<ChromaEmbeddingStore> _logger;

    public ChromaEmbeddingStore(HttpClient http, IConfiguration config, ILogger<ChromaEmbeddingStore> logger)
    {
        _http = http;
        _collection = config["Jarvis:ChromaCollection"] ?? "jarvis-memories";
        _logger = logger;
    }

    public async Task<bool> HasEmbeddingsAsync(CancellationToken ct)
    {
        try
        {
            var resp = await _http.GetAsync($"/api/v1/collections/{_collection}", ct);
            if (!resp.IsSuccessStatusCode) return false;
            var info = await resp.Content.ReadFromJsonAsync<ChromaCollectionInfo>(cancellationToken: ct);
            return (info?.Count ?? 0) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chroma collection check failed; treating as no embeddings");
            return false;
        }
    }

    public async Task<IReadOnlyDictionary<string, float[]>> GetEmbeddingsAsync(IReadOnlyList<string> memoryIds, CancellationToken ct)
    {
        if (memoryIds.Count == 0) return new Dictionary<string, float[]>();

        var resp = await _http.PostAsJsonAsync(
            $"/api/v1/collections/{_collection}/get",
            new { ids = memoryIds, include = new[] { "embeddings" } },
            ct);
        resp.EnsureSuccessStatusCode();

        var payload = await resp.Content.ReadFromJsonAsync<ChromaGetResponse>(cancellationToken: ct)
                      ?? new ChromaGetResponse();

        var result = new Dictionary<string, float[]>();
        for (var i = 0; i < payload.Ids.Count; i++)
        {
            if (i < payload.Embeddings.Count)
                result[payload.Ids[i]] = payload.Embeddings[i];
        }
        return result;
    }

    public async Task<IReadOnlyList<(string Id, double Similarity)>> QueryNeighborsAsync(string memoryId, int topK, CancellationToken ct)
    {
        var vectors = await GetEmbeddingsAsync(new[] { memoryId }, ct);
        if (!vectors.TryGetValue(memoryId, out var queryVector)) return Array.Empty<(string, double)>();

        var resp = await _http.PostAsJsonAsync(
            $"/api/v1/collections/{_collection}/query",
            new { query_embeddings = new[] { queryVector }, n_results = topK + 1 },
            ct);
        resp.EnsureSuccessStatusCode();

        var payload = await resp.Content.ReadFromJsonAsync<ChromaQueryResponse>(cancellationToken: ct)
                      ?? new ChromaQueryResponse();

        var ids = payload.Ids.FirstOrDefault() ?? new List<string>();
        var distances = payload.Distances.FirstOrDefault() ?? new List<double>();

        return ids.Zip(distances, (id, dist) => (id, Similarity: 1.0 - dist))
                   .Where(pair => pair.id != memoryId)
                   .Take(topK)
                   .ToList();
    }

    private sealed class ChromaCollectionInfo { public int Count { get; set; } }
    private sealed class ChromaGetResponse
    {
        public List<string> Ids { get; set; } = new();
        public List<float[]> Embeddings { get; set; } = new();
    }
    private sealed class ChromaQueryResponse
    {
        public List<List<string>> Ids { get; set; } = new();
        public List<List<double>> Distances { get; set; } = new();
    }
}