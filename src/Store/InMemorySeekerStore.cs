namespace SeekerSvc.Store;

/// <summary>
/// In-memory <see cref="ISeekerStore"/>: a faithful, dependency-free implementation used by tests, the
/// demo, and dry-runs. It uses the same <see cref="Audit"/> chaining as the SQLite provider, so the
/// tamper-evidence behavior it demonstrates is the real behavior. A single async mutex serializes all
/// mutations, which (among other things) keeps the event chain strictly sequential.
/// </summary>
public sealed class InMemorySeekerStore : ISeekerStore
{
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly Func<DateTimeOffset> _clock;

    private readonly Dictionary<(string Ats, string Handle), long> _companyKey = new();
    private readonly Dictionary<long, CompanyUpsert> _companies = new();
    private readonly Dictionary<(string Source, string ExternalId), long> _jobKey = new();
    private readonly Dictionary<long, JobRow> _jobs = new();
    private readonly Dictionary<long, ScoreRow> _scores = new();
    private readonly Dictionary<long, ApplicationRow> _apps = new();
    private readonly List<EventRow> _events = new();
    private readonly Dictionary<string, ClaimRow> _claims = new();
    private readonly List<string> _claimOrder = new();
    private readonly Dictionary<string, string> _config = new();

    private long _companySeq, _jobSeq, _appSeq;
    private long _profileId;
    private long _profileVersion;
    private string? _profileJson;

    public InMemorySeekerStore(Func<DateTimeOffset>? clock = null) => _clock = clock ?? (() => DateTimeOffset.UtcNow);

