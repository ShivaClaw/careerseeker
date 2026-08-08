# Codex Resume Handoff

Updated: 2026-07-30

## 2026-08-07 (Terra R0) - Fresh re-entry verification and autonomy bootstrap

Branch: `codex/r0-bootstrap`, based on fresh `origin/main` at
`e95b1b3ece212d13995fabe6669305be89907bf7`.

Executed evidence:

```text
> git fetch --all --prune
> scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage
Build succeeded.
    0 Warning(s)
    0 Error(s)
=== Offline total: 407 passed, 0 failed ===
```

The same successful command produced exactly one executable payload:
`output\release\_beta-msix-stage\CareerSeeker.exe` (74,606,153 bytes), and
the measured Beta package was:

```text
output\release\CareerSeeker-beta-win-x64.msix
Bytes: 33,672,974
SHA-256: F3B16A0EE5B0B6EF882BCE8C9132C1C87DDA3159D389A2E45E6C0254FA1CC689
```

This bootstrap adds the release-window mission, R0-R7 ladder, and root agent
pointer. The directly pushed `autonomy/codex-state` branch is the live
coordination heartbeat; `autonomy/claude-state` was absent after the initial
fetch, so no Claude file claim could be read.

Boundary: no deploy, console, email, purchase, signing, install, secret
access, certificate/store mutation, reboot, scheduled-task registration,
off-repo site edit, force-push, history rewrite, `.appdata`-original mutation,
or live provider/Gmail action occurred. The verification script only ran its
offline/package/publish paths.

The bytes remained 33,672,974 across the two R0 package runs, while the
unsigned MSIX SHA-256 changed. This entry therefore records the final
post-bootstrap gate's observed hash above and does not assert reproducibility.

## 2026-07-30 (Terra post-B8) - Ordered Beta hardening backlog

Branch: `codex/beta-hardening`

Integration base: `origin/main` at
`89ef81002410cc85ad49f529e571be3b5b4de5c1`, the confirmed B8 PR #17
merge. Implementation commit:
`596a770a61eee8c73ee1b891e23dee82733e94c3`
(`Harden beta migration import dashboard and doctor`).

The roadmap's six ordered post-B8 hardening items are complete:

1. `StoreParityHarness --migration-copy` uses SQLite's read-only backup API
   to copy a supplied database into a randomized system-temp directory. It
   migrates the copy twice, verifies integrity, preserves every pre-existing
   table row count, requires the four current application columns, and checks
   the source file's length, timestamp, and SHA-256 before/after. It reports
   candidate numbers and structural results, never source rows or paths.
2. Alpha-package import now rejects rooted/fully-qualified names, traversal,
   empty/dot segments, ADS/colon names, control characters, trailing
   dot/space aliases, Windows device names, and names over 1,024 characters.
   The existing secret-path quarantine remains in force. EngineHarness
   exercises 35 deterministic accepted/rejected entry-name cases inside the
   existing assertion, so the pinned count remains 407.
3. The local dashboard now has a skip link, visible focus treatment,
   language/main/navigation semantics, `aria-current`, labeled scrollable
   table regions, captions and column scopes, contextual application-control
   labels, and a manual Refresh status link. The five-second meta refresh was
   removed because it reset keyboard focus.
4. Required service-host doctor checks now acquire the lease for the actual
   configured database, so a database already owned by another engine fails
   closed. Tests also cover missing host directories.
5. Ten consecutive release-mode EngineHarness runs passed 159/0; observed
   durations were 4.570-4.800 seconds.
6. Latest .NET analyzers passed with warnings treated as errors, and
   `dotnet format ... analyzers --verify-no-changes` passed. The unreachable
   422-line historical Alpha ZIP verifier was removed from
   `Verify-Alpha.ps1`; the active Beta MSIX verifier is unchanged.
   PSScriptAnalyzer was not installed, so no PowerShell-analyzer pass is
   claimed and no module was installed.

Executed migration-copy evidence:

```text
> dotnet run --project tests\StoreParityHarness\StoreParityHarness.csproj -c Release --no-build -- --migration-copy .appdata\careerseeker-alpha.db --migration-copy .appdata\imported-smoke\careerseeker-alpha.db
=== CareerSeeker real Alpha DB migration-copy matrix ===
  PASS  candidate 1: copied migration is intact/idempotent and source is unchanged
  PASS  candidate 2: copied migration is intact/idempotent and source is unchanged
=== 2 passed, 0 failed ===
```

The first two-source migration attempt proved candidate 1, then failed while
removing SQLite auxiliary temp files; that run is not claimed as a matrix
pass. Cleanup was corrected with connection-pool clearing and a validated,
randomized system-temp prefix. The subsequent command above passed both real
Alpha databases. Only the temporary copy was deleted; both source databases
remain.

Executed timing evidence:

```text
> $runs = @(); foreach ($i in 1..10) { $sw = [System.Diagnostics.Stopwatch]::StartNew(); $output = & dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build 2>&1; $exitCode = $LASTEXITCODE; $sw.Stop(); $summary = $output | Where-Object { $_ -match '^=== \d+ passed, \d+ failed ===$' } | Select-Object -Last 1; $runs += [pscustomobject]@{ Run = $i; Exit = $exitCode; Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 3); Summary = [string]$summary }; if ($exitCode -ne 0) { $output | Select-Object -Last 40; break } }; $runs | Format-Table -AutoSize; if (($runs | Where-Object Exit -ne 0).Count -gt 0) { exit 1 }
Runs 1-10: exit 0; each `=== 159 passed, 0 failed ===`
Seconds: 4.800, 4.679, 4.570, 4.619, 4.631, 4.607, 4.584, 4.645, 4.602, 4.745
```

Executed analyzer evidence:

```text
> dotnet build CareerSeeker.sln -c Release --no-restore -warnaserror -p:EnableNETAnalyzers=true -p:AnalysisLevel=latest
Build succeeded.
    0 Warning(s)
    0 Error(s)

> dotnet format CareerSeeker.sln analyzers --verify-no-changes --severity warn --no-restore
Exit code: 0

> Get-Module -ListAvailable -Name PSScriptAnalyzer
PSScriptAnalyzer: unavailable; no installation attempted
```

The clean full gate passed at
`596a770a61eee8c73ee1b891e23dee82733e94c3`:

```text
> powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage
Build succeeded.
    0 Warning(s)
    0 Error(s)
=== Offline total: 407 passed, 0 failed ===
Package creation succeeded.
Beta MSIX: C:\Users\bkirk\Documents\CareerSeeker\output\release\CareerSeeker-beta-win-x64.msix
Bytes: 33673443
SHA-256: 41F456F09413758E707B556CBAF79EEFC268FE9B93EB67FF0EFE09C362B0271F
Executable payload: CareerSeeker.exe (one .exe)
Setup smoke completed through the local web flow.
  AI provider calls: 0
  Gmail calls/drafts: 0
Beta package self-check passed.
  identity: CareerSeeker.LocalBeta
  executable payload: 1 (CareerSeeker.exe)
  startup task: optional, disabled by default
  external user workspace preserved: yes
CareerSeeker alpha verification complete.
```

Dashboard QA boundary: the in-app Browser rendered an isolated loopback host
cleanly, exposed native links/buttons in its semantic tree, and one real Tab
transition visibly moved focus to the Jobs link. Repeated synthetic Tab
injection was intermittent, so a complete keystroke sequence is not claimed.
Static accessibility assertions are repeatable in EngineHarness. Starting the
isolated host also completed one bounded read-only public ATS cycle: 255
discovered, 34 quarantined, 203 rejected, 0 drafted, 0 errors. No provider or
Gmail call occurred. The host stopped through its local `stop.request`, its
listener was confirmed absent, and all Browser tabs were finalized.

Verification boundary: the package was created and unpacked, not signed,
installed, registered, Windows-uninstalled, or reboot-tested. No Gmail draft,
BYOK/provider call, send, deployment, scheduled-task registration, Cloudflare
mutation, Google/Play console change, OAuth queue read/write, Android/relay/
sync-vector change, off-repo site edit, purchase, new scope, account/config
change, or secret print occurred. The only network activity in this
hardening batch was the bounded public ATS read described above.

## 2026-07-30 (Terra B8) - Evidence, positioning, and human runbook

Branch: `codex/beta-M8-evidence`

Integration base: `origin/main` at
`1308345e10e93ee10fe40a3e6aa494ace17f936f`, the confirmed PR #16 merge.
Implementation commit:
`5d3d86122c0ccad3b6ab10f918c553e9aee76ba5`
(`docs(beta): reconcile public claims and operator runbook`).

B8 found a material documentation/repository divergence: README, Engine
README, project summary, audit handoff, and trust pages still described the
historical trusted-tester Alpha ZIP and `.cmd` helpers as the current product
surface. The repository's current artifact is the B7 unsigned MSIX with one
`CareerSeeker.exe`; Alpha launch/package helpers and evidence ZIP
export/import remain historical or advanced source tooling. The public copy
now says so explicitly.

The sweep updated README, Engine README, project summary, external-audit
handoff, and the repository/docs-site privacy, support, and autonomy copies.
All three Markdown trust-copy pairs are byte-identical. The wording scopes
local/server-retention claims to Windows-engine data, acknowledges the
separate tester-signup metadata service, labels support times as targets, and
does not claim signing, real install/uninstall, reboot survival, or production
readiness.

`docs/Positioning.md` is the requested sentence-to-invariant/harness/source
register. Its `UNPROVEN` rows cover whole-product analytics/tracker inventory,
broad server-retention wording, production signature, real reboot behavior,
production readiness, and support SLA evidence. `docs/Beta-Runbook.md` is one
ordered Sunday human list for the merge gate, truth-copy deployment,
`/api/signup` rate limiting, current OAuth test-user queue, OAuth verification
then Google-directed CASA work, MSIX signing, disposable Windows
install/reboot/uninstall testing, publication, and closeout. Every step is
marked human-only/PENDING until executed.

The in-app Browser rendered the local `docs-site` landing, privacy, support,
and autonomy pages. Expected headings and Beta wording were present; the
privacy page visibly disclosed the unsigned MSIX and
`%LOCALAPPDATA%\CareerSeeker` preservation path. The temporary loopback docs
server and tab were closed afterward. No production site was opened or
changed.

The clean full gate passed at
`5d3d86122c0ccad3b6ab10f918c553e9aee76ba5`:

```text
> powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage
Build succeeded.
    0 Warning(s)
    0 Error(s)
=== Offline total: 407 passed, 0 failed ===
Package creation succeeded.
Beta MSIX: C:\Users\bkirk\Documents\CareerSeeker\output\release\CareerSeeker-beta-win-x64.msix
Bytes: 33677048
SHA-256: 1EDC46E3731A449C4DCCA02FA7464CBCF5D2EDC7FADF3EFA3EEF8F0B8C7B7B39
Executable payload: CareerSeeker.exe (one .exe)
Setup smoke completed through the local web flow.
  AI provider calls: 0
  Gmail calls/drafts: 0
Beta package self-check passed.
  identity: CareerSeeker.LocalBeta
  executable payload: 1 (CareerSeeker.exe)
  startup task: optional, disabled by default
  external user workspace preserved: yes
CareerSeeker alpha verification complete.
```

The first B8 verifier run stopped at PowerShell parse time because an
encoding-sensitive Unicode arrow had been added to an assertion. A later
assertion run stopped on a wrapped README sentence, and another stopped on a
heading-capitalization mismatch. Each was corrected before the clean 407/0
and full-package evidence above; no successful result is attributed to those
failed runs.

Verification boundary: the MSIX was built and unpacked, not signed,
installed, registered, removed through Windows, or reboot-tested. No Gmail
draft, provider call, send, public ATS request, deployment, Cloudflare
mutation, Google/Play console change, OAuth queue read/write, scheduled-task
registration, Android/relay/sync-vector change, off-repo site edit, purchase,
new scope, account/config change, or secret print occurred.

## 2026-07-30 (Terra B7) - Single-executable Windows MSIX

Branch: `codex/beta-M7-installer`

Integration base: `origin/main` at
`efd31671f7edd8c02900bc8f702e7b9893d4d1fd`, the confirmed PR #15 merge.
Implementation commit:
`830f7c1` (`feat(beta): ship single-executable MSIX packaging`).

B7 selects MSIX over Inno Setup. The choice uses Microsoft-owned packaging
tools, does not require a machine-wide compiler install or a purchase, lets
Windows own the Start-menu registration and optional startup declaration, and
has package removal semantics that do not run a custom data-deletion action.
`Microsoft.Windows.SDK.BuildTools` `10.0.26100.7705` is locked in the new
build-only tools project. The supply-chain query found no known vulnerable
packages in that project or the solution.

