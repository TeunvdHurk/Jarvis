using Jarvis.Mind.Api.Models;

namespace Jarvis.Mind.Api.Services.Stores;

// Each of these returns an honestly-empty result, which the assembler renders as an
// empty region with sources[name] = "ok" (there's nothing wrong, there's just nothing
// there yet) - never fabricated agents/tools/docs. Replace one at a time as the real
// orchestrator/registry/manifest/conversation store come online; the assembler and the
// frontend don't need to change when you do.

public sealed class EmptyAgentRegistry : IAgentRegistry
{
    public Task<IReadOnlyList<AgentRecord>> GetAllAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<AgentRecord>>(Array.Empty<AgentRecord>());
}

public sealed class EmptyToolRegistry : IToolRegistry
{
    public Task<IReadOnlyList<ToolRecord>> GetAllAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ToolRecord>>(Array.Empty<ToolRecord>());
}

public sealed class EmptyKnowledgeManifest : IKnowledgeManifest
{
    public Task<IReadOnlyList<KnowledgeFileRecord>> GetAllAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<KnowledgeFileRecord>>(Array.Empty<KnowledgeFileRecord>());

    public Task<string?> GetPreviewAsync(string manifestPath, CancellationToken ct)
        => Task.FromResult<string?>(null);
}

public sealed class EmptyConversationRepo : IConversationRepo
{
    public Task<IReadOnlyList<ThreadRecord>> GetRecentAsync(int limit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ThreadRecord>>(Array.Empty<ThreadRecord>());
}