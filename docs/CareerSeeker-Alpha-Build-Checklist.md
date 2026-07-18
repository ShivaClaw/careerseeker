# CareerSeeker Alpha Build Checklist

Updated: 2026-07-16
Purpose: turn the current repo into a small-tester Windows alpha without pretending launch polish is done.

## Current baseline

- `CareerSeeker.sln` builds cleanly on Windows in Release.
- Offline harnesses are green: Slice, EngineHarness, ResearcherHarness, HookHarness, StoreParityHarness, GatewayGateHarness, DispatcherNoSendHarness.
- Live Scout ingestion is already verified against real ATS feeds.
- Live Gmail draft creation is already verified with `gmail.compose`.
- `src/Engine` is now a runnable executable entrypoint with demo and alpha modes.
- A self-contained `win-x64` single-file publish succeeds and the published `.exe` runs a demo cycle.
- Engine alpha mode can use SQLite + DPAPI OAuth + Gmail to create one real self-addressed L1 draft.

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

Exit:
- A tester can run the executable, complete Gmail OAuth, and create a real draft in their own Gmail account.

Verified:
- `dotnet run -c Release --project tests/GmailLiveHarness/GmailLiveHarness.csproj -- --email you@gmail.com --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi`
- `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- alpha --email you@gmail.com --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db`

## Phase C: useful application alpha

Status: after technical alpha

- Wire BYOK provider keys from the local DPAPI-backed secret path.
- Verify one real Tailor call and one real Gate entailment call.
- Add a basic document renderer path so the draft can attach a real resume PDF.
- Add a disconnect flow that revokes Gmail refresh tokens and deletes local token material.

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
```