`scripts/Package-BetaRelease.ps1` now produces one
`CareerSeeker-beta-win-x64.msix` with exactly one executable,
`CareerSeeker.exe`; the Alpha Bridge duplicate setup executable is absent.
The manifest declares a full-trust desktop entry, Windows Start-menu tile,
content-integrity enforcement, and a user-configurable startup task that is
disabled by default. Package-identity launches create/use
`%LOCALAPPDATA%\CareerSeeker`, copy the public installed/Desktop OAuth client
metadata only when the user copy is absent, and keep databases, artifacts,
job descriptions, onboarding state, and DPAPI vaults outside the immutable
package. Automatic Start-menu/startup activation after onboarding is forced
to discovery-only service-host mode; it cannot imply consent to draft.

The package self-check unpacks rather than installs. It asserts the manifest,
exactly one executable, no `.appdata`/output/vault/token/secret payload, and
disabled startup. It then invokes `CareerSeeker.exe` with no explicit mode and
traverses all ten local onboarding steps using a synthetic TXT resume, manual
provider, and Gmail skip. Finally it removes the unpacked application tree
and proves a synthetic external DPAPI-vault sentinel remains.

The first full `-IncludePublish -IncludePackage` attempt passed all 407
offline assertions and created the MSIX, then stopped during the unpacked
smoke because the no-mode launcher incorrectly treated the
`--workspace-root` value as a mode. No package pass is claimed for that
attempt. Mode detection was corrected to examine only the first argument.

The clean full gate passed at
`830f7c1f9deb4d54da2282405e9fbc7ab57d5522`:

```text
> powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage
Build succeeded.
    0 Warning(s)
    0 Error(s)
=== Offline total: 407 passed, 0 failed ===
Package creation succeeded.
Setup smoke completed through the local web flow.
  route sequence: welcome -> package-verify -> resume-select ->
  local-resume-extraction -> provider-manual -> extraction -> claim-review ->
  gmail-skip -> doctor -> first-run
  AI provider calls: 0
  Gmail calls/drafts: 0
Beta package self-check passed.
  identity: CareerSeeker.LocalBeta
  executable payload: 1 (CareerSeeker.exe)
  startup task: optional, disabled by default
  external user workspace preserved: yes
  bytes: 33677037
  SHA-256: B831041B7EC0323A4B7EA17F67B1E2889E6C6C5CAD70F9588C900FE2537B65FD
CareerSeeker alpha verification complete.
```

The B6 ZIP was 65,057,904 bytes. The clean B7 MSIX is 33,677,037
bytes: 31,380,867 bytes (48.24%) smaller.

Signing is wired but not executed. `scripts/Sign-BetaRelease.ps1` accepts a
human-owned PFX, reads its password only from the process environment, uses
SHA-256 plus timestamping, and never prints the password.
`docs/Beta-Windows-Package-Runbook.md` records the unsigned Windows 11 tester
path, production Publisher/certificate-subject requirement, Azure Artifact
Signing handoff, and separate explicit-confirm data deletion.

Verification boundary: no MSIX was installed, registered, removed, or signed;
therefore real Start-menu appearance, Startup Apps behavior, reboot survival,
and Windows uninstall UI were not claimed. Their manifest/removal structure
was verified by MakeAppx unpack and the external-workspace sentinel. No Gmail
draft, provider call, send, public ATS request, deployment, scheduled-task
registration, Cloudflare action, Google/Play console change, Android/relay/
sync-vector change, off-repo site edit, purchase, or secret print occurred.

## 2026-07-30 (Terra B6) - Local browser onboarding and claim review

Branch: `codex/beta-M6-onboarding-v2`

Integration base: `origin/main` at
`f985ec080de8b4ee0da115ef2f84c392bf71c0d5`, the confirmed PR #14 merge.
Implementation commit:
`f54987f` (`feat(beta): add local web onboarding flow`).
Packaged-copy correction:
`0ecd79e` (`test(beta): align packaged onboarding copy`).

B6 replaces the default interactive console wizard with a ten-step,
loopback-only browser flow that reuses the dashboard's visual language:
welcome/safety, package verification, resume selection, provider connection,
resume-provider consent/extraction, claim-by-claim review, Gmail consent,
doctor, first run, and completion. The earlier wizard remains intact behind
`setup --console`.

The flow:

- validates packaged SHA-256 entries and refuses continuation after a package
  mismatch;
- accepts PDF/DOCX/TXT/Markdown up to 20 MiB, extracts through a temporary
  local file, deletes that file immediately, and never sends the original;
- tests provider credentials before DPAPI storage, preserves any existing
  vault after failure, retains quota-authenticated credentials, and makes
  timeout/5xx unverified storage a separate explicit action;
- requires a separate checkbox before normalized resume text can reach the
  selected provider, encodes that untrusted text at the prompt boundary, and
  never treats resume instructions as commands;
- shows every claim with accept/edit/drop controls, evidence, source document,
  and a visible maximum confidence of `stated`; only accepted claims are
  imported;
- refuses Google OAuth JSON that is not an installed/Desktop client and shows
  the Alpha2 consent truth that `gmail.compose` is permission-capable of
  compose/send even though CareerSeeker implements drafts only;
- uses loopback/Host checks, a per-process form token, fixed-time token
  comparison, CSP/no-store/nosniff/no-referrer headers, and bounded request
  bodies;
- defaults first run to discovery-only, with no Gmail draft.

EngineHarness moved 149â†’159 and the offline total moved 397â†’407 in the same
count/doc/verifier commit.

Executed verification:

```text
> dotnet build CareerSeeker.sln -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)

> dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release
=== 159 passed, 0 failed ===

> powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1
=== Offline total: 407 passed, 0 failed ===
CareerSeeker alpha verification complete.
```

The in-app Browser then exercised all ten rendered screens against
`.appdata/b6-browser`: package development-mode notice; resume skip; manual
provider; extraction consent; one manual `distributed systems` claim accepted
while the other three were dropped; Gmail skip; final doctor `Ready`; finish
without engine start. The completion page reported one approved claim and no
Gmail draft. The loopback listener was gone afterward and stderr was blank.
No provider or Gmail call occurred.

The first clean publish/package attempt reached package generation but stopped
on one stale copied-walkthrough assertion, which still expected
`First Run (Alpha 2.0 Bridge)`. No package pass is claimed for that attempt.
The expectation was aligned with the shipped `First Run (Beta Local
Onboarding)` heading and committed.

Clean package verification then passed at
`0ecd79eba3b8f9b5e9596c4757f9472c4d3f0cf8`:

```text
> powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage
=== Offline total: 407 passed, 0 failed ===
Package release self-check:
  manifest: ok
  OAuth client type: installed/Desktop
  checksums: 51 verified
Setup route sequence:
  welcome -> package-verify -> resume-select -> local-resume-extraction ->
  provider-manual -> extraction -> claim-review -> gmail-skip -> doctor ->
  first-run
AI provider calls: 0
Gmail calls/drafts: 0
CareerSeeker alpha verification complete.
```

Generated ZIP: 65,057,904 bytes; SHA-256
`76AC122C106D9D9C732A292E71FECC8564AE94B27AE820F8B73270CECD44DEEB`.

Verification boundary: no real resume or credential was entered. Provider and
Gmail consent/network paths were structurally and offline tested but not used
against live services; the packaged traversal used a synthetic TXT resume and
manual skips. No Gmail draft, provider call, send, public ATS request,
deployment, scheduled-task registration, Cloudflare action, Google/Play
console change, Android/relay/sync-vector change, off-repo site edit,
dependency addition, or secret print occurred in B6.

## 2026-07-30 (Terra B5) - Hardened Scheduled Task engine host

Branch: `codex/beta-M5-service-grade`

Integration base: `origin/main` at
`1390f3b8a2a3a7aa64491a4c12faaaabe260c86e`, the confirmed PR #13 merge.
Implementation commit:
`5c0c382` (`feat(beta): harden scheduled task engine host`).

B5 takes the roadmap's explicit fallback: a hardened per-user Scheduled Task
around `EngineHost`, not a native SCM Windows Service. The old alpha task
started only a read-only dashboard and had no effective restart or clean pause
path. The replacement:

- runs the real engine with `run --service-host`, so closed task stdin cannot
  make the process exit;
- holds an exclusive database-adjacent lock-file handle, released on clean
  stop or process death, to refuse duplicate engines;
- runs at user logon, `StartWhenAvailable`, `IgnoreNew`, least privilege
  (`Limited`), with 12 one-minute task-level restart attempts;
- supervises nonzero child exits with capped exponential backoff and daily
  local logs;
- converts failed Scout boards into cycle errors and doubles the scheduler
  delay up to the configured ceiling, while successful cycles reset it;
- uses local `pause.request` and `stop.request` files: pause leaves dashboard
  and controls alive, resume removes the pause, and stop performs normal async
  host disposal without force-kill;
- reports `Paused`, current delay, and consecutive error cycles in `/status`;
- extends `doctor --require-service-host` with writable control/log path and
  duplicate-lock checks.

The count/doc/verifier lockstep moves EngineHarness 137→149 and the offline
total 385→397. StoreParity remains 25.

Focused verification executed:

```text
> dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build
=== 149 passed, 0 failed ===

> dotnet ... doctor --require-service-host --db tmp\b5-doctor\doctor.db ...
OK  service_host_paths: control and log directories are writable
OK  service_single_instance: second local engine lease was refused
secret values were not printed.

> powershell ... Manage-AlphaDashboardTask.ps1 -Action Install -DryRun -Published
Task definition validated by Windows Task Scheduler cmdlets.
  restart count: 12
  restart interval: PT1M
  multiple instances: IgnoreNew
Dry run only; scheduled task was not registered.
Task present before=False after=False

> powershell ... Start-BetaEngineHost.ps1 -SupervisorSelfTest ...
self-test child exited 7
engine exited 7; restart 1 in 5 seconds
supervisor stop request received during backoff; exiting cleanly
Supervisor self-test exit: 0

> powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1
=== Offline total: 397 passed, 0 failed ===
CareerSeeker alpha verification complete.
```

The real local process smoke used an isolated ignored DB and public Lever board:
initial `/status` was `running`, a second process for the same DB exited 2,
the pause file produced `status=paused, scheduler=Paused`, and the stop file
removed the listener cleanly with blank stderr.

Two adversarial findings were fixed during verification. First, the original
named `Mutex` was disposed on a different continuation thread and threw after
listener shutdown; it was replaced with a crash-releasing exclusive file
handle, and the full smoke then passed. Second, constructing the real Task
Scheduler definition showed the inherited `LeastPrivilege` value is invalid;
Windows accepts `Limited`, and the rerun validated the definition without
registering it. An earlier `Stop -DryRun` also required an installed task; its
check order was corrected.

The first full verifier attempt stopped only because a new README truth sentence
crossed a Markdown line break; the smoke was changed to assert its stable
clauses, and the complete rerun passed 397/0. The first publish/package attempt
then correctly refused to bless a manifest from the dirty milestone worktree.
No package result is claimed from that attempt; publish/package is rerun only
from the clean committed tip.

The next clean publish/package run reached the packaged self-check and found
two stale launcher-copy assertions: the launchers now truthfully say
`Engine task ... cancelled`, while the test still expected `Dashboard task ...
cancelled`. The assertions were updated to the shipped copy. No passing package
result is claimed from that failed attempt.

Clean package verification then passed at
`f36a4ac80e800127d61a408635767c43581321a9`:

```text
> powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage
=== Offline total: 397 passed, 0 failed ===
Published executable demo: errors: 0
Package trusted-tester alpha ZIP: created
Packaged release self-check, dashboard/task dry-runs, audit/evidence export,
and evidence import completed.
CareerSeeker alpha verification complete.
```

Verification boundary: no native Windows Service/SCM or tray UI was built.
The Windows at-logon task definition was validated but never registered, so no
real reboot result is claimed. The fallback is wired and process-verified;
reboot persistence remains a human acceptance check after explicit install.

No Gmail draft, BYOK/provider call, send, deployment, scheduled-task
registration, Cloudflare action, Google/Play console change, Android/relay/
sync-vector change, off-repo site edit, dependency change, or secret print
occurred in B5.

## 2026-07-30 (Terra B4) - Quarantine telemetry and measured signal rate

Branch: `codex/beta-M4-quarantine-telemetry`

Integration base: `origin/main` at
`b9728737b20124f016bffe26f83e0479d150941d`, the confirmed PR #12 merge.
Implementation commit:
`6017e799ac3dea01642a492f843362556d8e7140`.

B4 adds an idempotent `cycle_telemetry` table and matching in-memory store
behavior. Every completed engine tick persists discovered, quarantined,
rejected, drafted, and error counts; configured board identities; and trusted
classifier reason-code counts. Posting bodies never enter this record.
Configured identities come from the feed independently of results, so a
zero-result or timed-out cycle remains attributable to its board.

`/evidence`, `/evidence.html`, and hash-only-by-default audit exports now expose
the aggregate rows. Engine and store harnesses cover cycle construction,
empty-sensitive board identity, reason codes, body exclusion, dashboard
rendering, export inclusion, and memory/SQLite parity.

The count/doc/verifier lockstep moved EngineHarness 133→137 and
StoreParityHarness 24→25, for 380→385 total assertions.

