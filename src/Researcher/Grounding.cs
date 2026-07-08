using System.Text.RegularExpressions;

namespace SeekerSvc.Researcher;

/// <summary>
/// The Researcher's safety invariant, the research-layer analogue of the Fabrication Gate: a proposed
/// fact survives only if it is <b>grounded</b> — its cited URL was actually retrieved, and the cited
/// document's text supports the fact's wording. The model proposes; this filter, in code, decides what
/// becomes a dossier fact. A model that invents a flattering hook ("congrats on your Series C") cannot
/// surface it unless a retrieved source backs it, because the cover letter that hook lands in is sent in
/// the user's name.
///
/// Support test: enough of the fact's distinctive tokens (length ≥ 4, not stopwords) appear in the cited
/// document. Conservative by construction — when in doubt, the fact is dropped, not shown.
/// </summary>
public static class GroundingFilter
{
    private const int MinDistinctiveTokens = 2;     // a fact must share at least this many with its source
    private const double MinCoverage = 0.5;          // and at least half of its distinctive tokens

    private static readonly Regex Token = new(@"[A-Za-z][A-Za-z0-9'\-]{3,}", RegexOptions.Compiled);
    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "this","that","with","from","have","will","your","their","they","them","then","than","into","over",
        "company","companies","team","role","work","working","about","which","while","where","there","been",
        "also","more","most","such","other","using","used","make","made","like","very","much","many","some",
    };

    public sealed record Result(IReadOnlyList<DossierFact> Grounded, int Dropped);

    public static Result Apply(IReadOnlyList<ProposedFact> proposed, IReadOnlyList<ResearchDoc> docs)
    {
        var byUrl = new Dictionary<string, ResearchDoc>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in docs) byUrl[d.Url] = d;

        var kept = new List<DossierFact>();
        var dropped = 0;

        foreach (var p in proposed)
        {
            if (string.IsNullOrWhiteSpace(p.SourceUrl) || !byUrl.TryGetValue(p.SourceUrl, out var doc))
            { dropped++; continue; }                                  // cited a URL we never retrieved

            if (!Supported(p.Text, doc.Text)) { dropped++; continue; } // doc doesn't actually back the wording

            kept.Add(new DossierFact(p.Topic, p.Text.Trim(), p.SourceUrl, p.SourceTitle));
        }

        return new Result(kept, dropped);
    }

    private static bool Supported(string fact, string docText)
    {
        var factTokens = Distinctive(fact);
        if (factTokens.Count == 0) return false;                      // nothing checkable → not grounded

        var docTokens = Distinctive(docText);
        var present = factTokens.Count(t => docTokens.Contains(t));
        return present >= MinDistinctiveTokens && present >= (int)Math.Ceiling(MinCoverage * factTokens.Count);
    }

    private static HashSet<string> Distinctive(string text)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Token.Matches(text))
            if (!Stop.Contains(m.Value)) set.Add(m.Value);
        return set;
    }
}
