# CareerSeeker Engine Host

This project is the runnable alpha entrypoint for the local-first CareerSeeker engine. It hosts the
scheduled engine shell, the localhost dashboard, and the one-shot alpha smoke path that can create a
reviewable Gmail draft while the L1 application contains no Gmail send implementation.

## What Got Proven

The engine composes the same core modules used by the vertical slice:

- Store
- Scorer
- Pipeline
- Fabrication Gate
- Tailor through the LLM Gateway bridge
- Dispatcher

The load-bearing safety paths are covered by harnesses:

| Path | Result |
| --- | --- |
| Clean, profile-supported application | `READY -> DRAFTED`, Gmail draft created, audit chain intact |
| Unsupported claim | `BLOCKED_FABRICATION`, zero drafts |
| Low-legitimacy posting | `REJECTED_BY_ENGINE`, never tailored, never drafted |

The engine shell adds:

- `EngineCycle`: one discovery, decision, and action pass over a batch.
- `PeriodicScheduler`: immediate tick, then repeated ticks by interval.
- `LocalDashboard`: loopback-only responsive HTML dashboard and JSON status on `localhost`, with optional
  token-protected controls.
- `EngineHost`: composition root for counters, scheduler, and dashboard.

## Alpha Executable Modes

- Demo, offline and repeatable:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- demo --once`
- Demo with persistent SQLite state and audit evidence:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- demo --once --db .appdata/careerseeker-demo.db --artifacts .appdata/artifacts`
- Alpha Gmail smoke, live but one-shot:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- alpha --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db`
- Connect Gmail without creating a draft:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- connect-gmail --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi`
- Standalone dashboard over the real local alpha DB:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- dashboard --db .appdata/careerseeker-alpha.db --gmail-control`
- Windows-friendly dashboard launcher, from source or published executable:
  `powershell -ExecutionPolicy Bypass -File scripts/Start-AlphaDashboard.ps1`
- Double-click local workspace setup helper included in the release ZIP:
  `Setup-CareerSeeker-Alpha.cmd`
- Double-click source-of-truth profile import helper included in the release ZIP:
  `Import-CareerSeeker-Profile.cmd`
- Double-click BYOK provider-key import and doctor helper included in the release ZIP:
  `Connect-CareerSeeker-Providers.cmd`
- Double-click Gmail connect helper included in the release ZIP:
  `Connect-CareerSeeker-Gmail.cmd`
- Double-click live Gmail/BYOK readiness doctor included in the release ZIP:
  `Check-CareerSeeker-LiveReadiness.cmd`
- Double-click local BYOK vault clear helper included in the release ZIP:
  `Clear-CareerSeeker-Providers.cmd`
- Double-click Gmail revoke/local token-vault clear helper included in the release ZIP:
  `Disconnect-CareerSeeker-Gmail.cmd`
- Double-click safe local demo cycle helper included in the release ZIP:
  `Run-CareerSeeker-Demo.cmd`
- Double-click public ATS board ingest helper included in the release ZIP:
  `Run-CareerSeeker-Scout.cmd`
- Double-click selected-job draft helper included in the release ZIP:
  `Draft-CareerSeeker-Job.cmd`
- Double-click live L1 Gmail draft helper included in the release ZIP:
  `Run-CareerSeeker-Live.cmd`
- Double-click local evidence package helper included in the release ZIP:
  `Export-CareerSeeker-Evidence.cmd`
- Double-click release package self-check helper included in the release ZIP:
  `Verify-CareerSeeker-Alpha.cmd`
- Double-click dashboard launcher included in the release ZIP:
  `Start-CareerSeeker-Alpha.cmd`
- Optional per-user Windows logon task helper for the alpha dashboard:
  `powershell -ExecutionPolicy Bypass -File scripts/Manage-AlphaDashboardTask.ps1 -Action Install -DryRun`
- One-shot dashboard/evidence smoke:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- dashboard --once --db .appdata/careerseeker-alpha.db --gmail-control`
- Live ATS board ingest into the local SQLite store:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- scout-boards --board greenhouse:remotecom --board lever:mistral --db .appdata/careerseeker-alpha.db --jd-dir .appdata/job-descriptions`
- Draft a selected stored job row, with `--dry-run` available for local package/audit verification without Gmail:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- draft-job --job-id 123 --dry-run --db .appdata/careerseeker-alpha.db`
- Import real BYOK provider keys into the local DPAPI vault:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- import-byok --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi`
- Live BYOK provider smoke for Anthropic, Gemini, Tailor, Gate, and accounting:
  `dotnet run -c Release --project tests/ByokLiveHarness/ByokLiveHarness.csproj -- --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi`
- Live Brave/BYOK company research smoke:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- research-company --company GitLab --domain gitlab.com --llm byok --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi`
- Create and import a local source-of-truth profile for Tailor/Gate:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- profile-template --out .appdata/profile.template.json`
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- import-profile --profile .appdata/profile.template.json --db .appdata/careerseeker-alpha.db`
- Alpha Gmail smoke with real BYOK Tailor and Gate calls:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- alpha --llm byok --gate-semantic-candidates 3 --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db`
- Bounded BYOK alpha smoke for routine validation:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- alpha --llm byok --fast-smoke --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db`
- Startup doctor for local DB, artifact folder, Gmail config, and BYOK readiness:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- doctor --require-gmail --require-byok --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db --artifacts .appdata/artifacts`
- Local application control:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- control-app --db .appdata/careerseeker-alpha.db --application-id 123 --action pause|resume|kill`
- Disconnect Gmail, revoking OAuth and deleting the local vault:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- disconnect-gmail --vault .appdata/oauth/gmail-token.dpapi`
- Clear imported BYOK provider keys from the local vault:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- clear-byok --key-vault .appdata/secrets/byok-keys.dpapi`

