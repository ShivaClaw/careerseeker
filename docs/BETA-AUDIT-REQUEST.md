# CareerSeeker Beta Audit Request

Updated: 2026-08-12

This is the adversarial review index for the Windows Beta milestone ladder.
Each claim below is limited to evidence executed by Terra in the session that
recorded it. Commands are written from the repository root on Windows.

## R6(d) - Ordered hardening backlog regression audit

Branch: `codex/r6-ordered-backlog-audit`

### Claims and re-verification

| Claim | Exact reviewer command | Observed 2026-08-12 |
|---|---|---|
| The authoritative six-item post-B8 backlog is merged history, not an open implementation list. | `gh pr view 18 --json state,mergedAt,mergeCommit,files,commits`; inspect the `Post-B8 - Ordered hardening backlog` section below | PR #18 is MERGED as `e95b1b3`; its two commits and changed files cover the migration-copy matrix, archive-entry hardening, dashboard accessibility, service-host doctor, timing sweep, analyzers, and historical-gate removal. |
| Both retained Alpha databases still migrate only through copied databases and remain unchanged. | `dotnet run --project tests\StoreParityHarness\StoreParityHarness.csproj -c Release --no-build -- --migration-copy <candidate-1> --migration-copy <candidate-2>` | Candidate 1 and candidate 2 both passed integrity, idempotence, schema/row preservation, and source-identity checks: 2 passed, 0 failed. The command reported candidate numbers only. |
| Import safety, dashboard accessibility, and service-host lease hardening remain covered on the current tree. | `scripts\Verify-Alpha.ps1` | Build 0 warnings/0 errors; EngineHarness 217/0 and offline total 598/0. Named assertions passed for unsafe ZIP rejection, accessible dashboard markup/controls, and required service-host single-instance checks. |
| The current EngineHarness timing sweep has no observed intermittent failure. | Run EngineHarness ten consecutive times in Release with `--no-build`, recording exit, duration, and summary | Runs 1-10 each exited 0 at 217/0; observed duration range 4.532-4.769 seconds. This bounded sweep is not a proof that timing flakes cannot exist. |
| Analyzer and dead-gate cleanup remain current, and the analyzer wrapper works in Windows PowerShell 5.1 from outside the repository. | `powershell -File <absolute-path>\scripts\Test-PowerShellScripts.ps1`; .NET analyzer build; analyzer-format verification; `rg` for the two historical dead-gate markers | The first Windows PowerShell invocation exposed an empty `$PSScriptRoot` parameter-default defect. After moving path resolution below `param`, the absolute wrapper invocation passed with PSScriptAnalyzer 1.25.0 and 0 enforced findings. .NET analyzers built 0/0, analyzer formatting exited 0, and historical gate markers numbered 0. |
| The R6(b) blocker is reachable from merged handoff documents. | `gh pr view 26 --json state,isDraft,headRefOid`; compare main with PR #26's `BETA-BLOCKED.md` and `HUMAN-QUEUE.md` | PR #26 remains OPEN/DRAFT at `d1ab0d5`. Its R6(b) blocker and Q06-Q07 entries were absent from main, so this slice carries those evidence-only entries onto main without rerunning SBOM generation or weakening the two-attempt limit. |

### Verification boundary

The two `.appdata` source databases were accessed only through the existing
read-only backup/migration-copy harness; it verified each source unchanged.
No SBOM CI diagnostic or third attempt was started, and draft PR #26 was not
modified. PowerShell 7 is not installed locally, so no local PowerShell 7
wrapper result is claimed; GitHub's Windows job remains the cross-host check.

No deploy, console mutation, email, purchase, signing, install, secret access,
certificate/store mutation, reboot, scheduled-task registration, off-repo
site edit, Android/relay/sync change, force-push, history rewrite,
`.appdata`-original mutation, public ATS read, or live provider/Gmail action
occurred.

## R6(c) - Repository-wide PowerShell static analysis

Branch: `codex/r6-psscriptanalyzer`

### Claims and re-verification

