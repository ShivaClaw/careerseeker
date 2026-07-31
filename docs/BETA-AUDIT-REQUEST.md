# CareerSeeker Beta Audit Request

Updated: 2026-07-30

This is the adversarial review index for the Windows Beta milestone ladder.
Each claim below is limited to evidence executed by Terra in the session that
recorded it. Commands are written from the repository root on Windows.

## B8 - Evidence, positioning, and human runbook

Branch: `codex/beta-M8-evidence`

### Claims and re-verification

| Claim | Exact reviewer command | Observed 2026-07-30 |
|---|---|---|
| Current public/repository copy describes the unsigned one-exe MSIX and does not present the historical Alpha ZIP/helpers as the current app artifact. | `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1` | At `5d3d86122c0ccad3b6ab10f918c553e9aee76ba5`: docs assertions and all offline harnesses passed, 407/0. |
| Trust-copy Markdown pairs are byte-identical. | `$pairs=@(@('docs-site/privacy.md','docs/Privacy-Policy.md'),@('docs-site/support.md','docs/Support.md'),@('docs-site/autonomy-contract.md','docs/Autonomy-Contract.md')); $pairs \| ForEach-Object { (Get-FileHash $_[0]).Hash -eq (Get-FileHash $_[1]).Hash }` | `True`, `True`, `True`. |
| The claims register maps public sentences to invariants, harnesses, and source lines while highlighting unsupported operational/marketing claims. | `rg -n "UNPROVEN|signed and trusted|survives reboot|production-ready|whole-product|server-retention|support" docs\Positioning.md` | `UNPROVEN` rows exist for analytics/tracker inventory, broad retention wording, signing, reboot, production readiness, and support SLA evidence. |
| Brandon has one ordered human-only list for deployment, rate limiting, OAuth queue/verification/CASA, signing, installer testing, and publication. | `rg -n "single ordered Sunday list|Deploy the truth copy|Protect .api.signup|OAuth test-user queue|OAuth production verification|CASA|MSIX signing|installer matrix|Publish the signed Beta|PENDING" docs\Beta-Runbook.md` | All sections are present and the runbook says Terra executed none; unexecuted work remains `PENDING`. |
| The repository trust pages render locally with the expected Beta headings and controls. | `python -m http.server 8765 --bind 127.0.0.1 --directory docs-site` then open `http://127.0.0.1:8765/index.html`, `privacy.html`, `support.html`, and `autonomy-contract.html` in a browser. | The in-app Browser loaded all four pages; expected headings appeared; privacy visibly included Beta, unsigned-MSIX, and `%LOCALAPPDATA%\CareerSeeker` wording. The temporary server/tab were then stopped/closed. |
| The clean current artifact remains one unsigned MSIX/one exe with no provider/Gmail calls and external user data preserved. | `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage` | At `5d3d86122c0ccad3b6ab10f918c553e9aee76ba5`: 407/0; one `CareerSeeker.exe`; provider calls 0; Gmail calls/drafts 0; workspace sentinel preserved; 33,677,048 bytes; SHA-256 `1EDC46E3731A449C4DCCA02FA7464CBCF5D2EDC7FADF3EFA3EEF8F0B8C7B7B39`. |
| Frozen Android/relay paths are absent from the milestone diff. | `git diff --name-only 1308345e10e93ee10fe40a3e6aa494ace17f936f...HEAD \| rg '^(relay/|docs/Sync-Protocol\.md$|docs/sync-vectors/|.*Android|.*android)'` | Expected: no output, exit 1 from `rg`. |

### Verification boundary

The verifier initially exposed an encoding-sensitive PowerShell assertion and
two exact-copy mismatches; those runs stopped and are not claimed as passes.
The clean offline and full publish/package commands above passed afterward.

The MSIX was created and unpacked, not signed, installed, registered,
Windows-uninstalled, or reboot-tested. The docs site was served only from
loopback; no production deploy occurred. No Gmail draft, provider call, send,
public ATS request, Cloudflare mutation, Google/Play console change, OAuth
queue read/write, scheduled-task registration, Android/relay/sync-vector
change, off-repo site edit, purchase, new scope, account/config change, or
secret print was performed.

## B7 - Single-executable MSIX

Branch: `codex/beta-M7-installer`

### Claims and re-verification

