using Microsoft.Data.Sqlite;
using SeekerSvc.Store;

var realMigrationSources = ReadOptionValues(args, "--migration-copy");
if (realMigrationSources.Count > 0)
    return await ExerciseRealMigrationCopiesAsync(realMigrationSources);

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
Check("job summary exposes persisted score components and ranker identity",
    sqlite.JobSummary is
    {
        Fit: 4.4,
        Legitimacy: 4.7,
        Total: 4.4,
        SubscoresJson: "{\"cv\":4.4}",
        ModelUsed: "fake"
    });
Check("application artifact metadata persists into app and summary rows",
    sqlite.App is { ResumePath: "resume.pdf", CoverPath: "cover.pdf", AnswersJson: "{\"q\":\"a\"}" } &&
    sqlite.Summaries[0] is { ResumePath: "resume.pdf", CoverPath: "cover.pdf", HasAnswers: true });
Check("per-job application lookup is false before creation and true after in both stores",
    !sqlite.HasApplicationBefore && sqlite.HasApplicationAfter &&
    !memory.HasApplicationBefore && memory.HasApplicationAfter);
Check("state-set id lookup returns the matching application in both stores",
    sqlite.IdsMatching.Count == 1 && sqlite.IdsMatching[0] == sqlite.App?.Id &&
    sqlite.IdsMatching.SequenceEqual(memory.IdsMatching),
    $"sqlite={string.Join(",", sqlite.IdsMatching)} memory={string.Join(",", memory.IdsMatching)}");
Check("state-set id lookup returns empty for a non-matching state and for empty input",
    sqlite.IdsNoneMatching.Count == 0 && sqlite.IdsEmptyInput.Count == 0 &&
    memory.IdsNoneMatching.Count == 0 && memory.IdsEmptyInput.Count == 0);
Check("cycle telemetry round-trips trusted aggregate metadata",
    sqlite.Cycles.Count == 1 &&
    sqlite.Cycles[0] is { Discovered: 12, Quarantined: 2, Rejected: 3, Drafted: 1, Errors: 0 } &&
    sqlite.Cycles[0].BoardsJson == "[\"greenhouse:acme\"]" &&
    sqlite.Cycles[0].QuarantineReasonsJson == "{\"ignore_previous_instructions\":2}");

// Migration (L1): a DB created by an older schema — missing paused_from / resume_path / cover_path /
// answers_json — must be brought current on open, without dropping the pre-existing row, and the new
// columns must be writable. Presence is checked via PRAGMA, so a fresh open no longer throws+swallows.
var migration = await ExerciseMigrationAsync();
Check("migration adds the four artifact/paused_from columns to a pre-existing old-schema DB",
    migration.MigratedColumns, migration.ColumnList);
Check("migration is idempotent and preserves the pre-existing application row and its state",
    migration.PreExistingState == "SCREENED", migration.PreExistingState);
Check("artifact columns are writable after migration (round-trip through the store)",
    migration.RoundTrip is { ResumePath: "r.pdf", CoverPath: "c.pdf", AnswersJson: "{\"q\":\"a\"}" },
    $"{migration.RoundTrip?.ResumePath}/{migration.RoundTrip?.CoverPath}/{migration.RoundTrip?.AnswersJson}");