Verification executed:

```text
> powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1
Build succeeded.
    0 Warning(s)
    0 Error(s)
=== Offline total: 385 passed, 0 failed ===
CareerSeeker alpha verification complete.

> dotnet run --project tests\StoreParityHarness\StoreParityHarness.csproj --no-build
=== 25 passed, 0 failed ===

> dotnet run --project tests\EngineHarness\EngineHarness.csproj --no-build
=== 137 passed, 0 failed ===
```

Five bounded public-ATS discovery-only cycles were executed across Greenhouse,
Lever, and Ashby. Remote.com returned 61 postings: 14 quarantined, 47 rejected,
0 drafted, 0 errors. Both Mistral Lever attempts and both Deel Ashby attempts
returned zero postings and zero errors. The two re-runs verified the board-
identity correction for empty results. Canonical hash-only exports for all
three boards reported intact audit chains.

Manual limited-context review of all 14 flags found ordinary job-duty uses of
“act as”; every flag carried only `role_reassign`. The observed signal rate was
14/61 (22.95%), while the manually assessed false-positive rate among flags was
14/14 in this limited sample. `docs/Injection-Rate-Report-2026-08.md` contains
five anonymized pattern snippets, limitations, exact commands, and a proposed
threshold change. No classifier tuning was applied.

The first focused EngineHarness attempt after B4 implementation had two failed
expectations because the fixture's default synthetic identity is `feed:feed`,
not `feed:engine-harness`; the actual persisted value was correct. The
expectations were corrected and the rerun passed 137/0. The first zero-result
Lever/Ashby measurements also exposed missing configured identity in telemetry;
the feed identity seam was added and one bounded re-run per board verified it.
Two verifier launches were also mistakenly given a one-second process timeout
and were terminated after beginning; no result is claimed from either partial
run. Complete verifier runs before and at implementation commit
`6017e799ac3dea01642a492f843362556d8e7140` both passed 385/0.

No Gmail draft, BYOK/provider call, email, upload, deployment, scheduled-task
registration, Cloudflare action, Google/Play console change, Android/relay/
sync-vector change, off-repo site edit, dependency change, classifier tuning,
or secret print occurred in B4.

## 2026-07-30 (Terra B3) - Deterministic lexical ranking

Branch: `codex/beta-M3-lexical-ranking`

Integration base: `origin/main` at
`1dc1f817e0712b0ea2556d3d2aab46ff9ffd6100`, the confirmed PR #11 merge.
Implementation commit:
`8f659065c8cc2ccfbb0c424a103f775fb62e07a3`.

Ground truth confirmed the roadmap's placeholder finding and one related gap:
`run` used `DemoSemanticScorer(4.6, 4.2)` for every posting, and EngineCycle
computed `ScoreResult` without persisting it. Consequently, real job order was
largely feed order and the job dashboard had no score evidence to render.

B3 replaces that default with `lexical-v1`, a deterministic offline ranker:

- It loads only the active local profile claims, weights Skill and Title claims
  above narrative/metric/employer terms, and discounts weak/stated confidence.
- It tokenizes title and description as untrusted data, ignores boilerplate
  stop words, and weights title matches above body matches.
- It derives reproducible CV-match and growth components, stores a bounded
  matched-term rationale (never the full posting), and makes no provider or
  network call.
- EngineCycle now persists the existing unchanged scorer result plus its
  components/ranker identity. Both stores return those fields; recent scored
  jobs order by `total`, with unscored/quarantined evidence kept below them.
- `/jobs` HTML-encodes and displays total, fit, legitimacy, CV, compensation,
  growth, preferences, ranker identity, and the bounded rationale.

The load-bearing combination remains unchanged:
`total = min(fit, legitimacy) * red_flag_multiplier`. The Fabrication Gate,
pinned StrongCloud verifier stage, and Dispatcher no-send boundary were not
changed. The optional roadmap `byok-embed` ranker was not implemented; the
offline ranker is complete without it and no provider call was authorized.

The count/doc/verifier lockstep moved EngineHarness 127→133 and
StoreParityHarness 23→24, for 373→380 total assertions.

Verification executed:

```text
> dotnet build CareerSeeker.sln -c Release -warnaserror
Build succeeded.
    0 Warning(s)
    0 Error(s)

> dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build
=== 133 passed, 0 failed ===

> dotnet run --project tests\StoreParityHarness\StoreParityHarness.csproj -c Release --no-build
=== 24 passed, 0 failed ===

> dotnet src\Engine\bin\Release\net8.0\SeekerSvc.Engine.dll demo --once
  cycles: 1
  discovered: 3
  acted: 1
  drafted: 1
  blocked: 1
  rejected: 1
  errors: 0

> powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1
=== Offline total: 380 passed, 0 failed ===
CareerSeeker alpha verification complete.
```

The first full-verifier attempt stopped in the new doc smoke because the exact
phrase `deterministic offline lexical-v1` crossed a Markdown line break. The
assertion was corrected to the phrase actually present and the rerun above
passed. No product/harness assertion failed.

The in-app Browser visual check is not claimed. Three bounded attempts to keep
the local dashboard alive as a hidden background process all exited before
port 7791 bound. Exact attempts and the foreground human command are in
`docs/BETA-BLOCKED.md`. EngineHarness did execute the local dashboard renderer
and passed encoded component/rationale, persistence, and ordering assertions.

No Gmail draft, BYOK/provider call, email, upload, deployment, scheduled-task
registration, Cloudflare action, Google/Play console change, Android/relay/
sync-vector change, off-repo site edit, dependency change, or secret print
occurred in B3.

## 2026-07-30 (Terra B2) - Startup and periodic crash recovery

Branch: `codex/beta-M2-crash-recovery`

Integration base: `origin/main` at
`b5b4a98749d5bff814d067d37c310512c7e8b70b`, the confirmed PR #10 merge.
GitHub reported PR #10 `MERGED` at `2026-07-31T03:02:20Z`; `git fetch --all`
and `git rev-parse origin/main` returned the same merge SHA. No document/repo
divergence was found for the B2 scope: startup reconciliation already existed,
while the periodic engine tick was the missing path described by the roadmap.

Implementation commit:
`f97fbc0e512e2b5cd6560cd1ad4fb22334fcf5b8`.

`EngineCycle.TickAsync` now runs the actual pipeline's side-effect-free
`ReconcileAllAsync` before discovery on every cycle. Persistent executable
modes already run the same sweep before composing work, so recovery now occurs
both on process restart and while a long-running process stays alive. A
store-level sweep failure aborts that tick; one bad row is isolated by the
existing per-row handler and counted. Reconciliation never calls Gmail or a
submission provider.

An unresolved `PENDING` effect still fails closed for manual review. Because
the sweep is periodic now, the durable `reconcile_manual_review` audit fact is
idempotent by application and effect kind instead of being appended every
interval.

The new EngineHarness cases create the exact persisted post-crash shape:
`READY` plus a `SUCCEEDED` draft attempt with an external reference but no
local `DRAFTED` commit. They prove both a scheduled tick and a new engine
process complete the local transition while provider-call count remains zero
and the attempt count remains one. LifecycleHarness proves repeated sweeps do
not duplicate the manual-review audit fact.

Count-bearing docs, verifier assertions, and `$ExpectedOfflineTotal` moved
together: EngineHarness 124→127, LifecycleHarness 44→45, offline total
369→373.

Verification executed in this session:

```text
> dotnet build CareerSeeker.sln -c Release -warnaserror
Build succeeded.
    0 Warning(s)
    0 Error(s)

> dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build
=== 127 passed, 0 failed ===

> dotnet run --project tests\LifecycleHarness\LifecycleHarness.csproj -c Release --no-build
=== 45 passed, 0 failed ===

> powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1
=== Offline total: 373 passed, 0 failed ===
CareerSeeker alpha verification complete.
```

No Gmail draft, provider call, email, public ATS request, upload, deployment,
scheduled-task registration, Cloudflare action, Google/Play console change,
Android/relay/sync-vector change, off-repo site edit, dependency change, or
secret print occurred in B2.

## 2026-07-30 (Terra B1) - Real engine landed after adversarial no-redraft fix

Branch: `codex/beta-M1-engine-runs`

Integration base: `origin/main` at
`eb3c72bf49edb6ba08f4e01ae6f72a57681dfd0e`, the verified B0 PR #9 merge.
This is the expected divergence from the July 30 seed, which still names
`14a7dfec374cda410aa28b13c456d695f38e3507` as `main`.

Reviewed branch tip:
`origin/fix/engine-actually-runs` at
`40bc9a7166afb7d9742d75ef1b93b2ce0c8f5c1b`. Its merge base was the seed's
`14a7dfe` main and its diff changed 15 non-Android/non-relay files. The
advertised source claims checked out: quarantine was before the action cap,
dashboard status covered `faulted`, Scout carried true board/external identity,
and the 341-to-364 verifier/doc update was lockstep.

The adversarial pass found two release-blocking defects and fixed them on top:

- Periodic sweeps admitted the same stored job again every interval. That could
  produce repeated Gmail drafts for the same posting and keep the same first
  capped jobs ahead of never-processed jobs. Both stores now expose
  `HasApplicationForJobAsync`; identified cycles skip already-admitted jobs
  before spending the cap. Harnesses prove later cycles advance and settled jobs
  are never redrafted.
- `run --dry-run` used a fake Gmail client but committed the lifecycle as
  `DRAFTED`; launchers could also combine a fake LLM with real Gmail when the
  BYOK vault was absent. Discovery-only now stops Act decisions before
  Tailor/Gate/Dispatcher, creates no application or simulated draft, and the
  live Gmail path refuses `--llm fake` with exit 2.

No Fabrication Gate, pinned-stage routing, Dispatcher no-send, Android, relay,
sync-vector, secret, live-service, or off-repo site file changed. No dependency
was added.

Verification executed in this session:

```text
> powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1
=== Offline total: 364 passed, 0 failed ===
CareerSeeker alpha verification complete.
```

That first run was the reviewed branch unchanged. After the two fixes and the
count/doc lockstep:

```text
> dotnet build CareerSeeker.sln -c Release --warnaserror
Build succeeded.
    0 Warning(s)
    0 Error(s)

> dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build
=== 124 passed, 0 failed ===

> dotnet run --project tests\StoreParityHarness\StoreParityHarness.csproj -c Release --no-build
=== 23 passed, 0 failed ===

> dotnet run --project tests\LifecycleHarness\LifecycleHarness.csproj -c Release --no-build
=== 44 passed, 0 failed ===

> powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1
=== Offline total: 369 passed, 0 failed ===
CareerSeeker alpha verification complete.
```

Bounded public-ATS, no-Gmail smoke:

```text
> dotnet src\Engine\bin\Release\net8.0\SeekerSvc.Engine.dll run --once --dry-run --llm fake --board greenhouse:remotecom --discovery-timeout-seconds 90 --http-timeout-seconds 30 --max-drafts-per-cycle 2 --db tmp\beta-b1-live\careerseeker.db --artifacts tmp\beta-b1-live\artifacts --jd-dir tmp\beta-b1-live\job-descriptions
  cycles: 1
  discovered: 61
  acted: 0
  drafted: 0
  blocked: 0
  rejected: 41
  quarantined (injection): 14
  errors: 0
  audit chain: ok
```

The in-app Browser inspected the same real scheduler/dashboard path locally and
rendered `running`, `cycles 1`, `discovered 61`, `drafted 0`,
`quarantined 14`, and `errors 0`. The local test process was then stopped.

Fail-closed fake/live pairing check:

```text
> dotnet src\Engine\bin\Release\net8.0\SeekerSvc.Engine.dll run --once --llm fake
run refuses a fake LLM on the live Gmail path. Configure BYOK, or pass --dry-run for discovery-only operation.
EXIT_CODE=2
```

The first `-IncludePublish -IncludePackage` attempt was intentionally made
before committing and correctly stopped at `Alpha release manifest was
generated from a dirty working tree.` It was not treated as a pass. After the
implementation/count commit `21a13193e205de66606d9e3a74dd4fdf51702cc7`,
the clean-tree rerun completed:

```text
> powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage
=== Offline total: 369 passed, 0 failed ===
=== Published executable demo smoke ===
  errors: 0
=== Package trusted-tester alpha ZIP ===
  manifest: ok
  OAuth client type: installed/Desktop
  checksums: 50 verified
  dashboard smoke: passed
  Alpha 2.0 setup smoke: passed
CareerSeeker alpha verification complete.
```

All package/live-L1 helper paths in that run were preview/dry-run only. No
Gmail draft, provider call, email, upload, deployment, scheduled-task
registration, Cloudflare action, or other live-service mutation occurred.

## 2026-07-30 (Terra B0) - Beta preflight baseline

Branch: `codex/beta-M0-preflight`, cut from local
`codex/beta-integration` at `origin/main`.