    private string Now() => _clock().ToString("O");

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<long> UpsertCompanyAsync(CompanyUpsert company, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var key = (company.AtsKind, company.Handle);
            if (_companyKey.TryGetValue(key, out var id))
            {
                var existing = _companies[id];
                _companies[id] = company with
                {
                    Name = company.Name ?? existing.Name,
                    Domain = company.Domain ?? existing.Domain
                };
                return id;
            }
            id = ++_companySeq;
            _companyKey[key] = id;
            _companies[id] = company;
            return id;
        }
        finally { _mutex.Release(); }
    }

    public async Task<JobWriteResult> UpsertJobAsync(long companyId, JobUpsert job, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = Now();
            var firstSeen = string.IsNullOrWhiteSpace(job.FirstSeen) ? now : job.FirstSeen;
            var key = (job.Source, job.ExternalId);
            if (_jobKey.TryGetValue(key, out var id))
            {
                var existing = _jobs[id];
                var repost = existing.RepostCount + 1;
                _jobs[id] = existing with
                {
                    Url = job.Url,
                    ApplyUrl = job.ApplyUrl,
                    CompMin = job.CompMin ?? existing.CompMin,
                    CompMax = job.CompMax ?? existing.CompMax,
                    LastVerified = now,
                    RepostCount = repost
                };
                return new JobWriteResult(id, false, repost);
            }
            id = ++_jobSeq;
            _jobKey[key] = id;
            _jobs[id] = new JobRow(
                Id: id, CompanyId: companyId, Source: job.Source, ExternalId: job.ExternalId,
                Url: job.Url, ApplyUrl: job.ApplyUrl, Title: job.Title, TitleCanon: job.TitleCanon,
                DedupKey: job.DedupKey, Location: job.Location, Remote: job.Remote,
                CompMin: job.CompMin, CompMax: job.CompMax, CompCurrency: job.CompCurrency,
                CompInterval: job.CompInterval, CompSource: job.CompSource, JdPath: job.JdPath,
                SimHash: job.SimHash, Injected: job.Injected, InjectionSignals: job.InjectionSignals,
                FirstSeen: firstSeen, LastVerified: now, RepostCount: 0);
            return new JobWriteResult(id, true, 0);
        }
        finally { _mutex.Release(); }
    }

    public async Task<JobRow?> GetJobAsync(long jobId, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try { return _jobs.TryGetValue(jobId, out var j) ? j : null; }
        finally { _mutex.Release(); }
    }

    public async Task SaveScoreAsync(ScoreRow score, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try { _scores[score.JobId] = score; }
        finally { _mutex.Release(); }
    }

    public async Task<long> CreateApplicationAsync(long jobId, string autonomyLevel, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = Now();
            var id = ++_appSeq;
            _apps[id] = new ApplicationRow(id, jobId, "DISCOVERED", autonomyLevel, null, now, now);
            AppendLocked(new EventInput("engine", "state_change", "application", id.ToString(),
                $"{{\"to\":\"DISCOVERED\",\"job_id\":{jobId}}}"));
            return id;
        }
        finally { _mutex.Release(); }
    }

    public async Task TransitionApplicationAsync(long applicationId, string newState, string actor,
        string? payloadJson = null, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_apps.TryGetValue(applicationId, out var app))
                throw new InvalidOperationException($"No application {applicationId}");
            var now = Now();
            _apps[applicationId] = app with { State = newState, UpdatedAt = now };
            AppendLocked(new EventInput(actor, "state_change", "application", applicationId.ToString(),
                payloadJson ?? $"{{\"to\":\"{newState}\"}}"));
        }
        finally { _mutex.Release(); }
    }

    public async Task<ApplicationRow?> GetApplicationAsync(long applicationId, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try { return _apps.TryGetValue(applicationId, out var a) ? a : null; }
        finally { _mutex.Release(); }
    }

    public async Task<long> AppendEventAsync(EventInput e, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try { return AppendLocked(e); }
        finally { _mutex.Release(); }
    }

    public async Task<IReadOnlyList<EventRow>> GetEventsAsync(CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try { return _events.OrderBy(x => x.Seq).ToList(); }
        finally { _mutex.Release(); }
    }

    public async Task<AuditVerification> VerifyAuditAsync(CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try { return Audit.VerifyChain(_events.OrderBy(x => x.Seq).ToList()); }
        finally { _mutex.Release(); }
    }

    public async Task<long> UpsertProfileAsync(string json, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_profileId == 0) _profileId = 1;
            _profileVersion++;
            _profileJson = json;
            return _profileId;
        }
        finally { _mutex.Release(); }
    }

    public async Task AddClaimAsync(ClaimRow claim, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_claims.ContainsKey(claim.Id))
                _claimOrder.Add(claim.Id);
            _claims[claim.Id] = claim with { Confidence = NormalizeConfidence(claim.Confidence) };
        }
        finally { _mutex.Release(); }
    }

    public async Task<IReadOnlyList<ClaimRow>> GetClaimsAsync(long profileId, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try { return _claimOrder.Select(id => _claims[id]).Where(c => c.ProfileId == profileId).ToList(); }
        finally { _mutex.Release(); }
    }

    public async Task<string?> GetConfigAsync(string key, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try { return _config.TryGetValue(key, out var v) ? v : null; }
        finally { _mutex.Release(); }
    }

    public async Task SetConfigAsync(string key, string value, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try { _config[key] = value; }
        finally { _mutex.Release(); }
    }

    /// <summary>Append an event under the held mutex (keeps the chain strictly sequential).</summary>
    private long AppendLocked(EventInput e)
    {
        var prev = _events.Count == 0 ? Audit.Genesis : _events[^1].Hash;
        var seq = _events.Count + 1;
        var row = Audit.Link(seq, Now(), prev, e);
        _events.Add(row);
        return seq;
    }

    /// <summary>Test-only hook: overwrite a stored event to simulate tampering. Not part of ISeekerStore.</summary>
    internal void TamperForTest(int index, EventRow replacement) => _events[index] = replacement;

    private static string NormalizeConfidence(string confidence)
    {
        var normalized = confidence.Trim().ToLowerInvariant();
        return normalized is "verified" or "stated" or "weak" ? normalized : "stated";
    }
}
