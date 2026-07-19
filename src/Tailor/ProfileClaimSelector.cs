using System.Text.RegularExpressions;
using SeekerSvc.Pipeline;
using SeekerSvc.Verifier;

namespace SeekerSvc.Tailor;

/// <summary>
/// Minimizes profile facts before generation. The full source-of-truth profile remains local for the
/// Fabrication Gate; this selector only narrows what the writing model sees.
/// </summary>
public static class ProfileClaimSelector
{
    private const int MinimumOverlap = 1;
    private static readonly Regex TokenRx = new("[a-z0-9][a-z0-9+#.-]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "are", "but", "for", "from", "have", "into", "job", "not", "our", "role", "the", "their",
        "this", "that", "with", "will", "you", "your", "team", "work", "working"
    };

    public static IReadOnlyList<SourceClaim> Select(
        IReadOnlyList<SourceClaim> profile,
        PipelineJob job,
        IReadOnlyList<Violation> priorViolations)
    {
        if (profile.Count == 0)
            return profile;

        var context = Tokens(string.Join(" ", new[]
        {
            job.Title,
            job.Company,
            job.ApplyUrl ?? "",
            job.DescriptionText ?? "",
        }));
        var nearest = priorViolations
            .Select(v => v.NearestSource)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selected = profile
            .Select((claim, index) => new
            {
                Claim = claim,
                Index = index,
                Score = Score(claim, context, nearest),
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .Select(x => x.Claim)
            .ToList();

        return selected;
    }

    private static int Score(SourceClaim claim, HashSet<string> context, HashSet<string> nearest)
    {
        if (nearest.Contains(claim.Text))
            return 1000;

        var overlap = Tokens(claim.Text).Count(context.Contains);
        if (overlap < MinimumOverlap)
            return 0;

        var kindBoost = claim.Kind switch
        {
            ClaimKind.Credential => 40,
            ClaimKind.Metric => 30,
            ClaimKind.Skill => 20,
            ClaimKind.Title => 20,
            ClaimKind.Education => 10,
            _ => 0,
        };
        return kindBoost + overlap;
    }

    private static HashSet<string> Tokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in TokenRx.Matches(text.ToLowerInvariant()))
        {
            var token = match.Value.Trim('.', '-', '_');
            if (token.Length < 3 || Stop.Contains(token))
                continue;
            tokens.Add(token);
        }
        return tokens;
    }
}
