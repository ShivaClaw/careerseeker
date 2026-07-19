// LifecycleHarness — deterministic proofs for lifecycle concurrency, durable recovery, and
// side-effect idempotency. Determinism note: the concurrent-approval proofs do not depend on any
// particular thread schedule; the CAS election admits exactly one winner under every interleaving,
// so the asserted outcome (one submission) is schedule-independent.
using SeekerSvc.Pipeline;
using SeekerSvc.Scorer;
using SeekerSvc.Store;
using SeekerSvc.Verifier;

int passed = 0, failed = 0;
void Check(string name, bool condition, string? detail = null)
{
    if (condition) { passed++; Console.WriteLine($"  PASS  {name}"); }
    else { failed++; Console.WriteLine($"  FAIL  {name}{(detail is null ? "" : $"  -- {detail}")}"); }
}

Console.WriteLine("=== CareerSeeker lifecycle concurrency / recovery / idempotency ===\n");

// ---------- 1. CAS semantics at the store layer ----------
{
    var store = new InMemorySeekerStore();
    var appId = await store.CreateApplicationAsync(1, "L2");
    var before = (await store.GetEventsAsync()).Count;

    var wrong = await store.TryTransitionApplicationAsync(appId, "SCREENED", "EVALUATED", "engine");
    Check("CAS with wrong expected state returns false", !wrong);
    Check("failed CAS writes no state", (await store.GetApplicationAsync(appId))!.State == "DISCOVERED");
    Check("failed CAS appends no audit event", (await store.GetEventsAsync()).Count == before);

    var right = await store.TryTransitionApplicationAsync(appId, "DISCOVERED", "SCREENED", "engine");
    Check("CAS with correct expected state succeeds", right);
    Check("successful CAS appends exactly one event", (await store.GetEventsAsync()).Count == before + 1);
    Check("audit chain intact after CAS traffic", (await store.VerifyAuditAsync()).Ok);
}

// ---------- 2. Single-winner approval under concurrency ----------
{
    var (pipe, store, disp) = await SetupL2Async();
    var appId = (await AdmitL2Async(pipe)).ApplicationId;

    const int racers = 8;
    var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var racersList = Enumerable.Range(0, racers).Select(async _ =>
    {
        await start.Task; // all racers are created and parked here before the signal fires
        try { return (State: await pipe.ResolveApplyGateAsync(appId, approve: true), Threw: false); }
        catch { return (State: await pipe.GetStateAsync(appId), Threw: true); }
    }).ToList();
    start.SetResult();
    var results = await Task.WhenAll(racersList);

    Check("exactly one submission across 8 concurrent approvals", disp.Submits == 1, $"submits={disp.Submits}");
    Check("application ends AWAITING_RESPONSE", await pipe.GetStateAsync(appId) == AppState.AWAITING_RESPONSE);
    var approvedEvents = (await store.GetEventsAsync()).Count(e => e.PayloadJson.Contains("\"to\":\"APPROVED\""));
    Check("exactly one APPROVED transition in the audit log", approvedEvents == 1, $"count={approvedEvents}");
    Check("no racer observed a stuck GATE_PENDING", results.All(r => r.State != AppState.GATE_PENDING));
}

// ---------- 3. Sequential double approval is a no-op the second time ----------
{
    var (pipe, _, disp) = await SetupL2Async();
    var appId = (await AdmitL2Async(pipe)).ApplicationId;
    var first = await pipe.ResolveApplyGateAsync(appId, approve: true);
    var second = await pipe.ResolveApplyGateAsync(appId, approve: true);
    Check("second approval submits nothing", disp.Submits == 1, $"submits={disp.Submits}");
    Check("second approval reports the settled state", first == AppState.AWAITING_RESPONSE && second == AppState.AWAITING_RESPONSE);
}

// ---------- 4. Approval survives a restart (payload is durable, not process memory) ----------
{
    var (pipeA, store, disp) = await SetupL2Async();
    var appId = (await AdmitL2Async(pipeA)).ApplicationId;
    var pipeB = new ApplicationPipeline(store, new StubTailor(), disp, new YesMatcher()); // "restarted" engine
    var state = await pipeB.ResolveApplyGateAsync(appId, approve: true);
    Check("approval after restart submits from the durable payload", disp.Submits == 1 && state == AppState.AWAITING_RESPONSE);
}

