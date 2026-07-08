namespace SeekerSvc.Store;

/// <summary>Upsert input for a company, keyed by (AtsKind, Handle).</summary>
public sealed record CompanyUpsert(string AtsKind, string Handle, string? Name = null, string? Domain = null);

/// <summary>
/// Upsert input for a job, keyed by (Source, ExternalId). Produced from a Scout DiscoveredJob by
/// <see cref="Ingest"/>; comp fields are null when no figure was reliably available.
/// </summary>
public sealed record JobUpsert(
    string Source,
    string ExternalId,
    string Url,
    string Title,
    string TitleCanon,
    string DedupKey,
    string Remote,
    long SimHash,
    string FirstSeen,
    string? ApplyUrl = null,
    string? Location = null,
    decimal? CompMin = null,
    decimal? CompMax = null,
    string? CompCurrency = null,
    string? CompInterval = null,
    string? CompSource = null,
    string? JdPath = null,
    bool Injected = false,
    string? InjectionSignals = null);

/// <summary>Outcome of a job upsert: the row id, whether it was newly inserted, and the repost count.</summary>
public sealed record JobWriteResult(long JobId, bool Inserted, int RepostCount);

/// <summary>A persisted job row.</summary>
public sealed record JobRow(
    long Id,
    long CompanyId,
    string Source,
    string ExternalId,
    string Url,
    string? ApplyUrl,
    string Title,
    string TitleCanon,
    string DedupKey,
    string? Location,
    string Remote,
    decimal? CompMin,
    decimal? CompMax,
    string? CompCurrency,
    string? CompInterval,
    string? CompSource,
    string? JdPath,
    long SimHash,
    bool Injected,
    string? InjectionSignals,
    string FirstSeen,
    string LastVerified,
    int RepostCount);

/// <summary>A score row (one per job; spec section 5.4).</summary>
public sealed record ScoreRow(
    long JobId,
    double Fit,
    double Legitimacy,
    double RedFlagMult,
    double Total,
    string? SubscoresJson = null,
    string? ModelUsed = null);

/// <summary>An atomic profile claim — the Fabrication Gate's oracle (spec section 7.1).</summary>
public sealed record ClaimRow(
    string Id,
    long ProfileId,
    string Kind,
    string Text,
    string Confidence,
    string? SourceDoc = null);

/// <summary>A persisted application row.</summary>
public sealed record ApplicationRow(
    long Id,
    long JobId,
    string State,
    string AutonomyLevel,
    string? Channel,
    string CreatedAt,
    string UpdatedAt);