| Claim | Exact reviewer command | Observed 2026-07-30 |
|---|---|---|
| `-IncludePackage` now produces one MSIX artifact with exactly one executable and no duplicate setup launcher. | `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage` | At `830f7c1f9deb4d54da2282405e9fbc7ab57d5522`: 407/0; MakeAppx created and unpacked the MSIX; self-check found one `CareerSeeker.exe`; 33,677,037 bytes; SHA-256 `B831041B7EC0323A4B7EA17F67B1E2889E6C6C5CAD70F9588C900FE2537B65FD`. |
| The no-console executable defaults to browser setup and the unpacked package completes the ten-step smoke without provider/Gmail calls. | `powershell -ExecutionPolicy Bypass -File scripts\Test-BetaReleasePackage.ps1` | `CareerSeeker.exe` was invoked without an explicit mode; route sequence reached first-run; provider calls 0; Gmail calls/drafts 0. |
| Windows package metadata supplies the Start-menu application, full-trust entry point, integrity declaration, and optional startup task disabled by default. | `powershell -ExecutionPolicy Bypass -File scripts\Test-BetaReleasePackage.ps1` | Manifest XPath checks passed for `CareerSeeker.LocalBeta`, `Windows.FullTrustApplication`, `runFullTrust`, `PackageIntegrity/Content Enforcement=on`, and `StartupTask Enabled=false`. |
| Package activation uses a per-user external workspace; automatic packaged activation is discovery-only and cannot imply draft consent. | `rg -n "WorkspaceRoot|Environment.CurrentDirectory|packagedDefault|dryRun|serviceHost|File.Copy" src\Engine\PackagedRuntime.cs src\Engine\Program.cs` | Mutable defaults resolve below `%LOCALAPPDATA%\CareerSeeker`; existing local OAuth metadata is not overwritten; implicit packaged activation sets both dry-run and service-host. |
| Package removal does not delete user data or vaults. | `powershell -ExecutionPolicy Bypass -File scripts\Test-BetaReleasePackage.ps1` | Removing the unpacked package tree preserved a synthetic external `.appdata/secrets/byok-keys.dpapi` sentinel. No real package install/uninstall was performed. |
| The signing hook is present without performing certificate/account work or printing a password. | `rg -n "CAREERSEEKER_SIGNING_PASSWORD|signtool|/fd SHA256|/td SHA256|/tr" scripts\Sign-BetaRelease.ps1 docs\Beta-Windows-Package-Runbook.md` | Hook and human runbook are present. Signing was not executed. |
| The new Microsoft build-tool dependency is locked and has no reported vulnerability. | `dotnet list tools\WindowsSdkTools\WindowsSdkTools.csproj package --vulnerable --include-transitive` | `WindowsSdkTools has no vulnerable packages given the current sources.` |
| The B7 artifact is 31,380,867 bytes (48.24%) smaller than the B6 ZIP. | `$old=65057904; $new=(Get-Item output\release\CareerSeeker-beta-win-x64.msix).Length; [pscustomobject]@{Old=$old;New=$new;Reduction=$old-$new;Percent=[math]::Round((($old-$new)/$old)*100,2)}` | Old 65,057,904; new 33,677,037; reduction 31,380,867; 48.24%. |
| Frozen Android/relay paths are absent from the milestone diff. | `git diff --name-only efd31671f7edd8c02900bc8f702e7b9893d4d1fd...HEAD \| rg '^(relay/|docs/Sync-Protocol\.md$|docs/sync-vectors/|.*Android|.*android)'` | Expected: no output, exit 1 from `rg`. |

### Verification boundary

The first full package attempt found and stopped on a no-mode argument parsing
defect after the 407 offline assertions and package build passed. The defect
was corrected before the clean evidence run above; no pass is claimed for the
first attempt.

The MSIX was created and unpacked, not installed, registered, removed through
Windows, signed, or reboot-tested. Accordingly, the Start-menu, Startup Apps,
real uninstall UI, signature trust, and reboot behavior are structurally
wired but not claimed as executed. No Gmail draft, BYOK/provider call, email,
public ATS call, deployment, scheduled-task registration, Cloudflare action,
Google/Play console change, Android/relay/sync-vector change, off-repo site
edit, purchase, or secret print was performed.

## B6 - Onboarding v2 local web flow

Branch: `codex/beta-M6-onboarding-v2`

### Claims and re-verification