Console.WriteLine($"\n=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;

// Seeds a file DB whose `applications` table predates the four migrated columns, opens it through the
// store twice (proving idempotency), and confirms the columns arrive, the old row survives, and the
// new columns round-trip.
static async Task<MigrationResult> ExerciseMigrationAsync()
{
    var path = Path.Combine(Path.GetTempPath(), "CareerSeeker.Migration." + Guid.NewGuid().ToString("N") + ".db");
    try
    {
        var connStr = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        await using (var seed = new SqliteConnection(connStr))
        {
            await seed.OpenAsync();
            // Old schema: no paused_from / resume_path / cover_path / answers_json, and no FK (the
            // migration only adds columns; runtime FK enforcement is exercised by the parity path).
            using (var ddl = seed.CreateCommand())
            {
                ddl.CommandText = @"
CREATE TABLE applications (
  id             INTEGER PRIMARY KEY AUTOINCREMENT,
  job_id         INTEGER NOT NULL,
  state          TEXT    NOT NULL,
  autonomy_level TEXT    NOT NULL DEFAULT 'L1',
  gate_id        INTEGER,
  channel        TEXT,
  submitted_at   TEXT,
  created_at     TEXT    NOT NULL,
  updated_at     TEXT    NOT NULL
);";
                await ddl.ExecuteNonQueryAsync();
            }
            using (var ins = seed.CreateCommand())
            {
                ins.CommandText = @"INSERT INTO applications (job_id, state, autonomy_level, created_at, updated_at)
VALUES (1, 'SCREENED', 'L1', '2026-07-01T00:00:00.0000000+00:00', '2026-07-01T00:00:00.0000000+00:00');";
                await ins.ExecuteNonQueryAsync();
            }
        }

        await using var store = SqliteSeekerStore.ForFile(path);
        await store.InitializeAsync();
        await store.InitializeAsync(); // idempotent: no duplicate-column throw on the second open

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var check = new SqliteConnection(connStr))
        {
            await check.OpenAsync();
            using var info = check.CreateCommand();
            info.CommandText = "PRAGMA table_info(applications);";
            using var r = await info.ExecuteReaderAsync();
            while (await r.ReadAsync()) columns.Add(r.GetString(1));
        }
        var wanted = new[] { "paused_from", "resume_path", "cover_path", "answers_json" };

        const long preExistingId = 1;
        var preserved = await store.GetApplicationAsync(preExistingId);
        await store.SaveApplicationArtifactsAsync(preExistingId, "r.pdf", "c.pdf", "{\"q\":\"a\"}");
        var roundTrip = await store.GetApplicationAsync(preExistingId);

        return new MigrationResult(
            MigratedColumns: wanted.All(columns.Contains),
            ColumnList: string.Join(",", columns.OrderBy(c => c)),
            PreExistingState: preserved?.State,
            RoundTrip: roundTrip);
    }
    finally
    {
        DeleteIfExists(path);
        DeleteIfExists(path + "-wal");
        DeleteIfExists(path + "-shm");
    }
}

// Copies each existing database through SQLite's read-only backup API, runs current initialization twice on
// the copy, and compares structural facts only. Source values and paths are never printed, and the source
// file's bytes/metadata must remain unchanged.
static async Task<int> ExerciseRealMigrationCopiesAsync(IReadOnlyList<string> sources)
{
    Console.WriteLine("=== CareerSeeker real Alpha DB migration-copy matrix ===\n");
    var failed = 0;

    for (var index = 0; index < sources.Count; index++)
    {
        var sourcePath = Path.GetFullPath(sources[index]);
        var label = $"candidate {index + 1}";
        var tempRoot = Path.Combine(Path.GetTempPath(), "CareerSeeker.RealMigration." + Guid.NewGuid().ToString("N"));
        var copyPath = Path.Combine(tempRoot, "migration-copy.db");

        try
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("Migration source does not exist.", sourcePath);

            var sourceInfo = new FileInfo(sourcePath);
            var sourceLength = sourceInfo.Length;
            var sourceWriteUtc = sourceInfo.LastWriteTimeUtc;
            var sourceHashBefore = await HashFileAsync(sourcePath);

            Directory.CreateDirectory(tempRoot);
            var sourceBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = sourcePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private
            };
            var copyBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = copyPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private
            };

            await using (var source = new SqliteConnection(sourceBuilder.ToString()))
            await using (var destination = new SqliteConnection(copyBuilder.ToString()))
            {
                await source.OpenAsync();
                await destination.OpenAsync();
                source.BackupDatabase(destination);
            }

            var before = await ReadMigrationCopySnapshotAsync(copyPath);
            await using (var store = SqliteSeekerStore.ForFile(copyPath))
            {
                await store.InitializeAsync();
                await store.InitializeAsync();
            }
            var after = await ReadMigrationCopySnapshotAsync(copyPath);

            var wanted = new[] { "paused_from", "resume_path", "cover_path", "answers_json" };
            var existingTablesPreserved =
                before.RowCounts.All(pair =>
                    after.RowCounts.TryGetValue(pair.Key, out var afterCount) && afterCount == pair.Value);

            var sourceInfoAfter = new FileInfo(sourcePath);
            var sourceHashAfter = await HashFileAsync(sourcePath);
            var passed =
                before.IntegrityOk &&
                after.IntegrityOk &&
                existingTablesPreserved &&
                wanted.All(after.ApplicationColumns.Contains) &&
                sourceLength == sourceInfoAfter.Length &&
                sourceWriteUtc == sourceInfoAfter.LastWriteTimeUtc &&
                sourceHashBefore.SequenceEqual(sourceHashAfter);

            if (passed)
            {
                Console.WriteLine($"  PASS  {label}: copied migration is intact/idempotent and source is unchanged");
            }
            else
            {
                failed++;
                Console.WriteLine($"  FAIL  {label}: structural migration invariant failed");
            }
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine($"  FAIL  {label}: {ex.GetType().Name}; source details suppressed");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(copyPath);
            DeleteIfExists(copyPath + "-wal");
            DeleteIfExists(copyPath + "-shm");
            if (Directory.Exists(tempRoot))
            {
                var systemTemp = Path.GetFullPath(Path.GetTempPath())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                var resolvedTempRoot = Path.GetFullPath(tempRoot);
                if (!resolvedTempRoot.StartsWith(systemTemp, StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(resolvedTempRoot).StartsWith(
                        "CareerSeeker.RealMigration.",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Refusing to recursively remove an unexpected migration temp path.");
                }
                Directory.Delete(resolvedTempRoot, recursive: true);
            }
        }
    }

    Console.WriteLine($"\n=== {sources.Count - failed} passed, {failed} failed ===");
    return failed == 0 ? 0 : 1;
}

