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

    // Symbol-bearing identifiers: in "C++", "C#", "F#", "A+", "Security+" the symbol IS the
    // meaning. They are rewritten to letter forms BEFORE Punct strips symbols, otherwise
    // "C#" and "C++" both collapse to the token "c" and the Gate cannot tell them apart
    // (audit 2026-09-10, finding F02). Order matters: "++" before the trailing single "+".
    private static readonly Regex PlusPlusId = new(@"(\w)\+\+", RegexOptions.Compiled);
    private static readonly Regex TrailingPlusId = new(@"(\w)\+(?!\w)", RegexOptions.Compiled);
    private static readonly Regex SharpId = new(@"([a-z0-9])#", RegexOptions.Compiled);

    // Negation markers, matched on raw lowercased text BEFORE normalization strips
    // apostrophes, so contractions ("don't", "hasn't") still count. "not" is deliberately
    // NOT a stop word: a token-subset check discards extra source words, so without this
    // signal "I have not led X" lexically supports "I have led X" (audit finding F01).
    private static readonly Regex Negation = new(
        @"\b(not|no|never|none|neither|nor|cannot|without)\b|n['’]t\b",
        RegexOptions.Compiled);

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
        "by", "as", "is", "am", "was", "i", "we", "my", "our", "this", "that",
    };

    public static string Normalize(string text)
    {
        var t = text.ToLowerInvariant().Trim();
        t = PlusPlusId.Replace(t, "$1plusplus");
        t = TrailingPlusId.Replace(t, "$1plus");
        t = SharpId.Replace(t, "$1sharp");
        t = Punct.Replace(t, " ");
        t = Ws.Replace(t, " ");
        return t.Trim();
    }

    /// <summary>
    /// True when the text carries a negation marker ("not", "never", "without",
    /// "n't", ...). Evaluated on the raw text so contractions are visible. Subset
    /// matching is polarity-blind — the positive claim's tokens are a subset of the
    /// negated fact's — so every lexical accept in the Gate, and the fallback
    /// matcher, must refuse to match across a negation mismatch and leave the
    /// decision to real entailment, which fails closed.
    /// </summary>
    public static bool ContainsNegation(string text) => Negation.IsMatch(text.ToLowerInvariant());

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
        {
            var token = w.Trim('.');
            if (token.Length > 0 && !Stop.Contains(token))
                result.Add(token);
        }
        return result;
    }
}

/// <summary>
/// Contract for entailment checks. Entails(source, tailored) is true iff the
/// source text supports (entails) the tailored claim. Implementations MUST fail
/// closed: when entailment is uncertain, return false.
/// </summary>
public enum SemanticMatchStatus
{
    Entailed,
    NotEntailed,
    Unavailable,
}

public readonly record struct SemanticMatchResult(SemanticMatchStatus Status, string? Detail = null)
{
    public bool Entailed => Status == SemanticMatchStatus.Entailed;
    public bool Unavailable => Status == SemanticMatchStatus.Unavailable;

    public static SemanticMatchResult Supported() => new(SemanticMatchStatus.Entailed);
    public static SemanticMatchResult Unsupported() => new(SemanticMatchStatus.NotEntailed);
    public static SemanticMatchResult Deferred(string? detail = null) => new(SemanticMatchStatus.Unavailable, detail);
}

public interface ISemanticMatcher
{
    Task<SemanticMatchResult> EntailsAsync(string sourceText, string tailoredText, CancellationToken ct = default);
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

    public Task<SemanticMatchResult> EntailsAsync(string sourceText, string tailoredText, CancellationToken ct = default)
    {
        // Token coverage is polarity-blind: "I have not led X" covers 100% of
        // "I have led X". A negation mismatch is therefore never entailment here —
        // this matcher's contract is to fail closed (audit finding F01).
        if (Text.ContainsNegation(sourceText) != Text.ContainsNegation(tailoredText))
            return Task.FromResult(SemanticMatchResult.Unsupported());
        var src = Text.ContentTokens(sourceText);
        var tail = Text.ContentTokens(tailoredText);
        if (tail.Count == 0) return Task.FromResult(SemanticMatchResult.Unsupported());
        var covered = (double)tail.Count(src.Contains) / tail.Count;
        return Task.FromResult(covered >= _threshold
            ? SemanticMatchResult.Supported()
            : SemanticMatchResult.Unsupported());
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

    public Task<SemanticMatchResult> EntailsAsync(string sourceText, string tailoredText, CancellationToken ct = default)
    {
        var s = sourceText.ToLowerInvariant();
        var t = tailoredText.ToLowerInvariant();
        return Task.FromResult(_pairs.Any(p => s.Contains(p.Source) && t.Contains(p.Tailored))
            ? SemanticMatchResult.Supported()
            : SemanticMatchResult.Unsupported());
    }
}
