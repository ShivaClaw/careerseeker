# CareerSeeker External Audit Handoff

Updated: 2026-08-07
Audit target: Windows L1 Drafts Beta on `main`

## Audit question

Does the current source discover and rank real jobs, recover safely after crashes, prepare only
source-supported application materials, create reviewable Gmail drafts without a send path, preserve local
user control, and describe its actual installer/onboarding/host limits honestly?

The strongest review order is:

1. `CLAUDE.md` for repository constraints.
2. `docs/BETA-AUDIT-REQUEST.md` for milestone claims and exact re-verification commands.
3. `scripts/Verify-Alpha.ps1` for the pinned offline gate.
4. the invariant surfaces below.
5. `docs/Beta-Runbook.md` for human-only launch work that has not been executed.

## Current evidence

- Pinned offline verifier: **609 passed, 0 failed**.
- GitHub CI runs the warnings-as-errors Release build and the same verifier on `main`, `agent/**`,
  `codex/**`, and pull requests into `main`.
- Real engine path: `run` performs identified Greenhouse/Lever/Ashby discovery, local deterministic ranking,
  quarantine, crash reconciliation, bounded action, and honest scheduler status.
- Recovery harnesses simulate provider success followed by a lost local commit and prove restart/periodic
  reconciliation completes state without a second external effect.
- Ranking harnesses prove deterministic strong > adjacent > unrelated ordering, persisted dashboard
  components, and non-decreasing scores across nested 10/50/200-term profiles. The 120-posting calibration
  corpus holds 8/120 (6.7%) Act eligibility at the derived 4.0 threshold for every profile size.
- B4 executed five bounded discovery-only public ATS cycles: 61 discovered, 14 quarantined, 47 rejected,
  0 drafted, 0 cycle errors. Manual review classified all 14 quarantine flags as benign `act as`
  responsibility prose; tuning is proposed but deliberately not applied.
- The shipped continuous-host fallback is a hardened per-user Scheduled Task supervisor with restart/cycle
  backoff, local logs and controls, and a database-scoped single-instance lock. A native Windows Service/tray
  is not implemented.
- Browser onboarding traverses ten loopback-only steps, performs local resume extraction, requires separate
  provider/Gmail consent, caps AI-extracted claims at `stated`, imports only accepted claims, and defaults
  first run to discovery-only.
- B7 clean package gate at `830f7c1f9deb4d54da2282405e9fbc7ab57d5522`: one unsigned MSIX, one
  `CareerSeeker.exe`, 33,677,037 bytes, SHA-256
  `B831041B7EC0323A4B7EA17F67B1E2889E6C6C5CAD70F9588C900FE2537B65FD`; unpacked onboarding
  smoke reached first-run with zero provider/Gmail calls and preserved an external vault sentinel.
- Historical live connector evidence exists for Gmail draft creation, BYOK Anthropic/Gemini calls, Brave
  research, and public ATS reads. B0-B8 work did not repeat Gmail/provider live calls unless explicitly
  recorded in its milestone entry.

## Invariant map

| Invariant/capability | Primary implementation | Repeatable evidence |
|---|---|---|
| Unsupported claims cannot draft | `src/Verifier/FabricationGate.cs`, `src/Pipeline/ApplicationPipeline.cs` | `Slice`, `LifecycleHarness` |
| Gate tier is pinned and fail-closed | `src/Gateway/Routing.cs`, `src/Gateway/Stages.cs` | `GatewayGateHarness`, `Slice` |
| L1 has no Gmail send operation | `src/Dispatcher/Dispatch.cs`, `src/Dispatcher/Dispatcher.cs` | `DispatcherNoSendHarness` |
| `gmail.compose` capability is described honestly | `src/Dispatcher/GoogleOAuth.cs`, `docs-site/privacy.md`, `docs-site/autonomy-contract.md` | verifier trust wording smoke |
| Real public-board engine path | `src/Engine/Program.cs`, `src/Engine/ScoutJobFeed.cs` | `EngineHarness`, bounded B1/B4 ATS evidence |
| Injection signals quarantine before action/model work | `src/Engine/EngineCore.cs`, `src/Scout/Injection.cs` | `EngineHarness`; B4 rate report |
| Crash-window recovery does not repeat a successful effect | `src/Engine/EngineCore.cs`, `src/Pipeline/ApplicationPipeline.cs` | `EngineHarness`, `LifecycleHarness` |
| Ranking is local, deterministic, explainable | `src/Engine/LexicalSemanticScorer.cs` | `EngineHarness` |
| Status distinguishes viewer/starting/running/paused/faulted/stopped | `src/Engine/Host.cs`, `src/Engine/EngineCore.cs` | `EngineHarness` |
| Engine is single-instance per DB and supervised | `src/Engine/SingleInstanceLease.cs`, `scripts/Start-BetaEngineHost.ps1` | `EngineHarness`; supervisor self-test |
| Onboarding is loopback, consent-bound, and claim-reviewing | `src/Engine/BetaSetupWebFlow.cs` | `EngineHarness`; packaged setup smoke |
| MSIX has one exe and external user data | `scripts/Package-BetaRelease.ps1`, `src/Engine/PackagedRuntime.cs` | `scripts/Test-BetaReleasePackage.ps1` |
| Full-data deletion is exact-path and separately confirmed | `src/Engine/FullDataDeletion.cs`, `src/Engine/Program.cs` | `EngineHarness`; verifier confirmation preview |
| ATS-clean PDFs are deterministic | `src/Dispatcher/AtsPdfDocumentRenderer.cs` | `RendererHarness`, `DispatcherNoSendHarness` |
| Company research is grounded-or-dropped | `src/Researcher/Researcher.cs`, `src/Researcher/Grounding.cs` | `ResearcherHarness` |
| Dashboard/control surface is loopback/token bounded | `src/Engine/Host.cs`, `src/Engine/BetaSetupWebFlow.cs` | `EngineHarness` |
| Audit is hash-chained; default export is hash-only | `src/Store/Audit.cs`, `src/Engine/AlphaAuditExport.cs` | `EngineHarness`, `StoreParityHarness` |
| Secrets and mutable state are excluded from installer/source | `.gitignore`, package/self-check scripts | verifier hygiene and B7 package self-check |

