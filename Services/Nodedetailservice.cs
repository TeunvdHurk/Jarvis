using Jarvis.Mind.Api.Models;
using Jarvis.Mind.Api.Services.Stores;

namespace Jarvis.Mind.Api.Services;

public interface INodeDetailService
{
    Task<MindNodeDetail?> GetAsync(string nodeId, CancellationToken ct);
}

public sealed class NodeDetailService : INodeDetailService
{
    private readonly IMemoryStore _memoryStore;
    private readonly IEmbeddingStore _embeddingStore;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IToolRegistry _toolRegistry;
    private readonly IKnowledgeManifest _knowledgeManifest;

    public NodeDetailService(
        IMemoryStore memoryStore, IEmbeddingStore embeddingStore, IAgentRegistry agentRegistry,
        IToolRegistry toolRegistry, IKnowledgeManifest knowledgeManifest)
    {
        _memoryStore = memoryStore;
        _embeddingStore = embeddingStore;
        _agentRegistry = agentRegistry;
        _toolRegistry = toolRegistry;
        _knowledgeManifest = knowledgeManifest;
    }

    public async Task<MindNodeDetail?> GetAsync(string nodeId, CancellationToken ct)
    {
        var parts = nodeId.Split(':', 2);
        if (parts.Length != 2) return null;
        var (prefix, rawId) = (parts[0], parts[1]);

        return prefix switch
        {
            "mem" => await GetMemoryDetail(rawId, ct),
            "agent" => await GetAgentDetail(rawId, ct),
            "tool" => await GetToolDetail(rawId, ct),
            "know" => await GetKnowledgeDetail(rawId, ct),
            _ => null
        };
    }

    private async Task<MindNodeDetail?> GetMemoryDetail(string id, CancellationToken ct)
    {
        var mem = await _memoryStore.GetByIdAsync(id, ct);
        if (mem is null) return null;

        var hasEmbeddings = await _embeddingStore.HasEmbeddingsAsync(ct);
        var neighbors = new List<NeighborRef>();
        if (hasEmbeddings)
        {
            var live = await _embeddingStore.QueryNeighborsAsync(id, topK: 5, ct);
            foreach (var (neighborId, sim) in live)
            {
                var neighborRecord = await _memoryStore.GetByIdAsync(neighborId, ct);
                if (neighborRecord is null) continue; // dropped, not faked
                neighbors.Add(new NeighborRef { Id = $"mem:{neighborId}", Label = Truncate(neighborRecord.Text, 60), Similarity = sim });
            }
        }

        return new MindNodeDetail
        {
            Id = $"mem:{mem.Id}",
            Type = "memory",
            Label = Truncate(mem.Text, 60),
            Body = mem.Text,
            Source = mem.Source,
            Category = mem.Category,
            CreatedAt = mem.CreatedAt,
            UpdatedAt = mem.UpdatedAt,
            Importance = mem.Importance,
            Tags = mem.Tags.ToList(),
            Neighbors = neighbors
        };
    }

    private async Task<MindNodeDetail?> GetAgentDetail(string id, CancellationToken ct)
    {
        var agent = (await _agentRegistry.GetAllAsync(ct)).FirstOrDefault(a => a.Id == id);
        if (agent is null) return null;

        return new MindNodeDetail
        {
            Id = $"agent:{agent.Id}",
            Type = "agent",
            Label = agent.Name,
            Body = agent.Specialty,
            Extra = new()
            {
                ["accentColor"] = agent.AccentColor,
                ["avatarUrl"] = agent.AvatarUrl,
                ["model"] = agent.Model,
                ["toolIds"] = agent.ToolIds
            }
        };
    }

    private async Task<MindNodeDetail?> GetToolDetail(string id, CancellationToken ct)
    {
        var tool = (await _toolRegistry.GetAllAsync(ct)).FirstOrDefault(t => t.Id == id);
        if (tool is null) return null;

        return new MindNodeDetail
        {
            Id = $"tool:{tool.Id}",
            Type = "tool",
            Label = tool.Name,
            Category = tool.Category,
            Extra = new() { ["enabled"] = tool.Enabled }
        };
    }

    private async Task<MindNodeDetail?> GetKnowledgeDetail(string path, CancellationToken ct)
    {
        var files = await _knowledgeManifest.GetAllAsync(ct);
        var file = files.FirstOrDefault(f => f.Path == path);
        if (file is null) return null; // not in the manifest -> 404, never an arbitrary filesystem read

        var preview = await _knowledgeManifest.GetPreviewAsync(path, ct);

        return new MindNodeDetail
        {
            Id = $"know:{file.Path}",
            Type = "knowledge",
            Label = file.Title,
            Body = preview,
            UpdatedAt = file.UpdatedAt
        };
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";
}