// ---------- 5. Missing payload refuses to fake an APPLIED ----------
{
    var (pipe, store, disp) = await SetupL2Async();
    var appId = (await AdmitL2Async(pipe)).ApplicationId;
    await store.DeletePendingDispatchAsync(appId);
    var threw = false;
    try { await pipe.ResolveApplyGateAsync(appId, approve: true); } catch (InvalidOperationException) { threw = true; }
    Check("approve without durable payload throws", threw);
    Check("gate remains GATE_PENDING (no election happened)", await pipe.GetStateAsync(appId) == AppState.GATE_PENDING);
    Check("nothing was submitted", disp.Submits == 0);
}

// ---------- 6. Submit failure: known no-effect, retry stays available ----------
{
    var (pipe, store, disp) = await SetupL2Async();
    var appId = (await AdmitL2Async(pipe)).ApplicationId;
    disp.FailSubmits = true;
    var threw = false;
    try { await pipe.ResolveApplyGateAsync(appId, approve: true); } catch { threw = true; }
    var attempts = await store.GetEffectAttemptsAsync(appId, "submit");
    Check("failed submission throws and stays SUBMITTING", threw && await pipe.GetStateAsync(appId) == AppState.SUBMITTING);
    Check("attempt recorded FAILED (known no-effect)", attempts.LastOrDefault()?.Status == "FAILED");
    Check("reconcile reports RetryAvailable after FAILED", await pipe.ReconcileAsync(appId) == ReconcileOutcome.RetryAvailable);
}

// ---------- 7. Crash after provider success, before local commit (the hard window) ----------
{
    var inner = new InMemorySeekerStore();
    var faulty = new FaultingStore(inner) { FailOnceOnTransitionTo = "APPLIED" };
    var disp = new CountingDispatcher();
    var pipe = new ApplicationPipeline(faulty, new StubTailor(), disp, new YesMatcher());
    await SeedProfileAsync(inner);
    var appId = (await AdmitL2Async(pipe)).ApplicationId;

    var threw = false;
    try { await pipe.ResolveApplyGateAsync(appId, approve: true); } catch { threw = true; } // "crash"
    Check("crash simulated after submit succeeded", threw && disp.Submits == 1 && await pipe.GetStateAsync(appId) == AppState.SUBMITTING);
    Check("attempt shows SUCCEEDED despite lost commit",
        (await inner.GetEffectAttemptsAsync(appId, "submit")).LastOrDefault()?.Status == "SUCCEEDED");

    var outcome = await pipe.ReconcileAsync(appId);
    Check("reconcile completes from the record, no second submit",
        outcome == ReconcileOutcome.CompletedFromRecord && disp.Submits == 1
        && await pipe.GetStateAsync(appId) == AppState.AWAITING_RESPONSE);
}

// ---------- 8. Unknown-outcome attempts block automatic resubmission ----------
{
    var (pipe, store, disp) = await SetupL2Async();
    var appId = (await AdmitL2Async(pipe)).ApplicationId;
    disp.FailSubmits = true;
    try { await pipe.ResolveApplyGateAsync(appId, approve: true); } catch { /* now SUBMITTING */ }
    await store.BeginEffectAttemptAsync(appId, "submit"); // simulate a crash mid-call: PENDING, outcome unknown
    Check("reconcile demands manual review while an attempt is PENDING",
        await pipe.ReconcileAsync(appId) == ReconcileOutcome.ManualReviewRequired);
}

// ---------- 9. L1 draft crash window ----------
{
    var inner = new InMemorySeekerStore();
    var faulty = new FaultingStore(inner) { FailOnceOnTransitionTo = "DRAFTED" };
    var disp = new CountingDispatcher();
    var pipe = new ApplicationPipeline(faulty, new StubTailor(), disp, new YesMatcher());
    await SeedProfileAsync(inner);

    var threw = false;
    try { await pipe.AdmitAsync(new PipelineJob(1, "Engineer", "Acme"), AutonomyLevel.L1, Dispatch.Act); }
    catch { threw = true; } // crash after the Gmail draft succeeded
    Check("L1 crash leaves READY with one successful draft", threw && disp.Drafts == 1);

    // Find the application (id 1: first created) and reconcile.
    Check("reconcile completes READY -> DRAFTED without a second draft",
        await pipe.ReconcileAsync(1) == ReconcileOutcome.CompletedFromRecord
        && disp.Drafts == 1 && await pipe.GetStateAsync(1) == AppState.DRAFTED);
}

