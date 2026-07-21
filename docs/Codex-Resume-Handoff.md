# Codex Resume Handoff

Updated: 2026-07-21

## 2026-07-21 (Opus session) — audit batch committed + hardening batch

Never trust a SHA in this file — derive with `git rev-parse --short HEAD` / `git log --oneline -8`.
Roles switched this session: Claude Code (Fable for audit, Opus for building) is now primary coding
agent; Codex is the external auditor from Friday 2026-07-24. See
`Desktop/Career Seeker/Opus-Build-Roadmap-2026-07-21.md` for milestones (M-A..M-E) and gates (G1..G6).

Branch/PR topology now (all draft, none merged — awaiting Friday audit + Brandon):
- `main` @ `3fa65f5` — stale (156 behind the live line).
- `agent/repo-cleanup` @ `81d232c` — pre-audit live line; PR #1 → `main`, draft/open.
- `agent/audit-cleanup-h1h2h3` @ `f3021ec` — the previously-uncommitted H1/H2/H3 + CLAUDE.md, now
  committed. **PR #2 → `agent/repo-cleanup`** (draft, CI green).
- `claude/hardening-batch` — Phase-3 hardening, **PR #3 → `agent/audit-cleanup-h1h2h3`** (draft, CI green).
- `claude/alpha-finish` — Phase-4 alpha release candidate, based on the `claude/hardening-batch` tip so it
  carries every post-checkpoint commit. **PR #4 → `agent/repo-cleanup`** (draft). This PR is the
  consolidated "what Claude changed after the Codex checkpoint (`81d232c`)" diff you asked for; PR #2 and
  PR #3 remain open as the granular per-batch views of the same commits. Review whichever is more useful.

  Branch-base note: you suggested branching `claude/alpha-finish` directly off `agent/repo-cleanup`. Doing
  that literally would have dropped H1/H2/H3 and A1/L1/M1/M2, none of which are merged into
  `agent/repo-cleanup` yet. Basing on the hardening tip and targeting `agent/repo-cleanup` gives the same
  single clean diff against the checkpoint while preserving that work and the small per-item commits.