No product code, harness count, verifier expectation, Android file, live service,
secret, or off-repo site file changed in B0. The publish/package artifacts were
created locally under ignored paths and were not uploaded or deployed.

Ground truth derived before editing:

```text
> git fetch --all
> git rev-parse origin/main
14a7dfec374cda410aa28b13c456d695f38e3507

> git rev-parse origin/fix/engine-actually-runs
40bc9a7166afb7d9742d75ef1b93b2ce0c8f5c1b

> git status --short --branch
## main...origin/main
```

The July 30 summary agrees with those two remote SHAs. The prior newest handoff
entry was dated July 24 and described an earlier `main` snapshot, so it was
treated as historical evidence rather than current branch truth.

Environment:

```text
> dotnet --version
8.0.422

> Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, BuildNumber, OSArchitecture
Caption        : Microsoft Windows 11 Home
Version        : 10.0.26200
BuildNumber    : 26200
OSArchitecture : 64-bit
```

B0 verification executed on unchanged `main` at
`14a7dfec374cda410aa28b13c456d695f38e3507`:

```text
> dotnet build CareerSeeker.sln -c Release --warnaserror
Build succeeded.
    0 Warning(s)
    0 Error(s)

> powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1
=== Offline total: 341 passed, 0 failed ===
CareerSeeker alpha verification complete.

> powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage
=== Offline total: 341 passed, 0 failed ===
=== Publish win-x64 single-file executable ===
=== Published executable demo smoke ===
  errors: 0
=== Package trusted-tester alpha ZIP ===
  manifest: ok
  OAuth client type: installed/Desktop
  checksums: 50 verified
  dashboard smoke: passed
  Alpha 2.0 setup smoke: passed
CareerSeeker alpha verification complete.
```

The package run's scheduled-task helper explicitly reported `Dry run only;
scheduled task was not registered.` Provider/research/selected-job/live-L1
helpers were preview or dry-run only. No BYOK request, Gmail draft, email send,
Cloudflare action, or other live-service mutation occurred.

## 2026-07-24 (Opus post-launch iteration) - SQLitePCLRaw advisory cleared, 8AM deploy batch staged

Picked up after the mitigation round landed. That round was already committed and pushed as
`ffe3622 Harden alpha provider setup` (working tree was clean, not dirty as the session seed expected —
`02953f7 -> ffe3622 (amend)` in the reflog), so no re-commit was needed. Derived state: `main @ ffe3622`,
synced with origin.

Security dependency fix (committed `7018ff9`, pushed to `main`):

- Cleared advisory GHSA-2m69-gcr7-jv3q (High). `Microsoft.Data.Sqlite 8.0.11` pulled
  `SQLitePCLRaw.lib.e_sqlite3 2.1.6` transitively (vulnerable `<= 2.1.11`). Pinned
  `SQLitePCLRaw.bundle_e_sqlite3` to `2.1.12` directly in `src/Store/SeekerSvc.Store.csproj`; the direct
  (nearest-wins) reference propagates 2.1.12 to every project that references Store.
  `dotnet list package --vulnerable --include-transitive` now reports **no vulnerable packages across all
  23 projects**; resolved lib/core/provider = 2.1.12; `Microsoft.Data.Sqlite` unchanged at 8.0.11.
- Fixed a latent packager bug exposed by the bump: `scripts/Package-AlphaRelease.ps1` selected the
  NuGet-cache fallback `e_sqlite3.dll` with `Sort-Object FullName -Descending`, a lexical sort that ranks
  `2.1.6` above `2.1.12` (char `6` > `1`) and would reship the vulnerable native SQLite when both versions
  are cached. Now sorts by parsed `[System.Version]`. Verified the corrected selector picks 2.1.12.

Verification (all run this session):

- `scripts\Verify-Alpha.ps1`: **341 passed, 0 failed** (count-neutral; the bump added no assertions).
- `dotnet build CareerSeeker.sln -c Release --warnaserror`: 0 Warning(s), 0 Error(s).
- `scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage`: green — win-x64 single-file publish + demo
  smoke, package self-check (manifest ok, 50 checksums, dashboard + Alpha 2.0 setup smokes).

New Bridge ZIP (built from clean `7018ff9`, staged locally, NOT yet uploaded):

- `output/release/CareerSeeker-alpha2-bridge-win-x64-2026-07-24-7018ff9.zip`
- sha256 `3A4251F65AEF530BC5D73387422CD53556294970EC546C0112B6EF1BA4E900F2`, 64,937,092 bytes,
  manifest pins `7018ff9 / dirty:False`.
- Proof the fix shipped: the packaged `e_sqlite3.dll` sha256 (`B7385D72...FB2E`) is a byte-for-byte match
  to the cached **2.1.12** native lib and differs from 2.1.6 (`DCCBABB2...`).

Signup hygiene (site source `Desktop\site-v2`, off-repo, staged only):

- `functions/api/signup.js`: `signup-event:*` KV puts now carry `{ expirationTtl: 7776000 }` (90 days) so
  that keyspace stops growing forever.
- Built operator tool `Desktop\Process-PendingOAuthTestUsers.ps1`: lists `oauth-test-user-pending:*` via the
  wrangler OAuth session (`--remote`, `CLOUDFLARE_API_TOKEN` cleared), prints a paste-ready email list for
  Google Console > Audience > Test users, and with `-Apply` stamps each `signup:` record
  `oauth_test_user.status = "added"` and deletes the processed markers. Read-only by default.

8AM deploy batch — STAGED AND HELD (embargo: nothing deployed/uploaded to Cloudflare this session):

- Site edits in `Desktop\site-v2`: `functions/api/verify.js` download_url and `download/index.html`
  (filename x3 + hash, `SHA-UPDATED-F2.3` marker preserved) moved `ffe3622 -> 7018ff9` and
  `19BF787F... -> 3A4251F6...`.
- Remaining `[8AM]` steps unchanged from the seed: R2 upload of the new ZIP to
  `careerseeker/alpha/<new-name>.zip`, `wrangler pages deploy .` from inside `site-v2`, end-to-end verify,
  then email enablement + `/api/signup` rate-limit + one controlled e2e signup test.

Housekeeping: the `Desktop\dryrun` folder (flagged in the seed as still holding real API keys) no longer
exists — that cleanup already happened; nothing to delete.

### 8AM Cloudflare push — EXECUTED (Brandon's go). Scope chosen: core deploy + tee up email (no real mail)

- **R2:** uploaded object `alpha/CareerSeeker-alpha2-bridge-win-x64-2026-07-24-7018ff9.zip` to bucket
  `careerseeker` (new object; the old `ffe3622` object left untouched — never overwrite). Auth =
  `CLOUDFLARE_R2_API_TOKEN`.
- **Pages deploy:** `wrangler pages deploy .` from inside `site-v2` to production branch `main` (project
  `careerseeker-site`). Both success signals present ("Compiled Worker successfully" + "Uploading Functions
  bundle"). Deployments `fe23b36b` (site edits) then `2a57c81e` (signup.js REST-shape fix).
- **E2E verified on careerseeker.app:** public ZIP sha256 == `3A4251F6…E900F2` == build (64,937,092 bytes);
  `/download/` shows the new filename + hash with no stale refs; bad code -> **403**; traversal ->
  404/404/400 (all rejected).
- **Fixed a live signup.js bug while teeing up email:** the REST branch sent `from: { address, name }` and
  `reply_to: { address }`, but Cloudflare Email Service REST wants `from: { email, name }` and `reply_to`
  as a string — it would have 400'd every send even after onboarding + secrets. Fixed and deployed. (The
  `env.EMAIL` binding branch was already correct.)
- **Email enablement TEED UP, not enabled.** Production Pages `env_vars` still NONE -> leg still inert
  (honest 503; KV/codes still created). Handoff artifacts on Brandon's Desktop:
  `Email-Enablement-Checklist.md` + `pages-email-secrets.PATCH.json` (full merged config, preserves
  `BETA_KV`/`RELEASES`). Brandon's remaining steps: dashboard Onboard Domain (auto-creates the `cf-bounce`
  MX/SPF/DKIM/DMARC records; separate from the existing root Email Routing, no conflict), create an
  `Email Sending: Edit` token, add the two Pages secrets, redeploy. Then the rate-limit rule on
  `/api/signup` (dashboard) and the one controlled real-mail e2e test (his explicit go) remain.
- Operator tool `Desktop\Process-PendingOAuthTestUsers.ps1` in place for working the OAuth test-user queue.

### 2026-07-24 (later) - tester setup guide + animated homepage mark (all deployed + e2e-verified)

Site source is off-repo at `Desktop\site-v2`; deploys via `wrangler pages deploy .` from inside it.

- **Signup email upgraded** (`functions/api/signup.js`): first email now carries the "Most testers should start
  here" walkthrough (7 steps + safety notes) in text+HTML, and **attaches a setup-guide PDF**. The PDF is read
  at request time from R2 (`env.RELEASES.get("assets/CareerSeeker-Alpha-Setup-Guide.pdf")`, chunked base64,
  ~3 MiB raw guard for the CF Email Service 5 MiB message cap; sends without it rather than failing signup if
  missing). NOTE: the REST send shape is `from: { address, name }` + `reply_to` string — a live smoke proved
  `{ email }` returns 10001; do not "fix" it to match the docs.
- **Setup guide** (from the Claude Design project "Screenshots to infographic guide", `.dc.html`) ported to clean
  static HTML — no dc runtime. Rendered two ways: (1) embedded on the **beta post-signup state** (`beta/index.html`
  `#step-thanks`, "While you wait" section), and (2) a **regenerated 315 KB PDF** (Edge headless print of
  `Desktop\Career Seeker\setup-guide-print.html`) that replaced the original 1.6 MB image-only PDF as the email
  attachment. Screenshots live at `site-v2/assets/setup-guide/step[1-6]-*.png`.
- **Security fix on the screenshots:** Step 5 ("Copy key") showed a (throwaway) Gemini key + project ids; the
  API-key / project-name / project-number rows are pixelated (System.Drawing, `assets/setup-guide/step5-copy-key.png`)
  before publishing. The other screenshots are clean UI (SmartScreen, AI Studio, setup console).
- **Homepage** (`index.html`): added the **animated watch/radar hero mark** from the Claude Design "CareerSeeker
  Site Reskin" project — inline SVG + vanilla-JS rAF loop (minute hand 120 deg/s over a 36 s cycle, hour hand 1/12,
  lime radar-sweep wedge between them), `prefers-reduced-motion` aware. Scoped to just the icon in a two-column
  hero; the rest of the page is unchanged. Verified animating via two-frame headless diff.
- Live verification on careerseeker.app: beta page carries the guide + 6 step images; `step5` served bytes ==
  local blurred bytes; the PDF is NOT publicly reachable via `/releases/...` (assets/ prefix, releases fn only
  serves alpha/) -> 404; bad code still 403; download page still 200 with the 3A4251F6 hash; homepage serves the
  animated mark with the reduced-motion guard and correct UTF-8.

### Email enablement — LIVE (2026-07-24 ~11:38 local)

Brandon onboarded careerseeker.app to Cloudflare Email **Sending** (dashboard; `cf-bounce`
DKIM/SPF/DMARC auto-created, no conflict with the root Email Routing), created an
`Email Sending: Edit` token, and added the two Pages production secrets
(`CLOUDFLARE_EMAIL_ACCOUNT_ID` plain_text + `CLOUDFLARE_EMAIL_API_TOKEN` secret_text). I redeployed
to bind them (`d1af7641`) and confirmed both vars present with `BETA_KV`/`RELEASES` intact.

**Send-shape gotcha (important):** the Email Service REST endpoint and the Workers binding use
DIFFERENT named-address keys — REST wants `from: { address, name }`, the binding wants
`{ email, name }`. My first signup.js "fix" wrongly changed the REST branch to `{ email }`, which
returns `10001 invalid_request_schema`; the original `{ address }` was right. Corrected back to
`{ address, name }` (+ `reply_to` as a plain string) and redeployed (`a717e17b`). A live REST smoke
to `careerseeker.test.brandon@gmail.com` returned `success:true` and **delivered to inbox** with the
`CareerSeeker Testers <testers@careerseeker.app>` display name. The `env.EMAIL` binding branch was
already correct and untouched.

**Signup email now works end-to-end.** Remaining: (1) the backlog that signed up while email was
inert still needs codes delivered — `mdodson@gmail.com` + `testers@careerseeker.app` have codes (a
resend delivers), `akirksey1@gmail.com` + `dkirksey.jobs@gmail.com` have none (re-submit issues one);
(2) `/api/signup` rate-limit rule (dashboard); (3) OAuth test-user queue (2 pending: `mdodson`,
`testers@`) via `Process-PendingOAuthTestUsers.ps1`.

## 2026-07-24 (Alpha 2.0 Bridge) - setup ZIP built and post-audit fixes applied

Alpha 2.0 Bridge is the current local package target; the real per-user installer is intentionally deferred
to Beta. The Bridge package is a ZIP with an obvious first click:

