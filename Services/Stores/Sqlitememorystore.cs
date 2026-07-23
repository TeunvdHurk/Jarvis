using Jarvis.Mind.Api.Models;
using Microsoft.Data.Sqlite;

namespace Jarvis.Mind.Api.Services.Stores;

/// <summary>
/// Assumed schema (adjust column names in the SELECTs below to match your real table
/// once it exists - the shape, not the names, is what matters):
///
/// CREATE TABLE memories (
///   id           TEXT PRIMARY KEY,   -- uuid, becomes "mem:&lt;id&gt;"
///   text         TEXT NOT NULL,
///   type         TEXT NOT NULL,      -- e.g. "fact" | "preference" | "history"
///   category     TEXT NOT NULL,
///   source       TEXT NOT NULL,
///   created_at   TEXT NOT NULL,      -- ISO-8601
///   updated_at   TEXT NOT NULL,
///   importance   REAL NOT NULL DEFAULT 0.5,
///   tags         TEXT NOT NULL DEFAULT ''   -- comma-separated
/// );
/// </summary>
public sealed class SqliteMemoryStore : IMemoryStore
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteMemoryStore> _logger;

    public SqliteMemoryStore(IConfiguration config, ILogger<SqliteMemoryStore> logger)
    {
        var path = config["Jarvis:MemoryDbPath"] ?? "jarvis-memory.db";
        _connectionString = $"Data Source={path}";
        _logger = logger;
    }

    public async Task<int> CountAsync(CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM memories";
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<MemoryRecord>> GetWorkingSetAsync(int limit, CancellationToken ct)
    {
        // Bounded working set: newest + most important first. Degree-based re-ranking
        // (favoring memories that turn out to be well-connected) happens after similarity
        // edges are computed in the assembler, not here - this query has no notion of edges.
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, text, type, category, source, created_at, updated_at, importance, tags
            FROM memories
            ORDER BY importance DESC, created_at DESC
            LIMIT $limit
        """;
        cmd.Parameters.AddWithValue("$limit", limit);

        var results = new List<MemoryRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }
        return results;
    }

    public async Task<MemoryRecord?> GetByIdAsync(string id, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, text, type, category, source, created_at, updated_at, importance, tags
            FROM memories WHERE id = $id
        """;
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    private static MemoryRecord Map(SqliteDataReader r) => new(
        Id: r.GetString(0),
        Text: r.GetString(1),
        Type: r.GetString(2),
        Category: r.GetString(3),
        Source: r.GetString(4),
        CreatedAt: DateTimeOffset.Parse(r.GetString(5)),
        UpdatedAt: DateTimeOffset.Parse(r.GetString(6)),
        Importance: r.GetDouble(7),
        Tags: r.GetString(8).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    );
}