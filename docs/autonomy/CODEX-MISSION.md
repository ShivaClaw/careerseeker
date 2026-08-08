# Beta to Release Candidate mission (R0-R7)

**Window:** 2026-08-07 through 2026-08-18.  **Repository:**
`ShivaClaw/careerseeker`.  This is the controlling unattended-work mission
captured from Brandon's 2026-08-07 authorization.

## Operating contract

Every iteration starts with `git fetch --all --prune`, reads both agent state
files, syncs to fresh `origin/main`, selects one coherent rung-slice, works in
a worktree, verifies, records evidence, updates state, and ends cleanly.
Claims require a command run in the current session. After two real blocked
attempts, write a dated blocked entry with symptom, attempts, and the smallest
human unblock; do not guess across an embargo.

`CLAUDE.md` remains controlling law: preserve the Fabrication Gate, pinned
strong-cloud verifier stage, no-send Gmail-draft-only L1 path, local-first
storage, `gmail.compose` scope, and data-only treatment of external text.
The doc/verifier drift rule applies to any harness-count change: measure the
new total, update `$ExpectedOfflineTotal`, and update every count-reporting
document in the same commit.

## Embargoes

No Cloudflare/site/relay deployment; Google/Play/OAuth console work; account,
purchase, email send, certificate acquisition/store mutation, MSIX
install/register/uninstall, reboot, scheduled-task registration, off-repo
site-source change, secret access, force-push/history rewrite, or mutation of
`.appdata` originals. Database migration rehearsal uses the existing
read-only-backup `--migration-copy` path only.

Allowed during this window: merge this agent's PRs after rebase plus a full
green publish/package gate and two green CI runs; user-scope development-tool
installs; and exactly one R3 live Gmail drafting cycle, capped at ten drafts,
only after R1 and R2 are complete, with drafts left unsent in Gmail. A failed
R3 cycle is diagnosed offline and is never retried live without a blocked
entry and the next backstop day.

## Coordination

Codex owns Engine (except the sync seam), Verifier, Gateway, Tailor,
Researcher, Dispatcher, Pipeline, scripts, installer, docs-site, and all
tests except `SyncHarness`. Claude owns relay, Sync, sync protocol/vectors,
SyncHarness, `/pair`, the `BuildSyncBridge` Program seam, and incoming funnel
surfaces. Shared pinch points are `Verify-Alpha.ps1`, count-reporting docs,
`Host.cs`/dashboard, and `Program.cs`; fresh measured output resolves a count
conflict and Codex has merge right-of-way. Codex maintains and directly pushes
the docs-only `autonomy/codex-state` branch.

## Completion boundary

R0-R6 must be DONE or BLOCKED before R7 idle slices. At exhaustion, write a
final handoff with evidence index, human queue, and explicit statement of what
was not done. A stale state heartbeat (>14 hours) permits the backstop to run
exactly one rung-slice.
