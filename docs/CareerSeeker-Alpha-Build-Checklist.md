# CareerSeeker Alpha Build Checklist

Updated: 2026-07-20
Purpose: turn the current repo into a small-tester Windows alpha without pretending launch polish is done.

## Current baseline

- `CareerSeeker.sln` builds cleanly on Windows in Release.
- Offline harnesses are green: Slice, EngineHarness, ResearcherHarness, HookHarness, StoreParityHarness, GatewayGateHarness, DispatcherNoSendHarness, LifecycleHarness, RendererHarness.
- Live Scout ingestion is already verified against real ATS feeds.
- Live Gmail draft creation is already verified with `gmail.compose`.
- `src/Engine` is now a runnable executable entrypoint with demo and alpha modes.
- `scripts/Initialize-AlphaWorkspace.ps1` creates ignored local alpha directories, a starter profile template,
  and a blank env-secrets placeholder, and can run the startup doctor after setup.
- A self-contained `win-x64` single-file publish succeeds and the published `.exe` runs a demo cycle.
- Demo mode can also run against a persistent SQLite database with audit export support and local draft artifacts.
- Engine alpha mode can use SQLite + DPAPI OAuth + Gmail to create one real self-addressed L1 draft.
- Engine alpha mode preflights Gmail draft access before creating the draft.
- The alpha executable has a `connect-gmail` command that performs interactive Gmail OAuth, stores the local
  DPAPI token, and preflights Gmail draft access without creating a draft.
- The alpha executable can revoke Gmail OAuth and delete the local DPAPI token vault.
- Engine alpha mode can use `--llm byok` to route Tailor and Gate through local Anthropic/Gemini keys, preferring a DPAPI provider-key vault when present.
- Live BYOK provider smoke is green for Anthropic, Gemini, Tailor, Gate entailment, and Gateway accounting.
- Engine alpha mode has a bounded `--fast-smoke` BYOK path that verifies live Gate, live Tailor, Gmail draft creation, PDF attachment packaging, and SQLite audit in one routine command.
- Alpha BYOK Gate verification defaults to the top 3 semantic source candidates per claim to bound live entailment calls while failing closed.
- The unconstrained alpha `--llm byok` path now creates a real Gmail draft after live Tailor and live Gate verification.
- Engine alpha drafts attach a real ATS-clean resume PDF.
- The localhost dashboard can expose a token-protected Gmail disconnect control backed by the same local DPAPI revoke/delete path as the CLI.
- The localhost dashboard exposes `/applications` with recent application state, scores, draft refs, generated
  resume/cover document links, safe job/apply links, and token-protected pause/resume/kill controls.
- The localhost dashboard now uses a responsive shared alpha shell with stable navigation, metric cards, and
  readable recent-job/application tables while preserving the token-protected control routes.
- Dashboard HTML and token-protected document responses carry no-store, nosniff, no-referrer, and form-scoped
  CSP headers.
- Dashboard resume/cover document links are served through token-protected localhost document routes instead
  of raw `file://` links, and the Engine harness verifies the linked resume PDF bytes.
- The localhost dashboard exposes `/jobs` with visible job ids for selected-job drafting, recent discovered
  jobs, compensation/source metadata, safe job/apply links, repost counts, and prompt-injection flags.
- The localhost dashboard exposes `/evidence.html` for human audit-chain review and `/evidence` for recent
  audit event metadata JSON.
- The localhost dashboard exposes token-protected hash-only audit JSON and alpha package export controls when
  running against a SQLite DB.
- The alpha executable has a standalone `dashboard` mode for inspecting and controlling an existing SQLite
  alpha DB without starting a demo cycle.
- `scripts/Start-AlphaDashboard.ps1` gives trusted testers a Windows-friendly launcher for the same dashboard
  mode, including one-shot smoke checks and published-executable startup.
