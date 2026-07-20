using SeekerSvc.Scout;

namespace SeekerSvc.Store;

/// <summary>
/// The engine's storage contract. SQLite is the production implementation
/// (<c>SeekerSvc.Store.Sqlite</c>); an in-memory implementation backs tests and the demo. State
/// changes that matter for the audit trail append a hash-chained event as part of the same logical
/// operation, so the record and the data never drift apart.
/// </summary>
public interface ISeekerStore
{
    /// <summary>Create the schema if needed and apply connection pragmas. Idempotent.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    // ---- discovery ----
    Task<long> UpsertCompanyAsync(CompanyUpsert company, CancellationToken ct = default);
    Task<JobWriteResult> UpsertJobAsync(long companyId, JobUpsert job, CancellationToken ct = default);
    Task<JobRow?> GetJobAsync(long jobId, CancellationToken ct = default);
    Task<JobSummaryRow?> GetJobSummaryAsync(long jobId, CancellationToken ct = default);
    Task<IReadOnlyList<JobSummaryRow>> GetRecentJobsAsync(int limit = 25, CancellationToken ct = default);

    // ---- scoring ----
    Task SaveScoreAsync(ScoreRow score, CancellationToken ct = default);

    // ---- application lifecycle (each transition also appends an audit event) ----
    Task<long> CreateApplicationAsync(long jobId, string autonomyLevel, CancellationToken ct = default);
    Task TransitionApplicationAsync(long applicationId, string newState, string actor,
        string? payloadJson = null, CancellationToken ct = default);

    /// <summary>
    /// Compare-and-swap transition: atomically move to <paramref name="newState"/> and append the
    /// audit event iff the row's current state equals <paramref name="expectedState"/>. Returns false
    /// (no write, no event) when the precondition fails — the caller lost the race and must re-read.
    /// <paramref name="recordPausedFrom"/> is persisted into the row's paused_from (every transition
    /// overwrites it; pass the origin state when pausing, leave null otherwise so it clears).
    /// </summary>
    Task<bool> TryTransitionApplicationAsync(long applicationId, string expectedState, string newState,
        string actor, string? payloadJson = null, string? recordPausedFrom = null, CancellationToken ct = default);

    Task<ApplicationRow?> GetApplicationAsync(long applicationId, CancellationToken ct = default);
    Task<IReadOnlyList<ApplicationSummaryRow>> GetRecentApplicationsAsync(int limit = 25, CancellationToken ct = default);
    Task SaveApplicationArtifactsAsync(
        long applicationId,
        string? resumePath,
        string? coverPath,
        string? answersJson,
        CancellationToken ct = default);

    // ---- durable in-flight dispatch payload (L2 gate content survives restart; no audit event —
    //      the payload carries tailored content, which does not belong in the event log) ----
    Task SavePendingDispatchAsync(long applicationId, string payloadJson, CancellationToken ct = default);
    Task<string?> GetPendingDispatchAsync(long applicationId, CancellationToken ct = default);
    Task DeletePendingDispatchAsync(long applicationId, CancellationToken ct = default);

    // ---- side-effect attempts (crash-window evidence around external calls; each write is audited) ----
    Task<long> BeginEffectAttemptAsync(long applicationId, string kind, CancellationToken ct = default);
    Task ResolveEffectAttemptAsync(long attemptId, string status, string? externalRef = null, CancellationToken ct = default);
    Task<IReadOnlyList<EffectAttemptRow>> GetEffectAttemptsAsync(long applicationId, string? kind = null, CancellationToken ct = default);

    // ---- audit log ----
    Task<long> AppendEventAsync(EventInput e, CancellationToken ct = default);
    Task<IReadOnlyList<EventRow>> GetEventsAsync(CancellationToken ct = default);
    Task<AuditVerification> VerifyAuditAsync(CancellationToken ct = default);

    // ---- profile / claims (the Fabrication Gate's oracle) ----
    Task<long> UpsertProfileAsync(string json, CancellationToken ct = default);
    Task AddClaimAsync(ClaimRow claim, CancellationToken ct = default);
    Task ReplaceClaimsAsync(long profileId, IReadOnlyList<ClaimRow> claims, CancellationToken ct = default);
    Task<IReadOnlyList<ClaimRow>> GetClaimsAsync(long profileId, CancellationToken ct = default);

    // ---- config (rails, autonomy level, budgets, quiet hours) ----
    Task<string?> GetConfigAsync(string key, CancellationToken ct = default);
    Task SetConfigAsync(string key, string value, CancellationToken ct = default);
}

/// <summary>Convenience helpers layered on the store contract.</summary>
public static class SeekerStoreExtensions
{
    /// <summary>
    /// Persist a Scout posting: upsert its company, then upsert the job under that company.
    /// </summary>
    public static async Task<JobWriteResult> IngestAsync(
        this ISeekerStore store, DiscoveredJob job, CancellationToken ct = default)
    {
        var (company, jobUpsert) = Ingest.From(job);
        var companyId = await store.UpsertCompanyAsync(company, ct).ConfigureAwait(false);
        return await store.UpsertJobAsync(companyId, jobUpsert, ct).ConfigureAwait(false);
    }
}