| Claim | Exact reviewer command | Observed 2026-08-12 |
|---|---|---|
| The pinned enforced analyzer policy is clean. | `scripts\Test-PowerShellScripts.ps1` | PSScriptAnalyzer 1.25.0 recursively scanned `scripts/` and reported 0 enforced warning/error findings. |
| The policy does not hide unclassified debt. | `Invoke-ScriptAnalyzer -Path .\scripts -Recurse -Settings @{ Severity = @('Error','Warning','Information') }`, then group by severity/rule | After fixes, the explicit unfiltered scan reported 355 reviewed findings: 288 warnings and 67 informational findings, all in the six families enumerated and justified in `docs/PSScriptAnalyzer.md`. |
| Actionable automatic-variable and runspace findings were removed without changing wrapper intent. | `rg -n '\$args\b'` over the seven changed wrappers; parser pass over all `scripts\*.ps1`; run the five documented preview/dry-run wrappers; execute both export wrappers against `tmp\verify-alpha-demo\demo.db` | No changed wrapper assigns `$args`; all 23 scripts parsed with 0 errors. Preview/dry-run paths executed no live action. Audit/evidence exports succeeded with intact chain, hash-only payloads, and secret-path exclusion. The isolated background-job capture returned the exact test URL. |
| The merge-grade gate remains green. | `scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage` | Initial and post-fetch gates both built 0 warnings/0 errors with offline 598/0, published demo 1 acted/1 drafted/2 rejected/0 errors, and a one-executable self-check with provider calls 0 and Gmail calls/drafts 0. After fresh main remained `00b3705` and rebase was a no-op, the unsigned candidate measured 33,720,955 bytes with SHA-256 `9AB8D78299F8317273310429955932ADB2627538D08F68F04AE3F8BF473AE980`. |
| .NET analyzers remain clean. | `dotnet build CareerSeeker.sln -c Release --no-restore -warnaserror -p:EnableNETAnalyzers=true -p:AnalysisLevel=latest`; `dotnet format CareerSeeker.sln analyzers --verify-no-changes --severity warn --no-restore` | Analyzer build 0 warnings/0 errors; analyzer formatting verification exited 0 with no findings. |

### Verification boundary

PSScriptAnalyzer and its NuGet bootstrap provider were installed only at
current-user scope. No application package or machine-global tool was
installed. No preview/dry-run command contacted a provider, Gmail, or a public
ATS board. Initial CI runs `31657569672` and `31657606281` passed both jobs.

No deploy, console mutation, email, purchase, signing, application/MSIX
install, secret access, certificate/store mutation, reboot, scheduled-task
registration, off-repo site edit, Android/relay/sync change, force-push,
history rewrite, `.appdata`-original mutation, public ATS read, or live
provider/Gmail action occurred.

## R6(a) - Confirmed installed-workspace data deletion

Branch: `codex/r6-delete-all-data`

### Claims and re-verification

| Claim | Exact reviewer command | Observed 2026-08-07 |
|---|---|---|
| The first deletion invocation is non-mutating and displays the exact target plus a path-bound confirmation phrase. | `dotnet run --project src\Engine\SeekerSvc.Engine.csproj -c Release --no-build -- delete-all-data` | Exit 0; displayed `C:\Users\bkirk\AppData\Local\CareerSeeker`, reported `NOT DELETED`, and printed the required `DELETE ALL CAREERSEEKER DATA AT ...` phrase. No confirmation was supplied. |
| The confirmed operation is constrained and honestly reports its result. | `dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build` | EngineHarness passed 170/0. Six deletion assertions pin the installed path, exact phrase, mismatch no-op, volume-root refusal, complete exact-target removal with post-delete absence, and already-absent reporting. The destructive assertions used isolated `careerseeker-delete-harness-*` temp roots only. |
| The public workflow and disposable-VM sequence retain the uninstall/data boundary. | `scripts\Verify-Alpha.ps1`; inspect `docs\Support.md`, `docs\Privacy-Policy.md`, `docs\Beta-Windows-Package-Runbook.md`, and `scripts\New-BetaVmInstallMatrix.ps1` | Repository and site copies agree on the two-step command. VM10 deletes while installed; VM11 recreates a sentinel and tests uninstall preservation separately. |
| The merge-grade gate remains green. | `scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage` | Initial candidate: build 0 warnings/0 errors; offline 418/0; published demo 1 acted/1 drafted; one-executable package self-check passed with provider calls 0 and Gmail calls/drafts 0. After fresh main remained `e874c86` and the rebase was a no-op, the full gate repeated green; that unsigned candidate measured 33,666,365 bytes with SHA-256 `1D3793B15FC97DD66AD4A1487ABC99AF92D5156C0ECA88842BA3B9A396348FC7`. |
| .NET analyzers remain clean. | `dotnet build CareerSeeker.sln -c Release --no-restore -warnaserror -p:EnableNETAnalyzers=true -p:AnalysisLevel=latest`; `dotnet format CareerSeeker.sln analyzers --verify-no-changes --severity warn --no-restore` | Analyzer build 0 warnings/0 errors; analyzer formatting verification exited 0 with no findings. |

