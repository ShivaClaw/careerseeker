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
- Latest local offline verifier: `272 passed, 0 failed`.
- Fresh optional verifier, 2026-07-20: `scripts/Verify-Alpha.ps1 -IncludeLive -IncludePublish -IncludeResearch`
  passed locally on this branch. It covered the offline harness suite, win-x64 single-file publish smoke,
  BYOK key import, BYOK live provider smoke, required Gmail/BYOK startup doctor, dashboard one-shot smoke,
  and live Brave/BYOK company research.
- Fresh live Scout harness, 2026-07-20: all configured Greenhouse/Lever/Ashby boards responded, all three ATS
  kinds produced jobs, Store ingest round-tripped, and the run found 942 raw jobs, 635 deduped jobs, 389
  compensation-bearing jobs, and 81 prompt-injection signals.
- `scripts/Verify-Alpha.ps1 -IncludePackage` passed locally and produced a trusted-tester ZIP with the alpha
  executable, native runtime dependencies, double-click setup/profile/provider/Gmail/live-readiness/provider-clear/Gmail-disconnect/demo/scout/company-research/selected-job/live/audit-export/evidence-export/evidence-import/verify/dashboard and dashboard-task launchers, workspace initializer, dashboard/helper
  self-check scripts, quickstart, tester walkthrough, package-local audit snapshot, release manifest, checksums,
  and selected docs. The extracted-package verifier also smokes the packaged live readiness helper, dry-runs
  packaged dashboard logon-task install/uninstall, smokes dashboard task status, previews company research, exports packaged audit JSON, restores a packaged evidence ZIP, provider-key clear, and Gmail disconnect command paths
  against isolated temp vault paths, and checks the
  bundled Privacy, Support, and Autonomy docs for the real alpha off-ramp and evidence-package commands.
  The package self-check also asserts typed confirmation prompts for live Gmail draft creation, destructive
  local vault off-ramps, and persistent dashboard logon-task changes.
- Latest GitLab research smoke retrieved 10 docs; the live model proposed 0 facts, so deterministic source
  fallback produced 4 grounded facts with 0 dropped ungrounded facts, verified the domain, identified recruiter
  signals, and produced the grounded hook `GitLab has a public jobs page.`
- DNS mail-routing check, 2026-07-20: `careerseeker.app` publishes Cloudflare Email Routing MX records
  (`route1.mx.cloudflare.net`, `route2.mx.cloudflare.net`, `route3.mx.cloudflare.net`) and SPF includes
  `_spf.mx.cloudflare.net`. This confirms domain-level routing is present; it does not prove the final
  `support@` / `privacy@` forwarding destinations receive mail.
- The PR is open against `main` and GitHub reports checks passing.

## Evidence Map

| Invariant or capability | Primary surfaces | Repeatable evidence |
| --- | --- | --- |
| L1 cannot send or submit applications | `src/Dispatcher/Dispatch.cs`, `src/Dispatcher/Dispatcher.cs`, `src/Pipeline/ApplicationPipeline.cs` | `DispatcherNoSendHarness`; offline `Verify-Alpha.ps1` |
| Gmail is draft-only in the application even though `gmail.compose` can authorize sends | `src/Dispatcher/GoogleOAuth.cs`, `src/Dispatcher/Providers.cs`, trust docs | `DispatcherNoSendHarness`; trust wording smoke |
| Tailor output is checked against local profile evidence before drafting | `src/Tailor`, `src/Verifier`, `src/Pipeline` | `HookHarness`, `GatewayGateHarness`, `Slice`; live BYOK Gate smoke |
| Source-of-truth profile import replaces the claim oracle and refuses non-alpha profile artifacts or duplicate claim ids | `src/Engine/AlphaProfileImport.cs`, `src/Engine/Program.cs`, `scripts/Import-AlphaProfile.ps1` | `EngineHarness`; package profile-import launcher checks |
| Live ATS board ingest discovers and stores real jobs | `src/Scout`, `src/Engine/Program.cs`, `src/Store` | `ScoutLiveHarness`; `Run-CareerSeeker-Scout.cmd` package preview |
| Selected-job drafting refuses prompt-injection-flagged jobs unless explicitly overridden after manual review | `src/Engine/Program.cs`, `scripts/Draft-AlphaJob.ps1`, `Draft-CareerSeeker-Job.cmd` | `EngineHarness`; package selected-job preview and launcher checks |
| ATS-clean resume PDF is rendered and attached to Gmail drafts | `src/Dispatcher/AtsPdfDocumentRenderer.cs`, `src/Dispatcher/Packaging.cs`, `src/Dispatcher/Mime.cs` | `RendererHarness`, `DispatcherNoSendHarness`; package selected-job dry-run smoke |
| Real BYOK Tailor and Gate providers are wired through the Gateway | `src/Gateway/ProvidersHttp.cs`, `src/Gateway/Routing.cs`, `src/Engine/Program.cs` | `Verify-Alpha.ps1 -IncludeLive`; BYOK live provider smoke |
| Brave Search company research is grounded and fails closed on missing keys | `src/Researcher/BraveSearchWebResearch.cs`, `src/Researcher/Researcher.cs`, `src/Engine/StartupDoctor.cs` | `Verify-Alpha.ps1 -IncludeResearch`; startup doctor Brave check |
| Local state, OAuth tokens, provider keys, and generated artifacts stay out of source control | `.gitignore`, `scripts/Initialize-AlphaWorkspace.ps1`, `src/Engine/StartupDoctor.cs` | source-control hygiene smoke; initializer dry run; package manifest/checksum smoke; secret path filters |
| Trusted-tester ZIP carries source provenance, payload checksums, and provider-key quickstart guidance, plus typed confirmations for live/dangerous actions | `scripts/Package-AlphaRelease.ps1`, `scripts/Test-AlphaReleasePackage.ps1`, `Run-CareerSeeker-Live.cmd`, `Clear-CareerSeeker-Providers.cmd`, `Disconnect-CareerSeeker-Gmail.cmd`, `Install-CareerSeeker-DashboardTask.cmd`, `Uninstall-CareerSeeker-DashboardTask.cmd` | release manifest source commit checks; audit snapshot provenance checks; README-alpha provider-key checks; live draft confirmation checks; off-ramp confirmation checks; dashboard task confirmation checks; SHA-256 checksum smoke |
| Dashboard controls are loopback, token-protected, evidence-oriented, and served with no-store/nosniff/no-referrer/CSP headers | `src/Engine/Host.cs`, `src/Engine/Program.cs`, package helper scripts | dashboard one-shot smoke; packaged dashboard-task and evidence export/import smokes; `EngineHarness` header assertions |

