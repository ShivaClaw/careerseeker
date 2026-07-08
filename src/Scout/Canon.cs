using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace SeekerSvc.Scout;

/// <summary>
/// Deterministic text canonicalization for dedup keys and matching. Pure and
/// culture-independent so the same posting always produces the same key.
/// </summary>
public static class Canon
{
    private static readonly Regex Ws = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex NonAlnum = new(@"[^a-z0-9 ]", RegexOptions.Compiled);

    // Light title synonyms. Deliberately conservative: we do NOT strip seniority/level
    // tokens (II, III, Senior) so distinct levels stay distinct under the exact key.
    private static readonly (string Pattern, string Replacement)[] TitleSynonyms =
    {
        (@"\bsr\b", "senior"),
        (@"\bjr\b", "junior"),
        (@"\bswe\b", "software engineer"),
        (@"\bsde\b", "software engineer"),
        (@"\beng\b", "engineer"),
        (@"\bdev\b", "developer"),
        (@"\bmgr\b", "manager"),
        (@"\bpm\b", "product manager"),
    };

    private static string Base(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var t = text.ToLowerInvariant().Replace("&", " and ");
        t = NonAlnum.Replace(t, " ");
        t = Ws.Replace(t, " ").Trim();
        return t;
    }

    /// <summary>Canonical company identity (used in dedup keys and same-company grouping).</summary>
    public static string Company(string? name) => Base(name);

    /// <summary>Canonical job title with a few abbreviation expansions; preserves level tokens.</summary>
    public static string Title(string? title)
    {
        var t = Base(title);
        foreach (var (pattern, repl) in TitleSynonyms)
            t = Regex.Replace(t, pattern, repl);
        return Ws.Replace(t, " ").Trim();
    }

    /// <summary>
    /// Coarse locality key: the locality before the first comma, lowercased and stripped.
    /// "San Francisco, CA" and "San Francisco" both map to "san francisco"; anything
    /// containing "remote" maps to "remote". Reduces cross-source formatting noise while
    /// keeping different cities distinct.
    /// </summary>
    public static string LocalityKey(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return "";
        var low = location.ToLowerInvariant();
        if (low.Contains("remote")) return "remote";
        var head = location.Split(',')[0];
        return Base(head);
    }

    /// <summary>company | title | locality — the exact-match dedup key.</summary>
    public static string DedupKey(string? company, string? title, string? location) =>
        $"{Company(company)}|{Title(title)}|{LocalityKey(location)}";
}

/// <summary>
/// 64-bit SimHash over token shingles, with FNV-1a as the per-feature hash. Two texts
/// that are near-identical produce fingerprints a small Hamming distance apart; unrelated
/// texts are far apart. Used to catch the same posting re-listed across ATSs even when
/// titles or whitespace differ slightly.
/// </summary>
public static class SimHash
{
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;
    private const int ShingleSize = 3;

    private static readonly Regex Token = new(@"[a-z0-9]+", RegexOptions.Compiled);

    /// <summary>Compute the 64-bit fingerprint of a text (returned as a long).</summary>
    public static long Compute(string? text)
    {
        var features = Shingles(text);
        if (features.Count == 0) return 0L;

        // Weighted bit accumulator across all features.
        Span<int> bits = stackalloc int[64];
        foreach (var (feature, weight) in features)
        {
            var h = Fnv1a(feature);
            for (var i = 0; i < 64; i++)
            {
                if (((h >> i) & 1UL) != 0UL) bits[i] += weight;
                else bits[i] -= weight;
            }
        }

        ulong fingerprint = 0UL;
        for (var i = 0; i < 64; i++)
            if (bits[i] > 0) fingerprint |= (1UL << i);

        return unchecked((long)fingerprint);
    }

    /// <summary>Number of differing bits between two fingerprints (0 == identical).</summary>
    public static int Hamming(long a, long b) =>
        BitOperations.PopCount(unchecked((ulong)(a ^ b)));

    private static Dictionary<string, int> Shingles(string? text)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text)) return counts;

        var tokens = new List<string>();
        foreach (Match m in Token.Matches(text.ToLowerInvariant()))
            tokens.Add(m.Value);

        if (tokens.Count == 0) return counts;

        if (tokens.Count < ShingleSize)
        {
            // too short to shingle: fall back to individual tokens
            foreach (var tok in tokens)
                counts[tok] = counts.GetValueOrDefault(tok) + 1;
            return counts;
        }

        var sb = new StringBuilder();
        for (var i = 0; i + ShingleSize <= tokens.Count; i++)
        {
            sb.Clear();
            for (var k = 0; k < ShingleSize; k++)
            {
                if (k > 0) sb.Append(' ');
                sb.Append(tokens[i + k]);
            }
            var shingle = sb.ToString();
            counts[shingle] = counts.GetValueOrDefault(shingle) + 1;
        }
        return counts;
    }

    private static ulong Fnv1a(string s)
    {
        var hash = FnvOffset;
        foreach (var b in Encoding.UTF8.GetBytes(s))
        {
            hash ^= b;
            hash *= FnvPrime;
        }
        return hash;
    }
}