| Claim | Exact reviewer command | Observed 2026-07-30 |
|---|---|---|
| `setup` defaults to a loopback browser flow and the previous wizard remains an explicit console fallback. | `rg -n "HasFlag\\(\"--console\"\\)|BetaSetupWebFlow.RunAsync|setup --console" src\Engine\Program.cs src\Engine\BetaSetupWebFlow.cs` | Normal setup routes to `BetaSetupWebFlow`; `setup --console` routes to the prior bridge. |
| The local flow covers welcome/safety, package verification, resume, provider, extraction consent, per-claim review, Gmail, doctor, and first run. | `dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build` | `159 passed, 0 failed`; the offline smoke traversed all ten HTTP steps, including a synthetic TXT upload and local extraction, with zero provider/Gmail calls. |
| AI-extracted facts cannot be promoted above `stated`; every accepted claim is individually editable/droppable and retains resume provenance/evidence. | `dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build` | A fixture claiming `verified` normalized to `stated`; every fixture claim normalized to `sourceDoc=resume-ai`/`origin=ai-extracted-resume`; instruction-like resume text remained inert data. |
| Package and OAuth checks fail closed, and the web surface is bounded to loopback forms. | `rg -n "VerifyPackageAsync|IsInstalledDesktopOAuthClient|FixedTimeEquals|PromptQuarantine.Encode|Content-Security-Policy|MaxRequestBytes" src\Engine\BetaSetupWebFlow.cs` | SHA-256 mismatch blocks continuation; non-installed OAuth clients are refused; CSRF, loopback/Host checks, CSP/no-store, 21 MiB request cap, and prompt encoding are present. The HTTP smoke rejected a foreign Host and asserted safety headers. |
| Provider failure semantics preserve existing vaults: quota-authenticated credentials are retained; timeout/5xx storage requires a separate unverified action. | `rg -n "CredentialAuthenticated|CanSaveWithoutSuccessfulTest|preserved unchanged|saveUnverified" src\Engine\BetaSetupWebFlow.cs` | No failed provider test deletes or overwrites the previous vault. No provider test was executed in B6 verification. |
| Count/docs/verifier moved together to 407. | `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1` | `Offline total: 407 passed, 0 failed`. |
| The packaged setup executable traverses the web flow from a clean commit. | `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage` | At `0ecd79eba3b8f9b5e9596c4757f9472c4d3f0cf8`: 407/0; package self-check verified 51 checksums and traversed welcome through first-run; provider calls 0, Gmail calls/drafts 0. ZIP SHA-256 `76AC122C106D9D9C732A292E71FECC8564AE94B27AE820F8B73270CECD44DEEB`, 65,057,904 bytes. |
| Frozen Android/relay paths are absent from the milestone diff. | `git diff --name-only origin/main...codex/beta-M6-onboarding-v2 \| rg '^(relay/|docs/Sync-Protocol\.md$|docs/sync-vectors/|.*Android|.*android)'` | Expected: no output, exit 1 from `rg`. |

### Verification boundary

The in-app browser completed the ten screens against an isolated ignored
workspace, imported one manual test claim, skipped Gmail, ran doctor, and
finished without starting an engine. The packaged smoke used a synthetic
resume and manual provider/Gmail skips. No real provider credential, resume,
Gmail account, OAuth callback, or live first-run draft path was exercised.

The first clean package attempt stopped on a stale walkthrough heading before
the setup executable ran; no pass is claimed for that attempt. The assertion
was corrected in commit `0ecd79e`, and the full clean gate then passed.

No Gmail draft, BYOK/provider call, email, public ATS call, deployment,
scheduled-task registration, Cloudflare action, Google/Play console change,
Android/relay/sync-vector change, off-repo site edit, dependency addition, or
secret print was performed.

## B5 - Service-grade scheduled-task host

Branch: `codex/beta-M5-service-grade`

### Claims and re-verification