This session's commits on `claude/hardening-batch` (newest first — derive head yourself):
- `ci: also run on claude/** branches` — CI trigger fix (claude/** branches had no CI).
- `M2` — document accepted query-string doc-token tradeoff (no behavior change).
- `M1` — pin `$ExpectedOfflineTotal` in Verify-Alpha.ps1 and assert it (closes silent-total-drift;
  confirmed CI already runs the SQLite harnesses on windows-latest).
- `L1` — presence-check SQLite migration (`PRAGMA table_info`, no more throw-and-swallow) + pre-existing-DB
  migration test in StoreParityHarness.
- `A1` — reject IPv6 unspecified `::` in `PrivateNetworkGuard.IsPubliclyRoutable` + SSRF-guard test.

Verification (this session, on the hardening-batch tree): `dotnet build -c Release --warnaserror` 0W/0E;
`scripts\Verify-Alpha.ps1` **325 passed, 0 failed** (Researcher 52→53, StoreParity 19→22; pinned-total
assertion passes). Counts synced across docs + verifier per the CLAUDE.md drift trap.

Suggested Codex audit focus: A1 (`::` the only v6 gap?), L1 (no-FK old-schema seed acceptable? PRAGMA
column-index read), M1 (`$ExpectedOfflineTotal` now in the drift-trap set), M2 (documented acceptance, not
a fix — cookie migration is a deliberate follow-up if wanted). Remaining open from the 2026-07-20 audit
after this batch: none of A1/L1/M1/M2; M3/L2/L3 are documented-accepted residuals.

### Phase 4 — alpha release candidate (`claude/alpha-finish`)

Commits on this branch beyond the hardening batch:
- `docs: add Claude alpha build instructions and future design ideas` — the two previously-untracked docs,
  committed with owner approval after a secret-pattern scan (clean).

Exact commands run this session and their results:

| Command | Result |
| --- | --- |
| `dotnet build CareerSeeker.sln -c Release --warnaserror` | **0 warnings, 0 errors** |
| `scripts\Verify-Alpha.ps1` | **325 passed, 0 failed** (pinned-total assertion passes) |
| `scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage` | **passed** — details below |

`-IncludePublish`: win-x64 self-contained single-file publish succeeded; published-executable demo smoke
ran a SQLite demo cycle with `errors: 0`.

`-IncludePackage`: trusted-tester ZIP built at `output\release\CareerSeeker-alpha-win-x64.zip`
(~31.0 MB), `manifest: ok`, **46 checksums verified**; packaged dashboard smoke `errors: 0`; packaged
helper smokes, audit export, and evidence export/import into an isolated restore workspace all passed.
The ZIP is reproducible from committed source — it is a build artifact and is **not** committed
(`output/`, `.appdata/`, `secrets/`, `tmp/` are gitignored; `git status` is clean after the run).

Per-harness offline breakdown at this head: Slice 28 · EngineHarness 89 · ResearcherHarness 53 ·
HookHarness 14 · StoreParityHarness 22 · GatewayGateHarness 34 · DispatcherNoSendHarness 35 ·
LifecycleHarness 44 · RendererHarness 6 = **325**.

**Intentionally skipped, and why:**
- `-IncludeLive` (live BYOK/Gmail/Gateway smoke) and `-IncludeResearch` (live Brave + BYOK research) —
  these spend real provider credits and touch Gmail. Held behind the standing human gate (G2); the owner
  did not authorize live/spending runs this session. Codex's own guidance was to prefer dry-run/live-safe
  helpers.
- No real Gmail draft was created. No `Run-CareerSeeker-Live` LIVE path was exercised.
- Consequence: live evidence in this file dated 2026-07-20 is the most recent live proof; it predates
  A1/L1/M1/M2. Nothing in this batch touches the Gmail send/draft path, BYOK wiring, or the packaging
  scripts, but the live path has not been re-proven on this head.

**Known risks / open items for the Friday audit:**
- Live/research verification is stale by design (see above). Re-running `-IncludeLive` and
  `-IncludeResearch` is the highest-signal next evidence if the owner authorizes spending.
- A1's fail-closed multi-address rule still rejects legitimately multi-homed hosts that publish any
  private address — intended, but it is a behavioral tradeoff worth a second opinion.
- L1's migration test seeds an old-schema table without the foreign key; it proves column migration and
  round-trip, not FK-constrained upgrade behavior.
- M2 is an accepted residual, not a fix; the doc-route token still travels in the query string.
- `main` remains 156 commits behind the live line; the whole chain is unmerged pending your audit.

## Session Status (2026-07-21 earlier — audit-findings work, now superseded above)

- Branch: `agent/repo-cleanup`
- PR: `https://github.com/ShivaClaw/careerseeker/pull/1`
- Current head: **do not trust a SHA embedded in this file — run `git rev-parse --short HEAD`.**
  At this update the branch head is `81d232c Add Codex resume handoff`. Any version of this handoff
  is committed *above* the SHA it can name, so an embedded value is always at least one commit stale
  by construction — treat it as a snapshot pointer, not ground truth. (A prior revision claimed
  `bd2bf8c`, which was already one commit behind the head that recorded it; that drift is what audit
  finding H3 flagged.)
- Worktree: **not clean.** H2 (engine startup reconcile sweep) and H3 (this handoff correction) are in
  progress in the working tree and not yet committed. Run `git status -sb` before trusting any evidence
  below; the pushed head still predates the H2 change.
- Worktree at original handoff creation (2026-07-20): clean
- PR merge state at handoff creation: `CLEAN`
- GitHub CI at handoff creation: both `Build and offline harnesses` checks passed
- User instruction (2026-07-20 session): stop working; resume only after explicit user request. That
  resume happened on 2026-07-21 to work the audit findings.

## What Was Finished

- Wired and verified the alpha BYOK/Gmail/PDF path:
  - BYOK Anthropic/Gemini provider import through local DPAPI vault.
  - Live Tailor and Gate provider smoke.
  - Bounded Gate checks for alpha runs.
  - Real ATS-clean PDF renderer and Gmail draft attachment path.
- Hardened L1 no-send and local-control surfaces:
  - Dispatcher remains draft-only; send/submit paths are absent or throw.
  - Gmail label capability remains split from draft creation.
  - Dashboard controls use loopback, token, Host/Origin/Referer, content-type, and body-size checks.
  - Dashboard read/document routes reject foreign Host headers.
  - Dashboard document links serve only configured artifact roots.
- Hardened alpha package export/import:
  - Export skips secret-looking paths and artifact symlinks/junctions.
  - Import rejects unsafe paths, secret-looking entries, duplicates, unsupported entries, ambiguous database entries, too many entries, and oversized uncompressed contents.
  - Package import verifies restored SQLite audit chain.
- Hardened live research:
  - Brave adapter fetches public result pages before grounding.
  - URL filtering rejects localhost, private IPv4, link-local metadata, private IPv6, and non-text results.
  - Dossier prompt quarantine remains covered.
- Finished public trust-site sync:
  - `https://careerseeker.app/privacy/` contains Google API Limited Use and no-training language.
  - `https://careerseeker.app/autonomy-contract/` is live.
  - Homepage links to the Autonomy Contract.
- Built and verified trusted-tester packaging:
  - Release ZIP includes executable, launchers, scripts, docs, manifest, audit snapshot, and checksums.
  - Extracted package self-check passes.
  - Packaged helper smokes cover readiness, dashboard task dry runs/status, company research preview, selected-job preview, live dry-run, audit export, evidence export/import, BYOK clear, and Gmail disconnect.
- Refreshed external audit materials:
  - `docs/External-Audit-Handoff.md`
  - `docs/CareerSeeker-Project-Summary.md`
  - `README.md`
  - `src/Engine/README.md`
  - Historical audit note in `docs/repo-audit-2026-07-13.md`
  - PR #1 body synced from `docs/External-Audit-Handoff.md`

## Verification Evidence

Most recent known-good local evidence on current pushed head:

- `scripts\Verify-Alpha.ps1`
  - `297 passed, 0 failed`
- `scripts\Verify-Alpha.ps1 -IncludePublish`
  - default verifier passed
  - win-x64 single-file publish passed
  - published executable demo smoke passed
- `scripts\Verify-Alpha.ps1 -IncludePackage`
  - default verifier passed
  - trusted-tester ZIP built at `output\release\CareerSeeker-alpha-win-x64.zip`
  - release manifest/checksums verified
  - dashboard smoke passed
  - packaged helper smokes passed
  - evidence export/import smoke passed
- `scripts\Check-AlphaLiveReadiness.ps1 -RequireGmail -RequireByok`
  - Gmail OAuth client parsed
  - Gmail token vault present
  - BYOK providers present: `anthropic`, `google`
  - Brave Search configured via `BRAVE_SEARCH_API`
- `scripts\Verify-Alpha.ps1 -IncludeResearch`
  - default verifier passed
  - live GitLab research retrieved 10 docs
  - 4 grounded fallback facts
  - 0 dropped ungrounded facts
  - domain verified and recruiter identifiable
  - best hook: `GitLab has a public jobs page.`
- `scripts\Verify-Alpha.ps1 -IncludeLive`
  - default verifier passed
  - BYOK provider import passed
  - live Anthropic/Gemini provider smoke passed
  - Gateway Tailor/Gate/accounting smoke passed
  - required Gmail/BYOK startup doctor passed
  - dashboard one-shot smoke passed
- GitHub PR #1 checks:
  - both `Build and offline harnesses` check runs passed
  - merge state `CLEAN`

## Latest Important Commits

Newest first. This is historical context, not a live head pointer — confirm the actual head with
`git log --oneline -5` / `git rev-parse --short HEAD`.

- `81d232c Add Codex resume handoff` (records this handoff; the branch head as of 2026-07-20)
- `bd2bf8c Keep alpha verification evidence current`
- `114d0cd Reject foreign dashboard hosts on read routes`
- `db4a0a2 Harden Brave result URL filtering`
- `0948cfd Bound alpha package import size`
- `8360fc7 Record live trust site deployment`
- `41515ee Reject ambiguous alpha package databases`
- `4fddee1 Skip symlinks in alpha package export`
- `dfbb3bf Restrict dashboard documents to artifact roots`

## Known Remaining Gaps

These are not hidden pass conditions for the L1 technical alpha, but they are still product-launch work:

- Windows service/tray shell, polished installer, and code signing.
- OAuth production verification/CASA.
- Android relay/dashboard.
- Product-grade PDF visual polish beyond ATS-clean text PDF.
- Gmail label tree, intentionally deferred to preserve compose-only L1 scope.
- Broader legal/privacy review before public launch.

## Resume Checklist

When the user explicitly resumes:

1. Check repo/PR state (never trust a SHA copied from this file — derive it):
   - `git rev-parse --short HEAD` and `git log --oneline -5`
   - `git status -sb`
   - `gh pr view 1 --repo ShivaClaw/careerseeker --json mergeStateStatus,statusCheckRollup,headRefName,url`
2. If code changed externally, rerun the default verifier first:
   - `scripts\Verify-Alpha.ps1`
3. If preparing for another audit pass, prefer high-signal evidence:
   - `scripts\Verify-Alpha.ps1 -IncludePackage`
   - `scripts\Verify-Alpha.ps1 -IncludeLive`
   - `scripts\Verify-Alpha.ps1 -IncludeResearch`
4. Do not print secret values from `secrets/env.secrets`, OAuth client JSON, token vaults, or DPAPI vaults.
5. If any docs/evidence counts change, update the verifier doc-smoke expectations in `scripts/Verify-Alpha.ps1`.
6. If changes are made, run the relevant verifier, commit, push `agent/repo-cleanup`, and watch PR checks.

## Stop Marker

The user asked to stop working for this session. Do not continue autonomously from this file alone.