- `START HERE - CareerSeeker Setup.exe`
- `README - Start Here.txt`
- `SeekerSvc.Engine.exe`
- `resources/google-client.json`
- advanced `.cmd` helpers under `Advanced Tools/`

What the setup bridge now does:

- creates the local alpha workspace
- accepts a Gemini key and stores it directly in the per-user DPAPI vault
- asks for explicit consent before sending a resume to Gemini
- uses `gemini-2.5-flash-lite` for resume profile extraction
- treats resume text as untrusted data in the extraction prompt
- caps AI-extracted claims at `stated` and tags them with `sourceDoc: "resume-ai"`
- performs claim-by-claim review before import
- connects Gmail using packaged app-owned desktop OAuth metadata
- runs readiness checks and opens the dashboard

Claude's post-audit findings were resolved:

- D1: AI-extracted claims can no longer reach `AlphaProfileImport` as `verified`; setup normalizes before
  review and again before import.
- D2: `config/` is ignored so local OAuth client material is not accidentally committed.
- D3: resume text is placed in an untrusted-data block for text/Markdown extraction, and binary resume
  attachments are explicitly labeled as untrusted resume data.
- D4: the failed Gemini-key "save anyway" path preserves existing BYOK vault entries.
- D5: an interim claim-by-claim console review exists; a polished WinUI/webview review UI remains Beta work.

Verification after the fixes:

- `dotnet build CareerSeeker.sln -c Release --no-restore` passed with `0 Warning(s), 0 Error(s)`.
- `dotnet run -c Release --project src\Engine\SeekerSvc.Engine.csproj -- setup --smoke` passed.
- `scripts\Package-AlphaRelease.ps1 -OutputDirectory output\alpha2-bridge -PackageName CareerSeeker-alpha2-bridge-win-x64.zip`
  rebuilt the Bridge ZIP.
- The packaged self-check passed with dashboard smoke and Alpha 2.0 setup smoke.
- The ZIP scan found no `env.secrets`, DPAPI vaults, tokens, databases, or resume data.

Additional guardrail added after Claude's final note: `scripts\Test-AlphaReleasePackage.ps1` now asserts the
packaged OAuth metadata at `resources/google-client.json` is an installed/Desktop OAuth client, not a Web
client. This matters because shipping desktop client metadata is public-by-design; shipping a Web client secret
would not be acceptable.

Known non-blockers:

- The Bridge ZIP is larger than Alpha 1 because it carries both `SeekerSvc.Engine.exe` and the setup-named copy
  of the same self-contained executable. Beta installer work should avoid that duplication.
- Setup is still a console wizard. It is acceptable for Alpha 2.0 Bridge, but the Beta target remains a proper
  non-technical WinUI/webview onboarding surface.

Current Bridge artifact path:

- `output\alpha2-bridge\CareerSeeker-alpha2-bridge-win-x64.zip`

## 2026-07-23 (Codex C2 pre-ZIP audit follow-up) - root ZIP ignore added

Independent pre-ZIP audit confirmed the repo was ready to rebuild from `main` at `be335c0`, with
`dotnet build CareerSeeker.sln -c Release --warnaserror` reporting `0 Warning(s), 0 Error(s)` and
`scripts\Verify-Alpha.ps1` reporting `Offline total: 334 passed, 0 failed`. It also flagged the local
`output\release\CareerSeeker-alpha-win-x64.zip` as stale because its manifest source commit was `c1440a8`,
not the final `main` head. That artifact must not be uploaded.

Follow-up taken before C2: add a repo-root `*.zip` ignore rule so stray ZIPs dropped outside `output/` do not
dirty the tree during release preparation. C2 still requires a fresh trusted-tester ZIP rebuild from the final
`main` head after this commit, followed by package self-check and a new SHA-256.

## 2026-07-23 (Codex Gate C1 merge completed) - main advanced, Android excluded

Brandon approved Gate C1 in chat during this continuation. Per the seed runbook, the merge was performed as
fast-forward branch movement only:
- `origin/agent/repo-cleanup` advanced from `81d232c` to audited alpha tip `c1440a8`.
- `origin/main` advanced from `3fa65f5` to `c1440a8`.
- No Android/P1/P2 branches were merged. No C2 deployment, R2 upload, KV write, beta-code issue, live provider
  call, or Gmail draft was performed.

Fresh derived state after `git fetch origin --prune` immediately after the merge:
- `HEAD` was on `main` at `c1440a8`, clean on `main...origin/main`.
- `origin/main`, `origin/agent/repo-cleanup`, and `origin/claude/alpha-finish` all resolved to `c1440a8`.
- Android/P1/P2 tips remained `940c4e1`, `6c46545`, and `74dd862`.
- `git merge-base --is-ancestor origin/claude/alpha-finish origin/main` -> exit `0`.
- `git merge-base --is-ancestor origin/agent/repo-cleanup origin/main` -> exit `0`.
- `git merge-base --is-ancestor origin/claude/android-apk-build-setup-90d9d5 origin/main` -> exit `1`.
- `git merge-base --is-ancestor origin/claude/p1-sync origin/main` -> exit `1`.
- `git merge-base --is-ancestor origin/claude/p2-publisher origin/main` -> exit `1`.

GitHub state after C1:
- PR #4 (`claude/alpha-finish` -> `agent/repo-cleanup`) is `MERGED` at `2026-07-23T21:42:34Z`.
- PR #2 (`agent/audit-cleanup-h1h2h3` -> `agent/repo-cleanup`) is `MERGED` at `2026-07-23T21:42:34Z`.
- PR #1 (`agent/repo-cleanup` -> `main`) is `MERGED` at `2026-07-23T21:43:23Z`.
- PR #3 was closed as superseded by C1 after proving `origin/claude/hardening-batch` was contained in
  `origin/main`.
- Only Android PRs #5 and #6 remained open.

Stale/superseded branches pruned from origin after proving each tip was already an ancestor of `origin/main`:
`claude/hardening-batch`, `agent/audit-cleanup-h1h2h3`, `codex/b1-live-scout`,
`codex/l1-gmail-oauth-draft`.

Post-merge evidence on `main` at merge tip `c1440a8`:
- `dotnet build CareerSeeker.sln -c Release --warnaserror` completed with `Build succeeded`, `0 Warning(s)`,
  `0 Error(s)`.
- `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1` completed with
  `Offline total: 334 passed, 0 failed`.
- Main GitHub Actions run `30047284490`, job `89341292161`, completed successfully.

C2 reminder: because this handoff update is a docs-only commit after the C1 merge, rebuild the trusted-tester
ZIP from the final `main` head before any upload. Do not reuse a package whose manifest source commit is
`c1440a8`.

## 2026-07-23 (Codex C1 merge rehearsal) - fast-forward candidate proven

Continuation rehearsal at local time `2026-07-23 15:31:20 -06:00`. No branch merge, protected-branch
update, deployment, R2 upload, KV write, beta-code issue, live provider call, or Gmail draft was performed.

Fresh derived state after `git fetch origin --prune`:
- `HEAD` on `claude/alpha-finish`: `7d25f24`, clean on `claude/alpha-finish...origin/claude/alpha-finish`.
- PR #4 remained draft/clean from `claude/alpha-finish` to `agent/repo-cleanup`.
- Latest PR #4 CI at the time was run `30046261240`, job `89338006946`, `Build and offline harnesses`,
  conclusion `SUCCESS`.

Derived branch tips:
- `origin/main`: `3fa65f5`.
- `origin/agent/repo-cleanup`: `81d232c`.
- `origin/agent/audit-cleanup-h1h2h3`: `f3021ec`.
- `origin/claude/hardening-batch`: `8ba127c`.
- `origin/claude/alpha-finish`: `7d25f24`.
- Android/P2 tips: `940c4e1`, `6c46545`, `74dd862`.

C1 rehearsal result:
- `git merge-base --is-ancestor origin/main origin/agent/repo-cleanup` -> exit `0`.
- `git merge-base --is-ancestor origin/agent/repo-cleanup origin/agent/audit-cleanup-h1h2h3` -> exit `0`.
- `git merge-base --is-ancestor origin/agent/audit-cleanup-h1h2h3 origin/claude/hardening-batch` -> exit `0`.
- `git merge-base --is-ancestor origin/claude/hardening-batch origin/claude/alpha-finish` -> exit `0`.
- Therefore the non-Android C1 content is a straight fast-forward ancestry chain from current `main` to
  current alpha. At that moment, the effective C1 candidate tree was exactly `origin/claude/alpha-finish`.
- `git rev-list --count origin/main..origin/claude/alpha-finish` -> `183`.
- `git rev-list --count origin/agent/repo-cleanup..origin/claude/alpha-finish` -> `27`.

Android/P2 exclusion at rehearsal time:
- `git merge-base --is-ancestor <tip> origin/claude/alpha-finish` returned exit `1` for Android/P2 tips
  `940c4e1`, `6c46545`, and `74dd862`.
- `git merge-base --is-ancestor <tip> origin/main` returned exit `1` for the same three tips.
- `git rev-list --count origin/claude/alpha-finish..origin/claude/android-apk-build-setup-90d9d5` -> `3`
  Android-only commits not present in alpha.

C1 reminder: re-run these ancestry checks after every new commit and immediately before Brandon's merge
approval. After PR #4 content reaches `main`, the three Android/P2 tips should still return exit `1` from
`git merge-base --is-ancestor <tip> origin/main`.

## 2026-07-23 (Codex C2 preflight) - live readiness and cache trap

Continuation preflight at local time `2026-07-23 15:25:55 -06:00`. No merge, deployment, R2 upload,
KV write, beta-code issue, live provider call, or Gmail draft was performed.

Fresh derived state at start: `HEAD` on `claude/alpha-finish` was `b1b4fc4`, with `git status -sb`
clean on `claude/alpha-finish...origin/claude/alpha-finish`; PR #4 remained draft/clean with latest
GitHub Actions success from run `30045604897`.

Readiness evidence:
- `powershell -ExecutionPolicy Bypass -File scripts\Check-AlphaLiveReadiness.ps1 -RequireGmail -RequireByok`
  passed. Startup doctor reported SQLite/audit ok, artifacts writable, Gmail OAuth client JSON parsed,
  Gmail token vault present, BYOK providers `anthropic, google`, and Brave Search configured via
  `BRAVE_SEARCH_API`. Secret values were not printed.
- Public `https://careerseeker.app/download/` returned 200 and still advertises the old undated ZIP:
  first SHA on the page was `D8F4916F949E225E87B3FB4B8D09A6FEF50DC7F2B68E0E19ED0BDC1CB981C7C7`,
  with `CareerSeeker-alpha-win-x64.zip` as the ZIP reference.
- Public HEAD for `/releases/CareerSeeker-alpha-win-x64.zip` returned 200 with length `31,018,621` and
  `Cache-Control: public, max-age=14400`.
- Public HEAD for `/releases/CareerSeeker-alpha-win-x64-2026-07-24.zip` returned 404, as expected before
  Friday C2 upload/deploy.
- Public release path probes for encoded traversal, nested path, `.env`, and `.bak` names returned 404.
- Bad-code `POST /api/verify` returned 403; no valid beta code was used.
- `wrangler --version` reported `4.112.0`; `wrangler whoami` succeeded with the stored OAuth profile;
  `wrangler r2 bucket list` showed bucket `careerseeker`. The attempted `wrangler r2 object list`
  command is not valid in Wrangler 4.112.0 under `r2 object` and should not be used as a C2 proof.

Confirmed local packaging trap and fix:
- The first current-head `scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage` attempt failed because
  Wrangler created an untracked repo-local `.wrangler/cache/wrangler-account.json`, and the release manifest
  correctly refused a dirty working tree.
- `.wrangler/` is now ignored in `.gitignore` so read-only Wrangler probes from the repo root do not poison
  release packaging.
- After removing the generated cache file, `git status -sb` was clean and the current-head package rerun
  passed through publish/package/self-check/helper smokes. The local ZIP produced before this docs commit had
  manifest source commit `b1b4fc4`, `dirty: false`, size `31,020,876`, and SHA-256
  `DE617F1B389AD17F8FC262496B67B72FA8696AE1A9CDFA066626FEF20EBEB58B`.

C2 reminder: because `RELEASE-MANIFEST.json` pins the exact source commit, rebuild the final ZIP after every
commit and again after Brandon's C1 merge to `main`. Do not upload a ZIP whose manifest source commit differs
from the merged `main` head.

## 2026-07-23 (Codex C1 containment preflight) - Android still excluded

Continuation preflight at local time `2026-07-23 15:16:39 -06:00`. No merge or deployment was performed.
Fresh derived state after `git fetch origin --prune`: `HEAD` on `claude/alpha-finish` was `7b9736c`,
with `git status -sb` clean on `claude/alpha-finish...origin/claude/alpha-finish`.