| Claim | Exact reviewer command | Observed 2026-07-30 |
|---|---|---|
| The shipped fallback starts the real `run` mode, not a viewer, and does not exit on closed stdin. | `rg -n "service-host\|Start-BetaEngineHost\|Task.Delay\\(Timeout.InfiniteTimeSpan" src\Engine\Program.cs scripts\Manage-AlphaDashboardTask.ps1 scripts\Start-BetaEngineHost.ps1` | Scheduled task targets the supervisor; child arguments contain `run --service-host`; service mode waits for local stop/control instead of `Console.ReadLine`. |
| One database cannot have two engine processes. | `dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build` | `149 passed, 0 failed`; exclusive lock-file tests cover acquire, duplicate refusal, release/reacquire. A real second process also exited 2 before discovery. |
| Cycle errors cause capped backoff and board failures feed that error signal. | `dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build` | Scheduler delay changed 50→100 ms after an error; a failed fixture board persisted and counted one cycle error. |
| Pause/resume keep the host alive, stop disposes it cleanly, and status reports pause/backoff honestly. | `dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build` | Pause suppressed cycles, resume restarted them, control-file and dashboard runtime assertions passed. The real local host reported `paused` then removed its listener with blank stderr after `stop.request`. |
| Crash supervision logs failures and uses interruptible capped restart backoff. | `powershell -ExecutionPolicy Bypass -File scripts\Start-BetaEngineHost.ps1 -SupervisorSelfTest -ControlDirectory tmp\b5-supervisor-selftest\control -LogDirectory tmp\b5-supervisor-selftest\logs -MaximumRestartDelaySeconds 5` | Simulated child exit 7 was logged; restart 1 was scheduled for 5 seconds; stop during backoff was consumed and supervisor exited 0. |
| Windows accepts the at-logon task definition and no task is registered by verification. | `powershell -ExecutionPolicy Bypass -File scripts\Manage-AlphaDashboardTask.ps1 -Action Install -DryRun -Published` | Task Scheduler cmdlets reported restart count 12, interval `PT1M`, and `IgnoreNew`; task absent before and after. |
| Doctor verifies service-host paths and the duplicate-process rail without provider calls. | `dotnet src\Engine\bin\Release\net8.0\SeekerSvc.Engine.dll doctor --require-service-host --db tmp\b5-doctor\doctor.db --artifacts tmp\b5-doctor\artifacts --control-dir tmp\b5-doctor\control --log-dir tmp\b5-doctor\logs --secrets tmp\b5-doctor\none.env --key-vault tmp\b5-doctor\none.dpapi` | `service_host_paths` and `service_single_instance` both `OK`; secret values were not printed. |
| Count/docs/verifier moved together to 397. | `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1` | `Offline total: 397 passed, 0 failed`. |
| Published executable and packaged service-host path are green from a clean commit. | `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage` | At `f36a4ac80e800127d61a408635767c43581321a9`: offline 397/0, published demo errors 0, package/self-check/task dry-run/evidence import-export completed. |
| Frozen Android/relay paths are absent from the milestone diff. | `git diff --name-only origin/main...codex/beta-M5-service-grade \| rg '^(relay/|docs/Sync-Protocol\.md$|docs/sync-vectors/|.*Android|.*android)'` | Expected: no output, exit 1 from `rg`. |

### Verification boundary

The native Windows Service/SCM and tray UI were not implemented; B5 uses the
roadmap's hardened Scheduled Task fallback. The at-logon definition was
validated by Windows but deliberately not registered, so no real reboot test
is claimed. Process-level start, duplicate refusal, pause, status, and clean
stop were executed against an isolated ignored database. Crash/restart control
flow was executed through the supervisor self-test.

No Gmail draft, BYOK/provider call, send, deployment, scheduled-task
registration, Cloudflare action, Google/Play console change, Android/relay/
sync-vector change, off-repo site edit, dependency change, or secret print was
performed.

## B4 - Quarantine telemetry and measurement

Branch: `codex/beta-M4-quarantine-telemetry`

### Claims and re-verification

