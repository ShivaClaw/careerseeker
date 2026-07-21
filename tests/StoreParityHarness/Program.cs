using SeekerSvc.Store;

int passed = 0, failed = 0;
void Check(string name, bool condition, string? detail = null)
{
    if (condition)
    {
        passed++;
        Console.WriteLine($"  PASS  {name}");
    }
    else
    {
        failed++;
        Console.WriteLine($"  FAIL  {name}{(detail is null ? "" : $"  -- {detail}")}");
    }
}

Console.WriteLine("=== CareerSeeker Store parity (InMemory <-> SQLite) ===\n");

var memory = await ExerciseAsync(clock => new InMemorySeekerStore(clock));
var sqlite = await ExerciseSqliteAsync();

Check("SQLite snapshot matches in-memory snapshot", sqlite.SameAs(memory), sqlite.FirstDiff(memory));
Check("audit chain intact in-memory", memory.Audit.Ok, memory.Audit.Reason);
Check("audit chain intact SQLite", sqlite.Audit.Ok, sqlite.Audit.Reason);
Check("repost upsert preserves existing null-coalesced comp_min", sqlite.Job?.CompMin == 170000m, sqlite.Job?.CompMin?.ToString());
Check("repost upsert refreshes apply_url", sqlite.Job?.ApplyUrl == "mailto:apply-updated@example.com", sqlite.Job?.ApplyUrl);
Check("repost upsert refreshes jd_path", sqlite.Job?.JdPath == "jd-new.txt", sqlite.Job?.JdPath);
Check("claim confidence normalized before storage", sqlite.Claims.All(c => c.Confidence is "verified" or "weak"));
Check("config round-trips", sqlite.ConfigValue == "L1", sqlite.ConfigValue);
Check("CAS refuses a wrong expected state in both stores", !sqlite.CasWrong && !memory.CasWrong);
Check("CAS succeeds on the right expected state in both stores", sqlite.CasRight && memory.CasRight);
Check("pending dispatch round-trips and deletes", sqlite.PendingSeen == "{\"job\":1}" && sqlite.PendingAfterDelete is null);
Check("effect attempt bracket persists with its reference",
    sqlite.Attempts.Count == 1 && sqlite.Attempts[0] is { Kind: "submit", Status: "SUCCEEDED", ExternalRef: "ref-1" });
Check("paused_from is durable while PAUSED and cleared on resume",
    sqlite.PausedFromSeen == "EVALUATED" && sqlite.App?.PausedFrom is null);
Check("recent application summary joins job, company, and score",
    sqlite.Summaries.Count == 1 &&
    sqlite.Summaries[0] is { JobTitle: "Senior Software Engineer", CompanyName: "Acme" } &&
    sqlite.Summaries[0].Total == 4.4);
Check("recent job summary joins job and company metadata",
    sqlite.JobSummaries.Count >= 1 &&
    sqlite.JobSummaries[0] is
    {
        Title: "Senior Software Engineer",
        CompanyName: "Acme",
        ApplyUrl: "mailto:apply-updated@example.com",
        RepostCount: 1
    });
Check("job summary lookup returns the selected job",
    sqlite.JobSummary is { JobId: var id, Title: "Senior Software Engineer" } &&
    id == sqlite.First.JobId);
Check("application artifact metadata persists into app and summary rows",
    sqlite.App is { ResumePath: "resume.pdf", CoverPath: "cover.pdf", AnswersJson: "{\"q\":\"a\"}" } &&
    sqlite.Summaries[0] is { ResumePath: "resume.pdf", CoverPath: "cover.pdf", HasAnswers: true });
Check("state-set id lookup returns the matching application in both stores",
    sqlite.IdsMatching.Count == 1 && sqlite.IdsMatching[0] == sqlite.App?.Id &&
    sqlite.IdsMatching.SequenceEqual(memory.IdsMatching),
    $"sqlite={string.Join(",", sqlite.IdsMatching)} memory={string.Join(",", memory.IdsMatching)}");
Check("state-set id lookup returns empty for a non-matching state and for empty input",
    sqlite.IdsNoneMatching.Count == 0 && sqlite.IdsEmptyInput.Count == 0 &&
    memory.IdsNoneMatching.Count == 0 && memory.IdsEmptyInput.Count == 0);

