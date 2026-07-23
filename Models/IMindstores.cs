using Jarvis.Mind.Api.Models;

namespace Jarvis.Mind.Api.Services.Stores;

// These interfaces are the seam between "the mind map" and "wherever your data actually lives".
// The assembler (Tier 1) only ever talks to these - never to SQLite/Chroma/etc directly - so a
// broken or not-yet-built store degrades one region instead of crashing the endpoint.

public sealed record MemoryRecord(
    string Id,
    string Text,
    string Type,
    string Category,
    string Source,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    double Importance,
    IReadOnlyList<string> Tags
);

public interface IMemoryStore
{
    /// <summary>True total count of memories, independent of any cap applied for display.</summary>
    Task<int> CountAsync(CancellationToken ct);

    /// <summary>
    /// A bounded working set for the skeleton - most recent + highest importance + highest
    /// existing-degree, capped at `limit`. Never "all of them" once the store is large.
    /// </summary>
    Task<IReadOnlyList<MemoryRecord>> GetWorkingSetAsync(int limit, CancellationToken ct);

    Task<MemoryRecord?> GetByIdAsync(string id, CancellationToken ct);
}

public interface IEmbeddingStore
{
    /// <summary>Whether this store currently has usable vectors (vs. being empty/not yet populated).</summary>
    Task<bool> HasEmbeddingsAsync(CancellationToken ct);

    /// <summary>Raw vectors for the given memory ids, in the same order. Missing ids are simply absent from the result.</summary>
    Task<IReadOnlyDictionary<string, float[]>> GetEmbeddingsAsync(IReadOnlyList<string> memoryIds, CancellationToken ct);

    /// <summary>Live nearest-neighbor query for the inspector panel (Tier 7) - not the baked skeleton edges.</summary>
    Task<IReadOnlyList<(string Id, double Similarity)>> QueryNeighborsAsync(string memoryId, int topK, CancellationToken ct);
}

public sealed record AgentRecord(
    string Id,
    string Name,
    string Specialty,
    string AccentColor,
    string? AvatarUrl,
    string? Model,
    IReadOnlyList<string> ToolIds
);

public interface IAgentRegistry
{
    Task<IReadOnlyList<AgentRecord>> GetAllAsync(CancellationToken ct);
}

public sealed record ToolRecord(string Id, string Name, string Category, bool Enabled);

public interface IToolRegistry
{
    Task<IReadOnlyList<ToolRecord>> GetAllAsync(CancellationToken ct);
}

public sealed record KnowledgeFileRecord(string Path, string Title, DateTimeOffset UpdatedAt);

public interface IKnowledgeManifest
{
    Task<IReadOnlyList<KnowledgeFileRecord>> GetAllAsync(CancellationToken ct);

    /// <summary>Text preview for a manifest-listed path only. Must return null for any path not in the manifest - never read arbitrary paths.</summary>
    Task<string?> GetPreviewAsync(string manifestPath, CancellationToken ct);
}

public sealed record ThreadRecord(string Id, string Title, DateTimeOffset LastActiveAt);

public interface IConversationRepo
{
    Task<IReadOnlyList<ThreadRecord>> GetRecentAsync(int limit, CancellationToken ct);
}