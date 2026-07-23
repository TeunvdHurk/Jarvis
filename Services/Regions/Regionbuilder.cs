using Jarvis.Mind.Api.Models;
using Jarvis.Mind.Api.Services.Stores;

namespace Jarvis.Mind.Api.Services.Regions;

public sealed record RegionResult(List<MindNode> Nodes, List<MindEdge> Edges);

public sealed record MemoryRegionResult(RegionResult Region, int TotalCount, int ShownCount, bool IsSemantic);

/// <summary>
/// Builds the memory region: nodes sized by degree, freshness-lit, wired either by real
/// cosine similarity (when embeddings exist) or by honest tag/category co-occurrence
/// (when they don't) - and always produces 1-3 "recall" trunks back to the core, because
/// recall genuinely always flows core-ward, even when the web itself is sparse.
/// </summary>
public static class MemoryRegionBuilder
{
    public const int WorkingSetCap = 2000; // memory nodes actually rendered; raise with care, this is an O(n^2) similarity pass

    public static async Task<MemoryRegionResult> BuildAsync(
        IMemoryStore memoryStore, IEmbeddingStore embeddingStore, CancellationToken ct)
    {
        var total = await memoryStore.CountAsync(ct);
        var working = await memoryStore.GetWorkingSetAsync(WorkingSetCap, ct);
        if (working.Count == 0)
            return new(new RegionResult(new(), new()), total, 0, false);

        var byId = working.ToDictionary(m => m.Id);
        var hasEmbeddings = await embeddingStore.HasEmbeddingsAsync(ct);

        List<(string A, string B, double Weight)> pairs;
        bool isSemantic;

        if (hasEmbeddings)
        {
            var vectors = await embeddingStore.GetEmbeddingsAsync(working.Select(m => m.Id).ToList(), ct);
            pairs = vectors.Count >= 2
                ? SimilarityMath.TopKEdges(vectors)
                : new();
            isSemantic = true;
        }
        else
        {
            pairs = CooccurrenceEdges(working);
            isSemantic = false;
        }

        // Degree per memory, from whichever edge set we actually built.
        var degree = working.ToDictionary(m => m.Id, _ => 0);
        foreach (var (a, b, _) in pairs)
        {
            degree[a]++;
            degree[b]++;
        }

        var now = DateTimeOffset.UtcNow;
        var nodes = working.Select(m =>
        {
            var d = degree[m.Id];
            return new MindNode
            {
                Id = $"mem:{m.Id}",
                Type = "memory",
                Region = RegionIds.Memory,
                Label = Truncate(m.Text, 60),
                Color = "#A78BFA",
                Size = 0.6 + Math.Min(1.4, d * 0.12),   // well-connected memories are physically bigger
                Freshness = SimilarityMath.Freshness(m.CreatedAt, now),
                Extra = new()
                {
                    ["category"] = m.Category,
                    ["source"] = m.Source,
                    ["importance"] = m.Importance,
                    ["degree"] = d
                }
            };
        }).ToList();

        var edges = pairs.Select(p => new MindEdge
        {
            Source = $"mem:{p.A}",
            Target = $"mem:{p.B}",
            Kind = isSemantic ? "similarity" : "cooccurrence",
            Weight = Math.Clamp(p.Weight, 0, 1)
        }).ToList();

        // Trunks: three highest-degree memories, padded with freshest if the web is sparse/missing.
        var trunkSources = degree
            .OrderByDescending(kv => kv.Value)
            .ThenByDescending(kv => byId[kv.Key].CreatedAt)
            .Take(3)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var id in trunkSources)
        {
            edges.Add(new MindEdge { Source = $"mem:{id}", Target = "core:jarvis", Kind = "recall_trunk", Weight = 1.0 });
        }