### Verification boundary

The real installed workspace was resolved and displayed only; the confirmed
deletion command was never run against it. Destructive harness coverage used
fresh isolated temp directories. PR #25's initial push run `31235635615` and
pull-request run `31235656763` passed. R6(b), R6(c), and R6(d) remain pending.

No deploy, console mutation, email, purchase, signing, install, secret access,
certificate/store mutation, reboot, scheduled-task registration, off-repo
site edit, Android/relay/sync change, force-push, history rewrite,
`.appdata`-original mutation, public ATS read, or live provider/Gmail action
occurred.

## R5 - Repository-only distribution preparation

Branch: `codex/r5-distribution-prep`

### Claims and re-verification

| Claim | Exact reviewer command | Observed 2026-08-07 |
|---|---|---|
| The staged download copy identifies the Beta candidate without offering an artifact link or promoting the three unproven operational claims. | `scripts\Verify-Alpha.ps1`; `Select-String` over `docs-site\download.md`, `docs-site\download.html`, `docs\Beta-Changelog.md`, and `docs\Alpha-to-Beta-Migration.md` for the prohibited claim text | Both download pages say `Beta download is not yet available`, identify the unsigned candidate, retain the exact shipped Alpha baseline, and contain no Beta MSIX artifact href. The prohibited strings were absent. |
| The Alpha baseline is pinned to the shipped artifact rather than a later local ZIP. | `rg -n "7018ff9|3A4251|64,937,092" docs\Codex-Resume-Handoff.md docs\Beta-Changelog.md docs-site\download.md docs-site\download.html` | Changelog and download copy match the historical shipped artifact: commit `7018ff9`, 64,937,092 bytes, SHA-256 `3A4251F65AEF530BC5D73387422CD53556294970EC546C0112B6EF1BA4E900F2`. |
| The tester migration command is preservation-first and resolves the packaged per-user workspace without executing an import. | `$betaData = Join-Path $env:LOCALAPPDATA 'CareerSeeker\.appdata'; scripts\Import-AlphaPackage.ps1 -PreviewOnly ...` | Preview assembled the database, artifacts, and job-description paths under `%LOCALAPPDATA%\CareerSeeker\.appdata`; reported `overwrite: no`; and explicitly did not execute the command. |
| Positioning source references were refreshed against the post-R1 tree. | PowerShell regex extraction of every ``path:line`` reference in `docs\Positioning.md`, followed by reading the referenced line | Every numeric source reference resolved. The R1 claim points at `LexicalSemanticScorer.cs:74` job coverage and `EngineHarness:1022` monotonicity; package references point at the current executable, startup, and external-sentinel assertions. |
| The merge-grade gate remains green. | `scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage` | After fresh fetch and no-op rebase, build 0 warnings/0 errors; offline 412/0; published demo 1 acted/1 drafted; one-executable package self-check passed with provider calls 0 and Gmail calls/drafts 0. The unsigned candidate measured 33,671,071 bytes with SHA-256 `02762BEC262687B1BD608B27A2FBFEBABF3AF8A8F54DF5066BE08B116C7FF158`. |
| .NET analyzers remain clean. | `dotnet build CareerSeeker.sln -c Release --no-restore -warnaserror -p:EnableNETAnalyzers=true -p:AnalysisLevel=latest`; `dotnet format CareerSeeker.sln analyzers --verify-no-changes --severity warn --no-restore` | Analyzer build 0 warnings/0 errors; analyzer formatting verification exited 0 with no findings. |