| Claim | Exact reviewer command | Observed 2026-07-30 |
|---|---|---|
| Per-cycle counters, board identities, and reason-code counts persist with memory/SQLite parity. | `dotnet run --project tests\StoreParityHarness\StoreParityHarness.csproj -c Release --no-build` | `25 passed, 0 failed`; the exact telemetry row round-tripped in both stores. |
| Engine cycles populate durable telemetry without putting posting bodies in it. | `dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build` | `137 passed, 0 failed`; identified-cycle counter/board/reason assertions passed and posting text was absent. |
| Dashboard evidence and hash-only audit exports expose the aggregate telemetry. | `dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build` | The renderer and JSON assertions found recent cycles; the audit-export assertion found reason codes while default event payloads remained hash-only. |
| Count/docs/verifier moved together to 385. | `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1` | `Offline total: 385 passed, 0 failed`. |
| Bounded public-ATS measurement was draft-free and audit-clean. | See the three exact `run` commands and export command in `docs\Injection-Rate-Report-2026-08.md`. | Five total cycles across the three ATS kinds: 61 discovered, 14 quarantined, 47 rejected, 0 drafted, 0 errors; all three canonical exports reported intact audit chains. |
| The observed flags were assessed without changing the classifier. | `git diff origin/main...codex/beta-M4-quarantine-telemetry -- src\Scout` | Expected: no detector-expression or threshold diff; only `ScoutJobFeed` exposes configured board identity for empty-cycle telemetry. |
| Frozen Android/relay paths are absent from the milestone diff. | `git diff --name-only origin/main...codex/beta-M4-quarantine-telemetry \| rg '^(relay/|docs/Sync-Protocol\.md$|docs/sync-vectors/|.*Android|.*android)'` | Expected: no output, exit 1 from `rg`. |

### Measurement interpretation

The observed signal rate was 14/61 (22.95%), all `role_reassign`. Manual
context review assessed all 14 as benign job-duty language, so the report
proposes narrowing the bare `act as` trigger. The proposal was not applied.
Lever and Ashby returned zero postings in both attempts, so cross-board
classifier comparison remains unproven and is stated as such.

No Gmail draft, BYOK/provider call, email, upload, deployment, scheduled-task
registration, Cloudflare action, Google/Play console change, Android/relay/
sync-vector change, off-repo site edit, dependency change, classifier tuning,
or secret print was performed.

## B3 - Deterministic lexical ranking

Branch: `codex/beta-M3-lexical-ranking`

### Claims and re-verification

| Claim | Exact reviewer command | Observed 2026-07-30 |
|---|---|---|
| B3 starts from the confirmed B2 merge. | `git fetch --all; git rev-parse origin/main` | `1dc1f817e0712b0ea2556d3d2aab46ff9ffd6100`. |
| The placeholder constant is gone from production composition. | `rg -n "DemoSemanticScorer|new LexicalSemanticScorer" src\Engine` | No `DemoSemanticScorer`; `BuildDemoCycleCore` composes `LexicalSemanticScorer` with the active store/profile id. |
| Ranking is local, deterministic, title/Skill weighted, and explainable. | `dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build` | `133 passed, 0 failed`; identical inputs matched exactly, and strong > adjacent > unrelated fixtures. |
| The engine persists components and both stores expose them. | `dotnet run --project tests\StoreParityHarness\StoreParityHarness.csproj -c Release --no-build` | `24 passed, 0 failed`; SQLite positively returned fit, legitimacy, total, subscores, and ranker identity with memory parity. |
| Scored job reads order by meaningful total and `/jobs` renders encoded components/rationale. | `git show 8f65906 -- src/Store/InMemorySeekerStore.cs src/Store/SqliteSeekerStore.cs src/Engine/Host.cs tests/EngineHarness/Program.cs` | EngineHarness pins strong/adjacent/unrelated total order and encoded dashboard fields without full posting text. |
| The safety composition is unchanged. | `git diff 1dc1f817e0712b0ea2556d3d2aab46ff9ffd6100...codex/beta-M3-lexical-ranking -- src/Scorer/Scorer.cs src/Verifier src/Dispatcher` | Expected: no output. |
| Count/docs/verifier moved together to 380. | `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1` | `Offline total: 380 passed, 0 failed`. |
| Frozen Android/relay paths are absent from the milestone diff. | `git diff --name-only 1dc1f817e0712b0ea2556d3d2aab46ff9ffd6100...codex/beta-M3-lexical-ranking \| rg '^(relay/|docs/Sync-Protocol\.md$|docs/sync-vectors/|.*Android|.*android)'` | Expected: no output, exit 1 from `rg`. |

### Bounded limitation

The optional `byok-embed` ranker was not implemented; no provider call was
needed for B3's deterministic-first exit. Three local hidden-process attempts
could not keep the dashboard bound for an in-app Browser visual check, so no
Browser result is claimed. `docs/BETA-BLOCKED.md` records the attempts.

No Gmail draft, BYOK/provider call, email, upload, deployment, scheduled-task
registration, Cloudflare action, Google/Play console change, Android/relay/
sync-vector change, off-repo site edit, dependency change, or secret print was
performed.

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