Console.WriteLine($"\n=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;

static async Task<StoreSnapshot> ExerciseSqliteAsync()
{
    var path = Path.Combine(Path.GetTempPath(), "CareerSeeker.StoreParity." + Guid.NewGuid().ToString("N") + ".db");
    try
    {
        return await ExerciseAsync(clock => SqliteSeekerStore.ForFile(path, clock));
    }
    finally
    {
        DeleteIfExists(path);
        DeleteIfExists(path + "-wal");
        DeleteIfExists(path + "-shm");
    }
}

static async Task<StoreSnapshot> ExerciseAsync(Func<Func<DateTimeOffset>, ISeekerStore> create)
{
    var clock = new StepClock();
    var store = create(clock.Now);
    try
    {
        await store.InitializeAsync();
        await store.InitializeAsync();

        var companyId = await store.UpsertCompanyAsync(new CompanyUpsert(
            AtsKind: "greenhouse",
            Handle: "acme",
            Name: "Acme",
            Domain: "acme.example"));

        var first = await store.UpsertJobAsync(companyId, new JobUpsert(
            Source: "greenhouse",
            ExternalId: "job-1",
            Url: "https://boards.greenhouse.io/acme/jobs/1",
            Title: "Senior Software Engineer",
            TitleCanon: "senior software engineer",
            DedupKey: "acme|senior software engineer",
            Remote: "Remote",
            SimHash: 42L,
            FirstSeen: "2026-07-08T12:00:00.0000000+00:00",
            ApplyUrl: "mailto:apply@example.com",
            Location: "Remote",
            CompMin: 170000m,
            CompMax: 210000m,
            CompCurrency: "USD",
            CompInterval: "Year",
            CompSource: "Structured",
            JdPath: "jd-old.txt",
            Injected: true,
            InjectionSignals: "ignore_previous_instructions"));

        var second = await store.UpsertJobAsync(companyId, new JobUpsert(
            Source: "greenhouse",
            ExternalId: "job-1",
            Url: "https://boards.greenhouse.io/acme/jobs/1-updated",
            Title: "Senior Software Engineer",
            TitleCanon: "senior software engineer",
            DedupKey: "acme|senior software engineer",
            Remote: "Remote",
            SimHash: 42L,
            FirstSeen: "2026-07-08T12:00:00.0000000+00:00",
            ApplyUrl: "mailto:apply-updated@example.com",
            Location: "Remote",
            CompMin: null,
            CompMax: 225000m,
            CompCurrency: "USD",
            CompInterval: "Year",
            CompSource: "Structured",
            JdPath: "jd-new.txt"));

        await store.SaveScoreAsync(new ScoreRow(first.JobId, 4.4, 4.7, 1.0, 4.4, "{\"cv\":4.4}", "fake"));

        var profileId = await store.UpsertProfileAsync("{\"name\":\"Jordan Lee\"}");
        await store.AddClaimAsync(new ClaimRow("c0", profileId, "Title", "Senior Software Engineer", "Verified"));
        await store.AddClaimAsync(new ClaimRow("c1", profileId, "Skill", "Go", "Weak", "resume.pdf"));

        var appId = await store.CreateApplicationAsync(first.JobId, "L1");
        await store.TransitionApplicationAsync(appId, "SCREENED", "engine", "{\"to\":\"SCREENED\"}");
        await store.AppendEventAsync(new EventInput("engine", "store_parity", "job", first.JobId.ToString(), "{\"ok\":true}"));
        await store.SetConfigAsync("autonomy.level", "L1");

        // CAS semantics: a wrong expected state is a silent no-op; a right one writes state + event.
        var casWrong = await store.TryTransitionApplicationAsync(appId, "DISCOVERED", "EVALUATED", "engine");
        var casRight = await store.TryTransitionApplicationAsync(appId, "SCREENED", "EVALUATED", "engine");

        // Durable L2 payload + side-effect attempt bracket round-trips.
        await store.SavePendingDispatchAsync(appId, "{\"job\":1}");
        var pendingSeen = await store.GetPendingDispatchAsync(appId);
        var attemptId = await store.BeginEffectAttemptAsync(appId, "submit");
        await store.ResolveEffectAttemptAsync(attemptId, "SUCCEEDED", "ref-1");
        await store.SaveApplicationArtifactsAsync(appId, "resume.pdf", "cover.pdf", "{\"q\":\"a\"}");
        var attempts = (await store.GetEffectAttemptsAsync(appId)).ToList();
        await store.DeletePendingDispatchAsync(appId);
        var pendingAfterDelete = await store.GetPendingDispatchAsync(appId);

        // paused_from is written by the pausing CAS and cleared by the resuming one.
        await store.TryTransitionApplicationAsync(appId, "EVALUATED", "PAUSED", "user", null, recordPausedFrom: "EVALUATED");
        var pausedFromSeen = (await store.GetApplicationAsync(appId))?.PausedFrom;
        await store.TryTransitionApplicationAsync(appId, "PAUSED", "EVALUATED", "user");

        // State-set lookup (the reconcile sweep's query) must agree across stores and is a pure read:
        // it consumes the deterministic clock zero times, so it cannot skew any downstream timestamp.
        var idsMatching = (await store.GetApplicationIdsInStatesAsync(new[] { "EVALUATED", "SUBMITTING" })).ToList();
        var idsNoneMatching = (await store.GetApplicationIdsInStatesAsync(new[] { "DRAFTED" })).ToList();
        var idsEmptyInput = (await store.GetApplicationIdsInStatesAsync(Array.Empty<string>())).ToList();

        return new StoreSnapshot(
            CasWrong: casWrong,
            CasRight: casRight,
            PendingSeen: pendingSeen,
            PendingAfterDelete: pendingAfterDelete,
            Attempts: attempts,
            PausedFromSeen: pausedFromSeen,
            IdsMatching: idsMatching,
            IdsNoneMatching: idsNoneMatching,
            IdsEmptyInput: idsEmptyInput,
            First: first,
            Second: second,
            Job: await store.GetJobAsync(first.JobId),
            ProfileId: profileId,
            Claims: (await store.GetClaimsAsync(profileId)).ToList(),
            App: await store.GetApplicationAsync(appId),
            JobSummary: await store.GetJobSummaryAsync(first.JobId),
            Summaries: (await store.GetRecentApplicationsAsync()).ToList(),
            JobSummaries: (await store.GetRecentJobsAsync()).ToList(),
            Events: (await store.GetEventsAsync()).ToList(),
            Audit: await store.VerifyAuditAsync(),
            ConfigValue: await store.GetConfigAsync("autonomy.level"));
    }
    finally
    {
        if (store is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
    }
}

static void DeleteIfExists(string path)
{
    try
    {
        if (File.Exists(path))
            File.Delete(path);
    }
    catch
    {
        // Best-effort cleanup only; a leaked temp DB is less useful than hiding the parity result.
    }
}

sealed class StepClock
{
    private DateTimeOffset _next = new(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);

    public DateTimeOffset Now()
    {
        var value = _next;
        _next = _next.AddSeconds(1);
        return value;
    }
}

sealed record StoreSnapshot(
    bool CasWrong,
    bool CasRight,
    string? PendingSeen,
    string? PendingAfterDelete,
    IReadOnlyList<EffectAttemptRow> Attempts,
    string? PausedFromSeen,
    IReadOnlyList<long> IdsMatching,
    IReadOnlyList<long> IdsNoneMatching,
    IReadOnlyList<long> IdsEmptyInput,
    JobWriteResult First,
    JobWriteResult Second,
    JobRow? Job,
    long ProfileId,
    IReadOnlyList<ClaimRow> Claims,
    ApplicationRow? App,
    JobSummaryRow? JobSummary,
    IReadOnlyList<ApplicationSummaryRow> Summaries,
    IReadOnlyList<JobSummaryRow> JobSummaries,
    IReadOnlyList<EventRow> Events,
    AuditVerification Audit,
    string? ConfigValue)
{
    public bool SameAs(StoreSnapshot other) => FirstDiff(other) is null;

    public string? FirstDiff(StoreSnapshot other)
    {
        if (CasWrong != other.CasWrong) return "CAS wrong-expected outcome differs";
        if (CasRight != other.CasRight) return "CAS right-expected outcome differs";
        if (PendingSeen != other.PendingSeen) return "pending dispatch payload differs";
        if (PendingAfterDelete != other.PendingAfterDelete) return "pending dispatch delete differs";
        // Exact record equality, timestamps included. The stores consume the deterministic test
        // clock identically on every path — including failed CAS attempts, which tick zero times in
        // both (see SqliteSeekerStore.TryTransitionApplicationAsync's read-validate-then-write
        // structure). Any tick asymmetry introduced by a future store change will surface here as a
        // timestamp mismatch, and in the Events comparison below as a hash mismatch. Do NOT loosen
        // this to functional-fields-only: that was tried and it only masked the first symptom of a
        // real one-tick skew while every downstream row and event hash still diverged.
        if (Attempts.Count != other.Attempts.Count)
            return $"effect attempt count: {Attempts.Count} != {other.Attempts.Count}";
        for (var i = 0; i < Attempts.Count; i++)
            if (Attempts[i] != other.Attempts[i])
                return $"attempt[{i}]: {Attempts[i]} != {other.Attempts[i]}";
        if (PausedFromSeen != other.PausedFromSeen) return "paused_from round-trip differs";
        if (!IdsMatching.SequenceEqual(other.IdsMatching)) return "state-set id lookup (matching) differs";
        if (!IdsNoneMatching.SequenceEqual(other.IdsNoneMatching)) return "state-set id lookup (no match) differs";
        if (!IdsEmptyInput.SequenceEqual(other.IdsEmptyInput)) return "state-set id lookup (empty input) differs";
        if (First != other.First) return $"first write result: {First} != {other.First}";
        if (Second != other.Second) return $"second write result: {Second} != {other.Second}";
        if (Job != other.Job) return $"job row: {Job} != {other.Job}";
        if (ProfileId != other.ProfileId) return $"profile id: {ProfileId} != {other.ProfileId}";
        if (!Claims.SequenceEqual(other.Claims)) return "claim rows differ";
        if (App != other.App) return $"application row: {App} != {other.App}";
        if (JobSummary != other.JobSummary) return $"job summary lookup: {JobSummary} != {other.JobSummary}";
        if (!Summaries.SequenceEqual(other.Summaries)) return "application summaries differ";
        if (!JobSummaries.SequenceEqual(other.JobSummaries)) return "job summaries differ";
        if (!Events.SequenceEqual(other.Events)) return "event rows differ";
        if (Audit != other.Audit) return $"audit result: {Audit} != {other.Audit}";
        if (ConfigValue != other.ConfigValue) return $"config value: {ConfigValue} != {other.ConfigValue}";
        return null;
    }
}
