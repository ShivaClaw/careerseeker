# Codex Resume Handoff

Updated: 2026-07-21

## Session Status

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