- `Setup-CareerSeeker-Alpha.cmd` gives trusted testers a double-click setup helper in the release ZIP that
  creates the local alpha workspace and opens the profile template for editing.
- `Import-CareerSeeker-Profile.cmd` gives trusted testers a double-click source-of-truth profile import helper
  after they edit the generated profile template.
- `Connect-CareerSeeker-Providers.cmd` gives trusted testers a double-click BYOK provider-key import and
  startup doctor helper without printing secret values.
- `Connect-CareerSeeker-Gmail.cmd` gives trusted testers a double-click Gmail OAuth helper that preflights
  draft access without creating a draft.
- `Check-CareerSeeker-LiveReadiness.cmd` gives trusted testers a double-click live Gmail/BYOK readiness doctor.
- `Clear-CareerSeeker-Providers.cmd` gives trusted testers a double-click local BYOK vault clear helper that
  requires typing `CLEAR`.
- `Disconnect-CareerSeeker-Gmail.cmd` gives trusted testers a double-click Gmail revoke/local token-vault clear
  helper that requires typing `DISCONNECT`.
- `Run-CareerSeeker-Demo.cmd` gives trusted testers a double-click safe demo cycle that writes local SQLite
  and artifact evidence without touching Gmail.
- `Run-CareerSeeker-Scout.cmd` gives trusted testers a double-click public ATS board ingest that writes local
  job/posting evidence without touching Gmail.
- `Research-CareerSeeker-Company.cmd` gives trusted testers a double-click Brave/BYOK company research helper
  that reads public web pages, prints a grounded dossier, and creates no Gmail draft.
- `Draft-CareerSeeker-Job.cmd` gives trusted testers a double-click selected-job draft helper that defaults to
  a no-Gmail dry-run package and requires typing `LIVE` before creating a Gmail draft.
- `Run-CareerSeeker-Live.cmd` gives trusted testers a double-click live L1 alpha cycle that defaults to a
  no-Gmail dry-run preview and requires typing `LIVE` before creating one Gmail draft for review.
- `Export-CareerSeeker-Audit.cmd` gives trusted testers a double-click hash-only audit JSON export after a demo
  or live alpha cycle, with raw payloads opt-in.
- `Export-CareerSeeker-Evidence.cmd` gives trusted testers a double-click evidence package export after a demo
  or live alpha cycle.
- `Import-CareerSeeker-Package.cmd` gives trusted testers a double-click evidence package restore into a
  separate import workspace, preserving existing files unless overwrite is explicit.
- `Verify-CareerSeeker-Alpha.cmd` gives trusted testers a double-click package self-check for manifest,
  checksum, secret-path, and dashboard smoke validation.
- `Start-CareerSeeker-Alpha.cmd` gives trusted testers a double-click launcher in the release ZIP that starts
  the packaged dashboard path.
- `Install-CareerSeeker-DashboardTask.cmd` gives trusted testers a double-click helper to register the packaged
  dashboard as a per-user Windows logon task after typing `INSTALL`.
- `Status-CareerSeeker-DashboardTask.cmd` gives trusted testers a double-click helper to inspect that logon
  task.
- `Uninstall-CareerSeeker-DashboardTask.cmd` gives trusted testers a double-click helper to remove that logon
  task after typing `UNINSTALL`.
- Extracted-package verification smokes the packaged provider-key clear and Gmail disconnect command paths
  against isolated temp vault paths.
- Extracted-package verification exports packaged audit JSON, exports a packaged evidence ZIP, and imports it into an isolated restore
  workspace.
- Extracted-package verification smokes the packaged live readiness helper with optional Gmail/BYOK checks off.
- Extracted-package verification previews the packaged company research helper without spending Brave/BYOK calls.
- Extracted-package verification dry-runs packaged dashboard logon-task install and uninstall commands and
  smokes the status command.
