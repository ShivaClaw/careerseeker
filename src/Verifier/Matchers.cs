using System.Globalization;
using System.Text.RegularExpressions;

namespace SeekerSvc.Verifier;

/// <summary>
/// Text normalization, synonym canonicalization, and content-token extraction.
/// Shared by the matchers and the gate so the unit of comparison is identical
/// everywhere.
/// </summary>
public static class Text
{
    private static readonly Regex Ws = new(@"\s+", RegexOptions.Compiled);

    // Keep word chars, %, whitespace and the dot (for "B.S." etc.); else -> space.
    private static readonly Regex Punct = new(@"[^\w%\s.]", RegexOptions.Compiled);

    // Minimal seed synonym map. Onboarding's per-user title expansion (spec
    // section 4.2, Bank 1) feeds additional entries at runtime. Ordered so the
    // port is deterministic; none of these chain, so order does not affect output.
    private static readonly (string Key, string Value)[] DefaultSynonyms =
    {
        ("sde", "software engineer"),
        ("swe", "software engineer"),
        ("software developer", "software engineer"),
        ("ml", "machine learning"),
        ("k8s", "kubernetes"),
        ("pm", "product manager"),
    };

    public static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "of", "in", "at", "to", "and", "with", "for", "on",
        "by", "as", "is", "was", "i", "we", "my", "our", "this", "that",
    };

    public static string Normalize(string text)
    {
        var t = text.ToLowerInvariant().Trim();
        t = Punct.Replace(t, " ");
        t = Ws.Replace(t, " ");
        return t.Trim();
    }

    public static string Canonicalize(string text)
    {
        var t = Normalize(text);
        foreach (var (key, value) in DefaultSynonyms)
            t = Regex.Replace(t, $@"\b{Regex.Escape(key)}\b", value);
        return t;
    }

    /// <summary>
    /// Canonicalized tokens with stop-words removed. The unit of comparison
    /// everywhere in the Gate, so that "Java" and "JavaScript" never collide
    /// (substring matching, which they would defeat, is never used).
    /// </summary>
    public static HashSet<string> ContentTokens(string text)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in Canonicalize(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (!Stop.Contains(w))
                result.Add(w);
        return result;
    }
}

/// <summary>
/// Contract for entailment checks. Entails(source, tailored) is true iff the
/// source text supports (entails) the tailored claim. Implementations MUST fail
/// closed: when entailment is uncertain, return false.
/// </summary>
public interface ISemanticMatcher
{
    Task<bool> EntailsAsync(string sourceText, string tailoredText, CancellationToken ct = default);
}

/// <summary>
/// Conservative, dependency-free fallback. Fails CLOSED. Entails only when the
/// tailored claim's content tokens are very nearly a subset of the source's.
/// Production swaps this for an NLI/LLM-backed check routed through the LLM
/// Gateway (spec section 5.6), behind this same interface.
/// </summary>
public sealed class DefaultSemanticMatcher : ISemanticMatcher
{
    private readonly double _threshold;

    public DefaultSemanticMatcher(double threshold = 0.85) => _threshold = threshold;

    public Task<bool> EntailsAsync(string sourceText, string tailoredText, CancellationToken ct = default)
    {
        var src = Text.ContentTokens(sourceText);
        var tail = Text.ContentTokens(tailoredText);
        if (tail.Count == 0) return Task.FromResult(false);
        var covered = (double)tail.Count(src.Contains) / tail.Count;
        return Task.FromResult(covered >= _threshold);
    }
}

/// <summary>
/// Deterministic matcher for tests and demos: entails only the explicit pairs
/// given. Each pair is (sourceSubstring, tailoredSubstring); entailment holds
/// when the source contains its substring and the tailored text contains its own.
/// </summary>
public sealed class RuleSemanticMatcher : ISemanticMatcher
{
    private readonly (string Source, string Tailored)[] _pairs;

    public RuleSemanticMatcher(IEnumerable<(string Source, string Tailored)> pairs) =>
        _pairs = pairs.Select(p => (p.Source.ToLowerInvariant(), p.Tailored.ToLowerInvariant()))
                      .ToArray();

    public Task<bool> EntailsAsync(string sourceText, string tailoredText, CancellationToken ct = default)
    {
        var s = sourceText.ToLowerInvariant();
        var t = tailoredText.ToLowerInvariant();
        return Task.FromResult(_pairs.Any(p => s.Contains(p.Source) && t.Contains(p.Tailored)));
    }
}