// ---------- 10. Pause / resume survive a restart ----------
{
    var (pipeA, store, disp) = await SetupL2Async();
    var appId = (await AdmitL2Async(pipeA)).ApplicationId;
    await pipeA.PauseAsync(appId);
    Check("pause persists the origin state on the row", (await store.GetApplicationAsync(appId))!.PausedFrom == "GATE_PENDING");

    var pipeB = new ApplicationPipeline(store, new StubTailor(), disp, new YesMatcher()); // restart
    var resumed = await pipeB.ResumeAsync(appId);
    Check("resume after restart restores the prior state", resumed == AppState.GATE_PENDING
        && await pipeB.GetStateAsync(appId) == AppState.GATE_PENDING);
    Check("resume clears the durable paused_from", (await store.GetApplicationAsync(appId))!.PausedFrom is null);
}

// ---------- 11. Kill wins, and a killed gate never submits ----------
{
    var (pipe, _, disp) = await SetupL2Async();
    var appId = (await AdmitL2Async(pipe)).ApplicationId;
    await pipe.KillAsync(appId);
    var state = await pipe.ResolveApplyGateAsync(appId, approve: true);
    Check("approval after kill reports USER_KILLED and submits nothing",
        state == AppState.USER_KILLED && disp.Submits == 0);
}

// ---------- 12. Expire vs approve: the earlier resolution wins ----------
{
    var (pipe, _, disp) = await SetupL2Async();
    var appId = (await AdmitL2Async(pipe)).ApplicationId;
    await pipe.ExpireGateAsync(appId);
    var state = await pipe.ResolveApplyGateAsync(appId, approve: true);
    Check("approval after expiry reports GATE_EXPIRED and submits nothing",
        state == AppState.GATE_EXPIRED && disp.Submits == 0);
    await pipe.ExpireGateAsync(appId); // second expiry: race-tolerant no-op
    Check("repeated expiry is a harmless no-op", await pipe.GetStateAsync(appId) == AppState.GATE_EXPIRED);
}

// ---------- 13. SQLite-backed: durable L2 payload and single-winner election on the REAL store ----------
// The in-memory store shares the pipeline's process; only this section proves the durable payload,
// CAS election, and effect-attempt bracket actually survive through real SQLite. Skipped (not failed)
// where the native SQLite provider cannot load, so offline/sandbox runs stay meaningful.
{
    var dbPath = Path.Combine(Path.GetTempPath(), "CareerSeeker.Lifecycle." + Guid.NewGuid().ToString("N") + ".db");
    SqliteSeekerStore? store = null;
    try
    {
        store = SqliteSeekerStore.ForFile(dbPath);
        await store.InitializeAsync();
        await store.UpsertProfileAsync("{\"name\":\"Jordan Lee\"}");
        await store.AddClaimAsync(new ClaimRow("c0", 1, "Skill", "Experienced engineer", "verified"));

        // Real SQLite enforces applications.job_id -> jobs.id (the in-memory store does not),
        // so this section must create a legitimate company + job and use the returned id.
        var companyId = await store.UpsertCompanyAsync(new CompanyUpsert(
            AtsKind: "greenhouse", Handle: "acme", Name: "Acme", Domain: "acme.example"));
        var seeded = await store.UpsertJobAsync(companyId, new JobUpsert(
            Source: "greenhouse",
            ExternalId: "lifecycle-13",
            Url: "https://boards.greenhouse.io/acme/jobs/13",
            Title: "Engineer",
            TitleCanon: "engineer",
            DedupKey: "acme|engineer",
            Remote: "Remote",
            SimHash: 13L,
            FirstSeen: "2026-07-08T12:00:00.0000000+00:00",
            ApplyUrl: "mailto:apply@acme.example",
            Location: "Remote"));

        var disp = new CountingDispatcher();
        var pipeA = new ApplicationPipeline(store, new StubTailor(), disp, new YesMatcher());
        var appId = (await pipeA.AdmitAsync(
            new PipelineJob(seeded.JobId, "Engineer", "Acme"), AutonomyLevel.L2, Dispatch.Act)).ApplicationId;
        Check("sqlite: gate is pending with a durable payload",
            await pipeA.GetStateAsync(appId) == AppState.GATE_PENDING
            && await store.GetPendingDispatchAsync(appId) is not null);

        // "Restart": a fresh pipeline over the same database, holding no process state.
        var pipeB = new ApplicationPipeline(store, new StubTailor(), disp, new YesMatcher());
        var racers = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try { return await pipeB.ResolveApplyGateAsync(appId, approve: true); }
            catch { return await pipeB.GetStateAsync(appId); }
        }));
        Check("sqlite: exactly one submission across 4 concurrent post-restart approvals",
            disp.Submits == 1, $"submits={disp.Submits}");
        Check("sqlite: application settled at AWAITING_RESPONSE",
            await pipeB.GetStateAsync(appId) == AppState.AWAITING_RESPONSE && racers.All(s => s != AppState.GATE_PENDING));
        Check("sqlite: submit attempt persisted as SUCCEEDED with its provider reference",
            (await store.GetEffectAttemptsAsync(appId, "submit")).LastOrDefault() is { Status: "SUCCEEDED", ExternalRef: not null });
        Check("sqlite: pending payload cleared after the gate settled",
            await store.GetPendingDispatchAsync(appId) is null);
        Check("sqlite: audit chain intact across CAS + attempt traffic", (await store.VerifyAuditAsync()).Ok);
    }
    catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException
                               or EntryPointNotFoundException or NotImplementedException
                               or PlatformNotSupportedException)
    {
        // Provider genuinely unavailable (or stubbed in a sandbox). Skip loudly rather than fail —
        // this section is additive proof on the real store, and a missing provider is an
        // environment fact, not a defect. Note the narrow filter: assertion failures inside the
        // section still register through Check() and are never swallowed here.
        Console.WriteLine($"  SKIP  sqlite-backed lifecycle section (SQLite provider unavailable: {ex.GetType().Name})");
        Console.WriteLine("        Run this harness on Windows with the real Microsoft.Data.Sqlite package for full coverage.");
    }
    finally
    {
        if (store is not null)
            try { await store.DisposeAsync(); } catch { /* best-effort: never let cleanup mask results */ }
        foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            try { if (File.Exists(p)) File.Delete(p); } catch { /* best-effort cleanup */ }
    }
}

