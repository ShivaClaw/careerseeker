# CareerSeeker Beta Audit Request

Updated: 2026-07-30

This is the adversarial review index for the Windows Beta milestone ladder.
Each claim below is limited to evidence executed by Terra in the session that
recorded it. Commands are written from the repository root on Windows.

## B2 - Crash recovery

Branch: `codex/beta-M2-crash-recovery`

### Claims and re-verification

| Claim | Exact reviewer command | Observed 2026-07-30 |
|---|---|---|
| B2 starts from the confirmed B1 merge. | `git fetch --all; git rev-parse origin/main` | `b5b4a98749d5bff814d067d37c310512c7e8b70b`. |
| The startup sweep covers every local application in `SUBMITTING` or `READY`; each decision is side-effect-free. | `rg -n "GetApplicationIdsInStatesAsync\|ReconcileAsync\|ReconcileAllAsync\|ReconcileStartupAsync" src\Pipeline\ApplicationPipeline.cs src\Engine\Program.cs` | Startup composition invokes `ReconcileAllAsync`; its state query is limited to `SUBMITTING`/`READY`; completed effects only commit missing local transitions. |
| Every engine cycle reconciles before discovery, so a surviving process self-heals on its next periodic tick. | `git show f97fbc0 -- src/Engine/EngineCore.cs tests/EngineHarness/Program.cs` | Reconciliation precedes both identified and synthetic discovery; the scheduled-tick crash fixture reached `DRAFTED` with zero new Gmail calls and one recorded attempt. |
| A new process self-heals the persisted provider-success/lost-commit shape. | `dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build` | `127 passed, 0 failed`, including a fresh `demo --once` process reopening SQLite and reconciling `READY + SUCCEEDED` without another effect attempt. |
| Unknown provider outcomes remain manual-review-only and periodic sweeps do not flood the audit log. | `dotnet run --project tests\LifecycleHarness\LifecycleHarness.csproj -c Release --no-build` | `45 passed, 0 failed`; repeated sweep kept the application unresolved and the matching manual-review event count at one. |
| Count/docs/verifier moved together to 373. | `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1` | `Offline total: 373 passed, 0 failed`. |
| Frozen Android/relay paths are absent from the milestone diff. | `git diff --name-only b5b4a98749d5bff814d067d37c310512c7e8b70b...codex/beta-M2-crash-recovery \| rg '^(relay/|docs/Sync-Protocol\.md$|docs/sync-vectors/|.*Android|.*android)'` | Expected: no output, exit 1 from `rg`. |

### Scope exclusions

No Gmail draft, provider call, email, public ATS request, upload, deployment,
scheduled-task registration, Cloudflare action, Google/Play console change,
Android/relay/sync-vector change, off-repo site edit, dependency change, or
secret print was performed.

## B1 - Engine actually runs

Branch: `codex/beta-M1-engine-runs`

### Claims and re-verification