Open PR topology remained:
- #1 `agent/repo-cleanup` -> `main`, draft/clean.
- #2 `agent/audit-cleanup-h1h2h3` -> `agent/repo-cleanup`, draft/clean.
- #3 `claude/hardening-batch` -> `agent/audit-cleanup-h1h2h3`, draft/clean.
- #4 `claude/alpha-finish` -> `agent/repo-cleanup`, draft/clean.
- #5 `claude/android-apk-build-setup-90d9d5` -> `claude/alpha-finish`, draft/unknown.
- #6 `claude/p1-sync` -> `claude/android-apk-build-setup-90d9d5`, draft/clean.

Current branch tips derived during this check:
- Alpha head: `7b9736c`.
- PR #4 base `origin/agent/repo-cleanup`: `81d232c`.
- `origin/main`: `3fa65f5`.
- Android P0 tip `origin/claude/android-apk-build-setup-90d9d5`: `940c4e1`.
- Android P1 tip `origin/claude/p1-sync`: `6c46545`.
- Additional non-PR remote `origin/claude/p2-publisher`: `74dd862`; it contains P0/P1 and is also out of
  alpha/main.

Containment evidence:
- PR #4 currently has `25` commits over `origin/agent/repo-cleanup`.
- `origin/agent/repo-cleanup`, `origin/agent/audit-cleanup-h1h2h3`, and `origin/claude/hardening-batch`
  all returned exit code `0` from `git merge-base --is-ancestor <branch> HEAD`, confirming those intended
  audit branches are contained in alpha.
- Android/P2 tips returned exit code `1` from `git merge-base --is-ancestor <tip> HEAD`, confirming none is
  contained in alpha.
- The same Android/P2 tips returned exit code `1` from `git merge-base --is-ancestor <tip> origin/main`,
  confirming none is in current `main`.
- Android branches fork from alpha at `dca6eb5`; alpha is now `12` commits past that fork, while Android P0
  is `3` commits down its own branch.

C1 reminder: re-derive all branch tips again immediately before Brandon's merge approval. After PR #4's
content is brought into `main`, repeat the Android/P2 `merge-base --is-ancestor <tip> main` checks and
expect exit code `1` for each excluded branch tip.

## 2026-07-23 (Codex package preflight) - tester ZIP path re-verified

Continuation preflight at local time `2026-07-23 15:11:37 -06:00`, still before Brandon-only C1/C2
approval. Fresh derived state before the run: `git rev-parse --short HEAD` -> `5f187c1`, with
`git status -sb` clean on `claude/alpha-finish...origin/claude/alpha-finish`; PR #4 remained draft/clean
from `claude/alpha-finish` to `agent/repo-cleanup`.

Fresh offline package evidence:
- `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage`
  completed successfully.
- Default offline verifier inside that run: `Offline total: 334 passed, 0 failed`.
- Win-x64 single-file publish completed.
- Published executable demo smoke completed with final counters `errors: 0`.
- Trusted-tester ZIP built at `output\release\CareerSeeker-alpha-win-x64.zip`, `31,020,857` bytes.
- ZIP SHA-256: `34B8200018C9371BC85D3ECD1CBEF2369EA31CAF543A17CDDC3C67A88073786B`.
- Package self-check reported `manifest: ok`, `checksums: 46 verified`, and `dashboard smoke: passed`.
- Packaged helper smokes exercised readiness, scheduled-task dry run, safe demo evidence, Scout dry run,
  research preview, selected-job preview, live-L1 dry run, audit export, BYOK clear, Gmail disconnect,
  evidence package export, and evidence package import.

No live provider calls, real Gmail draft, merge, deploy, or secret-value prints were performed. The package
artifact is local release evidence only until Brandon approves C1/C2 and the production download is exposed.

## 2026-07-23 (Codex readiness recheck) - PR #4 still green

Continuation recheck at local time `2026-07-23 15:06:18 -06:00`. This environment still reports
Thursday 2026-07-23 America/Denver; the Friday gates remain Brandon-only regardless of the date label.

Fresh derived state after `git fetch origin --prune`:
- `git rev-parse --short HEAD` -> `95b389a`.
- `git status -sb` -> clean on `claude/alpha-finish...origin/claude/alpha-finish`.
- Open PR topology remained #1 `agent/repo-cleanup`, #2 `agent/audit-cleanup-h1h2h3`, #3
  `claude/hardening-batch`, #4 `claude/alpha-finish`, #5 `claude/android-apk-build-setup-90d9d5`,
  and #6 `claude/p1-sync`. PR #5 remains chained onto PR #4, and PR #6 remains chained onto PR #5.
- PR #4 remained an open draft from `claude/alpha-finish` to `agent/repo-cleanup`, merge state `CLEAN`.
- Latest observed PR #4 check was GitHub Actions run `30044492077`, job `89332202866`,
  `Build and offline harnesses`, conclusion `SUCCESS`, completed `2026-07-23T21:02:53Z`.

Fresh local evidence on the same head:
- `dotnet build CareerSeeker.sln -c Release --warnaserror` -> 0 warnings, 0 errors.
- `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1` -> Release build 0 warnings,
  0 errors; `Offline total: 334 passed, 0 failed`.

No code changes, live provider calls, Gmail actions, package builds, merges, deployments, or secret-value
prints were performed in this continuation. C1/C2 are still pending Brandon approval. During C1, re-derive
the current Android branch tips and verify the merged `main` does not contain PR #5/#6 content.

## 2026-07-23 (Codex external-auditor F1) — provider error redaction + Gemini Tailor parser hardening

Environment note: this Codex environment reports Thursday 2026-07-23 America/Denver; the resume prompt
and seed are dated Friday 2026-07-24. Dates below use observed local session dates. Never trust a SHA in
this file — derive with git before acting.

Derived starting state after `git fetch origin --prune`: checkout was clean on `claude/alpha-finish`,
tracking `origin/claude/alpha-finish`, with head `04d57a2` before this audit work. Open PRs observed with
`gh pr list --state open`: #1 `agent/repo-cleanup`, #2 `agent/audit-cleanup-h1h2h3`, #3
`claude/hardening-batch`, #4 `claude/alpha-finish`, #5 `claude/android-apk-build-setup-90d9d5`, #6
`claude/p1-sync`. No merges were performed.

Baseline evidence before edits:
- `dotnet build CareerSeeker.sln -c Release --warnaserror` -> 0 warnings, 0 errors.
- `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1` -> `Offline total: 327 passed, 0 failed`.

Triage and fixes:
- **Provider error-body surfacing (`4c30249`) — confirmed too strong as written, fixed.** Source confirms
  the app sends provider keys in headers only: Anthropic uses `x-api-key`, Google uses `x-goog-api-key`,
  and neither request JSON body carries a key. The prior "never contains the API key" claim still relied
  on providers/proxies never echoing headers in diagnostic response bodies. `ProviderHttpErrors` now
  redacts the exact key used for the request before truncating and surfacing provider error text. Added
  GatewayGateHarness coverage for both Anthropic and Google error bodies that deliberately echo the dummy
  request key; both must keep actionable error text while replacing the dummy key with `[redacted-api-key]`.
- **Gemini-as-Tailor-fallback `JsonReaderException` — confirmed likely parser asymmetry, fixed offline.**
  Tailor previously parsed the entire model response after stripping only leading markdown fences, while
  Researcher already tolerated prose-wrapped balanced JSON. `GatewayTailorModel.ParseDraft` now extracts
  balanced JSON object candidates from prose/fenced responses, preserves braces inside strings, rejects
  non-object JSON, and still throws on real parse failure. Added HookHarness coverage for prose-prefixed
  JSON and citation-bracket text before the JSON object.

Post-fix verification:
- `dotnet run --project tests\GatewayGateHarness\GatewayGateHarness.csproj -c Release` -> `36 passed, 0 failed`.
- `dotnet run --project tests\HookHarness\HookHarness.csproj -c Release` -> `16 passed, 0 failed`.
- `dotnet build CareerSeeker.sln -c Release --warnaserror` -> 0 warnings, 0 errors.
- `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1` -> `Offline total: 331 passed, 0 failed`.
  Per-harness count is now Slice 28 · Engine 89 · Researcher 55 · Hook 16 · StoreParity 22 · GatewayGate
  36 · DispatcherNoSend 35 · Lifecycle 44 · Renderer 6. `$ExpectedOfflineTotal` and the count-bearing
  docs/verifier assertions were bumped in lockstep.

No secrets were printed; only dummy test keys appear in harness fixtures. No live BYOK/Gmail run was
performed, so the parser fix has offline evidence but not a fresh live Gemini Tailor proof. Gates C1/C2
remain Brandon-only decisions.

Subsequent F1 SSRF scrutiny found and fixed one additional classifier gap. `IsPubliclyRoutable` still
accepted IANA non-global special-purpose destinations, including RFC 8215's local-use translation prefix
`64:ff9b:1::/48`, plus IPv4 documentation/protocol-assignment ranges. The guard now admits globally
assigned IPv6 unicast (with the already-audited public NAT64/6to4 handling) and rejects the non-global
IANA ranges covered by new tests. Primary references:
`https://www.iana.org/assignments/iana-ipv4-special-registry/iana-ipv4-special-registry.xhtml`,
`https://www.iana.org/assignments/iana-ipv6-special-registry/iana-ipv6-special-registry.xhtml`, and
`https://www.rfc-editor.org/info/rfc8215`.

SSRF-fix evidence:
- `dotnet run --project tests\ResearcherHarness\ResearcherHarness.csproj -c Release` -> `57 passed, 0 failed`.
- `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1` -> Release build 0 warnings, 0 errors;
  `Offline total: 333 passed, 0 failed`.
- The count-bearing docs, verifier assertions, and `$ExpectedOfflineTotal` moved together from 331 to 333.
- The configured-system-proxy (`SocketsHttpHandler.UseProxy`) residual remains accepted and unchanged; changing
  proxy policy is a Brandon product decision.

H2/store-parity scrutiny:
- **`draft-job` startup sweep gap confirmed and fixed.** Unlike `demo`, `alpha`, and `dashboard`, the
  selected-job command initialized the durable SQLite store and immediately began a new L1 pipeline run.
  It now calls the same side-effect-free `ReconcileStartupAsync` first. A behavioral EngineHarness case
  leaves a successful draft attempt stranded at `READY`, invokes `draft-job`, and verifies the prior
  application reaches `DRAFTED` before the command begins new work.
- The other unswept commands are not autonomous engine starts: `scout-boards` only ingests jobs;
  `export-audit` and `export-alpha-package` are observational; `import-profile` maintains the claim
  oracle; and `control-app` is an explicit human action. No automatic sweep was added to those boundaries.
- **Store parity confirmed.** `GetApplicationIdsInStatesAsync` is a pure read in both stores:
  the in-memory implementation filters under its mutex, while SQLite executes an ordered parameterized
  `SELECT`; neither method calls `Now()`.

H2-fix evidence:
- `dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release` -> `90 passed, 0 failed`.
- `powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1` -> Release build 0 warnings, 0 errors;
  `Offline total: 334 passed, 0 failed`.
- The count-bearing docs, verifier assertions, and `$ExpectedOfflineTotal` moved together from 333 to 334.

GitHub/release closeout:
- PR #4 remains an open draft targeting `agent/repo-cleanup`; no review submissions or inline review threads
  were present. Its stale description was replaced with the current 334-test evidence, F1 fixes, residuals,
  and the explicit no-merge/no-deploy gate language.
- After a fresh fetch, both current Android branch tips returned exit code 1 from
  `git merge-base --is-ancestor <tip> HEAD`: neither PR #5 nor PR #6 content is contained in the alpha head.
  Re-derive those tips and repeat the check against merged `main` during Gate C1.

## 2026-07-22 (Opus session) — publish-to-web roadmap, phases W0–W3 (blocked at W1 on R2)

Executing the 60-hour alpha publish roadmap (`Alpha-Publish-Roadmap-2026-07-22.md`, Fable 5) toward a
tester-downloadable alpha on `careerseeker.app`. Never trust a SHA here — re-derive.

**W0 — consolidation (done).** `claude/alpha-finish` fast-forwarded onto the F2 branch
(`claude/codex-audit-pr2-triage-mjdur6`) and pushed, so PR #4 is now the single complete Friday diff
(checkpoint → H1/H2/H3 → A1/L1/M1/M2 → F2). **The F2 claim is now verified on Windows**, closing the
"not executed here" caveat in the Fable section below: `dotnet build -c Release --warnaserror` 0W/0E,
and the full `scripts/Verify-Alpha.ps1` prints `Offline total: 327 passed, 0 failed` with the measured
total equal to the pinned `$ExpectedOfflineTotal`. The packaged path Fable could not exercise on Linux
is green too: `-IncludePublish -IncludePackage` gave publish smoke `errors: 0`, `manifest: ok`,
`checksums: 46 verified`.

**Release candidate artifact (rehearsal build, from head `1d1a5a4`):**
`output/release/CareerSeeker-alpha-win-x64.zip`, 31,018,621 bytes,
SHA-256 `D8F4916F949E225E87B3FB4B8D09A6FEF50DC7F2B68E0E19ED0BDC1CB981C7C7`.
This is the **rehearsal** artifact for infra testing only — the tester-facing ZIP is rebuilt from the
merged line on Friday and its hash replaces this one everywhere.

