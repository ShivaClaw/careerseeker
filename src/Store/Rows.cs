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

/// <summary>A persisted application row. <see cref="PausedFrom"/> is the durable pre-pause state:
/// set only while the row is PAUSED, cleared by every other transition, so resume survives restart.</summary>
public sealed record ApplicationRow(
    long Id,
    long JobId,
    string State,
    string AutonomyLevel,
    string? Channel,
    string CreatedAt,
    string UpdatedAt,
    string? PausedFrom = null);

/// <summary>Read-only dashboard summary for recent applications. Payload bodies stay out of this row.</summary>
public sealed record ApplicationSummaryRow(
    long ApplicationId,
    string State,
    string AutonomyLevel,
    string? Channel,
    string CreatedAt,
    string UpdatedAt,
    string? PausedFrom,
    long JobId,
    string JobTitle,
    string? CompanyName,
    string? CompanyDomain,
    string? Location,
    string Remote,
    string JobUrl,
    string? ApplyUrl,
    double? Fit,
    double? Legitimacy,
    double? Total,
    string? DraftStatus,
    string? DraftExternalRef);

/// <summary>
/// A durable side-effect attempt record bracketing an external call (Gmail draft, ATS submit).
/// PENDING is written before the call, SUCCEEDED/FAILED after — so after a crash, PENDING means
/// "outcome unknown at the provider", FAILED means "known not to have happened" (safe to retry),
/// and SUCCEEDED with a stale application state means "happened; finish the transition, never re-call".
/// </summary>
public sealed record EffectAttemptRow(
    long Id,
    long ApplicationId,
    string Kind,      // 'draft' | 'submit'
    string Status,    // 'PENDING' | 'SUCCEEDED' | 'FAILED'
    string? ExternalRef,
    string CreatedAt,
    string UpdatedAt);

internal static class StoreNormalization
{
    private static readonly HashSet<string> ValidConfidences = new(StringComparer.Ordinal)
    {
        "verified",
        "stated",
        "weak",
    };

    public static ClaimRow Normalize(ClaimRow claim)
    {
        var confidence = claim.Confidence.Trim().ToLowerInvariant();
        if (!ValidConfidences.Contains(confidence))
            throw new ArgumentException($"Unsupported claim confidence '{claim.Confidence}'.", nameof(claim));

        return claim with { Confidence = confidence };
    }
}