- `scripts/Package-AlphaRelease.ps1` creates a trusted-tester ZIP with the published executable, native runtime
  dependencies, double-click setup/profile/provider/Gmail/live-readiness/provider-clear/Gmail-disconnect/demo/scout/company-research/selected-job/live/audit-export/evidence-export/evidence-import/verify/dashboard and dashboard-task launchers, workspace initializer, dashboard/helper self-check scripts, quickstart, tester walkthrough, audit
  snapshot, release manifest, SHA-256 checksums, and selected docs while excluding local databases, vaults,
  provider keys, and generated artifacts.
- The alpha executable can export a local audit JSON package with payload hashes by default.
- The alpha executable can export a local alpha ZIP package with a manifest, audit export, SQLite snapshot,
  draft artifacts, and saved job-description artifacts while filtering secret-looking paths.
- The alpha executable can safely import a local alpha ZIP package into `.appdata/imported` by default, require a
  CareerSeeker alpha manifest, reject unsafe ZIP paths, preserve existing files unless `--overwrite` is passed,
  and verify the restored audit chain.
- The alpha executable has a `doctor` startup smoke for SQLite/audit health, artifact writability, Gmail config,
  Gmail vault presence, and BYOK provider availability.
- The alpha executable has an audited `control-app` command for pausing, resuming, or killing a local application row.
- The alpha executable has `profile-template` and `import-profile` commands so testers can replace the local
  Tailor/Gate source-of-truth profile without mixing in seeded demo claims.
- The alpha executable has a `scout-boards` command for live Greenhouse/Lever/Ashby board ingestion into SQLite,
  including local full-posting JD artifacts, a hash-chained ingest event, and repost refresh behavior.
- The alpha executable has a `draft-job` command for creating an L1 draft package from a selected stored job id,
  with posting-body loading from `jd_path` and a `--dry-run` path for package/artifact/audit verification without Gmail.
- A real Brave Search web-research adapter and `research-company` alpha command are implemented and live-verified
  with Brave Search plus BYOK dossier modeling.
- `scripts/Manage-AlphaDashboardTask.ps1` can optionally register a per-user Windows logon task for keeping
  the alpha dashboard available without claiming the full Windows Service/tray/installer work is done.

## Alpha target

An alpha build is good enough when a tester can:

- launch a Windows executable without installing the .NET SDK
- see a localhost dashboard
- run at least one real or demo cycle
- connect Gmail in L1 drafts mode
- verify that drafts are created but nothing can send

## Phase A: runnable executable

Status: complete as of 2026-07-16

- Add a real `Program.cs` in `src/Engine`.
- Change `src/Engine/SeekerSvc.Engine.csproj` to `OutputType=Exe`.
- Support a demo mode that exercises the engine shell with fake feed/scoring/tailoring so we can publish and distribute a binary immediately.
- Publish a self-contained Windows build:
  `dotnet publish src/Engine/SeekerSvc.Engine.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true`

Exit:
- We have a `SeekerSvc.Engine.exe` that starts, serves the dashboard, and can run a demo cycle.

Verified:
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- demo --once`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- demo --once --db .appdata/careerseeker-demo.db --artifacts .appdata/artifacts`
- `dotnet publish src/Engine/SeekerSvc.Engine.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true`
- `src/Engine/bin/Release/net8.0/win-x64/publish/SeekerSvc.Engine.exe demo --once`

## Phase B: technical alpha for trusted testers

Status: complete as of 2026-07-16

- Build the production composition root for the alpha path.
- Wire SQLite store instead of in-memory store.
- Wire DPAPI token vault paths for local secrets.
- Reuse the existing Gmail OAuth flow from `src/Dispatcher/GoogleOAuth.cs`.
- Add a startup preflight that reports missing OAuth client JSON, missing token vault, or disabled Gmail API clearly.
- Add a simple local config convention for alpha secrets and paths.
- Add a first-class Gmail connect command so OAuth setup does not require creating a draft.
- Add a disconnect flow that revokes Gmail refresh tokens and deletes local token material.
- Add BYOK provider wiring for Tailor and Gate from local environment or `env.secrets`.
- Add BYOK provider-key import/clear commands for the local DPAPI vault.
- Add bounded Gate semantic candidate selection for live BYOK alpha runs.
- Add a deterministic ATS-clean PDF renderer for alpha resume attachments.