**Trap worth recording:** the first `-IncludePackage` run failed with *"Alpha release manifest was
generated from a dirty working tree"*. Cause was an untracked, **non-gitignored** `careerseeker-site-v2.zip`
sitting in the repo root (a site snapshot, not repo content). Moved to
`Desktop/careerseeker-site-v2-snapshot-2026-07-21.zip`; the run then passed. The packaging step requires a
clean tree, and `*.zip` is not gitignored at the repo root — worth adding to `.gitignore` so this cannot
recur or, worse, get committed.

**W1 — distribution infrastructure: RESOLVED and working.** It was blocked mid-session for a reason the
roadmap did not anticipate. The roadmap assumed the only risk was token scope; there were two distinct
failures:
- `CLOUDFLARE_ACCOUNT_API_TOKEN` is valid (it lists the `careerseeker-site` Pages project fine) but had
  **no R2 permission** — `wrangler r2 bucket list` failed with API error **10000** (authentication).
- An independent credential path (Cloudflare MCP OAuth) authenticated fine but failed with error
  **10042: "Please enable R2 through the Cloudflare Dashboard"** — i.e. **R2 had never been activated on
  the account**, a one-time owner action that no token grant substitutes for.

Brandon then enabled R2, created bucket **`careerseeker`** (note: *not* `careerseeker-releases` as the
roadmap specified — harmless, because the Function references the *binding* name `RELEASES`, which is
independent of the bucket name), and added `CLOUDFLARE_R2_API_TOKEN` (R2 + Pages edit) to
`secrets/env.secrets`.

**Credential map — which token does what (learned the hard way, none of them covers everything):**

| Operation | Working credential |
| --- | --- |
| R2 bucket/object ops | `CLOUDFLARE_R2_API_TOKEN` |
| Pages project GET/PATCH (bindings), deploys | `CLOUDFLARE_R2_API_TOKEN` (also has Pages edit) |
| Pages project list | `CLOUDFLARE_ACCOUNT_API_TOKEN` |
| **KV read/write/list** | **none of the tokens — use the stored `wrangler` OAuth session** (unset `CLOUDFLARE_API_TOKEN` and wrangler falls back to it) |
| Zone cache purge | none (zone token returns 401) — dashboard only |

**`wrangler kv key list` defaults to a LOCAL simulated namespace.** Without `--remote` it returns `[]`
with no error and no auth required — which looks exactly like "the namespace is empty". Every KV command
here needs `--remote`. This produced one wrong claim earlier in the session (see the correction below).

**W1.2/W1.4/W1.5 — done and proven.** ZIP uploaded to `careerseeker/alpha/CareerSeeker-alpha-win-x64.zip`;
`r2 object get` round-trip hash matched the local hash exactly. `RELEASES` binding added to **both**
production and preview via a PATCH built from the live config so `BETA_KV` was carried forward rather
than clobbered (verified present in both afterwards). Redeployed; both Functions lines printed.

**Site changes made (live only in `C:\Users\bkirk\Desktop\site-v2`, still NOT under version control —
strongly recommend git-initializing it).** Backup taken first at `Desktop/site-v2-backup-2026-07-22`.
- New `functions/releases/[[path]].js` — streams from the `RELEASES` R2 binding under the `alpha/`
  prefix, flat-filename regex (no traversal), `Content-Disposition: attachment`, `nosniff`. Added one
  guard beyond the roadmap's listing: a missing `env.RELEASES` returns 404 rather than throwing a 500
  with a stack — which is the *current* state, since the binding does not exist yet.
- `functions/api/verify.js` — `download_url` now
  `https://careerseeker.app/releases/CareerSeeker-alpha-win-x64.zip` (the dead `CareerSeeker-Alpha-Setup.exe`
  TODO is gone). This URL is identical under both the R2 and GitHub-Releases paths, so it is not blocked.
- `download/index.html` — rewritten: ZIP contents, tester quickstart, the draft-only invariant stated
  plainly, SHA-256 in a `<code>` block marked `<!-- SHA-UPDATED-F2.3 -->`, and unsigned-binary/SmartScreen
  guidance. Sole CTA is still "Request alpha access" → `/beta/`; the raw file URL stays unpublished.
**Deployed to production** (Brandon authorized production deploys explicitly; note the roadmap's W1.5
command targets **production**, not preview — `--branch <name>` is what gives a preview). Deploy
`b656a582`, run from inside `site-v2`, printed both required lines — `Compiled Worker successfully` and
`Uploading Functions bundle` — so Functions shipped. Verified against both the per-deploy URL and
`https://careerseeker.app`:

| Check | Result |
| --- | --- |
| `/releases/CareerSeeker-alpha-win-x64.zip` | 404, body exactly `Not found.` — the Function's own body, **not** a 500 |
| `/releases/..%2fsecrets`, `/releases/nested/path.zip` | 404 (path hygiene holds) |
| `POST /api/verify` bad code | 403 `{"error":"Invalid code…"}` — **BETA_KV survived adding the new Function** |
| control: `/definitely-not-a-real-page-xyz` | serves the site index — i.e. Pages' default 404, proving the `/releases/` 404 above really is the Function |

That first deploy happened while R2 was still blocked, which was deliberate: it proved the Function
compiles and ships, and confirmed the `!env.RELEASES` guard against a genuinely absent binding.

**Correction to a claim made earlier this session:** the justification given at the time was "BETA_KV is
empty — zero signups, zero issued codes, checked before deploying." That check ran `wrangler kv key list`
**without `--remote`**, so it read the local simulated namespace and was not valid evidence of anything.
The conclusion happened to be correct — a later `--remote` list via the OAuth session showed the
namespace genuinely held no keys — but the reasoning was unsound when it was stated, and the same mistake
would silently hide real signups. Always pass `--remote`.

**After the binding landed — the full tester journey, verified end to end on `https://careerseeker.app`:**

| Step | Result |
| --- | --- |
| `POST /api/signup` | `{"ok":true}` and the key **actually persisted** (`signup:…`, confirmed with `--remote`) |
| Issue code via `wrangler kv key put --remote` | written |
| `POST /api/verify` with that code | `{"ok":true,"download_url":"https://careerseeker.app/releases/CareerSeeker-alpha-win-x64.zip"}` |
| GET that URL | **200**, `application/zip`, 31,018,621 bytes, SHA-256 **matches** the built artifact exactly |
| Response headers | `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff`, ETag present |
| `/releases/..%2fsecrets`, `/releases/nested/path.zip`, `/releases/.env`, `/releases/*.bak` | all 404 |
| `/releases/alpha/…` (probing the prefix) | 404 — the `alpha/` prefix does not leak |
| After deleting the test code | `POST /api/verify` returns 403 again |

Test keys (`code:SMOKETEST22`, `signup:alpha-rehearsal-test@…`) were deleted; KV is empty again.

**⚠ FRIDAY HAZARD — edge cache will serve the stale ZIP.** The download responds with
`Cache-Control: public, max-age=14400` and `cf-cache-status: HIT` (observed `Age: 244`). The Function
sets `max-age=3600`; something zone-side (likely Browser Cache TTL) rewrites it to **4 hours**, so the
Function's own header is not authoritative. Overwriting the R2 object in F2.3 therefore does **not**
immediately change what testers download — for up to 4 hours the edge can serve Wednesday's bytes while
`/download/` advertises Friday's SHA-256. That mismatch is indistinguishable from a corrupted download
and would burn tester trust on day one. The zone token **cannot** purge (401). Two fixes, pick one:
1. **Publish under a dated filename** (e.g. `CareerSeeker-alpha-win-x64-2026-07-24.zip`) and point
   `verify.js` + `/download/` at it. A new URL is never stale — no permissions needed, deterministic,
   and doubles as provenance. **Recommended.**
2. Brandon purges the URL from the dashboard (Caching → Configuration) immediately after upload, then
   re-fetches and re-checks the hash before handing any code to a tester.

**DECIDED (Brandon, 2026-07-22): option 1 — Friday's build publishes under a dated filename, never
overwriting.** Concretely, F2.3 becomes: upload to
`careerseeker/alpha/CareerSeeker-alpha-win-x64-2026-07-24.zip`, point `verify.js`'s `download_url` at
`https://careerseeker.app/releases/CareerSeeker-alpha-win-x64-2026-07-24.zip`, update the SHA-256 at the
`<!-- SHA-UPDATED-F2.3 -->` marker plus the filename referenced in the quickstart's `Get-FileHash`
example, then redeploy. The serving Function needs **no change** — it already accepts any flat filename
under `alpha/`. Leave the old undated object in place; it is unreferenced once `verify.js` moves, and
deleting it would only make an already-cached URL start 404ing.

**Per-deploy URLs lag.** The first verification pass against `b656a582.careerseeker-site.pages.dev`
returned Cloudflare's *"Deployment Not Found"* page, which is a 404 that looks exactly like a real one.
Re-probe after ~30 s and check the **body**, not just the status code, before concluding anything is
broken. (The existing note about the `preview.` alias lagging applies to the hash URL too.)

**W3.1 clean-machine rehearsal — done, green.** Fresh extract of the ZIP to `%TEMP%`, then:
`Verify-CareerSeeker-Alpha.cmd` → `manifest: ok`, `checksums: 46 verified`, `dashboard smoke: passed`;
`Setup-CareerSeeker-Alpha.cmd` → workspace, `profile.template.json`, `secrets/env.secrets` all created;
`Run-CareerSeeker-Demo.cmd` → `errors: 0` (cycles 1, discovered 3, acted 1, drafted 1, blocked 1,
rejected 1); dashboard → `/` and `/evidence.html` both HTTP 200 carrying `no-store` / `nosniff` /
`no-referrer`. No credits spent, no Gmail, no network.

Friction list from the rehearsal (tester-facing, small):
1. **`Setup-CareerSeeker-Alpha.cmd` lists the demo as step 6** — behind profile editing, API keys, Gmail
   OAuth, and live-readiness. The demo needs none of those. A tester following that order hits three
   credential chores before seeing the product work. Reorder so the demo is step 1. (Site copy already
   says "run the demo first", so the launcher currently contradicts the download page.)
2. Setup step 3 tells the tester to hand-edit `secrets\env.secrets` in Notepad to add API keys. That is
   the roughest edge in the flow and undercuts the "the app walks you through setup" promise.
3. Setup spawns Notepad on `profile.template.json` and the launchers end in `pause` — both correct for a
   double-clicking tester, but they make headless/CI invocation hang. Not a tester defect; noted so the
   next session does not mistake it for one.

**Friction fix shipped (friction item 1 above).** `Setup-CareerSeeker-Alpha.cmd` and the
`README-alpha.txt` generated by `Package-AlphaRelease.ps1` both now lead with the demo and the
dashboard, then break to a "when you want it working on real jobs" section for profile/keys/Gmail.
Setup's notepad message no longer reads as "edit this now". Copy-only; every string asserted by
`Test-AlphaReleasePackage.ps1` was preserved (those are substring `Contains` checks with no ordering
dependency), and a full `-IncludePublish -IncludePackage` rebuild confirms it ships: `manifest: ok`,
`checksums: 46 verified`, offline 327/0.

That rebuild changed the ZIP hash to `4E743CEAAD9AD6FC23181FF2D11B29448631B777E6B777903D6EBD73F2E673B3`
(31,018,859 bytes). It was **deliberately not uploaded**: what is published (`D8F4916…`) still matches
both the `/download/` page and what the edge serves, and Friday's F2.2 build supersedes both anyway.
Re-uploading now would only desynchronise those three and burn the 4-hour cache window for no gain.

**Packaging requires a committed, clean tree** — `$sourceDirty` comes from `git status --short`, which
counts **untracked** files. So the order is always edit → commit → package; you cannot package
uncommitted work. This bit twice in one session (the stray site zip, then the launcher edits).

**W3.2 SmartScreen / AV — done, with one part I could not do.**
- **Defender scan is clean** on both the downloaded ZIP and the extracted 68 MB
  `SeekerSvc.Engine.exe` (engine 1.1.26060.3008, signatures 1.455.271.0, real-time protection on).
- **Mark-of-the-Web propagates to all 47 extracted files at `ZoneId=3`** when a marked ZIP is extracted
  the way Explorer does it (Shell COM). So every `.cmd` launcher and the `.exe` are marked, and each
  one prompts on first run.
- **Unblocking the ZIP before extracting leaves zero MOTW on the extracted files** — verified by
  extracting an unblocked copy and finding no `Zone.Identifier` stream on the exe or any launcher.
  This is strictly better tester advice than "click through the warning", so `/download/` now leads
  with it and the quickstart's extract step says to unblock first.
- MOTW does **not** block non-interactive execution (the exe ran fine from the marked extraction) —
  the barrier is purely the interactive Explorer/SmartScreen dialog.