### Verification boundary

R5 changed repository copy and tests only. The MSIX hash above is a measured
unsigned candidate, not final release metadata. No artifact URL was created,
and the off-repo production site was not read or changed.

No deploy, console mutation, email, purchase, signing, install, secret access,
certificate/store mutation, reboot, scheduled-task registration, off-repo
site edit, Android/relay/sync change, force-push, history rewrite,
`.appdata`-original mutation, public ATS read, or live provider/Gmail action
occurred.

## R4 - Signing and install readiness preparation

Branch: `codex/r4-signing-install-readiness`

### Claims and re-verification

| Claim | Exact reviewer command | Observed 2026-08-07 |
|---|---|---|
| The PFX signing flow validates its package/certificate/timestamp parameters without reading a certificate, password, or signing anything. | `scripts\Sign-BetaRelease.ps1 -PackagePath tmp\r4-release-validation\validation-only.msix -CertificatePath tmp\r4-release-validation\not-read.pfx -ValidateOnly` (executed inside `scripts\Verify-Alpha.ps1`) | Validation passed, reported `no signing or certificate read` and `password read: no`; an HTTP timestamp URL was rejected. |
| Production-signed package expectations are executable before a real certificate exists. | `scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage` | A production-shaped package with publisher `CN=CareerSeeker R4 Offline Validation` passed exact subject matching and unsigned-OID absence; the same intentionally unsigned artifact was rejected by `-RequireSigned` with `No signature found`. |
| The disposable-VM checklist is deterministic and non-mutating in validation mode. | `scripts\New-BetaVmInstallMatrix.ps1 ... -ValidateOnly` (executed inside the verifier) | Exact SHA-256 and publisher validation passed; all eleven IDs VM01-VM11 were present; no signature check, install, or output write occurred. The execution mode is gated by signed-package verification and emits `PENDING` recorded-output slots. |
| The human queue contains current setup/sign/verify/VM/publish commands. | `rg -n "artifact-signing|azure/login@v3|artifact-signing-action@v2|New-BetaVmInstallMatrix|wrangler r2 object (put|get)" docs\autonomy\HUMAN-QUEUE.md` | Q03-Q05 cover Azure account/profile/RBAC, GitHub OIDC signing, exact publisher verification, the VM matrix, and versioned R2 upload/re-download/hash verification. Wrangler was measured at 4.112.0; no Wrangler mutation command was run. |
| The merge-grade gate remains green. | `scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage` | Build 0 warnings/0 errors; offline 412/0; published demo 1 acted/1 drafted; one-executable package self-check passed with provider calls 0 and Gmail calls/drafts 0. The unsigned baseline MSIX was 33,671,092 bytes with SHA-256 `3FF2AF41E95DF3B01B666AEA6881BCC7BA1EAD11B05857E4804230DE3ACDE911`. |
| .NET analyzers remain clean. | `dotnet build CareerSeeker.sln -c Release --no-restore -warnaserror -p:EnableNETAnalyzers=true -p:AnalysisLevel=latest`; `dotnet format CareerSeeker.sln analyzers --verify-no-changes --severity warn --no-restore` | Analyzer build 0 warnings/0 errors; analyzer formatting verification exited 0 with no findings. |

### Verification boundary

R4 prepared and validated the offline flow only. No Azure resource, identity,
RBAC assignment, repository secret/variable, signing request, certificate,
signature, package registration, install, reboot, startup change, uninstall,
data deletion, R2 object, or site deployment was created or executed. Signing,
the VM matrix, and publication remain human steps Q03-Q05.

No deploy, console mutation, email, purchase, signing, install, secret access,
certificate/store mutation, reboot, scheduled-task registration, off-repo
site edit, Android/relay/sync change, force-push, history rewrite,
`.appdata`-original mutation, or live provider/Gmail action occurred.

## R3 - Sole live Gmail cycle prerequisite (BLOCKED)