## Repeatable Commands

Trusted-tester walkthrough:

```powershell
docs/Alpha-Tester-Walkthrough.md
```

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
powershell -ExecutionPolicy Bypass -File scripts/Import-AlphaProfile.ps1
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

Extracted release package self-check:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-AlphaReleasePackage.ps1 -RunDashboardSmoke
```

Trusted-tester dashboard launcher:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Start-AlphaDashboard.ps1
```

Safe local demo cycle:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Run-AlphaDemoCycle.ps1
```

Live ATS board ingest:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Run-AlphaScoutBoards.ps1
```

Selected stored-job draft package:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Draft-AlphaJob.ps1 -JobId 1
```

Live L1 Gmail draft cycle:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Run-AlphaLiveCycle.ps1
```

Local alpha evidence package:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Export-AlphaEvidencePackage.ps1
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
- Profile import: `import-profile` requires the CareerSeeker alpha profile format and replaces the local profile
  claim oracle instead of mixing imported claims with seeded demo facts; duplicate claim ids are refused before
  replacement.
- Local dashboard controls: loopback dashboard has token-protected Gmail disconnect, application controls,
  hash-only audit JSON export, alpha package export, and token-protected document downloads. Application
  controls are hidden for terminal rows. Dashboard/document responses carry no-store, nosniff, no-referrer,
  and form-scoped CSP headers.
- Store audit chain: local SQLite and in-memory stores share hash-chain verification and parity coverage.
- Secret handling: `secrets/`, `.appdata/`, generated artifacts, OAuth tokens, and provider keys are ignored and
  should not be printed.
- Tester launchers: live Gmail drafts require typing `LIVE`; audit payload export requires `PAYLOADS`;
  provider-key clear requires `CLEAR`; Gmail disconnect requires `DISCONNECT`; dashboard task install/remove
  requires `INSTALL` or `UNINSTALL`; selected-job prompt-injection override requires `REVIEWED`.
  Confirmation variables are cleared before prompting and evaluated through environment-backed PowerShell checks.
- Free-form tester inputs in selected-job draft, company research, and package import launchers are forwarded
  through environment-backed PowerShell argument arrays instead of interpolated directly into batch command lines.

## Current Alpha Capabilities

- Runnable `src/Engine` executable with `demo`, `alpha`, `dashboard`, `scout-boards`, `draft-job`,
  `research-company`, `profile-template`, `import-profile`, `doctor`, `export-audit`, `export-alpha-package`,
  `import-alpha-package`, `control-app`, Gmail OAuth connect/disconnect, and BYOK import/clear modes.
- Live Greenhouse/Lever/Ashby board ingestion into SQLite with local posting-body artifacts.
- Selected stored job drafting with posting-body context and dry-run verification.
- Real ATS-clean resume PDF renderer and Gmail draft attachment packaging.
- Standalone localhost dashboard over an existing SQLite alpha DB.
- Dashboard `/applications`, `/jobs`, `/evidence.html`, `/evidence`, application controls, Gmail disconnect,
  token-protected alpha package export, local resume/cover document routes, visible job ids for selected-job
  drafting, and a responsive shared alpha shell for status and recent-item views.
- BYOK Anthropic/Gemini Tailor and Gate wiring through the Gateway.
- Brave Search + BYOK company dossier command with deterministic grounding and fallback source snippets.
- Local source-of-truth profile template/import commands for Tailor/Gate facts, with format validation on import.
- Local alpha ZIP package export/import with manifest, audit export, SQLite snapshot, draft artifacts, and saved
  job-description artifacts; secret/token/key-looking paths are filtered, import requires the CareerSeeker alpha
  manifest, unsafe ZIP paths are rejected, and import verifies the restored SQLite audit chain.
- Trusted-tester release ZIP packaging for the published executable, native runtime dependencies, workspace
  initializer, double-click setup/profile/provider/Gmail/live-readiness/provider-clear/Gmail-disconnect/demo/scout/company-research/selected-job/live/audit-export/evidence-export/evidence-import/verify/dashboard and dashboard-task launchers, quickstart, tester walkthrough, package-local audit snapshot, release manifest, dashboard/helper
  self-check scripts, SHA-256 checksums, and selected trust/audit docs without local databases, vaults, provider
  keys, or generated artifacts. Live draft, destructive local off-ramp, and persistent dashboard task launchers
  require typed confirmation.
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
- Historical audit context, with a supersession note at the top: `docs/repo-audit-2026-07-13.md`
- Trust docs: `docs/Privacy-Policy.md`, `docs/Support.md`, `docs/Autonomy-Contract.md`
- Verification script: `scripts/Verify-Alpha.ps1`