- **Not verified visually:** the exact dialog text and click path. Driving a real browser download and
  clicking through an OS dialog is not something this session could do (browsers are read-only to the
  desktop-automation tooling), so the two dialogs named on `/download/` — "Windows protected your PC"
  for the `.exe`, "Open File - Security Warning" for a `.cmd` — are the standard Windows behaviour for
  unsigned + MOTW files rather than something observed here. **Brandon should eyeball this once** on a
  real download before testers do; if the wording differs, fix `/download/` and `README-alpha.txt`.

**Gate B approved and executed (2026-07-22) — W4.1 and W4.2 are green on the F2-consolidated head.**
The live evidence gap the roadmap flagged (live-path proof dated 2026-07-20, predating H1..M2/F2) is
closed. Evidence from `Verify-Alpha.ps1 -IncludeLive -IncludeResearch`, exit 0:

| Step | Result |
| --- | --- |
| Offline suite (same run) | 327 passed, 0 failed |
| BYOK live provider smoke | **7 passed, 0 failed** — Anthropic completion, Gemini completion, Gateway Tailor parseable draft, Gate live entailment, Gate spend accounted, **and the live Tailor draft passes the bounded Fabrication Gate** |
| Startup doctor (require Gmail + BYOK) | all OK — sqlite audit chain, artifacts, OAuth client, token vault, byok `anthropic, google`, Brave |
| Dashboard one-shot | audit chain ok, 72 events, all controls available |
| Live Brave/BYOK research (W4.2 — exercises H1+A1+F2 against the real network) | GitLab via brave: 9 docs retrieved, **facts: 4**, passed on attempt 1 of 3 |

**The first Gate B attempt failed, and the reason matters for testers.** The Anthropic account had
run out of prepaid credit; the harness reported only `HttpRequestException: 400 (Bad Request)` because
both providers read the response body and then discarded it via `EnsureSuccessStatusCode()`. The body
said *"Your credit balance is too low to access the Anthropic API."* Fixed in `4c30249`: both providers
now raise the provider's own error text (600-char cap; no key exposure — keys travel in headers).
Verified live: the same failure now prints the credit message verbatim. This is the single most likely
BYOK failure mode for testers — exhausted credit, revoked key, rate limit all arrive as a bare 4xx.
`/download/` now carries a prepaid-key heads-up (deploy `0a71f9cc`).

Two observations from the outage worth keeping:
1. **The Gemini fallback path through Tailor failed both times it actually served.** While Anthropic
   was declining, StrongCloud failed over to `gemini-3.1-pro-preview` and the Tailor draft parse threw
   `JsonReaderException` in both runs. With Anthropic healthy, Tailor parses fine. So Gemini currently
   works as a completion provider but not as a Tailor-stage server — relevant to the provider-priority
   question below, and worth a targeted look regardless: it is the failover the design counts on.
2. Research quality note: the GitLab dossier shows `proposed facts: 0, fallback facts: 4` — extraction
   proposed nothing and the dossier was built from fallback facts. The smoke's criterion (`facts > 0`)
   passed legitimately, but the extraction path deserves a quality pass post-alpha.

**Provider-priority question (Brandon, 2026-07-22 — decision deliberately deferred past Friday).**
Prompted by the credit outage, Brandon suggested considering Google as the primary inference provider:
Google bills post-facto while Anthropic requires a prepaid balance a novice won't monitor, and testers
already link a Google account for Gmail. Recorded pros/cons for when this is picked up:
- *For:* post-paid billing; AI Studio keys have a free tier (a tester can mint a working key with no
  billing set up at all); same Google account as the Gmail link (though note: Gmail OAuth does **not**
  provision a Gemini API key — the tester still creates one in AI Studio, so the friction win is
  smaller than it looks).
- *Against, currently:* `GatewayGateHarness` pins the StrongCloud primary **by name and price**
  (`claude-sonnet-4-6`, 3.00/15.00) and the exact failover order — the switch is an audited, asserted
  routing change with doc/count lockstep, not a config flip; observation 1 above means Gemini needs
  Tailor-stage parsing/prompt work before it could be primary; and the routing comment's design intent
  is "two strong vendors, neither alone load-bearing", which a same-vendor primary+Gmail coupling cuts
  against. **Do not change routing before Friday** — it would invalidate the Gate B evidence above and
  reopen the audited diff.

**W4.3 complete (2026-07-23, ~10:41 local) — Gate B is fully closed and all W-phases are done.**
Brandon ran the packaged LIVE path from a fresh extraction (`Desktop\dryrun`) of the F2-consolidated
ZIP, connected to the dedicated test account `careerseeker.test.brandon@gmail.com`. Evidence, from two
independent sources that agree to the minute:
- **Gmail:** a new draft at 10:41 AM — subject "Application for Senior Software Engineer at CareerSeeker
  Alpha", **self-addressed to the test account**, ATS resume **PDF attached** — distinct in subject and
  time from every pre-hardening draft (Jul 8–19). **Sent remained empty.**
- **The extraction's own audit chain:** 10 events at 2026-07-23T16:41:42–45Z (= 10:41 local) for
  application/1 — six `state_change`, two `effect_attempt`, one `artifacts_saved` — with the hash chain
  intact at the tail.

Two traps hit on the way, both worth folding into tester docs later:
1. A fresh extraction's `secrets\env.secrets` is an empty template; the live path fails with
   "BYOK mode could not find provider keys" until the tester fills it (or runs
   `Connect-CareerSeeker-Providers.cmd`). The error message itself was clear — it named every location
   it checked and the exact variables expected.
2. **Opening an old Gmail draft resaves it**, bumping its date to today and floating it to the top —
   which looks exactly like a new draft. The repo-root audit chain (flat since Jul 19, count matching
   the Gate B dashboard's 72) is what proved the first "new" draft was a re-dated old one. Verify
   drafts by audit-chain timestamp, not by Gmail's date column.

Also of standing value: the test account's Drafts hold ~16 drafts accumulated across Jul 8–23 with
**zero messages ever in Sent** — a two-week cumulative demonstration of the draft-only invariant.

**Remaining:** F1 audit support and F2 merge/build/publish (Friday 2026-07-24). Gates C1/C2 remain
Brandon's. The Friday runbook deltas earlier in this section still apply (dated filename, `--remote`
on KV, bucket named `careerseeker`).

**Seven untracked planning docs appeared in `docs/` and were removed — resolved.** At 11:27 local on
2026-07-22 these landed in `docs/` (all created in the same second, contents dating back to June):
`Alpha-Publish-Roadmap-2026-07-22.md`, `Android-Dashboard-Pro-Spec-2026-07-22.md`,
`CareerSeeker-Spec-5.6-LLM-Gateway.md`, `Cleanup-Handoff-2026-07-21.md`, `Opus-Build-Roadmap-2026-07-21.md`,
`alpha-audit-2026-07-20.md`, `claude-code-deploy-prompt.md`. They blocked packaging (untracked files make
the tree dirty), and since **this repo is public**, committing them would have published internal planning —
including an Android spec, when the Android work is deliberately kept in a private repo. A scan found no
credential-shaped strings, so it was a publication-appropriateness question, not a leak. Brandon confirmed
they were copied in by mistake and belong only in his offline `Desktop\Career Seeker\` folder. Each parked
copy was verified **byte-identical (SHA-256)** to the copy already in that folder before deletion, so
nothing was lost. `docs/` is clean again.

*Guard worth adding later:* nothing prevents a repeat — a stray file in `docs/` or a `*.zip` at the repo
root both silently break packaging, and the second one nearly got committed earlier in the session.

**Friday runbook deltas — read before F2.3.** The distribution path is already built and proven, so
Friday is: merge (C1) → rebuild the ZIP from merged `main` (F2.2) → upload → update the published hash →
deploy → issue codes (C2). Three things differ from the roadmap as written:
1. The bucket is **`careerseeker`**, not `careerseeker-releases`.
2. KV commands need `--remote` **and** the OAuth session, not a token.
3. The edge-cache hazard above — use a dated filename or purge, or testers get Wednesday's bytes.

## 2026-07-22 (Codex-role audit, Fable 5) — PR #2/#4 triage + one confirmed fix

Never trust a SHA in this file — derive with `git rev-parse --short HEAD` / `git log --oneline -8`.
This session ran the Phase-2 audit-support role (Codex active) against the consolidated post-checkpoint
diff (PR #4 `claude/alpha-finish` → `agent/repo-cleanup`, which carries PR #2's H1/H2/H3 + CLAUDE.md and
PR #3's A1/L1/M1/M2). Work landed on branch `claude/codex-audit-pr2-triage-mjdur6`, based on the
`claude/alpha-finish` tip so the fix rides on top of everything under audit.

**Environment note:** audited on Linux with .NET 8; the offline harnesses are cross-platform and were run
directly. `scripts/Verify-Alpha.ps1` is Windows/PowerShell-oriented (workspace initializer, docs-site,
publish/package steps) and was **not** executed here; instead the two things it would catch were validated
directly — the measured offline total (327, matching `$ExpectedOfflineTotal`) and every count-bearing
doc-smoke assertion string. Re-run the full `Verify-Alpha.ps1` on Windows to confirm the packaged path.

Triage verdicts against source (confirm = claim holds; the PR bodies' self-disclosures all held up):
- **H1 connect-time guard** — CONFIRMED sound: fail-closed multi-address rule, dials the validated IP
  (no re-resolution TOCTOU), redirects re-enter the ConnectCallback. **But** its IP classifier had a real
  gap (see the fix below).
- **H2 sweep scope** (demo/alpha/dashboard swept; six one-shots unswept) — CONFIRMED correct.
- **Store parity** `GetApplicationIdsInStatesAsync` — CONFIRMED a pure read, zero `Now()` in both stores;
  parity case passes (StoreParity 22).
- **A1** (`::` rejected), **L1** (`PRAGMA table_info` migration; column index 1 is the name; idempotent,
  pre-existing row preserved, round-trips), **M1** (pinned `$ExpectedOfflineTotal` + drift throw; the
  premise correction that CI already runs the SQLite harnesses is right), **M2** (query-string doc token
  is a documented acceptance behind loopback + `RequestCameFromThisDashboard` + per-process token) — all
  CONFIRMED as described.
- **Verifier whitespace-normalized row assertions** — CONFIRMED robust.

**Confirmed finding fixed this session — F2 (SSRF classifier, IPv6 embedded-IPv4):**
`PrivateNetworkGuard.IsPubliclyRoutable` returned `true` for IPv6 forms that embed or route to a private/
loopback IPv4 — IPv4-compatible `::/96` (e.g. `::7f00:1` = 127.0.0.1, `::169.254.169.254`), NAT64
`64:ff9b::/96`, and 6to4 `2002::/16`. The guard already unwraps IPv4-*mapped* `::ffff:` for exactly this
reason and A1 had just closed `::`; these were the same family of gap left open. Fix reclassifies any such
address by the IPv4 it reaches (`TryExtractEmbeddedIPv4`), so a private v4 can no longer slip through in a
v6 disguise. Two harness cases added to the `[ SSRF guard ]` section (reject the private-embedding forms;
regression-guard that genuinely-public v6 and NAT64/6to4-wrapping-a-public-v4 stay routable).
ResearcherHarness 53→55, offline total 325→327; `$ExpectedOfflineTotal` and all five asserted doc counts
bumped in lockstep per the CLAUDE.md drift trap.

**Residual noted, NOT changed (needs a product decision — G6-adjacent):** the guarded `HttpClient` leaves
`SocketsHttpHandler.UseProxy` at its default (true), so if a system/environment HTTP proxy is configured
the `ConnectCallback` validates the *proxy's* address, not the redirect target — the connect-time IP guard
is bypassed for the real destination when a proxy is present. Confirmed by repro (the handler routed
through an injected env proxy). Low/situational for a local Windows alpha (the string pre-filter still
blocks literal-IP private targets). Forcing `UseProxy=false` would break testers who need a corporate
proxy for outbound internet, so this is left for Brandon/Opus to decide rather than changed in a triage
pass.

Verification (this session, on this branch): `dotnet build CareerSeeker.sln -c Release --warnaserror`
0W/0E; all nine offline harnesses green, measured total **327** (Slice 28 · Engine 89 · Researcher 55 ·
Hook 14 · StoreParity 22 · GatewayGate 34 · DispatcherNoSend 35 · Lifecycle 44 · Renderer 6), equal to the
pinned `$ExpectedOfflineTotal`. Invariants unchanged: no Gate bypass, `VerifierEntailment` pin untouched,
Dispatcher still no-send, local-first, reconcile side-effect-free. No secrets printed; no live/spending
runs (G2 intact); no Gmail draft created.

**Gate G1 (merge PR #2/#4 → `agent/repo-cleanup`) is unchanged and remains Brandon's call** — nothing was
merged this session. When G1 happens, re-derive the merged head with `git rev-parse --short HEAD` and
record it here (no embedded head, per H3).

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
