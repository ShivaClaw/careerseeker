using System.Text.RegularExpressions;
using SeekerSvc.Scorer;
using SeekerSvc.Store;

namespace SeekerSvc.Engine;

/// <summary>
/// Deterministic, offline fit signals derived from the local source-of-truth profile and an untrusted
/// posting treated strictly as data. Title matches are weighted above description matches; Skill and
/// Title claims are weighted above narrative/metric/employer text. No posting text is executed or sent
/// to a provider.
/// </summary>
public sealed partial class LexicalSemanticScorer : ISemanticScorer
{
    private readonly ISeekerStore _store;
    private readonly long _profileId;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "been", "bring", "built", "by", "can",
        "for", "from", "have", "i", "in", "into", "is", "it", "my", "of", "on", "or", "our",
        "that", "the", "their", "this", "to", "was", "we", "were", "will", "with", "would", "you",
        "your", "experience", "experienced", "team", "role", "work", "working", "years",
    };

    private static readonly HashSet<string> GrowthTerms = new(StringComparer.Ordinal)
    {
        "architecture", "build", "coach", "design", "greenfield", "improve", "influence", "lead",
        "leadership", "learn", "mentor", "modernize", "own", "ownership", "scale", "strategy",
        "strategic", "transform",
    };

    public LexicalSemanticScorer(ISeekerStore store, long profileId)
    {
        _store = store;
        _profileId = profileId;
    }

    public async Task<SemanticScores> ScoreAsync(JobPosting posting, CancellationToken ct = default)
    {
        var claims = await _store.GetClaimsAsync(_profileId, ct).ConfigureAwait(false);
        var profileTerms = ProfileTerms(claims);
        var titleTerms = Tokenize(posting.Title + " " + posting.TitleCanon);
        var descriptionTerms = Tokenize(posting.DescriptionText);

        if (profileTerms.Count == 0)
            return new SemanticScores(2.5, GrowthScore(titleTerms, descriptionTerms), "lexical-v1",
                "No rankable local profile terms; CV match held neutral.");

        var totalProfileWeight = profileTerms.Values.Sum();
        var matchedWeight = 0.0;
        var matched = new List<(string Term, double Weight, string Where)>();
        foreach (var (term, weight) in profileTerms)
        {
            if (titleTerms.Contains(term))
            {
                matchedWeight += weight;
                matched.Add((term, weight, "title"));
            }
            else if (descriptionTerms.Contains(term))
            {
                matchedWeight += weight * 0.65;
                matched.Add((term, weight * 0.65, "description"));
            }
        }

        var titleCoverage = titleTerms.Count == 0
            ? 0.0
            : titleTerms.Count(profileTerms.ContainsKey) / (double)titleTerms.Count;
        var profileCoverage = matchedWeight / Math.Max(1.0, totalProfileWeight);
        var combined = Math.Clamp(0.70 * profileCoverage + 0.30 * titleCoverage, 0.0, 1.0);
        var cvMatch = Math.Round(1.5 + 3.5 * combined, 2);
        var growth = GrowthScore(titleTerms, descriptionTerms);

        var evidence = matched
            .OrderByDescending(m => m.Weight)
            .ThenBy(m => m.Term, StringComparer.Ordinal)
            .Take(8)
            .Select(m => $"{m.Term} ({m.Where})")
            .ToArray();
        var rationale = evidence.Length == 0
            ? $"No meaningful profile-term overlap; profile coverage {profileCoverage:P0}, title coverage {titleCoverage:P0}."
            : $"Matched {string.Join(", ", evidence)}; profile coverage {profileCoverage:P0}, title coverage {titleCoverage:P0}.";

        return new SemanticScores(cvMatch, growth, "lexical-v1", rationale);
    }

    private static Dictionary<string, double> ProfileTerms(IReadOnlyList<ClaimRow> claims)
    {
        var terms = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var claim in claims)
        {
            var kindWeight = claim.Kind.ToLowerInvariant() switch
            {
                "skill" => 3.0,
                "title" => 2.5,
                "metric" => 1.25,
                "other" => 1.0,
                "employer" => 0.5,
                _ => 0.75,
            };
            var confidenceWeight = claim.Confidence.ToLowerInvariant() switch
            {
                "verified" => 1.0,
                "stated" => 0.8,
                "weak" => 0.35,
                _ => 0.5,
            };
            var weight = kindWeight * confidenceWeight;
            foreach (var term in Tokenize(claim.Text))
            {
                if (!terms.TryGetValue(term, out var current) || weight > current)
                    terms[term] = weight;
            }
        }
        return terms;
    }

    private static double GrowthScore(IReadOnlySet<string> title, IReadOnlySet<string> description)
    {
        var hits = GrowthTerms.Count(term => title.Contains(term) || description.Contains(term));
        return Math.Round(Math.Clamp(2.2 + 0.35 * hits, 0.0, 5.0), 2);
    }

    private static HashSet<string> Tokenize(string text)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in WordRegex().Matches(text.ToLowerInvariant()))
        {
            var token = match.Value.TrimStart('.');
            if (token.Length < 2 || StopWords.Contains(token) || token.All(char.IsDigit))
                continue;
            tokens.Add(token);
        }
        return tokens;
    }

    [GeneratedRegex(@"[a-z0-9.]+(?:[+#][a-z0-9]*)?", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