        return new(new RegionResult(nodes, edges), total, working.Count, isSemantic);
    }

    /// <summary>Honest fallback: two memories are linked if they share a category or a tag. Weight scales with overlap. Labeled "cooccurrence", never "similarity".</summary>
    private static List<(string, string, double)> CooccurrenceEdges(IReadOnlyList<MemoryRecord> memories)
    {
        var edges = new List<(string, string, double)>();
        for (var i = 0; i < memories.Count; i++)
        {
            var scored = new List<(int j, double weight)>();
            for (var j = 0; j < memories.Count; j++)
            {
                if (i == j) continue;
                var a = memories[i];
                var b = memories[j];
                var sharedTags = a.Tags.Intersect(b.Tags).Count();
                var sameCategory = a.Category == b.Category ? 1 : 0;
                var weight = sharedTags * 0.2 + sameCategory * 0.3;
                if (weight > 0) scored.Add((j, Math.Min(1.0, weight)));
            }

            foreach (var (j, w) in scored.OrderByDescending(s => s.weight).Take(SimilarityMath.TopKPerNode))
            {
                var a = memories[i].Id;
                var b = memories[j].Id;
                var pair = string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a);
                edges.Add((pair.Item1, pair.Item2, w));
            }
        }
        return edges.DistinctBy(e => (e.Item1, e.Item2)).ToList();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";
}

public static class AgentsRegionBuilder
{
    public static async Task<RegionResult> BuildAsync(IAgentRegistry registry, CancellationToken ct)
    {
        var agents = await registry.GetAllAsync(ct);
        var nodes = new List<MindNode>();
        var edges = new List<MindEdge>();

        foreach (var a in agents)
        {
            nodes.Add(new MindNode
            {
                Id = $"agent:{a.Id}",
                Type = "agent",
                Region = RegionIds.Agents,
                Label = a.Name,
                Color = a.AccentColor,
                Size = 1.4,
                Extra = new()
                {
                    ["specialty"] = a.Specialty,
                    ["avatarUrl"] = a.AvatarUrl,
                    ["model"] = a.Model,
                    ["toolCount"] = a.ToolIds.Count
                }
            });
            edges.Add(new MindEdge { Source = "core:jarvis", Target = $"agent:{a.Id}", Kind = "dispatch_trunk", Weight = 1.0 });

            foreach (var toolId in a.ToolIds)
            {
                edges.Add(new MindEdge { Source = $"agent:{a.Id}", Target = $"tool:{toolId}", Kind = "membership", Weight = 0.6 });
            }
        }

        return new RegionResult(nodes, edges);
    }
}

public static class RimRegionBuilder
{
    public static async Task<RegionResult> BuildAsync(IToolRegistry registry, CancellationToken ct)
    {
        var tools = await registry.GetAllAsync(ct);
        var nodes = tools.Select(t => new MindNode
        {
            Id = $"tool:{t.Id}",
            Type = "tool",
            Region = RegionIds.Rim,
            Label = t.Name,
            Color = "#8B93A1",
            Size = t.Enabled ? 1.0 : 0.6,
            Extra = new() { ["category"] = t.Category, ["enabled"] = t.Enabled }
        }).ToList();

        var categoryTrunks = tools
            .GroupBy(t => t.Category)
            .Select(g => g.OrderByDescending(t => t.Enabled).First())
            .Select(t => new MindEdge { Source = "core:jarvis", Target = $"tool:{t.Id}", Kind = "capability_trunk", Weight = t.Enabled ? 1.0 : 0.3 })
            .ToList();

        return new RegionResult(nodes, categoryTrunks);
    }
}

public static class KnowledgeRegionBuilder
{
    public static async Task<RegionResult> BuildAsync(IKnowledgeManifest manifest, CancellationToken ct)
    {
        var files = await manifest.GetAllAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var nodes = files.Select(f => new MindNode
        {
            Id = $"know:{f.Path}",
            Type = "knowledge",
            Region = RegionIds.Knowledge,
            Label = f.Title,
            Color = "#F5A524",
            Size = 1.0,
            Freshness = SimilarityMath.Freshness(f.UpdatedAt, now),
            Extra = new() { ["path"] = f.Path }
        }).ToList();

        var edges = nodes.Select(n => new MindEdge { Source = "core:jarvis", Target = n.Id, Kind = "membership", Weight = 0.5 }).ToList();
        return new RegionResult(nodes, edges);
    }
}

public static class WorkingRegionBuilder
{
    public static async Task<RegionResult> BuildAsync(IConversationRepo repo, int limit, CancellationToken ct)
    {
        var threads = await repo.GetRecentAsync(limit, ct);
        var now = DateTimeOffset.UtcNow;
        var nodes = threads.Select(t => new MindNode
        {
            Id = $"thread:{t.Id}",
            Type = "thread",
            Region = RegionIds.Working,
            Label = t.Title,
            Color = "#67E8F9",
            Size = 0.8,
            Freshness = SimilarityMath.Freshness(t.LastActiveAt, now)
        }).ToList();

        return new RegionResult(nodes, new());
    }
}