static async Task<MigrationCopySnapshot> ReadMigrationCopySnapshotAsync(string path)
{
    var builder = new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadOnly,
        Cache = SqliteCacheMode.Private
    };
    await using var connection = new SqliteConnection(builder.ToString());
    await connection.OpenAsync();

    string integrity;
    using (var command = connection.CreateCommand())
    {
        command.CommandText = "PRAGMA integrity_check;";
        integrity = Convert.ToString(await command.ExecuteScalarAsync()) ?? "";
    }

    var tables = new List<string>();
    using (var command = connection.CreateCommand())
    {
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));
    }

    var rowCounts = new Dictionary<string, long>(StringComparer.Ordinal);
    foreach (var table in tables)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table.Replace("\"", "\"\"")}\";";
        rowCounts[table] = Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (tables.Contains("applications", StringComparer.Ordinal))
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(applications);";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));
    }

    return new MigrationCopySnapshot(
        IntegrityOk: string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase),
        RowCounts: rowCounts,
        ApplicationColumns: columns);
}

static async Task<byte[]> HashFileAsync(string path)
{
    await using var stream = new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 64 * 1024,
        useAsync: true);
    return await System.Security.Cryptography.SHA256.HashDataAsync(stream);
}

static IReadOnlyList<string> ReadOptionValues(string[] arguments, string option)
{
    var values = new List<string>();
    for (var index = 0; index < arguments.Length; index++)
    {
        if (!string.Equals(arguments[index], option, StringComparison.OrdinalIgnoreCase))
            continue;
        if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{option} requires a database path.");
        values.Add(arguments[++index]);
    }
    return values;
}

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

        var hasApplicationBefore = await store.HasApplicationForJobAsync(first.JobId);
        var appId = await store.CreateApplicationAsync(first.JobId, "L1");
        var hasApplicationAfter = await store.HasApplicationForJobAsync(first.JobId);
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
        await store.SaveCycleTelemetryAsync(new CycleTelemetryInput(
            "2026-07-08T13:00:00.0000000+00:00",
            "2026-07-08T13:00:05.0000000+00:00",
            12,
            2,
            3,
            1,
            0,
            "[\"greenhouse:acme\"]",
            "{\"ignore_previous_instructions\":2}"));

        return new StoreSnapshot(
            CasWrong: casWrong,
            CasRight: casRight,
            PendingSeen: pendingSeen,
            PendingAfterDelete: pendingAfterDelete,
            Attempts: attempts,
            PausedFromSeen: pausedFromSeen,
            HasApplicationBefore: hasApplicationBefore,
            HasApplicationAfter: hasApplicationAfter,
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
            Cycles: (await store.GetRecentCycleTelemetryAsync()).ToList(),
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

sealed record MigrationResult(
    bool MigratedColumns,
    string ColumnList,
    string? PreExistingState,
    ApplicationRow? RoundTrip);

sealed record MigrationCopySnapshot(
    bool IntegrityOk,
    IReadOnlyDictionary<string, long> RowCounts,
    IReadOnlySet<string> ApplicationColumns);

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
    bool HasApplicationBefore,
    bool HasApplicationAfter,
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
    IReadOnlyList<CycleTelemetryRow> Cycles,
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
        if (HasApplicationBefore != other.HasApplicationBefore) return "pre-create job application lookup differs";
        if (HasApplicationAfter != other.HasApplicationAfter) return "post-create job application lookup differs";
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
        if (!Cycles.SequenceEqual(other.Cycles)) return "cycle telemetry differs";
        if (!Events.SequenceEqual(other.Events)) return "event rows differ";
        if (Audit != other.Audit) return $"audit result: {Audit} != {other.Audit}";
        if (ConfigValue != other.ConfigValue) return $"config value: {ConfigValue} != {other.ConfigValue}";
        return null;
    }
}
