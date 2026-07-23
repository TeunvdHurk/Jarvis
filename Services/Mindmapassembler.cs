using Jarvis.Mind.Api.Models;
using Jarvis.Mind.Api.Services.Regions;
using Jarvis.Mind.Api.Services.Stores;

namespace Jarvis.Mind.Api.Services;

public interface IMindMapAssembler
{
    Task<MindMapSkeleton> AssembleAsync(CancellationToken ct);
}

/// <summary>
/// Deliberately takes explicit store interfaces via constructor injection rather than
/// reaching into globals/HttpContext - so it can be unit tested with fakes for each store,
/// including a store that throws, without spinning up the web host.
/// </summary>
public sealed class MindMapAssembler : IMindMapAssembler
{
    private readonly IMemoryStore _memoryStore;
    private readonly IEmbeddingStore _embeddingStore;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IToolRegistry _toolRegistry;
    private readonly IKnowledgeManifest _knowledgeManifest;
    private readonly IConversationRepo _conversationRepo;
    private readonly ILogger<MindMapAssembler> _logger;

    private const string AccentColor = "#495DB5"; // Jarvis core accent
    private const int WorkingThreadLimit = 25;

    public MindMapAssembler(
        IMemoryStore memoryStore,
        IEmbeddingStore embeddingStore,
        IAgentRegistry agentRegistry,
        IToolRegistry toolRegistry,
        IKnowledgeManifest knowledgeManifest,
        IConversationRepo conversationRepo,
        ILogger<MindMapAssembler> logger)
    {
        _memoryStore = memoryStore;
        _embeddingStore = embeddingStore;
        _agentRegistry = agentRegistry;
        _toolRegistry = toolRegistry;
        _knowledgeManifest = knowledgeManifest;
        _conversationRepo = conversationRepo;
        _logger = logger;
    }

    public async Task<MindMapSkeleton> AssembleAsync(CancellationToken ct)
    {
        var nodes = new List<MindNode>
        {
            new()
            {
                Id = "core:jarvis",
                Type = "core",
                Region = RegionIds.Core,
                Label = "Jarvis",
                Color = AccentColor,
                Size = 3.0
            }
        };
        var edges = new List<MindEdge>();
        var sourceStatus = new Dictionary<string, string>();

        var memoryStats = await RunRegion("memory", sourceStatus, async () =>
        {
            var result = await MemoryRegionBuilder.BuildAsync(_memoryStore, _embeddingStore, ct);
            nodes.AddRange(result.Region.Nodes);
            edges.AddRange(result.Region.Edges);
            return result;
        });

        await RunRegion("agents", sourceStatus, async () =>
        {
            var result = await AgentsRegionBuilder.BuildAsync(_agentRegistry, ct);
            nodes.AddRange(result.Nodes);
            edges.AddRange(result.Edges);
            return true;
        });

        await RunRegion("rim", sourceStatus, async () =>
        {
            var result = await RimRegionBuilder.BuildAsync(_toolRegistry, ct);
            nodes.AddRange(result.Nodes);
            edges.AddRange(result.Edges);
            return true;
        });

        await RunRegion("knowledge", sourceStatus, async () =>
        {
            var result = await KnowledgeRegionBuilder.BuildAsync(_knowledgeManifest, ct);
            nodes.AddRange(result.Nodes);
            edges.AddRange(result.Edges);
            return true;
        });

        await RunRegion("working", sourceStatus, async () =>
        {
            var result = await WorkingRegionBuilder.BuildAsync(_conversationRepo, WorkingThreadLimit, ct);
            nodes.AddRange(result.Nodes);
            edges.AddRange(result.Edges);
            return true;
        });

        var nodeIds = nodes.Select(n => n.Id).ToHashSet();
        var validEdges = edges.Where(e => nodeIds.Contains(e.Source) && nodeIds.Contains(e.Target)).ToList();
        var droppedEdgeCount = edges.Count - validEdges.Count;
        if (droppedEdgeCount > 0)
            _logger.LogWarning("Dropped {Count} edges referencing unknown nodes", droppedEdgeCount);

        var counts = nodes.GroupBy(n => n.Region).ToDictionary(g => g.Key, g => g.Count());

        var stats = new RegionStats
        {
            MemoryTotal = memoryStats?.TotalCount ?? 0,
            MemoryShown = memoryStats?.ShownCount ?? 0,
            MemoryCap = MemoryRegionBuilder.WorkingSetCap,
            SimilarityIsSemantic = memoryStats?.IsSemantic ?? false,
            Sources = sourceStatus,
            NodeCountsByRegion = counts,
            Warning = sourceStatus.Any(kv => kv.Value == "error")
                ? "One or more regions failed to load - see sources for detail."
                : null
        };

        return new MindMapSkeleton { Nodes = nodes, Edges = validEdges, Stats = stats };
    }

    /// <summary>Runs one region's builder; on failure, records "error" in stats and leaves that region empty rather than throwing.</summary>
    private async Task<T?> RunRegion<T>(string name, Dictionary<string, string> sourceStatus, Func<Task<T>> build) where T : class
    {
        try
        {
            var result = await build();
            sourceStatus[name] = "ok";
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Region '{Region}' failed to build; leaving it empty", name);
            sourceStatus[name] = "error";
            return null;
        }
    }
}