namespace Jarvis.Mind.Api.Services;

public static class SimilarityMath
{
    /// <summary>Top-K neighbors kept per memory. Load-bearing constant #1 - keeps the web readable instead of a hairball.</summary>
    public const int TopKPerNode = 3;

    /// <summary>Minimum cosine similarity to draw an edge at all. Load-bearing constant #2 - below this, two memories aren't "about the same thing".</summary>
    public const double SimilarityThreshold = 0.35;

    /// <summary>
    /// Computes cosine similarity for every pair in `vectors` (already assumed non-empty),
    /// returns, per source id, its top-K neighbors above the threshold, deduplicated as
    /// unordered pairs (a,b) == (b,a). O(n^2) - fine at a few thousand nodes, which is the
    /// bounded working-set size the memory store returns; not intended for the full store.
    /// </summary>
    public static List<(string A, string B, double Weight)> TopKEdges(
        IReadOnlyDictionary<string, float[]> vectors,
        int topK = TopKPerNode,
        double threshold = SimilarityThreshold)
    {
        var ids = vectors.Keys.ToArray();
        var n = ids.Length;
        if (n < 2) return new();

        // Normalize once.
        var unit = new float[n][];
        for (var i = 0; i < n; i++)
        {
            var v = vectors[ids[i]];
            var norm = MathF.Sqrt(v.Sum(x => x * x));
            unit[i] = norm > 0 ? v.Select(x => x / norm).ToArray() : v;
        }

        var seen = new HashSet<(string, string)>();
        var edges = new List<(string, string, double)>();

        for (var i = 0; i < n; i++)
        {
            var scored = new List<(int j, double sim)>();
            for (var j = 0; j < n; j++)
            {
                if (i == j) continue; // diagonal excluded, equivalent to filling with -1
                double sim = Dot(unit[i], unit[j]);
                if (sim >= threshold) scored.Add((j, sim));
            }

            foreach (var (j, sim) in scored.OrderByDescending(s => s.sim).Take(topK))
            {
                var pair = string.CompareOrdinal(ids[i], ids[j]) < 0 ? (ids[i], ids[j]) : (ids[j], ids[i]);
                if (seen.Add(pair))
                    edges.Add((pair.Item1, pair.Item2, sim));
            }
        }

        return edges;
    }

    private static double Dot(float[] a, float[] b)
    {
        double sum = 0;
        for (var k = 0; k < a.Length; k++) sum += a[k] * b[k];
        return sum;
    }

    /// <summary>Exponential freshness decay, floored so nothing goes fully black. Matches the spec exactly.</summary>
    public static double Freshness(DateTimeOffset createdAt, DateTimeOffset now)
    {
        var ageDays = Math.Max(0, (now - createdAt).TotalDays);
        var value = Math.Pow(0.5, ageDays / 30.0);
        return Math.Max(0.15, value);
    }
}