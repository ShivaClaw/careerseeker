# CareerSeeker Alpha Build Checklist

Updated: 2026-07-19
Purpose: turn the current repo into a small-tester Windows alpha without pretending launch polish is done.

## Current baseline

- `CareerSeeker.sln` builds cleanly on Windows in Release.
- Offline harnesses are green: Slice, EngineHarness, ResearcherHarness, HookHarness, StoreParityHarness, GatewayGateHarness, DispatcherNoSendHarness, LifecycleHarness, RendererHarness.
- Live Scout ingestion is already verified against real ATS feeds.
- Live Gmail draft creation is already verified with `gmail.compose`.
- `src/Engine` is now a runnable executable entrypoint with demo and alpha modes.
- A self-contained `win-x64` single-file publish succeeds and the published `.exe` runs a demo cycle.
- Engine alpha mode can use SQLite + DPAPI OAuth + Gmail to create one real self-addressed L1 draft.
- Engine alpha mode preflights Gmail draft access before creating the draft.
- The alpha executable can revoke Gmail OAuth and delete the local DPAPI token vault.
- Engine alpha mode can use `--llm byok` to route Tailor and Gate through local Anthropic/Gemini keys, preferring a DPAPI provider-key vault when present.
- Live BYOK provider smoke is green for Anthropic, Gemini, Tailor, Gate entailment, and Gateway accounting.
- Engine alpha mode has a bounded `--fast-smoke` BYOK path that verifies live Gate, live Tailor, Gmail draft creation, PDF attachment packaging, and SQLite audit in one routine command.
- Alpha BYOK Gate verification defaults to the top 3 semantic source candidates per claim to bound live entailment calls while failing closed.
- The unconstrained alpha `--llm byok` path now creates a real Gmail draft after live Tailor and live Gate verification.
- Engine alpha drafts attach a real ATS-clean resume PDF.
- The localhost dashboard can expose a token-protected Gmail disconnect control backed by the same local DPAPI revoke/delete path as the CLI.
- A real Brave Search web-research adapter and `research-company` alpha command are implemented; live verification is pending a Brave Search key.

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
- Add a disconnect flow that revokes Gmail refresh tokens and deletes local token material.
- Add BYOK provider wiring for Tailor and Gate from local environment or `env.secrets`.
- Add BYOK provider-key import/clear commands for the local DPAPI vault.
- Add bounded Gate semantic candidate selection for live BYOK alpha runs.
- Add a deterministic ATS-clean PDF renderer for alpha resume attachments.

Exit:
- A tester can run the executable, complete Gmail OAuth, and create a real draft in their own Gmail account.

Verified:
- `dotnet run -c Release --project tests/GmailLiveHarness/GmailLiveHarness.csproj -- --email you@gmail.com --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- import-byok --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi`
- `dotnet run -c Release --project tests/ByokLiveHarness/ByokLiveHarness.csproj -- --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- alpha --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- alpha --llm byok --fast-smoke --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- alpha --llm byok --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi`
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

Suggested entries:

```text
CLOUDFLARE_API_TOKEN=...
CLOUDFLARE_ZONE_NAME=careerseeker.app
CAREERSEEKER_GMAIL_TEST_EMAIL=...
ANTHROPIC_API_KEY=...
GEMINI_API_KEY=...
```
