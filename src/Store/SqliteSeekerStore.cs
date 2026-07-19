using Microsoft.Data.Sqlite;

namespace SeekerSvc.Store;

/// <summary>
/// Production <see cref="ISeekerStore"/> backed by SQLite via Microsoft.Data.Sqlite. Holds one
/// connection (WAL mode), and serializes every operation behind an async mutex: SQLite has a single
/// writer, and — more importantly — the audit chain must be strictly sequential, so appends compute
/// <c>seq = MAX(seq)+1</c> and the row hash under the same lock and transaction that inserts the row.
/// Application state changes commit their state row and their audit event in one transaction, so the
/// data and the tamper-evident record can never drift apart.
///
/// The SQL here is validated against the sqlite3 CLI; this file compiles where the
/// Microsoft.Data.Sqlite package can be restored.
/// </summary>
public sealed class SqliteSeekerStore : ISeekerStore, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private SqliteConnection? _conn;

    public SqliteSeekerStore(string connectionString, Func<DateTimeOffset>? clock = null)
    {
        _connectionString = connectionString;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Convenience: open a file-backed store at the given path.</summary>
    public static SqliteSeekerStore ForFile(string path, Func<DateTimeOffset>? clock = null)
        => new(new SqliteConnectionStringBuilder { DataSource = path }.ToString(), clock);

    private string Now() => _clock().ToString("O");

    private static string JsonBool(string? value) => string.IsNullOrWhiteSpace(value) ? "false" : "true";

    private SqliteConnection Conn => _conn ?? throw new InvalidOperationException("Call InitializeAsync first.");

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_conn is not null) return;
            var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            foreach (var pragma in Schema.Pragmas)
                await ExecAsync(conn, pragma, ct).ConfigureAwait(false);
            await ExecAsync(conn, Schema.Ddl, ct).ConfigureAwait(false);
            try { await ExecAsync(conn, "ALTER TABLE applications ADD COLUMN paused_from TEXT;", ct).ConfigureAwait(false); }
            catch (SqliteException) { /* column already exists (fresh DDL or prior migration) */ }
            try { await ExecAsync(conn, "ALTER TABLE applications ADD COLUMN resume_path TEXT;", ct).ConfigureAwait(false); }
            catch (SqliteException) { /* column already exists (fresh DDL or prior migration) */ }
            try { await ExecAsync(conn, "ALTER TABLE applications ADD COLUMN cover_path TEXT;", ct).ConfigureAwait(false); }
            catch (SqliteException) { /* column already exists (fresh DDL or prior migration) */ }
            try { await ExecAsync(conn, "ALTER TABLE applications ADD COLUMN answers_json TEXT;", ct).ConfigureAwait(false); }
            catch (SqliteException) { /* column already exists (fresh DDL or prior migration) */ }
            _conn = conn;
        }
        finally { _mutex.Release(); }
    }

    public Task<long> UpsertCompanyAsync(CompanyUpsert company, CancellationToken ct = default)
        => Locked(async () =>
        {
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO companies (name, domain, ats_kind, ats_handle)
VALUES ($name, $domain, $ats, $handle)
ON CONFLICT(ats_kind, ats_handle) DO UPDATE SET
  name   = COALESCE(excluded.name, companies.name),
  domain = COALESCE(excluded.domain, companies.domain)
RETURNING id;";
            P(cmd, "$name", company.Name);
            P(cmd, "$domain", company.Domain);
            P(cmd, "$ats", company.AtsKind);
            P(cmd, "$handle", company.Handle);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }, ct);

    public Task<JobWriteResult> UpsertJobAsync(long companyId, JobUpsert job, CancellationToken ct = default)
        => Locked(async () =>
        {
            var now = Now();
            var firstSeen = string.IsNullOrWhiteSpace(job.FirstSeen) ? now : job.FirstSeen;
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO jobs (company_id, source, external_id, url, apply_url, title, title_canon, dedup_key,
                  location, remote, comp_min, comp_max, comp_currency, comp_interval, comp_source,
                  jd_path, simhash, injected, injection_signals, first_seen, last_verified, repost_count)
VALUES ($company, $source, $ext, $url, $apply, $title, $canon, $dedup,
        $loc, $remote, $cmin, $cmax, $ccur, $cint, $csrc,
        $jd, $simhash, $injected, $signals, $first, $last, 0)
ON CONFLICT(source, external_id) DO UPDATE SET
  last_verified = excluded.last_verified,
  repost_count  = jobs.repost_count + 1,
  url           = excluded.url,
  apply_url     = excluded.apply_url,
  comp_min      = COALESCE(excluded.comp_min, jobs.comp_min),
  comp_max      = COALESCE(excluded.comp_max, jobs.comp_max)
RETURNING id, repost_count;";
            P(cmd, "$company", companyId);
            P(cmd, "$source", job.Source);
            P(cmd, "$ext", job.ExternalId);
            P(cmd, "$url", job.Url);
            P(cmd, "$apply", job.ApplyUrl);
            P(cmd, "$title", job.Title);
            P(cmd, "$canon", job.TitleCanon);
            P(cmd, "$dedup", job.DedupKey);
            P(cmd, "$loc", job.Location);
            P(cmd, "$remote", job.Remote);
            P(cmd, "$cmin", (double?)job.CompMin);
            P(cmd, "$cmax", (double?)job.CompMax);
            P(cmd, "$ccur", job.CompCurrency);
            P(cmd, "$cint", job.CompInterval);
            P(cmd, "$csrc", job.CompSource);
            P(cmd, "$jd", job.JdPath);
            P(cmd, "$simhash", job.SimHash);
            P(cmd, "$injected", job.Injected ? 1 : 0);
            P(cmd, "$signals", job.InjectionSignals);
            P(cmd, "$first", firstSeen);
            P(cmd, "$last", now);

            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);
            var id = reader.GetInt64(0);
            var repost = reader.GetInt32(1);
            return new JobWriteResult(id, repost == 0, repost);
        }, ct);

    public Task<JobRow?> GetJobAsync(long jobId, CancellationToken ct = default)
        => Locked(async () =>
        {
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM jobs WHERE id = $id;";
            P(cmd, "$id", jobId);
            using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await r.ReadAsync(ct).ConfigureAwait(false) ? MapJob(r) : null;
        }, ct);

    public Task<IReadOnlyList<JobSummaryRow>> GetRecentJobsAsync(
        int limit = 25,
        CancellationToken ct = default)
        => Locked(async () =>
        {
            var safeLimit = Math.Clamp(limit, 1, 100);
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = @"
SELECT
  j.id, j.source, j.external_id, j.title, c.name, c.domain, j.remote, j.location,
  j.url, j.apply_url, j.comp_min, j.comp_max, j.comp_currency, j.comp_interval,
  j.comp_source, j.injected, j.injection_signals, j.last_verified, j.repost_count
FROM jobs j
LEFT JOIN companies c ON c.id = j.company_id
ORDER BY j.last_verified DESC, j.id DESC
LIMIT $limit;";
            P(cmd, "$limit", safeLimit);

            var rows = new List<JobSummaryRow>();
            using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
                decimal? D(int i) => r.IsDBNull(i) ? null : (decimal)r.GetDouble(i);
                rows.Add(new JobSummaryRow(
                    r.GetInt64(0),
                    r.GetString(1),
                    r.GetString(2),
                    r.GetString(3),
                    S(4),
                    S(5),
                    r.GetString(6),
                    S(7),
                    r.GetString(8),
                    S(9),
                    D(10),
                    D(11),
                    S(12),
                    S(13),
                    S(14),
                    r.GetInt64(15) != 0,
                    S(16),
                    r.GetString(17),
                    r.GetInt32(18)));
            }
            return (IReadOnlyList<JobSummaryRow>)rows;
        }, ct);

    public Task SaveScoreAsync(ScoreRow s, CancellationToken ct = default)
        => Locked(async () =>
        {
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO scores (job_id, fit, legitimacy, red_flag_mult, total, subscores_json, scored_at, model_used)
VALUES ($job, $fit, $legit, $rf, $total, $sub, $at, $model)
ON CONFLICT(job_id) DO UPDATE SET
  fit=excluded.fit, legitimacy=excluded.legitimacy, red_flag_mult=excluded.red_flag_mult,
  total=excluded.total, subscores_json=excluded.subscores_json, scored_at=excluded.scored_at,
  model_used=excluded.model_used;";
            P(cmd, "$job", s.JobId);
            P(cmd, "$fit", s.Fit);
            P(cmd, "$legit", s.Legitimacy);
            P(cmd, "$rf", s.RedFlagMult);
            P(cmd, "$total", s.Total);
            P(cmd, "$sub", s.SubscoresJson);
            P(cmd, "$at", Now());
            P(cmd, "$model", s.ModelUsed);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return true;
        }, ct);

    public Task<long> CreateApplicationAsync(long jobId, string autonomyLevel, CancellationToken ct = default)
        => Locked(async () =>
        {
            var now = Now();
            using var tx = (SqliteTransaction)await Conn.BeginTransactionAsync(ct).ConfigureAwait(false);

            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO applications (job_id, state, autonomy_level, created_at, updated_at)
VALUES ($job, 'DISCOVERED', $auto, $now, $now) RETURNING id;";
            P(cmd, "$job", jobId);
            P(cmd, "$auto", autonomyLevel);
            P(cmd, "$now", now);
            var appId = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));

            await AppendEventTxAsync(tx, new EventInput("engine", "state_change", "application",
                appId.ToString(), $"{{\"to\":\"DISCOVERED\",\"job_id\":{jobId}}}"), ct).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return appId;
        }, ct);

    public Task TransitionApplicationAsync(long applicationId, string newState, string actor,
        string? payloadJson = null, CancellationToken ct = default)
        => Locked(async () =>
        {
            var now = Now();
            using var tx = (SqliteTransaction)await Conn.BeginTransactionAsync(ct).ConfigureAwait(false);

            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE applications SET state=$state, paused_from=NULL, updated_at=$now WHERE id=$id;";
            P(cmd, "$state", newState);
            P(cmd, "$now", now);
            P(cmd, "$id", applicationId);
            var changed = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            if (changed == 0) throw new InvalidOperationException($"No application {applicationId}");

            await AppendEventTxAsync(tx, new EventInput(actor, "state_change", "application",
                applicationId.ToString(), payloadJson ?? $"{{\"to\":\"{newState}\"}}"), ct).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }, ct);

    public Task<bool> TryTransitionApplicationAsync(long applicationId, string expectedState, string newState,
        string actor, string? payloadJson = null, string? recordPausedFrom = null, CancellationToken ct = default)
        => Locked(async () =>
        {
            using var tx = (SqliteTransaction)await Conn.BeginTransactionAsync(ct).ConfigureAwait(false);

            // Read-validate first, and only then consume the clock. This mirrors the in-memory
            // implementation exactly: a missing row throws and a lost race returns false with ZERO
            // clock reads. That parity is load-bearing — StoreParityHarness drives both stores with
            // one deterministic StepClock and compares rows and event hashes byte-for-byte, so a
            // failure path that ticks in one store but not the other skews every timestamp (and
            // therefore every event hash) downstream of the first failed CAS.
            // Atomicity: all writers serialize through Locked() on a single connection, so
            // SELECT-then-UPDATE inside this transaction cannot interleave with another writer; the
            // WHERE state=$expected clause below is retained as defense-in-depth, not as the guard.
            string currentState;
            using (var read = Conn.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = "SELECT state FROM applications WHERE id=$id;";
                P(read, "$id", applicationId);
                var v = await read.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (v is null or DBNull)
                {
                    await tx.RollbackAsync(ct).ConfigureAwait(false);
                    throw new InvalidOperationException($"No application {applicationId}");
                }
                currentState = (string)v;
            }
            if (!string.Equals(currentState, expectedState, StringComparison.Ordinal))
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                return false; // lost the race: no write, no event, no clock consumed
            }

            var now = Now();
            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE applications SET state=$state, paused_from=$pf, updated_at=$now
WHERE id=$id AND state=$expected;";
            P(cmd, "$state", newState);
            P(cmd, "$pf", recordPausedFrom);
            P(cmd, "$now", now);
            P(cmd, "$id", applicationId);
            P(cmd, "$expected", expectedState);
            var changed = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            if (changed == 0)
            {
                // Unreachable given the serialized read above; kept as a hard failure rather than a
                // silent false so any future locking regression is loud.
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"Application {applicationId}: CAS UPDATE matched 0 rows after a validating read; store serialization is broken.");
            }

            await AppendEventTxAsync(tx, new EventInput(actor, "state_change", "application",
                applicationId.ToString(), payloadJson ?? $"{{\"to\":\"{newState}\"}}"), ct).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }, ct);

    public Task<ApplicationRow?> GetApplicationAsync(long applicationId, CancellationToken ct = default)
        => Locked(async () =>
        {
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = "SELECT id, job_id, state, autonomy_level, channel, created_at, updated_at, paused_from, resume_path, cover_path, answers_json FROM applications WHERE id=$id;";
            P(cmd, "$id", applicationId);
            using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
            return new ApplicationRow(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5), r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9),
                r.IsDBNull(10) ? null : r.GetString(10));
        }, ct);

    public Task<IReadOnlyList<ApplicationSummaryRow>> GetRecentApplicationsAsync(
        int limit = 25,
        CancellationToken ct = default)
        => Locked(async () =>
        {
            var rows = new List<ApplicationSummaryRow>();
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = @"
SELECT
  a.id, a.state, a.autonomy_level, a.channel, a.created_at, a.updated_at, a.paused_from,
  j.id, j.title, c.name, c.domain, j.location, j.remote, j.url, j.apply_url,
  s.fit, s.legitimacy, s.total,
  (SELECT e.status FROM effect_attempts e WHERE e.application_id=a.id AND e.kind='draft' ORDER BY e.id DESC LIMIT 1) AS draft_status,
  (SELECT e.external_ref FROM effect_attempts e WHERE e.application_id=a.id AND e.kind='draft' ORDER BY e.id DESC LIMIT 1) AS draft_ref,
  a.resume_path, a.cover_path, a.answers_json
FROM applications a
JOIN jobs j ON j.id = a.job_id
LEFT JOIN companies c ON c.id = j.company_id
LEFT JOIN scores s ON s.job_id = j.id
ORDER BY a.updated_at DESC, a.id DESC
LIMIT $limit;";
            P(cmd, "$limit", Math.Clamp(limit, 1, 100));
            using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
                double? D(int i) => r.IsDBNull(i) ? null : r.GetDouble(i);
                rows.Add(new ApplicationSummaryRow(
                    r.GetInt64(0),
                    r.GetString(1),
                    r.GetString(2),
                    S(3),
                    r.GetString(4),
                    r.GetString(5),
                    S(6),
                    r.GetInt64(7),
                    r.GetString(8),
                    S(9),
                    S(10),
                    S(11),
                    r.GetString(12),
                    r.GetString(13),
                    S(14),
                    D(15),
                    D(16),
                    D(17),
                    S(18),
                    S(19),
                    S(20),
                    S(21),
                    !string.IsNullOrWhiteSpace(S(22))));
            }
            return (IReadOnlyList<ApplicationSummaryRow>)rows;
        }, ct);

    public Task SaveApplicationArtifactsAsync(
        long applicationId,
        string? resumePath,
        string? coverPath,
        string? answersJson,
        CancellationToken ct = default)
        => Locked<object?>(async () =>
        {
            var now = Now();
            using var tx = (SqliteTransaction)await Conn.BeginTransactionAsync(ct).ConfigureAwait(false);

            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE applications
SET resume_path=$resume, cover_path=$cover, answers_json=$answers, updated_at=$now
WHERE id=$id;";
            P(cmd, "$resume", resumePath);
            P(cmd, "$cover", coverPath);
            P(cmd, "$answers", answersJson);
            P(cmd, "$now", now);
            P(cmd, "$id", applicationId);
            var changed = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            if (changed == 0) throw new InvalidOperationException($"No application {applicationId}");

            await AppendEventTxAsync(tx, new EventInput("engine", "artifacts_saved", "application",
                applicationId.ToString(),
                $"{{\"resume\":{JsonBool(resumePath)},\"cover\":{JsonBool(coverPath)},\"answers\":{JsonBool(answersJson)}}}"), ct)
                .ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return null;
        }, ct);

    public Task SavePendingDispatchAsync(long applicationId, string payloadJson, CancellationToken ct = default)
        => Locked<object?>(async () =>
        {
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO pending_dispatch (application_id, payload_json, created_at) VALUES ($id, $json, $now)
ON CONFLICT(application_id) DO UPDATE SET payload_json=$json;";
            P(cmd, "$id", applicationId);
            P(cmd, "$json", payloadJson);
            P(cmd, "$now", Now());
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return null;
        }, ct);

    public Task<string?> GetPendingDispatchAsync(long applicationId, CancellationToken ct = default)
        => Locked(async () =>
        {
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = "SELECT payload_json FROM pending_dispatch WHERE application_id=$id;";
            P(cmd, "$id", applicationId);
            var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return v is null or DBNull ? null : (string)v;
        }, ct);

    public Task DeletePendingDispatchAsync(long applicationId, CancellationToken ct = default)
        => Locked<object?>(async () =>
        {
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = "DELETE FROM pending_dispatch WHERE application_id=$id;";
            P(cmd, "$id", applicationId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return null;
        }, ct);

    public Task<long> BeginEffectAttemptAsync(long applicationId, string kind, CancellationToken ct = default)
        => Locked(async () =>
        {
            var now = Now();
            using var tx = (SqliteTransaction)await Conn.BeginTransactionAsync(ct).ConfigureAwait(false);
            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO effect_attempts (application_id, kind, status, created_at, updated_at)
VALUES ($app, $kind, 'PENDING', $now, $now) RETURNING id;";
            P(cmd, "$app", applicationId);
            P(cmd, "$kind", kind);
            P(cmd, "$now", now);
            var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
            await AppendEventTxAsync(tx, new EventInput("engine", "effect_attempt", "application",
                applicationId.ToString(), $"{{\"attempt\":{id},\"kind\":\"{kind}\",\"status\":\"PENDING\"}}"), ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return id;
        }, ct);

    public Task ResolveEffectAttemptAsync(long attemptId, string status, string? externalRef = null, CancellationToken ct = default)
        => Locked<object?>(async () =>
        {
            var now = Now();
            using var tx = (SqliteTransaction)await Conn.BeginTransactionAsync(ct).ConfigureAwait(false);
            using var read = Conn.CreateCommand();
            read.Transaction = tx;
            read.CommandText = "SELECT application_id, kind FROM effect_attempts WHERE id=$id;";
            P(read, "$id", attemptId);
            using var r = await read.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await r.ReadAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException($"No effect attempt {attemptId}");
            var appId = r.GetInt64(0);
            var kind = r.GetString(1);
            await r.CloseAsync().ConfigureAwait(false);

            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE effect_attempts SET status=$status, external_ref=$ref, updated_at=$now WHERE id=$id;";
            P(cmd, "$status", status);
            P(cmd, "$ref", externalRef);
            P(cmd, "$now", now);
            P(cmd, "$id", attemptId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await AppendEventTxAsync(tx, new EventInput("engine", "effect_attempt", "application",
                appId.ToString(), $"{{\"attempt\":{attemptId},\"kind\":\"{kind}\",\"status\":\"{status}\"}}"), ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return null;
        }, ct);

    public Task<IReadOnlyList<EffectAttemptRow>> GetEffectAttemptsAsync(long applicationId, string? kind = null, CancellationToken ct = default)
        => Locked(async () =>
        {
            var rows = new List<EffectAttemptRow>();
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = @"
SELECT id, application_id, kind, status, external_ref, created_at, updated_at
FROM effect_attempts WHERE application_id=$app AND ($kind IS NULL OR kind=$kind) ORDER BY id;";
            P(cmd, "$app", applicationId);
            P(cmd, "$kind", kind);
            using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                rows.Add(new EffectAttemptRow(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5), r.GetString(6)));
            return (IReadOnlyList<EffectAttemptRow>)rows;
        }, ct);

    public Task<long> AppendEventAsync(EventInput e, CancellationToken ct = default)
        => Locked(async () =>
        {
            using var tx = (SqliteTransaction)await Conn.BeginTransactionAsync(ct).ConfigureAwait(false);
            var seq = await AppendEventTxAsync(tx, e, ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return seq;
        }, ct);

    public Task<IReadOnlyList<EventRow>> GetEventsAsync(CancellationToken ct = default)
        => Locked(async () =>
        {
            var rows = new List<EventRow>();
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = "SELECT seq, ts, actor, kind, entity, entity_id, payload_json, prev_hash, hash FROM events ORDER BY seq;";
            using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                rows.Add(new EventRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3),
                    r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8)));
            return (IReadOnlyList<EventRow>)rows;
        }, ct);

    public async Task<AuditVerification> VerifyAuditAsync(CancellationToken ct = default)
        => Audit.VerifyChain(await GetEventsAsync(ct).ConfigureAwait(false));

    public Task<long> UpsertProfileAsync(string json, CancellationToken ct = default)
        => Locked(async () =>
        {
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO profile (id, json, version, updated_at) VALUES (1, $json, 1, $now)
ON CONFLICT(id) DO UPDATE SET json=excluded.json, version=profile.version+1, updated_at=excluded.updated_at
RETURNING id;";
            P(cmd, "$json", json);
            P(cmd, "$now", Now());
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }, ct);

    public Task AddClaimAsync(ClaimRow c, CancellationToken ct = default)
        => Locked(async () =>
        {
            var claim = StoreNormalization.Normalize(c);
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO claims (id, profile_id, kind, text, confidence, source_doc, created_at)
VALUES ($id, $pid, $kind, $text, $conf, $src, $now)
ON CONFLICT(id) DO UPDATE SET kind=excluded.kind, text=excluded.text,
  confidence=excluded.confidence, source_doc=excluded.source_doc;";
            P(cmd, "$id", claim.Id);
            P(cmd, "$pid", claim.ProfileId);
            P(cmd, "$kind", claim.Kind);
            P(cmd, "$text", claim.Text);
            P(cmd, "$conf", claim.Confidence);
            P(cmd, "$src", claim.SourceDoc);
            P(cmd, "$now", Now());
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return true;
        }, ct);

    public Task<IReadOnlyList<ClaimRow>> GetClaimsAsync(long profileId, CancellationToken ct = default)
        => Locked(async () =>
        {
            var rows = new List<ClaimRow>();
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = "SELECT id, profile_id, kind, text, confidence, source_doc FROM claims WHERE profile_id=$pid ORDER BY id;";
            P(cmd, "$pid", profileId);
            using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                rows.Add(new ClaimRow(r.GetString(0), r.GetInt64(1), r.GetString(2), r.GetString(3),
                    r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5)));
            return (IReadOnlyList<ClaimRow>)rows;
        }, ct);

    public Task<string?> GetConfigAsync(string key, CancellationToken ct = default)
        => Locked(async () =>
        {
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM config WHERE key=$k;";
            P(cmd, "$k", key);
            var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return v is null or DBNull ? null : (string)v;
        }, ct);

    public Task SetConfigAsync(string key, string value, CancellationToken ct = default)
        => Locked(async () =>
        {
            using var cmd = Conn.CreateCommand();
            cmd.CommandText = "INSERT INTO config (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
            P(cmd, "$k", key);
            P(cmd, "$v", value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return true;
        }, ct);

    // ---- internals ----

    /// <summary>Append one event within an existing transaction. seq and hash are computed under the lock.</summary>
    private async Task<long> AppendEventTxAsync(SqliteTransaction tx, EventInput e, CancellationToken ct)
    {
        long seq;
        string prevHash;
        using (var head = Conn.CreateCommand())
        {
            head.Transaction = tx;
            head.CommandText = "SELECT seq, hash FROM events ORDER BY seq DESC LIMIT 1;";
            using var hr = await head.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await hr.ReadAsync(ct).ConfigureAwait(false))
            {
                seq = hr.GetInt64(0) + 1;
                prevHash = hr.GetString(1);
            }
            else
            {
                seq = 1;
                prevHash = Audit.Genesis;
            }
        }

        var ts = Now();
        var row = Audit.Link(seq, ts, prevHash, e);

        using var ins = Conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = @"
INSERT INTO events (seq, ts, actor, kind, entity, entity_id, payload_json, prev_hash, hash)
VALUES ($seq, $ts, $actor, $kind, $entity, $eid, $payload, $prev, $hash);";
        P(ins, "$seq", row.Seq);
        P(ins, "$ts", row.Ts);
        P(ins, "$actor", row.Actor);
        P(ins, "$kind", row.Kind);
        P(ins, "$entity", row.Entity);
        P(ins, "$eid", row.EntityId);
        P(ins, "$payload", row.PayloadJson);
        P(ins, "$prev", row.PrevHash);
        P(ins, "$hash", row.Hash);
        await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return seq;
    }

    private static JobRow MapJob(SqliteDataReader r)
    {
        int O(string n) => r.GetOrdinal(n);
        string? S(string n) => r.IsDBNull(O(n)) ? null : r.GetString(O(n));
        decimal? D(string n) => r.IsDBNull(O(n)) ? null : (decimal)r.GetDouble(O(n));
        return new JobRow(
            Id: r.GetInt64(O("id")),
            CompanyId: r.GetInt64(O("company_id")),
            Source: r.GetString(O("source")),
            ExternalId: r.GetString(O("external_id")),
            Url: r.GetString(O("url")),
            ApplyUrl: S("apply_url"),
            Title: r.GetString(O("title")),
            TitleCanon: r.GetString(O("title_canon")),
            DedupKey: r.GetString(O("dedup_key")),
            Location: S("location"),
            Remote: r.GetString(O("remote")),
            CompMin: D("comp_min"),
            CompMax: D("comp_max"),
            CompCurrency: S("comp_currency"),
            CompInterval: S("comp_interval"),
            CompSource: S("comp_source"),
            JdPath: S("jd_path"),
            SimHash: r.GetInt64(O("simhash")),
            Injected: r.GetInt64(O("injected")) != 0,
            InjectionSignals: S("injection_signals"),
            FirstSeen: r.GetString(O("first_seen")),
            LastVerified: r.GetString(O("last_verified")),
            RepostCount: r.GetInt32(O("repost_count")));
    }

    private static void P(SqliteCommand cmd, string name, object? value)
        => cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static async Task ExecAsync(SqliteConnection conn, string sql, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<T> Locked<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try { return await work().ConfigureAwait(false); }
        finally { _mutex.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_conn is not null)
        {
            await _conn.DisposeAsync().ConfigureAwait(false);
            _conn = null;
        }
        _mutex.Dispose();
    }
}