Branch: `codex/r3-prerequisite-gate`

| Claim | Exact reviewer command | Observed 2026-08-07 |
|---|---|---|
| Fresh merge-tracked state does not authorize R3. | `git fetch --all --prune`; `git rev-parse origin/main`; `git show origin/main:docs/autonomy/CODEX-STATE.md` | `origin/main` was `d4864590c38cd52a332349f20853423e477e9e0f`; R1 was DONE and R2 was BLOCKED. |
| Both controlling R3 rules require R2 green/complete. | `git show origin/main:docs/autonomy/R-LADDER.md`; `git show origin/main:docs/autonomy/CODEX-MISSION.md` | Ladder: “After R1/R2 green”; mission: live drafting allowed “only after R1 and R2 are complete.” |
| No live-cycle implementation or safety surface changed. | `git diff --name-only origin/main...HEAD` | Expected final PR diff is evidence/autonomy documentation only. No Engine, Dispatcher, Gmail, OAuth, secret, or database path is changed. |

R3 is BLOCKED by its explicit prerequisite. No Gmail or OAuth readiness check,
token/secret access, draft creation, or live retry was attempted; doing so
would have been unauthorized. The sole live-cycle allowance remains unused.
The smallest human unblock is to make R2 DONE with a fresh accepted rehearsal,
as recorded in `docs/BETA-BLOCKED.md` and `docs/autonomy/HUMAN-QUEUE.md`.

## R2 - Real-profile rehearsal (BLOCKED)

Branch: `codex/r2-real-profile-rehearsal`

### Claims and re-verification

| Claim | Exact reviewer command | Observed 2026-08-07 MDT / 2026-08-08 UTC |
|---|---|---|
| The rehearsal database is a retained SQLite backup, and its source is unchanged. | `dotnet run --project tests\StoreParityHarness\StoreParityHarness.csproj -c Release --no-build -- --migration-copy C:\Users\bkirk\Documents\CareerSeeker\.appdata\careerseeker-alpha.db --migration-output tmp\r2-rehearsal\careerseeker-r2.db`, followed by `Get-FileHash -Algorithm SHA256` on the source | Copy integrity/current columns and idempotent migration passed. Source before/after: 172,032 bytes, last-write UTC `2026-07-19T23:04:58`, SHA-256 `0A560528C486375383F1F84F1BA8EA1536B341C75C8BC5EF0CF3D1BEE4E18192`. |
| The copy uses a realistic-size synthetic profile rather than the repeated demo claims. | `dotnet run --project src\Engine\SeekerSvc.Engine.csproj -c Release --no-build -- import-profile --profile tests\fixtures\r2-realistic-profile.json --db tmp\r2-rehearsal\careerseeker-r2.db` | Import reported 31 claims and `replacement verified: yes`; an exact scorer-tokenizer-equivalent count measured 321 distinct rankable terms. |
| The non-empty public cycle remained draft-free and audit-clean, but did not satisfy R2 acceptance. | `dotnet run --project src\Engine\SeekerSvc.Engine.csproj -c Release --no-build -- run --once --dry-run --llm fake --board greenhouse:remotecom --discovery-timeout-seconds 90 --http-timeout-seconds 30 --max-drafts-per-cycle 10 --db tmp\r2-rehearsal\careerseeker-r2.db --artifacts tmp\r2-rehearsal\artifacts --jd-dir tmp\r2-rehearsal\job-descriptions` | 58 discovered, 12 quarantined, 46 scored/rejected, 0 act-eligible/acted/drafted/errors; audit chain ok. Offline copied-DB analysis measured total 2.36–3.63, mean 2.932. |
| The second and final bounded public attempt was empty. | Same command with `--board lever:mistral` | 0 discovered/scored/act-eligible/drafted/errors; audit chain ok. |
| The final default audit export contains only hash evidence. | `dotnet run --project src\Engine\SeekerSvc.Engine.csproj -c Release --no-build -- export-audit --db tmp\r2-rehearsal\careerseeker-r2.db --out tmp\r2-rehearsal\audit-final.json` | Audit ok; two named cycle rows; 256 events; `payloadsIncluded: false`; Remote reason counts `{"role_reassign":12}`. |

