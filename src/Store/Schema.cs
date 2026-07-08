namespace SeekerSvc.Store;

/// <summary>
/// The canonical engine schema (spec section 7.1). One source of truth, shared by the SQLite
/// provider. Every statement is idempotent (IF NOT EXISTS) so initialization is safe to re-run.
///
/// The <c>events</c> table is the tamper-evident audit log: every row carries the hash of the
/// previous row, so any edit, deletion, or reordering breaks the chain (see <see cref="Audit"/>).
/// </summary>
public static class Schema
{
    /// <summary>Pragmas applied per connection. WAL gives concurrent readers; foreign keys are enforced.</summary>
    public static readonly IReadOnlyList<string> Pragmas = new[]
    {
        "PRAGMA journal_mode=WAL;",
        "PRAGMA foreign_keys=ON;",
        "PRAGMA synchronous=NORMAL;",
        "PRAGMA busy_timeout=5000;",
    };

    public const string Ddl = @"
CREATE TABLE IF NOT EXISTS profile (
  id          INTEGER PRIMARY KEY,
  json        TEXT    NOT NULL,
  version     INTEGER NOT NULL DEFAULT 1,
  updated_at  TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS claims (
  id          TEXT    PRIMARY KEY,
  profile_id  INTEGER NOT NULL REFERENCES profile(id) ON DELETE CASCADE,
  kind        TEXT    NOT NULL,
  text        TEXT    NOT NULL,
  confidence  TEXT    NOT NULL CHECK (confidence IN ('verified','stated','weak')),
  source_doc  TEXT,
  created_at  TEXT    NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_claims_profile ON claims(profile_id);

CREATE TABLE IF NOT EXISTS companies (
  id           INTEGER PRIMARY KEY AUTOINCREMENT,
  name         TEXT,
  domain       TEXT,
  ats_kind     TEXT    NOT NULL,
  ats_handle   TEXT    NOT NULL,
  dossier_path TEXT,
  dossier_at   TEXT,
  flags        TEXT,
  UNIQUE (ats_kind, ats_handle)
);

CREATE TABLE IF NOT EXISTS jobs (
  id            INTEGER PRIMARY KEY AUTOINCREMENT,
  company_id    INTEGER NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
  source        TEXT    NOT NULL,
  external_id   TEXT    NOT NULL,
  url           TEXT    NOT NULL,
  apply_url     TEXT,
  title         TEXT    NOT NULL,
  title_canon   TEXT    NOT NULL,
  dedup_key     TEXT    NOT NULL,
  location      TEXT,
  remote        TEXT    NOT NULL DEFAULT 'Unknown',
  comp_min      REAL,
  comp_max      REAL,
  comp_currency TEXT,
  comp_interval TEXT,
  comp_source   TEXT,
  jd_path       TEXT,
  simhash       INTEGER NOT NULL DEFAULT 0,
  injected      INTEGER NOT NULL DEFAULT 0,
  injection_signals TEXT,
  first_seen    TEXT    NOT NULL,
  last_verified TEXT    NOT NULL,
  repost_count  INTEGER NOT NULL DEFAULT 0,
  UNIQUE (source, external_id)
);
CREATE INDEX IF NOT EXISTS ix_jobs_company ON jobs(company_id);
CREATE INDEX IF NOT EXISTS ix_jobs_dedup   ON jobs(dedup_key);
CREATE INDEX IF NOT EXISTS ix_jobs_simhash ON jobs(simhash);

CREATE TABLE IF NOT EXISTS scores (
  job_id         INTEGER PRIMARY KEY REFERENCES jobs(id) ON DELETE CASCADE,
  fit            REAL    NOT NULL,
  legitimacy     REAL    NOT NULL,
  red_flag_mult  REAL    NOT NULL DEFAULT 1.0,
  total          REAL    NOT NULL,
  subscores_json TEXT,
  scored_at      TEXT    NOT NULL,
  model_used     TEXT
);

CREATE TABLE IF NOT EXISTS applications (
  id             INTEGER PRIMARY KEY AUTOINCREMENT,
  job_id         INTEGER NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
  state          TEXT    NOT NULL CHECK (state IN (
                   'DISCOVERED','SCREENED','EVALUATED','REJECTED_BY_ENGINE','TAILORED',
                   'VERIFIED','BLOCKED_FABRICATION','READY','DRAFTED','GATE_PENDING',
                   'APPROVED','SUBMITTING','APPLIED','SKIPPED','GATE_EXPIRED',
                   'AWAITING_RESPONSE','RECRUITER_REPLY','CORRESPONDENCE','FOLLOWUP_DUE',
                   'FOLLOWUP_SENT','INTERVIEW_PROPOSED','SLOTS_OFFERED','SCHEDULED',
                   'REJECTED','OFFER','GHOSTED','USER_KILLED','PAUSED')),
  autonomy_level TEXT    NOT NULL DEFAULT 'L1' CHECK (autonomy_level IN ('L1','L2','L3')),
  resume_path    TEXT,
  cover_path     TEXT,
  answers_json   TEXT,
  gate_id        INTEGER,
  channel        TEXT    CHECK (channel IS NULL OR channel IN ('ats_form','email','manual_finish')),
  submitted_at   TEXT,
  created_at     TEXT    NOT NULL,
  updated_at     TEXT    NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_app_job   ON applications(job_id);
CREATE INDEX IF NOT EXISTS ix_app_state ON applications(state);

CREATE TABLE IF NOT EXISTS gates (
  id             INTEGER PRIMARY KEY AUTOINCREMENT,
  application_id INTEGER REFERENCES applications(id) ON DELETE CASCADE,
  kind           TEXT    NOT NULL CHECK (kind IN ('apply','reply','calendar','lesson')),
  payload_json   TEXT,
  status         TEXT    NOT NULL DEFAULT 'open' CHECK (status IN ('open','approved','skipped','expired')),
  requested_at   TEXT    NOT NULL,
  resolved_at    TEXT,
  resolved_via   TEXT    CHECK (resolved_via IS NULL OR resolved_via IN ('push','digest','localhost'))
);
CREATE INDEX IF NOT EXISTS ix_gates_status ON gates(status);
CREATE INDEX IF NOT EXISTS ix_gates_app    ON gates(application_id);

CREATE TABLE IF NOT EXISTS threads (
  id              INTEGER PRIMARY KEY AUTOINCREMENT,
  application_id  INTEGER NOT NULL REFERENCES applications(id) ON DELETE CASCADE,
  gmail_thread_id TEXT    NOT NULL,
  last_class      TEXT,
  last_msg_at     TEXT
);
CREATE INDEX IF NOT EXISTS ix_threads_app ON threads(application_id);

CREATE TABLE IF NOT EXISTS events (
  seq          INTEGER PRIMARY KEY AUTOINCREMENT,
  ts           TEXT    NOT NULL,
  actor        TEXT    NOT NULL CHECK (actor IN ('engine','user','relay')),
  kind         TEXT    NOT NULL,
  entity       TEXT    NOT NULL,
  entity_id    TEXT    NOT NULL,
  payload_json TEXT    NOT NULL DEFAULT '',
  prev_hash    TEXT    NOT NULL,
  hash         TEXT    NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_events_entity ON events(entity, entity_id);
CREATE INDEX IF NOT EXISTS ix_events_ts     ON events(ts);

CREATE TABLE IF NOT EXISTS lessons (
  id            INTEGER PRIMARY KEY AUTOINCREMENT,
  text          TEXT    NOT NULL,
  evidence_json TEXT,
  status        TEXT    NOT NULL DEFAULT 'proposed' CHECK (status IN ('proposed','confirmed','rejected')),
  affects       TEXT,
  created_at    TEXT    NOT NULL,
  resolved_at   TEXT
);

CREATE TABLE IF NOT EXISTS stories (
  id             INTEGER PRIMARY KEY AUTOINCREMENT,
  title          TEXT    NOT NULL,
  situation      TEXT,
  task           TEXT,
  action         TEXT,
  result         TEXT,
  reflection     TEXT,
  tags_json      TEXT,
  source_app_ids TEXT
);

CREATE TABLE IF NOT EXISTS config (
  key   TEXT PRIMARY KEY,
  value TEXT
);
";
}
