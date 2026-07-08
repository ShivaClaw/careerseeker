using SeekerSvc.Scout;

namespace SeekerSvc.Scorer;

/// <summary>Coarse seniority bands, ordered. Used to grade title-vs-target alignment.</summary>
public enum SeniorityBand
{
    Unknown = -1,
    Intern = 0,
    Junior = 1,
    Mid = 2,
    Senior = 3,
    Lead = 4,
    Staff = 5,
    Principal = 6,
    Director = 7,
    Exec = 8,
}

/// <summary>The user's remote-work stance.</summary>
public enum RemoteStance { Any, RemoteRequired, OnSiteRequired, HybridPreferred }

/// <summary>What the engine is permitted to do with a scored job.</summary>
public enum Dispatch
{
    /// <summary>Eligible to act on (subject to the autonomy level's gates downstream).</summary>
    Act,

    /// <summary>May be surfaced to the user, but the engine must never act on it autonomously.</summary>
    ShowOnly,

    /// <summary>Anti-targeted (e.g. a mission-blocklist hit): not a match at all.</summary>
    Reject,
}

/// <summary>Severity of a posting red flag.</summary>
public enum RedFlagSeverity { Moderate, Severe }

/// <summary>A compensation target, expressed at some interval (normalized to annual when scoring).</summary>
public sealed record CompTarget(decimal Floor, decimal Target, decimal Stretch, CompInterval Interval = CompInterval.Year);

/// <summary>
/// The user's structured targeting/economics preferences. All optional: absent preferences score
/// neutral rather than penalizing. Mission blocklist terms are hard constraints (a hit → Reject).
/// </summary>
public sealed record UserPreferences
{
    public CompTarget? Comp { get; init; }
    public RemoteStance Remote { get; init; } = RemoteStance.Any;
    public SeniorityBand Seniority { get; init; } = SeniorityBand.Unknown;

    /// <summary>Normalized locality keys the user will work in (empty = anywhere). Remote jobs always match.</summary>
    public IReadOnlyList<string> Locations { get; init; } = Array.Empty<string>();

    /// <summary>Hard "never these" constraints (e.g. defense, gambling). A match makes the job a Reject.</summary>
    public IReadOnlyList<string> MissionBlocklist { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The posting being scored. <see cref="DescriptionText"/> is UNTRUSTED (Scout's contract): the
/// Scorer scans it for red-flag and mission patterns as DATA only — it is never instructions.
/// </summary>
public sealed record JobPosting
{
    public required string Title { get; init; }
    public required string TitleCanon { get; init; }
    public string? Location { get; init; }
    public RemoteMode Remote { get; init; }
    public Compensation? Compensation { get; init; }
    public required string DescriptionText { get; init; }
    public int RepostCount { get; init; }
    public DateTimeOffset? FirstPublished { get; init; }

    /// <summary>From Scout: the description tripped prompt-injection heuristics. Slashes legitimacy.</summary>
    public bool DescriptionLikelyInjected { get; init; }

    /// <summary>Optional researched signals (null = unknown, scored neutral).</summary>
    public bool? RecruiterIdentifiable { get; init; }
    public bool? CompanyDomainVerified { get; init; }

    /// <summary>Build a posting from a Scout discovery plus the repost count the store assigned.</summary>
    public static JobPosting FromDiscovered(DiscoveredJob j, int repostCount = 0) => new()
    {
        Title = j.Title,
        TitleCanon = j.TitleCanon,
        Location = j.Location,
        Remote = j.Remote,
        Compensation = j.Compensation,
        DescriptionText = j.DescriptionText,
        RepostCount = repostCount,
        FirstPublished = j.FirstPublished,
        DescriptionLikelyInjected = j.DescriptionLikelyInjected,
    };
}

/// <summary>
/// The semantic sub-scores (0..5) that require a model's judgment, supplied by the LLM quick/deep
/// score stage. The Scorer computes everything else deterministically and never invents these.
/// </summary>
public sealed record SemanticScores(double CvMatch, double GrowthSignal);

/// <summary>Tunable weights and thresholds. Defaults are the spec's (section 5.4 and 4.4).</summary>
public sealed record ScorerOptions
{
    public double WCvMatch { get; init; } = 0.35;
    public double WCompVsTarget { get; init; } = 0.25;
    public double WGrowthSignal { get; init; } = 0.20;
    public double WPrefsAlignment { get; init; } = 0.20;

    /// <summary>Absolute legitimacy floor: below this the engine may show but never act (section 5.4). Not user-lowerable.</summary>
    public double LegitimacyFloor { get; init; } = 2.5;

    /// <summary>Minimum total to act (the configurable rail; section 4.4 default).</summary>
    public double ActThreshold { get; init; } = 4.0;

    public static ScorerOptions Default { get; } = new();
}

/// <summary>A detected red flag with its severity and the evidence snippet.</summary>
public sealed record RedFlag(string Code, RedFlagSeverity Severity, string Evidence);

/// <summary>The fit axis broken into its four sub-scores (each 0..5).</summary>
public sealed record FitBreakdown(double CvMatch, double CompVsTarget, double GrowthSignal, double PrefsAlignment);

/// <summary>The legitimacy axis broken into its rubric components (each 0..1) plus the injection penalty.</summary>
public sealed record LegitimacyBreakdown(
    double CompTransparency,
    double SpecSpecificity,
    double RepostHealth,
    double RecruiterIdentifiable,
    double DomainVerified,
    double Freshness,
    bool InjectionPenaltyApplied);

/// <summary>
/// The full scoring result. <see cref="Total"/> = min(<see cref="Fit"/>, <see cref="Legitimacy"/>)
/// · <see cref="RedFlagMultiplier"/> — a scam can never outrank its worst axis.
/// </summary>
public sealed record ScoreResult(
    double Fit,
    double Legitimacy,
    double RedFlagMultiplier,
    double Total,
    FitBreakdown FitParts,
    LegitimacyBreakdown LegitParts,
    IReadOnlyList<RedFlag> RedFlags,
    Dispatch Dispatch,
    string Rationale);