Exit:
- A tester can run the executable, complete Gmail OAuth, and create a real draft in their own Gmail account.

Verified:
- `powershell -ExecutionPolicy Bypass -File scripts/Verify-Alpha.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/Initialize-AlphaWorkspace.ps1 -DryRun`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- profile-template --out .appdata/profile.template.json`
- `powershell -ExecutionPolicy Bypass -File scripts/Import-AlphaProfile.ps1`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- import-profile --profile .appdata/profile.template.json --db .appdata/careerseeker-alpha.db`
- `powershell -ExecutionPolicy Bypass -File scripts/Verify-Alpha.ps1 -IncludeResearch`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- connect-gmail --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi`
- `dotnet run -c Release --project tests/GmailLiveHarness/GmailLiveHarness.csproj -- --email you@gmail.com --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi`
- `powershell -ExecutionPolicy Bypass -File scripts/Connect-AlphaProviders.ps1`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- import-byok --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi`
- `dotnet run -c Release --project tests/ByokLiveHarness/ByokLiveHarness.csproj -- --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- alpha --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- alpha --llm byok --fast-smoke --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- alpha --llm byok --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- dashboard --once --db .appdata/careerseeker-alpha.db --gmail-control --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi`
- `powershell -ExecutionPolicy Bypass -File scripts/Start-AlphaDashboard.ps1 -Once`
- `powershell -ExecutionPolicy Bypass -File scripts/Start-AlphaDashboard.ps1 -Published -PublishIfMissing -Once`
- `powershell -ExecutionPolicy Bypass -File scripts/Run-AlphaDemoCycle.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/Test-AlphaReleasePackage.ps1 -RunDashboardSmoke`
- `powershell -ExecutionPolicy Bypass -File scripts/Manage-AlphaDashboardTask.ps1 -Action Install -DryRun`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- doctor --require-gmail --require-byok --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db --artifacts .appdata/artifacts --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- scout-boards --board greenhouse:remotecom --board lever:mistral --db .appdata/scout-boards-smoke.db --jd-dir .appdata/job-descriptions`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- draft-job --job-id 1 --dry-run --llm byok --db .appdata/scout-boards-smoke.db --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- control-app --db .appdata/careerseeker-alpha.db --application-id 1 --action pause`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- disconnect-gmail --vault .appdata/oauth/gmail-token.dpapi`
- `dotnet run -c Release --project tests/RendererHarness/RendererHarness.csproj --no-build`

## Phase C: useful application alpha

Status: after technical alpha

- Add a polished HTML/Chromium renderer when visual templates become product-facing.

Exit:
- A tester can run an end-to-end L1 draft flow on real jobs with real Gmail drafts and real tailored content.

## Not required for alpha

- Windows Service host
- tray app
- polished installer
- code signing
- OAuth production verification completion
- CASA completion
- Android relay or dashboard

## Domain and secrets

- Domain is registered: `careerseeker.app`
- Recommended support endpoints for OAuth verification prep:
  - `https://careerseeker.app/`
  - `https://careerseeker.app/privacy`
  - `https://careerseeker.app/support`
- Keep local machine secrets under the already-ignored `secrets/` directory.
- Recommended file for local agent-readable secrets:
  `secrets/env.secrets`

Suggested entries for the current alpha verification path:

```text
ANTHROPIC_API_KEY=...
GEMINI_API_KEY=...
BRAVE_SEARCH_API_KEY=...
```

`research-company` also accepts `BRAVE_SEARCH_API` and `CAREERSEEKER_BRAVE_SEARCH_API_KEY` as local aliases.

