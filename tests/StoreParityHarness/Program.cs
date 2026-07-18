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
Check("claim confidence normalized before storage", sqlite.Claims.All(c => c.Confidence is "verified" or "weak"));
Check("config round-trips", sqlite.ConfigValue == "L1", sqlite.ConfigValue);

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
            CompSource: "Structured"));

        await store.SaveScoreAsync(new ScoreRow(first.JobId, 4.4, 4.7, 1.0, 4.4, "{\"cv\":4.4}", "fake"));

        var profileId = await store.UpsertProfileAsync("{\"name\":\"Jordan Lee\"}");
        await store.AddClaimAsync(new ClaimRow("c0", profileId, "Title", "Senior Software Engineer", "Verified"));
        await store.AddClaimAsync(new ClaimRow("c1", profileId, "Skill", "Go", "Weak", "resume.pdf"));

        var appId = await store.CreateApplicationAsync(first.JobId, "L1");
        await store.TransitionApplicationAsync(appId, "SCREENED", "engine", "{\"to\":\"SCREENED\"}");
        await store.AppendEventAsync(new EventInput("engine", "store_parity", "job", first.JobId.ToString(), "{\"ok\":true}"));
        await store.SetConfigAsync("autonomy.level", "L1");

        return new StoreSnapshot(
            First: first,
            Second: second,
            Job: await store.GetJobAsync(first.JobId),
            ProfileId: profileId,
            Claims: (await store.GetClaimsAsync(profileId)).ToList(),
            App: await store.GetApplicationAsync(appId),
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
    JobWriteResult First,
    JobWriteResult Second,
    JobRow? Job,
    long ProfileId,
    IReadOnlyList<ClaimRow> Claims,
    ApplicationRow? App,
    IReadOnlyList<EventRow> Events,
    AuditVerification Audit,
    string? ConfigValue)
{
    public bool SameAs(StoreSnapshot other) => FirstDiff(other) is null;

    public string? FirstDiff(StoreSnapshot other)
    {
        if (First != other.First) return $"first write result: {First} != {other.First}";
        if (Second != other.Second) return $"second write result: {Second} != {other.Second}";
        if (Job != other.Job) return $"job row: {Job} != {other.Job}";
        if (ProfileId != other.ProfileId) return $"profile id: {ProfileId} != {other.ProfileId}";
        if (!Claims.SequenceEqual(other.Claims)) return "claim rows differ";
        if (App != other.App) return $"application row: {App} != {other.App}";
        if (!Events.SequenceEqual(other.Events)) return "event rows differ";
        if (Audit != other.Audit) return $"audit result: {Audit} != {other.Audit}";
        if (ConfigValue != other.ConfigValue) return $"config value: {ConfigValue} != {other.ConfigValue}";
        return null;
    }
}
