using System.Text.RegularExpressions;
using SeekerSvc.Scout;

namespace SeekerSvc.Scorer;

/// <summary>Interval normalization so pay at any cadence can be compared on one annual scale.</summary>
internal static class CompMath
{
    public static decimal Annualize(decimal amount, CompInterval interval) => interval switch
    {
        CompInterval.Hour => amount * 2080m,   // 40h * 52w
        CompInterval.Day => amount * 260m,     // 5d * 52w
        CompInterval.Week => amount * 52m,
        CompInterval.Month => amount * 12m,
        _ => amount,                            // Year / Unknown treated as annual
    };

    /// <summary>Annualized midpoint of a comp range, using whichever bound(s) are present.</summary>
    public static decimal? AnnualMidpoint(Compensation c)
    {
        var lo = c.Min;
        var hi = c.Max;
        decimal? mid = (lo, hi) switch
        {
            (not null, not null) => (lo + hi) / 2m,
            (not null, null) => lo,
            (null, not null) => hi,
            _ => null,
        };
        return mid is null ? null : Annualize(mid.Value, c.Interval);
    }
}

/// <summary>Parse a coarse seniority band out of a canonical title.</summary>
internal static class Seniority
{
    public static SeniorityBand FromTitle(string titleCanon)
    {
        var t = " " + titleCanon.ToLowerInvariant() + " ";
        if (Has(t, "intern")) return SeniorityBand.Intern;
        if (Has(t, "principal")) return SeniorityBand.Principal;
        if (Has(t, "staff")) return SeniorityBand.Staff;
        if (Has(t, "director") || Has(t, "head of")) return SeniorityBand.Director;
        if (Has(t, "vp") || Has(t, "vice president") || Has(t, "chief") || t.Contains(" c-level ")) return SeniorityBand.Exec;
        if (Has(t, "lead")) return SeniorityBand.Lead;
        if (Has(t, "senior") || Has(t, "sr")) return SeniorityBand.Senior;
        if (Has(t, "junior") || Has(t, "jr") || Has(t, "entry") || Has(t, "associate")) return SeniorityBand.Junior;
        // a plain professional title with no modifier reads as mid-level
        return SeniorityBand.Mid;
    }

    private static bool Has(string padded, string word) => padded.Contains(" " + word + " ");

    public static double Alignment(SeniorityBand user, SeniorityBand job)
    {
        if (user == SeniorityBand.Unknown || job == SeniorityBand.Unknown) return 0.6;
        return Math.Abs((int)user - (int)job) switch
        {
            0 => 1.0,
            1 => 0.7,
            2 => 0.4,
            _ => 0.15,
        };
    }
}

/// <summary>
/// Red-flag detection over UNTRUSTED posting text. Patterns are matched as DATA; nothing in the
/// description is ever treated as an instruction. Any Severe hit forces the multiplier to its 0.1
/// hard floor (spec section 5.4); a Moderate-only hit yields 0.5; clean text is 1.0.
/// </summary>
internal static class RedFlags
{
    private sealed record Pattern(string Code, RedFlagSeverity Severity, Regex Rx);

    private static Regex R(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Pattern[] Patterns =
    {
        // --- severe: scam-defining ---
        new("fee_to_apply", RedFlagSeverity.Severe,
            R(@"\b(application|registration|processing|onboarding|training)\s+fee\b|\bfee\s+to\s+apply\b|\bpay\b[^.\n]{0,30}\b(fee|deposit)\b")),
        new("pii_pre_offer", RedFlagSeverity.Severe,
            R(@"\bsocial security number\b|\bssn\b|\bdate of birth\b|\bdob\b|\bbank account (number|details|info)\b")),
        new("equity_only", RedFlagSeverity.Severe,
            R(@"\bequity[- ]only\b|\bpaid (only )?in equity\b|\bno (base )?salary\b|\bsweat equity\b|\bunpaid\b")),
        new("crypto_payroll", RedFlagSeverity.Severe,
            R(@"\bpaid in (crypto|bitcoin|btc|eth|usdt|cryptocurrency)\b|\bcrypto(currency)?\s+(salary|payroll|payment)\b")),
        new("reshipping_mule", RedFlagSeverity.Severe,
            R(@"\breshipping\b|\bpackage forwarding\b|\bforward (packages|parcels|funds)\b|\bmoney transfer agent\b|\bwire (the )?funds\b|\bpayment processing agent\b")),

        // --- moderate: scam-adjacent / low-quality ---
        new("mlm_language", RedFlagSeverity.Moderate,
            R(@"\bbe your own boss\b|\bunlimited earning potential\b|\bmulti-?level marketing\b|\bmlm\b|\bfinancial freedom\b")),
        new("no_interview", RedFlagSeverity.Moderate,
            R(@"\bno interview (required|needed)\b|\bhired on the spot\b|\bimmediate start\b[^.\n]{0,30}\bno experience\b")),
        new("commission_only", RedFlagSeverity.Moderate,
            R(@"\bcommission[- ]only\b|\b100% commission\b")),
    };

    public static (IReadOnlyList<RedFlag> Flags, double Multiplier) Scan(string descriptionText)
    {
        var found = new List<RedFlag>();
        foreach (var p in Patterns)
        {
            var m = p.Rx.Match(descriptionText);
            if (m.Success) found.Add(new RedFlag(p.Code, p.Severity, Snippet(descriptionText, m.Index, m.Length)));
        }

        double mult = 1.0;
        if (found.Any(f => f.Severity == RedFlagSeverity.Severe)) mult = 0.1;
        else if (found.Count > 0) mult = 0.5;
        return (found, mult);
    }

    private static string Snippet(string text, int index, int length)
    {
        var start = Math.Max(0, index - 12);
        var end = Math.Min(text.Length, index + length + 12);
        return text.Substring(start, end - start).Replace('\n', ' ').Trim();
    }
}

/// <summary>The ghost-job legitimacy rubric (spec section 5.4): evidence-grounded, no LLM.</summary>
internal static class Legitimacy
{
    public static (double Score, LegitimacyBreakdown Parts) Evaluate(JobPosting p, DateTimeOffset now)
    {
        var compTransparency = p.Compensation is not null ? 1.0 : 0.4;
        var specSpecificity = Math.Clamp(0.2 + 0.8 * (p.DescriptionText.Length - 120.0) / (600.0 - 120.0), 0.2, 1.0);
        var repostHealth = p.RepostCount <= 1 ? 1.0 : 1.0 / (1.0 + 0.6 * (p.RepostCount - 1));
        var recruiter = p.RecruiterIdentifiable switch { true => 1.0, false => 0.3, null => 0.6 };
        var domain = p.CompanyDomainVerified switch { true => 1.0, false => 0.3, null => 0.6 };
        var freshness = Freshness(p.FirstPublished, now);

        var raw = 0.20 * compTransparency + 0.20 * specSpecificity + 0.20 * repostHealth
                + 0.15 * recruiter + 0.15 * domain + 0.10 * freshness;

        // A posting that tries to manipulate the agent is, by that fact, a serious legitimacy problem.
        var injectionPenalty = p.DescriptionLikelyInjected;
        if (injectionPenalty) raw *= 0.35;

        var score = Math.Clamp(Math.Round(raw * 5.0, 2), 0.0, 5.0);
        return (score, new LegitimacyBreakdown(compTransparency, specSpecificity, repostHealth, recruiter, domain, freshness, injectionPenalty));
    }