| Claim | Exact reviewer command | Observed 2026-07-30 |
|---|---|---|
| The reviewed honesty-fix branch was exactly `40bc9a7166afb7d9742d75ef1b93b2ce0c8f5c1b`. | `git fetch --all; git rev-parse origin/fix/engine-actually-runs` | Exact SHA matched. |
| Frozen Android/relay paths are absent from the milestone diff. | `git diff --name-only origin/main...codex/beta-M1-engine-runs \| rg '^(relay/|docs/Sync-Protocol\.md$|docs/sync-vectors/|.*Android|.*android)'` | Expected: no output, exit 1 from `rg`. |
| Quarantine is evaluated before the action cap and never reaches the model path. | `git diff origin/main...codex/beta-M1-engine-runs -- src/Engine/EngineCore.cs tests/EngineHarness/Program.cs` | Inspect `LikelyInjected` branch before cap/semantic calls; harness pins quarantine after cap fills. |
| Dashboard status comes from `PeriodicScheduler.State`, including `Faulted`; viewer-only never claims running. | `git diff origin/main...codex/beta-M1-engine-runs -- src/Engine/Host.cs tests/EngineHarness/Program.cs` | EngineHarness status-honesty assertions passed. |
| Real Scout jobs retain company/board identity and external IDs. | `git diff origin/main...codex/beta-M1-engine-runs -- src/Engine/ScoutJobFeed.cs src/Store/Ingest.cs tests/EngineHarness/Program.cs` | Identified-feed identity and persisted dedupe assertions passed. |
| Periodic cycles do not create a second application/draft for an already-admitted job, and capped work advances. | `dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release` | `124 passed, 0 failed`, including no-repeat application, next-cycle advancement, and never-redraft assertions. |
| Per-job application existence reads agree in memory and SQLite. | `dotnet run --project tests\StoreParityHarness\StoreParityHarness.csproj -c Release` | `23 passed, 0 failed`. |
| Discovery-only creates no simulated draft or application, while live fake-LLM pairing fails closed. | `dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release; dotnet src\Engine\bin\Release\net8.0\SeekerSvc.Engine.dll run --once --llm fake` | Harness `124/0`; command refused fake LLM on live Gmail path with exit 2. |
| Count/docs/verifier moved together to 369. | `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1` | `Offline total: 369 passed, 0 failed`. |
| Publish/package path is green from a clean commit. | `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage` | Offline `369/0`; publish demo `errors: 0`; manifest/OAuth/checksum/dashboard/setup checks passed. |
| The bounded public-ATS run is draft-free and audit-clean. | `dotnet src\Engine\bin\Release\net8.0\SeekerSvc.Engine.dll run --once --dry-run --llm fake --board greenhouse:remotecom --discovery-timeout-seconds 90 --http-timeout-seconds 30 --max-drafts-per-cycle 2 --db tmp\beta-b1-live\careerseeker.db --artifacts tmp\beta-b1-live\artifacts --jd-dir tmp\beta-b1-live\job-descriptions` | Observed 61 discovered, 41 rejected, 14 quarantined, 0 acted, 0 drafted, 0 errors, audit ok. Public board counts are volatile; safety outcomes must remain. |

### Adversarial findings fixed

The original branch was offline-green at 364 but was not safe to merge unchanged:
it re-admitted the same job every periodic tick and represented fake-client
dry-runs as `DRAFTED`. Both were fixed and regression-tested before the B1 PR.

No Gmail draft, provider call, email, upload, deployment, scheduled-task
registration, Cloudflare action, Google/Play console change, Android change, or
secret print was performed.

## B0 - Preflight baseline

Branch: `codex/beta-M0-preflight`

### Claims and re-verification

| Claim | Exact reviewer command | Observed 2026-07-30 |
|---|---|---|
| Remote `main` baseline is `14a7dfec374cda410aa28b13c456d695f38e3507`. | `git fetch --all; git rev-parse origin/main` | Exact SHA matched. |
| The unmerged honesty-fix tip is `40bc9a7166afb7d9742d75ef1b93b2ce0c8f5c1b`. | `git fetch --all; git rev-parse origin/fix/engine-actually-runs` | Exact SHA matched. |
| Release build is warning/error clean on the B0 base. | `dotnet build CareerSeeker.sln -c Release --warnaserror` | `Build succeeded`, `0 Warning(s)`, `0 Error(s)`. |
| The pinned offline Alpha baseline is green at 341. | `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1` | `Offline total: 341 passed, 0 failed`. |
| Local publish and package paths complete. | `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage` | Offline 341/0; published demo `errors: 0`; manifest and installed/Desktop OAuth checks passed; 50 checksums verified; dashboard and Alpha 2.0 setup smokes passed. |
| B0 changed documentation only and did not touch the frozen Android program. | `git diff --name-only origin/main...codex/beta-M0-preflight` | Expected after the B0 commit: only `docs/Codex-Resume-Handoff.md` and `docs/BETA-AUDIT-REQUEST.md`. |
| B0 did not change the pinned assertion total or count-bearing docs. | `git diff origin/main...codex/beta-M0-preflight -- scripts/Verify-Alpha.ps1 README.md src/Engine/README.md docs/CareerSeeker-Project-Summary.md docs/External-Audit-Handoff.md` | Expected: no output. |

### Scope exclusions

The package was built locally under ignored output paths. No Cloudflare,
Google Console, OAuth test-user, Play Console, email, purchase, new-scope,
off-repo site, relay, or Android action was performed. No live BYOK, Brave, or
Gmail smoke was part of B0.
