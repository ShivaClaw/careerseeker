namespace SeekerSvc.Scout;

/// <summary>The supported applicant-tracking systems with public JSON job feeds.</summary>
public enum AtsKind
{
    Greenhouse,
    Lever,
    Ashby,
}

/// <summary>Workplace arrangement for a posting.</summary>
public enum RemoteMode
{
    Unknown,
    OnSite,
    Remote,
    Hybrid,
}

/// <summary>Pay period a compensation figure refers to.</summary>
public enum CompInterval
{
    Unknown,
    Year,
    Month,
    Week,
    Day,
    Hour,
}

/// <summary>Whether a compensation figure came from a structured API field or was parsed from free text.</summary>
public enum CompSource
{
    /// <summary>Read from a dedicated API field (Lever salaryRange, Ashby components).</summary>
    Structured,

    /// <summary>Regex-extracted from description / summary text (Greenhouse, or fallback). Lower trust.</summary>
    ParsedFromText,
}

/// <summary>
/// A pay range attached to a posting. Money is decimal. We never synthesize a figure:
/// if neither a structured field nor an explicit text range is present, comp is null.
/// </summary>
public sealed record Compensation(
    decimal? Min,
    decimal? Max,
    string? Currency,
    CompInterval Interval,
    CompSource Source,
    string? RawText = null);

/// <summary>
/// A board to ingest. Handle is the ATS-specific token/slug (the segment in the board URL).
/// DisplayName, when supplied by the registry or the user, is the company's real name; it is
/// used as the dedup identity so the same company on two ATSs collapses correctly (the ATS
/// feeds themselves do not return a company name).
/// </summary>
public sealed record CompanyBoard(AtsKind Ats, string Handle, string? DisplayName = null);

/// <summary>
/// A normalized job posting. The shape every provider maps into.
///
/// SECURITY: <see cref="DescriptionText"/> is UNTRUSTED third-party input. A job description
/// is authored by whoever posted the job and may contain prompt-injection aimed at any
/// downstream LLM. Scout never treats it as instructions; it is data on this record only, and
/// <see cref="DescriptionLikelyInjected"/> flags suspicious text as a legitimacy signal WITHOUT
/// dropping the posting. Downstream stages must keep this field out of any instruction context.
/// </summary>
public sealed record DiscoveredJob
{
    public required AtsKind Source { get; init; }
    public required string BoardHandle { get; init; }
    public string? CompanyName { get; init; }

    public required string JobId { get; init; }
    public required string Title { get; init; }

    /// <summary>Canonicalized title used for dedup and downstream matching.</summary>
    public required string TitleCanon { get; init; }

    public string? Location { get; init; }
    public RemoteMode Remote { get; init; }
    public Compensation? Compensation { get; init; }

    /// <summary>Plain-text description. UNTRUSTED. See the type remarks.</summary>
    public required string DescriptionText { get; init; }

    /// <summary>64-bit SimHash of the description, for near-duplicate detection.</summary>
    public long DescriptionSimHash { get; init; }

    public required string Url { get; init; }
    public string? ApplyUrl { get; init; }
    public DateTimeOffset? FirstPublished { get; init; }

    /// <summary>company | title | locality key — the exact-match dedup key.</summary>
    public required string DedupKey { get; init; }

    /// <summary>True if the description contains likely prompt-injection. A SIGNAL, not a filter.</summary>
    public bool DescriptionLikelyInjected { get; init; }

    /// <summary>Names of the injection heuristics that fired (empty when clean).</summary>
    public IReadOnlyList<string> InjectionSignals { get; init; } = Array.Empty<string>();
}

/// <summary>Tunable knobs for discovery. Conservative, polite defaults.</summary>
public sealed record ScoutOptions
{
    /// <summary>Max boards fetched in parallel across all hosts.</summary>
    public int MaxConcurrency { get; init; } = 4;

    /// <summary>Max concurrent requests to a single host (politeness).</summary>
    public int PerHostConcurrency { get; init; } = 1;

    /// <summary>Minimum spacing between requests to the same host.</summary>
    public TimeSpan MinDelayPerHost { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Retry attempts on 429 / 5xx / transient transport errors.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Base delay for exponential backoff (doubles each attempt, plus jitter).</summary>
    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Per-request timeout.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Max Hamming distance (of 64) between description SimHashes for two same-company
    /// postings to be treated as the same job. Strict by default to avoid collapsing
    /// genuinely distinct roles that share boilerplate.
    /// </summary>
    public int SimHashDuplicateMaxHamming { get; init; } = 3;

    public string UserAgent { get; init; } = "CareerSeeker-Scout/0.1 (+https://careerseeker.app)";

    public static ScoutOptions Default { get; } = new();
}

/// <summary>Outcome of ingesting one board. Failures are isolated here; one bad board never sinks a run.</summary>
public sealed record BoardResult(
    CompanyBoard Board,
    bool Ok,
    int JobCount,
    string? Error = null,
    int HttpStatus = 0);

/// <summary>Result of a full discovery pass: deduped jobs plus per-board diagnostics.</summary>
public sealed record DiscoveryResult(
    IReadOnlyList<DiscoveredJob> Jobs,
    IReadOnlyList<BoardResult> Boards,
    int DuplicatesCollapsed)
{
    public int FlaggedCount => Jobs.Count(j => j.DescriptionLikelyInjected);
    public int BoardsOk => Boards.Count(b => b.Ok);
    public int BoardsFailed => Boards.Count(b => !b.Ok);
}
