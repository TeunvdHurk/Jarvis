namespace Jarvis.Mind.Api.Models;

/// <summary>
/// The six fixed regions of the mind. Values are the exact strings the frontend
/// routes layout/coloring on - do not rename without updating regions.js's ANCHORS map.
/// </summary>
public static class RegionIds
{
    public const string Core = "core";
    public const string Memory = "memory";
    public const string Working = "working";
    public const string Agents = "agents";
    public const string Knowledge = "knowledge";
    public const string Rim = "rim";
}

/// <summary>
/// A single node. Flat and self-describing on purpose - the frontend never needs
/// to join back against a second table to render a node, only to inspect one.
/// Id is namespaced: mem:&lt;uuid&gt;, agent:&lt;slug&gt;, tool:&lt;name&gt;, know:&lt;path&gt;, thread:&lt;id&gt;, core:jarvis
/// </summary>
public sealed class MindNode
{
    public required string Id { get; init; }
    public required string Type { get; init; }      // "memory" | "agent" | "tool" | "knowledge" | "thread" | "core"
    public required string Region { get; init; }     // one of RegionIds
    public required string Label { get; init; }
    public required string Color { get; init; }      // hex, defaults to the region color unless overridden (e.g. per-agent accent)
    public double Size { get; init; } = 1.0;          // relative scale multiplier, region layout may further scale
    public double Freshness { get; init; } = 1.0;     // 0-1, exponential decay on age; 1.0 if no timestamp exists

    /// <summary>Type-specific extras the frontend may read but never requires to render the node itself.</summary>
    public Dictionary<string, object?> Extra { get; init; } = new();
}

/// <summary>A flat edge. Kind drives color/behavior on the frontend (Tier 5).</summary>
public sealed class MindEdge
{
    public required string Source { get; init; }
    public required string Target { get; init; }
    public required string Kind { get; init; }        // "similarity" | "recall_trunk" | "dispatch_trunk" | "capability_trunk" | "membership" | "cooccurrence"
    public double Weight { get; init; } = 1.0;         // 0-1, drives edge alpha / flow-comet visibility
}

public sealed class RegionStats
{
    public int MemoryTotal { get; init; }
    public int MemoryShown { get; init; }
    public int MemoryCap { get; init; }
    public bool SimilarityIsSemantic { get; init; }    // false => edges are honest tag/category co-occurrence, not embeddings
    public Dictionary<string, string> Sources { get; init; } = new();   // e.g. "memory": "ok" | "error" | "empty"
    public Dictionary<string, int> NodeCountsByRegion { get; init; } = new();
    public string? Warning { get; init; }              // surfaced in the HUD stats line; null when everything is healthy
}

public sealed class MindMapSkeleton
{
    public List<MindNode> Nodes { get; init; } = new();
    public List<MindEdge> Edges { get; init; } = new();
    public required RegionStats Stats { get; init; }
}

/// <summary>Full lazy-loaded detail for a single node, fetched only on click.</summary>
public sealed class MindNodeDetail
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string Label { get; init; }
    public string? Body { get; init; }                 // memory text, knowledge file preview, etc.
    public string? Source { get; init; }
    public string? Category { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public double? Importance { get; init; }
    public List<string> Tags { get; init; } = new();
    public List<NeighborRef> Neighbors { get; init; } = new();  // live similarity query, memory nodes only
    public Dictionary<string, object?> Extra { get; init; } = new();
}

public sealed class NeighborRef
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required double Similarity { get; init; }
}