### Block and boundary

R2 requires `act-eligible > 0`, so it is BLOCKED after two bounded attempts.
The 4.0 rail was not changed to fit a volatile feed, and no third public cycle
was run. The smallest human unblock is recorded in `docs/BETA-BLOCKED.md`.
Because R2 is not DONE, R3's one authorized live Gmail draft cycle remains
ineligible and was not executed.

No deploy, console mutation, email, purchase, signing, install, secret access,
certificate/store mutation, reboot, scheduled-task registration, off-repo
site edit, Android/relay/sync change, force-push, history rewrite,
`.appdata`-original mutation, or live provider/Gmail action occurred. Public
ATS GETs were the only network activity.

## R1 - Scoring realism calibration

Branch: `codex/r1-scoring-calibration`

### Claims and re-verification

| Claim | Exact reviewer command | Observed 2026-08-07 |
|---|---|---|
| The former whole-profile denominator creates a rich-profile dead zone. | `dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build` after adding the calibration assertions and before changing the scorer | 10 terms: 8/120 Act; 50 terms: 0/120; 200 terms: 0/120. Monotonicity, eligibility-band, targeted-band, and rationale/version assertions failed; `159 passed, 4 failed`. |
| `lexical-v2` makes the same posting non-decreasing as the profile grows and keeps eligibility sane. | `dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release` | All 10/50/200-term profiles produced CV 1.50–3.88, total 2.99–4.20, target minimum 4.20, adjacent maximum 3.20, and 8/120 Act (6.7%); healthy demo remained Act; `164 passed, 0 failed`. |
| Count-bearing docs and the verifier moved together. | `scripts\Verify-Alpha.ps1` | Build 0 warnings/0 errors; demo 1 acted/1 drafted/2 rejected; `Offline total: 412 passed, 0 failed`. |
| The merge-grade publish/package path remains structurally green. | `scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage` | Build 0 warnings/0 errors; offline 412/0; published demo 1 acted/1 drafted; one-executable MSIX structural check passed; provider calls 0; Gmail calls/drafts 0. |
| Latest .NET analyzers remain clean. | `dotnet build CareerSeeker.sln -c Release --no-restore -warnaserror -p:EnableNETAnalyzers=true -p:AnalysisLevel=latest` and `dotnet format CareerSeeker.sln analyzers --verify-no-changes --severity warn --no-restore` | Analyzer build: 0 warnings/0 errors; format analyzer verification exited 0 with no output. |

### Verification boundary

The 120-posting corpus is controlled synthetic calibration, not a claim about
the production job market. R2 must still run the bounded public-ATS dry-run on
a copied database with a realistic rich profile. No live provider or Gmail
call occurred.

No deploy, console mutation, email, purchase, signing, install, secret access,
certificate/store mutation, reboot, scheduled-task registration, off-repo
site edit, Android/relay/sync change, force-push, history rewrite, or
`.appdata`-original mutation occurred.

## Post-B8 - Ordered hardening backlog

Branch: `codex/beta-hardening`

### Claims and re-verification

