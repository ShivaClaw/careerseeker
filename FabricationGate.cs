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

    // ---- scoring ----
    Task SaveScoreAsync(ScoreRow score, CancellationToken ct = default);

    // ---- application lifecycle (each transition also appends an audit event) ----
    Task<long> CreateApplicationAsync(long jobId, string autonomyLevel, CancellationToken ct = default);
    Task TransitionApplicationAsync(long applicationId, string newState, string actor,
        string? payloadJson = null, CancellationToken ct = default);
    Task<ApplicationRow?> GetApplicationAsync(long applicationId, CancellationToken ct = default);

    // ---- audit log ----
    Task<long> AppendEventAsync(EventInput e, CancellationToken ct = default);
    Task<IReadOnlyList<EventRow>> GetEventsAsync(CancellationToken ct = default);
    Task<AuditVerification> VerifyAuditAsync(CancellationToken ct = default);

    // ---- profile / claims (the Fabrication Gate's oracle) ----
    Task<long> UpsertProfileAsync(string json, CancellationToken ct = default);
    Task AddClaimAsync(ClaimRow claim, CancellationToken ct = default);
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