The alpha mode uses SQLite for engine state, the DPAPI token vault for Gmail OAuth, and the real
`GmailDraftClient`. Demo mode is in-memory by default, but `demo --db <path>` runs the same local
dashboard/cycle shell against `SqliteSeekerStore` so testers can inspect persistent status and audit
evidence without touching Gmail. Demo and alpha draft paths persist generated PDFs under
`.appdata/artifacts` by default, overridable with `--artifacts`. Alpha preflights Gmail draft access before
creating anything, renders an ATS-clean PDF resume attachment, then intentionally runs one cycle and creates
one self-addressed L1 draft so early testing cannot accidentally produce drafts on a timer.

`connect-gmail` uses the same OAuth client JSON, DPAPI vault, fixed `gmail.compose` scope, and Gmail drafts
preflight as alpha mode, but it stops before drafting. It is the trusted-tester setup path for creating or
refreshing the local token vault.

`profile-template` writes a starter JSON profile. `import-profile` replaces the local profile claim oracle in
SQLite and records the active `alpha.profileId`, so Tailor and Gate use imported source facts instead of
accumulating demo claims.

By default it uses fake inference. Pass `--llm byok` to use local Anthropic/Gemini keys from the DPAPI
provider-key vault, environment variables, or `secrets/env.secrets`. `--email` is optional when Gmail
profile lookup is available. For quick routine validation, use `--llm byok --fast-smoke`; it performs one
bounded live Gate entailment check, one bounded live Tailor call, then runs the normal Gmail/PDF draft path.
In BYOK alpha mode, the Gate defaults to the top 3 semantically relevant source candidates per claim to
keep live entailment calls bounded; pass `--gate-semantic-candidates 0` for exhaustive comparison.

`research-company` reads Brave Search keys from `--brave-key`, the process environment, or
`secrets/env.secrets`. Accepted names are `BRAVE_SEARCH_API_KEY`, `BRAVE_SEARCH_API`, and
`CAREERSEEKER_BRAVE_SEARCH_API_KEY`.

## Injected Ports

- `IJobFeed`: candidate postings. Production is Scout over ATS feeds; sandbox is a fixed batch.
- `ISemanticScorer`: CV-match and growth sub-scores. Production is the LLM Gateway; sandbox is deterministic.
- `IDocumentRenderer`: production alpha is the deterministic ATS-clean PDF renderer. Future product polish
  can add an HTML/Chromium renderer.

## Verified Status