Console.WriteLine($"\n=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;

// ---------- helpers ----------
static async Task SeedProfileAsync(ISeekerStore store)
{
    await store.InitializeAsync();
    await store.UpsertProfileAsync("{\"name\":\"Jordan Lee\"}");
    await store.AddClaimAsync(new ClaimRow("c0", 1, "Skill", "Experienced engineer", "verified"));
}

static async Task<(ApplicationPipeline Pipe, InMemorySeekerStore Store, CountingDispatcher Disp)> SetupL2Async()
{
    var store = new InMemorySeekerStore();
    await SeedProfileAsync(store);
    var disp = new CountingDispatcher();
    return (new ApplicationPipeline(store, new StubTailor(), disp, new YesMatcher()), store, disp);
}

static Task<AdmitResult> AdmitL2Async(ApplicationPipeline pipe)
    => pipe.AdmitAsync(new PipelineJob(1, "Engineer", "Acme"), AutonomyLevel.L2, Dispatch.Act);

sealed class StubTailor : ITailor
{
    public Task<TailoredApplication> TailorAsync(PipelineJob job, IReadOnlyList<SourceClaim> profile,
        IReadOnlyList<Violation> priorViolations, CancellationToken ct = default)
        => Task.FromResult(new TailoredApplication(
            new[] { new TailoredClaim(ClaimKind.Other, "Experienced engineer", "Experienced engineer") },
            ResumeText: "Experienced engineer",
            CoverText: "Experienced engineer",
            Answers: new Dictionary<string, string>()));
}

sealed class YesMatcher : ISemanticMatcher
{
    public Task<SemanticMatchResult> EntailsAsync(string sourceText, string tailoredText, CancellationToken ct = default)
        => Task.FromResult(SemanticMatchResult.Supported());
}

sealed class CountingDispatcher : IDispatcher
{
    private int _drafts, _submits;
    public int Drafts => Volatile.Read(ref _drafts);
    public int Submits => Volatile.Read(ref _submits);
    public bool FailSubmits { get; set; }

    public Task<DispatchOutcome> CreateDraftAsync(PipelineJob job, TailoredApplication app, CancellationToken ct = default)
    {
        var n = Interlocked.Increment(ref _drafts);
        return Task.FromResult(new DispatchOutcome(true, DispatchChannel.Email, $"draft-{n}"));
    }

    public Task<DispatchOutcome> SubmitAsync(PipelineJob job, TailoredApplication app, CancellationToken ct = default)
    {
        if (FailSubmits) throw new InvalidOperationException("simulated provider failure (known: nothing submitted)");
        var n = Interlocked.Increment(ref _submits);
        return Task.FromResult(new DispatchOutcome(true, DispatchChannel.AtsForm, $"submit-{n}"));
    }
}

/// <summary>Delegating store that throws once on a chosen CAS target — simulates a crash between an
/// external effect succeeding and the local state commit.</summary>
sealed class FaultingStore : ISeekerStore
{
    private readonly ISeekerStore _inner;
    private int _armed = 1;
    public string? FailOnceOnTransitionTo { get; set; }

    public FaultingStore(ISeekerStore inner) => _inner = inner;

    public Task<bool> TryTransitionApplicationAsync(long applicationId, string expectedState, string newState,
        string actor, string? payloadJson = null, string? recordPausedFrom = null, CancellationToken ct = default)
    {
        if (newState == FailOnceOnTransitionTo && Interlocked.Exchange(ref _armed, 0) == 1)
            throw new IOException("simulated process death before the state commit");
        return _inner.TryTransitionApplicationAsync(applicationId, expectedState, newState, actor, payloadJson, recordPausedFrom, ct);
    }

    public Task InitializeAsync(CancellationToken ct = default) => _inner.InitializeAsync(ct);
    public Task<long> UpsertCompanyAsync(CompanyUpsert company, CancellationToken ct = default) => _inner.UpsertCompanyAsync(company, ct);
    public Task<JobWriteResult> UpsertJobAsync(long companyId, JobUpsert job, CancellationToken ct = default) => _inner.UpsertJobAsync(companyId, job, ct);
    public Task<JobRow?> GetJobAsync(long jobId, CancellationToken ct = default) => _inner.GetJobAsync(jobId, ct);
    public Task SaveScoreAsync(ScoreRow score, CancellationToken ct = default) => _inner.SaveScoreAsync(score, ct);
    public Task<long> CreateApplicationAsync(long jobId, string autonomyLevel, CancellationToken ct = default) => _inner.CreateApplicationAsync(jobId, autonomyLevel, ct);
    public Task TransitionApplicationAsync(long applicationId, string newState, string actor, string? payloadJson = null, CancellationToken ct = default)
        => _inner.TransitionApplicationAsync(applicationId, newState, actor, payloadJson, ct);
    public Task<ApplicationRow?> GetApplicationAsync(long applicationId, CancellationToken ct = default) => _inner.GetApplicationAsync(applicationId, ct);
    public Task<long> AppendEventAsync(EventInput e, CancellationToken ct = default) => _inner.AppendEventAsync(e, ct);
    public Task<IReadOnlyList<EventRow>> GetEventsAsync(CancellationToken ct = default) => _inner.GetEventsAsync(ct);
    public Task<AuditVerification> VerifyAuditAsync(CancellationToken ct = default) => _inner.VerifyAuditAsync(ct);
    public Task<long> UpsertProfileAsync(string json, CancellationToken ct = default) => _inner.UpsertProfileAsync(json, ct);
    public Task AddClaimAsync(ClaimRow claim, CancellationToken ct = default) => _inner.AddClaimAsync(claim, ct);
    public Task<IReadOnlyList<ClaimRow>> GetClaimsAsync(long profileId, CancellationToken ct = default) => _inner.GetClaimsAsync(profileId, ct);
    public Task<string?> GetConfigAsync(string key, CancellationToken ct = default) => _inner.GetConfigAsync(key, ct);
    public Task SetConfigAsync(string key, string value, CancellationToken ct = default) => _inner.SetConfigAsync(key, value, ct);
    public Task SavePendingDispatchAsync(long applicationId, string payloadJson, CancellationToken ct = default) => _inner.SavePendingDispatchAsync(applicationId, payloadJson, ct);
    public Task<string?> GetPendingDispatchAsync(long applicationId, CancellationToken ct = default) => _inner.GetPendingDispatchAsync(applicationId, ct);
    public Task DeletePendingDispatchAsync(long applicationId, CancellationToken ct = default) => _inner.DeletePendingDispatchAsync(applicationId, ct);
    public Task<long> BeginEffectAttemptAsync(long applicationId, string kind, CancellationToken ct = default) => _inner.BeginEffectAttemptAsync(applicationId, kind, ct);
    public Task ResolveEffectAttemptAsync(long attemptId, string status, string? externalRef = null, CancellationToken ct = default) => _inner.ResolveEffectAttemptAsync(attemptId, status, externalRef, ct);
    public Task<IReadOnlyList<EffectAttemptRow>> GetEffectAttemptsAsync(long applicationId, string? kind = null, CancellationToken ct = default) => _inner.GetEffectAttemptsAsync(applicationId, kind, ct);
}
