# CareerSeeker External Audit Handoff

Updated: 2026-07-20
Branch: `agent/repo-cleanup`
Pull request: `#1`

## Audit Target

CareerSeeker is a local-first Windows L1 Drafts alpha. The alpha should discover jobs, score and filter them,
tailor draft materials with source-backed claims only, render an ATS-clean resume PDF, and create reviewable
Gmail drafts without any application send path.

The highest-value audit question is whether the current source preserves these invariants while using real
local SQLite state, local DPAPI vaults, BYOK LLM providers, Brave Search, and Gmail draft creation.

## Current Evidence

- GitHub CI is green on this branch and runs the Release warnings-as-errors build plus
  `scripts/Verify-Alpha.ps1`, including the source-mode SQLite demo smoke and offline harness suite.
- Latest local offline verifier: `236 passed, 0 failed`.
- `scripts/Verify-Alpha.ps1 -IncludeLive -IncludePublish` passed locally after the current alpha wiring:
  offline harnesses, win-x64 single-file publish smoke, BYOK live provider smoke, startup doctor, and
  dashboard smoke.
- `scripts/Verify-Alpha.ps1 -IncludePackage` passed locally and produced a trusted-tester ZIP with the alpha
  executable, native runtime dependencies, double-click launcher, workspace initializer, dashboard/helper
  self-check scripts, quickstart, release manifest, checksums, and selected docs.
- `scripts/Verify-Alpha.ps1 -IncludeResearch` passed locally with live Brave Search plus BYOK dossier
  modeling. Latest GitLab smoke retrieved 10 docs, used 3 deterministic grounded fallback facts after the model
  proposed 0 facts, and dropped 0 ungrounded facts.
- The PR is open against `main` and GitHub reports checks passing.

## Repeatable Commands

Default offline verifier:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Verify-Alpha.ps1
```

Local workspace initialization:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Initialize-AlphaWorkspace.ps1
```

Local source-of-truth profile setup:

```powershell
dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- profile-template --out .appdata/profile.template.json
dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- import-profile --profile .appdata/profile.template.json --db .appdata/careerseeker-alpha.db
```

Live BYOK/Gmail checks, using ignored local secrets and vault files:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Verify-Alpha.ps1 -IncludeLive
```

Live Brave/BYOK research smoke:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Verify-Alpha.ps1 -IncludeResearch
```

Win-x64 publish smoke:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Verify-Alpha.ps1 -IncludePublish
```

Trusted-tester release ZIP:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Verify-Alpha.ps1 -IncludePackage
```

Trusted-tester dashboard launcher:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Start-AlphaDashboard.ps1
```

Local alpha evidence package:

```powershell
dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- export-alpha-package --db .appdata/careerseeker-alpha.db --out output/careerseeker-alpha-package.zip
```

Safe local alpha package restore:

```powershell
dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- import-alpha-package --package output/careerseeker-alpha-package.zip
```

Optional per-user Windows logon task preview:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Manage-AlphaDashboardTask.ps1 -Action Install -DryRun
```

## Safety Surfaces To Audit

- L1 no-send boundary: `src/Dispatcher` exposes draft creation only; `SubmitAsync` throws.
- Gmail scope discipline: L1 uses `gmail.compose`; label management is split behind a separate capability and is
  off by default.
- Fabrication Gate: only verified applications can draft; unsupported claims block or defer, and live BYOK Gate
  calls fail closed.
- Prompt quarantine: job descriptions and retrieved web documents are untrusted data blocks, not instructions.
- Tailor minimization: generation receives only posting-relevant profile claims; Gate verification still checks
  against the local source profile.
- Profile import: `import-profile` replaces the local profile claim oracle instead of mixing imported claims with
  seeded demo facts.
- Local dashboard controls: loopback dashboard has token-protected Gmail disconnect, application controls, and
  token-protected document downloads.
- Store audit chain: local SQLite and in-memory stores share hash-chain verification and parity coverage.
- Secret handling: `secrets/`, `.appdata/`, generated artifacts, OAuth tokens, and provider keys are ignored and
  should not be printed.

## Current Alpha Capabilities

- Runnable `src/Engine` executable with `demo`, `alpha`, `dashboard`, `scout-boards`, `draft-job`,
  `research-company`, `profile-template`, `import-profile`, `doctor`, `export-audit`, `export-alpha-package`,
  `import-alpha-package`, `control-app`, OAuth disconnect, and BYOK import/clear modes.
- Live Greenhouse/Lever/Ashby board ingestion into SQLite with local posting-body artifacts.
- Selected stored job drafting with posting-body context and dry-run verification.
- Real ATS-clean resume PDF renderer and Gmail draft attachment packaging.
- Standalone localhost dashboard over an existing SQLite alpha DB.
- Dashboard `/applications`, `/jobs`, `/evidence`, application controls, Gmail disconnect, token-protected alpha
  package export, local resume/cover document routes, and a responsive shared alpha shell for status and
  recent-item views.
- BYOK Anthropic/Gemini Tailor and Gate wiring through the Gateway.
- Brave Search + BYOK company dossier command with deterministic grounding and fallback source snippets.
- Local source-of-truth profile template/import commands for Tailor/Gate facts.
- Local alpha ZIP package export/import with manifest, audit export, SQLite snapshot, draft artifacts, and saved
  job-description artifacts; secret/token/key-looking paths are filtered, unsafe ZIP paths are rejected, and
  import verifies the restored SQLite audit chain.
- Trusted-tester release ZIP packaging for the published executable, native runtime dependencies, workspace
  initializer, double-click dashboard launcher, quickstart, release manifest, dashboard/helper self-check scripts,
  SHA-256 checksums, and selected trust/audit docs without local databases, vaults, provider keys, or generated
  artifacts.
- GitHub CI mirrors the offline alpha verifier for `main`, `agent/**`, `codex/**`, and PRs into `main`.

## Known Gaps

These are not hidden pass conditions for the current L1 technical alpha:

- No Windows Service host, WinUI tray, polished installer, or code signing yet.
- No OAuth production verification or CASA assessment yet.
- No Android relay/dashboard yet.
- Current PDF renderer is ATS-clean text, not a polished HTML/Chromium template.
- Gmail label tree remains deferred to preserve compose-only L1 scope.
- Public launch still needs legal/privacy review, signing, OAuth verification, and broader product-shell work.

## Useful Entry Points

- Product/spec handoff: `docs/CareerSeeker-Project-Summary.md`
- Alpha checklist: `docs/CareerSeeker-Alpha-Build-Checklist.md`
- Historical audit context: `docs/repo-audit-2026-07-13.md`
- Trust docs: `docs/Privacy-Policy.md`, `docs/Support.md`, `docs/Autonomy-Contract.md`
- Verification script: `scripts/Verify-Alpha.ps1`