- `dotnet build CareerSeeker.sln -c Release`: 0 warnings, 0 errors.
- Latest offline harness total: 258 passed, 0 failed.
- `scripts/Verify-Alpha.ps1` runs the repeatable build, initializer dry run, source-mode SQLite demo smoke, and
  offline harness suite; optional switches add live BYOK/Gmail checks, the win-x64 publish smoke, the
  trusted-tester release ZIP, and live Brave/BYOK company research.
- `scripts/Package-AlphaRelease.ps1` creates a self-contained alpha ZIP with the executable, native runtime
  dependencies, quickstart, tester walkthrough, audit snapshot, release manifest, double-click setup/profile/provider/Gmail/live-readiness/provider-clear/Gmail-disconnect/demo/scout/company-research/selected-job/live/audit-export/evidence-export/evidence-import/verify/dashboard and dashboard-task launchers,
  workspace initializer, dashboard/helper self-check scripts, checksums, and selected docs without bundling local
  databases, vaults, provider keys, or generated artifacts.
- `scripts/Initialize-AlphaWorkspace.ps1` creates ignored local alpha directories, a starter profile template,
  and a blank env-secrets placeholder, and can run the startup doctor after setup.
- `scripts/Start-AlphaDashboard.ps1` wraps the standalone dashboard mode for trusted testers; it can smoke-check
  the local dashboard with `-Once`, run from source, run the published single-file executable with
  `-Published -PublishIfMissing`, or run from the packaged release root with `-Published`.
- `Start-CareerSeeker-Alpha.cmd` wraps that published dashboard path for double-click tester startup from the
  extracted release ZIP.
- `Install-CareerSeeker-DashboardTask.cmd` wraps the scheduled-task helper for double-click per-user dashboard
  startup at Windows sign-in and requires typing `INSTALL`.
- `Status-CareerSeeker-DashboardTask.cmd` reports whether that per-user dashboard startup task is installed.
- `Uninstall-CareerSeeker-DashboardTask.cmd` removes that per-user dashboard startup task and requires typing
  `UNINSTALL`.
- `Setup-CareerSeeker-Alpha.cmd` wraps the workspace initializer for double-click tester setup from the
  extracted release ZIP, then opens the generated profile template.
- `Import-CareerSeeker-Profile.cmd` wraps `import-profile` for double-click tester profile setup from the
  extracted release ZIP after the profile template is edited.
- `Connect-CareerSeeker-Providers.cmd` wraps provider-key import and `doctor --require-byok` for double-click
  tester BYOK setup from the extracted release ZIP without printing secret values.
- `Connect-CareerSeeker-Gmail.cmd` wraps `connect-gmail` for double-click tester OAuth setup from the
  extracted release ZIP; it preflights Gmail draft access without creating a draft.
- `Check-CareerSeeker-LiveReadiness.cmd` wraps `doctor --require-gmail --require-byok` for double-click live
  Gmail/BYOK readiness checks without printing secret values.
- `Clear-CareerSeeker-Providers.cmd` wraps `clear-byok` for double-click local BYOK vault clearing and requires
  typing `CLEAR`.
- `Disconnect-CareerSeeker-Gmail.cmd` wraps `disconnect-gmail` for double-click Gmail revoke/local token-vault
  clearing and requires typing `DISCONNECT`.
- `Run-CareerSeeker-Demo.cmd` wraps a one-shot SQLite demo cycle for double-click tester evidence generation
  from the extracted release ZIP without creating a Gmail draft.
- `Run-CareerSeeker-Scout.cmd` wraps public ATS board ingestion for double-click tester job discovery from the
  extracted release ZIP without creating a Gmail draft.
- `Research-CareerSeeker-Company.cmd` wraps live Brave/BYOK company research for double-click tester review
  from the extracted release ZIP without creating a Gmail draft.
- `Draft-CareerSeeker-Job.cmd` wraps selected stored-job drafting; it defaults to a no-Gmail dry-run package and
  requires typing `LIVE` before creating a Gmail draft.
- `Run-CareerSeeker-Live.cmd` wraps `alpha --llm byok --fast-smoke`; it defaults to a no-Gmail dry-run preview
  and requires typing `LIVE` before creating a Gmail draft.
- `Export-CareerSeeker-Audit.cmd` wraps `export-audit` for double-click hash-only audit JSON handoff from the
  extracted release ZIP; raw payloads require explicitly typing `PAYLOADS`.