## Repeatable commands

Default offline gate:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1
```

Clean publish and package gate:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage
```

Non-installing MSIX audit:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Test-BetaReleasePackage.ps1
```

Safe real-board discovery:

```powershell
dotnet src\Engine\bin\Release\net8.0\SeekerSvc.Engine.dll run `
  --once --dry-run --llm fake --board greenhouse:remotecom `
  --db tmp\audit\cycle.db --artifacts tmp\audit\artifacts `
  --jd-dir tmp\audit\job-descriptions --max-drafts-per-cycle 0
```

Service-host structure without registration:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Manage-AlphaDashboardTask.ps1 `
  -Action Install -DryRun -Published
powershell -ExecutionPolicy Bypass -File scripts\Start-BetaEngineHost.ps1 `
  -SupervisorSelfTest -ControlDirectory tmp\host-audit\control `
  -LogDirectory tmp\host-audit\logs -MaximumRestartDelaySeconds 5
```

Dependency advisories:

```powershell
dotnet list CareerSeeker.sln package --vulnerable --include-transitive
dotnet list tools\WindowsSdkTools\WindowsSdkTools.csproj package --vulnerable --include-transitive
```

Optional `-IncludeLive` and `-IncludeResearch` checks require existing ignored local credentials and can
create provider cost or a Gmail draft. They are not needed to reproduce the offline Beta evidence.

## Safety surfaces to inspect adversarially

- Verify quarantine precedes action-cap consumption and model calls.
- Verify `SubmitAsync` remains unsupported and no public Dispatcher send method exists.
- Verify the package default after onboarding is `dryRun` plus `serviceHost`.
- Verify package identity cannot be asserted by an argument.
- Verify package removal instructions separate app removal from user-data deletion.
- Verify `delete-all-data` accepts only the displayed path-bound phrase and never follows directory links.
- Verify provider failures never delete/replace a previously usable key vault without explicit replacement.
- Verify AI resume extraction never promotes a claim above `stated` and preserves source/evidence.
- Verify crash recovery reads the effect-attempt journal and never retries a `SUCCEEDED` effect.
- Verify dashboard viewer-only state never claims an engine is running.
- Verify evidence imports reject unsafe, duplicate, excessive, secret-looking, and unsupported ZIP entries.
- Verify every external string entering prompts is encoded as untrusted data.

## Known gaps and non-claims

- The MSIX is unsigned. It was created/unpacked, not installed, signed, removed through Windows, or
  reboot-tested.
- Start-menu registration, Startup Apps behavior, and Windows uninstall UI are structurally declared but not
  claimed as executed.
- Native Windows Service, tray, and WinUI shell are not built.
- OAuth production verification and Google-directed CASA assessment are pending.
- The repository `docs-site` truth-copy updates require a separate human deployment; no B8 deployment occurred.
- The `role_reassign` detector has a measured high false-positive rate for ordinary “act as” job prose.
  Proposed tuning is not applied.
- Historical Alpha `.cmd` launchers and `Package-AlphaRelease.ps1` remain in source, but the current
  `-IncludePackage` product artifact is the MSIX.
- Local evidence export/import still uses ZIP; that is user evidence, not the application installer.
- No Android/relay scope is part of this Windows audit.
- No L2/L3 send, inbox, calendar, or ATS-submit capability exists.

## Useful entry points

- Current implementation summary: `docs/CareerSeeker-Project-Summary.md`
- Claims register: `docs/Positioning.md`
- Human launch list: `docs/Beta-Runbook.md`
- Milestone re-verification: `docs/BETA-AUDIT-REQUEST.md`
- B4 measurement: `docs/Injection-Rate-Report-2026-08.md`
- Windows package runbook: `docs/Beta-Windows-Package-Runbook.md`