    private static double Freshness(DateTimeOffset? firstPublished, DateTimeOffset now)
    {
        if (firstPublished is null) return 0.6;
        var days = (now - firstPublished.Value).TotalDays;
        if (days <= 30) return 1.0;
        if (days >= 120) return 0.3;
        return 1.0 - 0.7 * (days - 30) / 90.0;
    }
}

/// <summary>Deterministic fit sub-scores: comp-vs-target and preferences alignment.</summary>
internal static class FitMath
{
    public static double CompVsTarget(Compensation? comp, CompTarget? target)
    {
        if (target is null || comp is null) return 3.0; // unknown -> neutral, never invented
        var ann = CompMath.AnnualMidpoint(comp);
        if (ann is null) return 3.0;

        decimal floor = CompMath.Annualize(target.Floor, target.Interval);
        decimal tgt = CompMath.Annualize(target.Target, target.Interval);
        decimal stretch = CompMath.Annualize(target.Stretch, target.Interval);
        double a = (double)ann.Value;

        if (a >= (double)stretch) return 5.0;
        if (a >= (double)tgt)
            return 4.0 + Clamp01((a - (double)tgt) / Math.Max(1, (double)(stretch - tgt)));
        if (a >= (double)floor)
            return 2.5 + 1.5 * Clamp01((a - (double)floor) / Math.Max(1, (double)(tgt - floor)));
        return Math.Max(0.0, 2.5 * a / Math.Max(1.0, (double)floor));
    }

    public static double PrefsAlignment(JobPosting p, UserPreferences prefs)
    {
        var remote = RemoteAlignment(prefs.Remote, p.Remote);
        var location = LocationAlignment(p, prefs);
        var seniority = Seniority.Alignment(prefs.Seniority, Seniority.FromTitle(p.TitleCanon));
        return ((remote + location + seniority) / 3.0) * 5.0;
    }

    private static double RemoteAlignment(RemoteStance stance, RemoteMode job) => stance switch
    {
        RemoteStance.Any => 1.0,
        RemoteStance.RemoteRequired => job switch { RemoteMode.Remote => 1.0, RemoteMode.Hybrid => 0.6, RemoteMode.OnSite => 0.0, _ => 0.5 },
        RemoteStance.OnSiteRequired => job switch { RemoteMode.OnSite => 1.0, RemoteMode.Hybrid => 0.6, RemoteMode.Remote => 0.2, _ => 0.5 },
        RemoteStance.HybridPreferred => job switch { RemoteMode.Hybrid => 1.0, RemoteMode.Remote => 0.8, RemoteMode.OnSite => 0.5, _ => 0.6 },
        _ => 0.6,
    };

    private static double LocationAlignment(JobPosting p, UserPreferences prefs)
    {
        if (prefs.Locations.Count == 0) return 1.0;             // no constraint
        if (p.Remote is RemoteMode.Remote or RemoteMode.Hybrid) return 1.0; // remote satisfies any location
        if (p.Location is null) return 0.6;
        var jobKey = Canon.LocalityKey(p.Location);
        foreach (var want in prefs.Locations)
            if (Canon.LocalityKey(want) == jobKey) return 1.0;
        return 0.2;
    }

    private static double Clamp01(double x) => Math.Clamp(x, 0.0, 1.0);
}
