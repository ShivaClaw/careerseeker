using System.Text.RegularExpressions;

namespace SeekerSvc.Scorer;

/// <summary>
/// The two-axis scorer (spec section 5.4). Fit (how good a match) and legitimacy (how real the
/// posting) are scored separately; a red-flag multiplier can torpedo the total. The combining rule
///
///     total = min(fit, legitimacy) · red_flag_multiplier
///
/// means a scam can never outrank its worst axis, and the legitimacy floor is an absolute "may not
/// act" gate at every autonomy level. These properties live in the math here, not in downstream
/// policy — exactly as the Fabrication Gate's no-bypass and the audit log's tamper-evidence do.
///
/// Only CvMatch and GrowthSignal come from a model; everything else is deterministic and grounded in
/// the posting and the user's stated preferences.
/// </summary>
public static class Scorer
{
    public static ScoreResult Score(
        JobPosting posting,
        UserPreferences prefs,
        SemanticScores semantic,
        ScorerOptions? options = null,
        DateTimeOffset? now = null)
    {
        var opt = options ?? ScorerOptions.Default;
        var clock = now ?? DateTimeOffset.UtcNow;

        // ---- fit axis ----
        var cvMatch = Clamp05(semantic.CvMatch);
        var growth = Clamp05(semantic.GrowthSignal);
        var compVsTarget = FitMath.CompVsTarget(posting.Compensation, prefs.Comp);
        var prefsAlignment = FitMath.PrefsAlignment(posting, prefs);

        var fit = opt.WCvMatch * cvMatch
                + opt.WCompVsTarget * compVsTarget
                + opt.WGrowthSignal * growth
                + opt.WPrefsAlignment * prefsAlignment;
        fit = Clamp05(fit);
        var fitParts = new FitBreakdown(cvMatch, compVsTarget, growth, prefsAlignment);

        // ---- legitimacy axis ----
        var (legitimacy, legitParts) = Legitimacy.Evaluate(posting, clock);

        // ---- red-flag multiplier (untrusted text scanned as data) ----
        var (flags, multiplier) = RedFlags.Scan(posting.DescriptionText);

        // ---- combine: a scam can never outrank its worst axis ----
        var total = Math.Round(Math.Min(fit, legitimacy) * multiplier, 2);

        // ---- dispatch decision ----
        var missionHit = MissionHit(posting, prefs.MissionBlocklist);
        var dispatch = Decide(missionHit, legitimacy, multiplier, total, opt, out var reason);

        var rationale = Rationale(dispatch, reason, fit, legitimacy, multiplier, total, flags, missionHit);
        return new ScoreResult(Math.Round(fit, 2), legitimacy, multiplier, total,
            fitParts, legitParts, flags, dispatch, rationale);
    }

    private static Dispatch Decide(
        string? missionHit, double legitimacy, double multiplier, double total,
        ScorerOptions opt, out string reason)
    {
        if (missionHit is not null)
        {
            reason = $"mission blocklist: \"{missionHit}\"";
            return Dispatch.Reject;
        }
        if (legitimacy < opt.LegitimacyFloor)
        {
            reason = $"legitimacy {legitimacy:0.0} below floor {opt.LegitimacyFloor:0.0}";
            return Dispatch.ShowOnly;
        }
        if (multiplier <= 0.1)
        {
            reason = "severe red flag";
            return Dispatch.ShowOnly;
        }
        if (total < opt.ActThreshold)
        {
            reason = $"total {total:0.0} below act threshold {opt.ActThreshold:0.0}";
            return Dispatch.ShowOnly;
        }
        reason = $"total {total:0.0} clears threshold; legitimacy ok";
        return Dispatch.Act;
    }

    private static string? MissionHit(JobPosting posting, IReadOnlyList<string> blocklist)
    {
        if (blocklist.Count == 0) return null;
        var haystack = (posting.Title + " " + posting.DescriptionText);
        foreach (var term in blocklist)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;
            if (Regex.IsMatch(haystack, $@"\b{Regex.Escape(term.Trim())}\b", RegexOptions.IgnoreCase))
                return term.Trim();
        }
        return null;
    }

    private static string Rationale(
        Dispatch dispatch, string reason, double fit, double legitimacy, double multiplier,
        double total, IReadOnlyList<RedFlag> flags, string? missionHit)
    {
        var flagText = flags.Count == 0 ? "none" : string.Join(", ", flags.Select(f => $"{f.Code}({f.Severity})"));
        return $"{dispatch}: {reason}. fit={fit:0.0}, legit={legitimacy:0.0}, mult={multiplier:0.0#}, total={total:0.0}; red_flags=[{flagText}]";
    }

    private static double Clamp05(double x) => Math.Clamp(x, 0.0, 5.0);
}