- `Export-CareerSeeker-Evidence.cmd` wraps `export-alpha-package` for double-click tester audit handoff from
  the extracted release ZIP after a demo or live alpha cycle.
- `Import-CareerSeeker-Package.cmd` wraps `import-alpha-package` for double-click tester/auditor restore into
  a separate import workspace.
- `Verify-CareerSeeker-Alpha.cmd` wraps the package self-check and dashboard smoke for double-click tester
  verification from the extracted release ZIP.
- `scripts/Manage-AlphaDashboardTask.ps1` can register, remove, start, stop, and inspect a per-user Windows
  logon task for the alpha dashboard while the full service/tray/installer stack remains future work.
- `SqliteSeekerStore` is included through `Microsoft.Data.Sqlite`, with `StoreParityHarness` covering
  in-memory/SQLite behavior parity plus the recent-application and recent-job read models, and
  `EngineHarness` covering a SQLite-backed engine cycle.
- Live connector status: Scout ingestion, Gmail draft creation, BYOK provider calls, full alpha BYOK
  Gmail/PDF draft creation, alpha Gmail/PDF smoke, and dashboard Gmail disconnect wiring are verified.
- `scout-boards` gives the alpha executable a live ATS ingest path for Greenhouse, Lever, and Ashby boards;
  it writes postings into SQLite, stores full posting bodies as ignored local JD artifacts, records a hash-chained
  ingest event, and treats repeat sightings as reposts.
- `draft-job` lets testers create an L1 draft package for a selected stored job id; the dry-run path verifies
  package/artifact/audit behavior without touching Gmail, and the normal path uses the same Gmail draft port.
  When a selected job has a `jd_path`, Tailor and dispatch packaging receive the posting body as untrusted data.
- Bounded BYOK alpha smoke is verified for live Gate, live Tailor, Gmail draft creation, PDF attachment
  packaging, and SQLite audit.
- Brave web-research adapter source and the `research-company` alpha command are implemented, offline
  verified, live-verified with Brave Search plus BYOK dossier modeling, and exposed through a double-click
  trusted-tester helper.
- Dashboard `/applications` exposes recent job/application state, scores, draft refs, generated resume/cover
  document links, safe job/apply links, and token-protected pause/resume/kill controls in the shared alpha
  dashboard shell. Controls are shown for active/paused rows and hidden for terminal application states.
  `/evidence.html` exposes a human audit-chain page and `/evidence` exposes recent audit event metadata JSON
  without payload bodies.
- Dashboard home exposes token-protected hash-only audit JSON and alpha package export controls when running
  against a SQLite DB.
- Dashboard `/jobs` exposes visible job ids for selected-job drafting, recently discovered jobs,
  compensation metadata, source, safe job/apply links, repost count, and prompt-injection flags without raw
  job descriptions.
- `dashboard` serves the same `/jobs`, `/applications`, `/evidence.html`, `/evidence`, Gmail disconnect, and
  application controls against an existing SQLite alpha DB without starting a demo cycle.
- `export-audit` writes a local audit JSON package; payloads are hash-only by default and opt-in with
  `--include-payloads`.
- `export-alpha-package` writes a local ZIP package with a manifest, audit export, SQLite snapshot, draft
  artifacts, and saved job-description artifacts while filtering secret/token/key-looking paths.
- `import-alpha-package` restores a local ZIP package into a safe default `.appdata/imported` workspace,
  rejects unsafe entries, preserves existing files unless `--overwrite` is passed, and verifies the restored
  SQLite audit chain. The package helper exposes the same restore path without hand-typed commands.
- `doctor` checks local SQLite/audit health, artifact writability, Gmail OAuth/vault presence when required,
  and BYOK provider availability without printing secret values.
- `control-app` gives testers a local audited pause, resume, and kill switch for a specific application row.
- `profile-template` and `import-profile` let testers replace the local Tailor/Gate source-of-truth profile
  with their own verified/stated/weak claims.

## Not Yet Built

- Windows Service host, tray controls, and broader dashboard polish around `EngineHost`.
- Full onboarding UI, WinUI tray, OAuth/CASA, installer, and code signing.