| Claim | Exact reviewer command | Observed 2026-07-30 |
|---|---|---|
| Current migrations succeed twice on copies of both available real Alpha databases without changing either source. | `dotnet run --project tests\StoreParityHarness\StoreParityHarness.csproj -c Release --no-build -- --migration-copy .appdata\careerseeker-alpha.db --migration-copy .appdata\imported-smoke\careerseeker-alpha.db` | At `596a770a61eee8c73ee1b891e23dee82733e94c3`: candidate 1 PASS, candidate 2 PASS, `2 passed, 0 failed`; integrity/row counts/current columns passed and each source's length/timestamp/SHA-256 remained unchanged. |
| Alpha ZIP entry names fail closed against Windows traversal/alias/device-name shapes while valid Unicode and literal percent names remain usable. | `dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build` | `159 passed, 0 failed`; the existing unsafe-entry assertion now evaluates 35 deterministic accepted/rejected cases and also checks that no file escaped the two permitted import roots. |
| Dashboard markup is keyboard/accessibility-oriented and no timed refresh resets focus. | `dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build` | `159 passed, 0 failed`; status/applications/jobs/evidence assertions found language, skip, focus, navigation/current-page, refresh, captions, scopes, labeled regions/controls and found no meta refresh. |
| Required service-host doctor checks the configured database lease and fails closed when that database is already owned. | `dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build` | `159 passed, 0 failed`; the service-host doctor passed on a free configured DB, failed on a held configured DB lease, and failed when control/log directories were omitted. |
| The EngineHarness timing sweep produced no intermittent failure. | `$runs = @(); foreach ($i in 1..10) { $sw = [System.Diagnostics.Stopwatch]::StartNew(); $output = & dotnet run --project tests\EngineHarness\EngineHarness.csproj -c Release --no-build 2>&1; $exitCode = $LASTEXITCODE; $sw.Stop(); $summary = $output \| Where-Object { $_ -match '^=== \d+ passed, \d+ failed ===$' } \| Select-Object -Last 1; $runs += [pscustomobject]@{ Run = $i; Exit = $exitCode; Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 3); Summary = [string]$summary }; if ($exitCode -ne 0) { $output \| Select-Object -Last 40; break } }; $runs \| Format-Table -AutoSize; if (($runs \| Where-Object Exit -ne 0).Count -gt 0) { exit 1 }` | Runs 1-10 each exited 0 at 159/0; observed range 4.570-4.800 seconds. This is a bounded sweep, not a proof that timing flakes cannot exist. |
| Latest built-in .NET analyzers are warning-clean, analyzer formatting has no pending change, and the unreachable historical Alpha ZIP gate is gone. | `dotnet build CareerSeeker.sln -c Release --no-restore -warnaserror -p:EnableNETAnalyzers=true -p:AnalysisLevel=latest; dotnet format CareerSeeker.sln analyzers --verify-no-changes --severity warn --no-restore; rg -n -e 'if \(\$?false\)' -e 'historical audit code' scripts\Verify-Alpha.ps1` | Build: 0 warnings/0 errors; format-analyzers exit 0; final `rg` expected no output/exit 1. PSScriptAnalyzer was unavailable and was not installed, so no PowerShell-analyzer pass is claimed. |
| The clean committed artifact remains a one-executable unsigned MSIX with the full pinned gate and no provider/Gmail calls. | `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage` | At `596a770a61eee8c73ee1b891e23dee82733e94c3`: 407/0; one `CareerSeeker.exe`; provider calls 0; Gmail calls/drafts 0; external workspace preserved; 33,673,443 bytes; SHA-256 `41F456F09413758E707B556CBAF79EEFC268FE9B93EB67FF0EFE09C362B0271F`. |
| Frozen Android/relay paths are absent from the hardening diff. | `git diff --name-only 89ef81002410cc85ad49f529e571be3b5b4de5c1...HEAD \| rg '^(relay/|docs/Sync-Protocol\.md$|docs/sync-vectors/|.*Android|.*android)'` | Expected: no output, exit 1 from `rg`; executed result was no matches. |

### Verification boundary

The first real-DB matrix attempt stopped during temporary WAL/SHM cleanup and
is not claimed as a pass. Cleanup was corrected before the two-source 2/0 run.
Both source databases remain; only randomized system-temp copies were removed.

The in-app Browser rendered the isolated dashboard and one real Tab transition
visibly focused the Jobs link. Repeated synthetic Tab injection was
intermittent, so full keyboard navigation is not claimed; repeatable markup
coverage is in EngineHarness. The isolated host made one bounded public ATS
read (255 discovered, 34 quarantined, 203 rejected, 0 drafted, 0 errors) and
was then stopped with its listener absent. No Computer action was taken
because that skill forbids using computer control on the Codex/ChatGPT UI.

The package was created and unpacked, not signed, installed, registered,
Windows-uninstalled, or reboot-tested. No Gmail draft, provider call, send,
deployment, scheduled-task registration, Cloudflare mutation, Google/Play
console change, OAuth queue read/write, Android/relay/sync-vector change,
off-repo site edit, purchase, new scope, account/config change, or secret
print occurred. The bounded public ATS read above was the only network
activity in this hardening batch.

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
