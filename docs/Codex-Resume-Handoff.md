# Codex Resume Handoff

Updated: 2026-07-24

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
