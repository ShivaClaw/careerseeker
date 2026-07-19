# CareerSeeker Engine Host

This project is the runnable alpha entrypoint for the local-first CareerSeeker engine. It hosts the
scheduled engine shell, the localhost dashboard, and the one-shot alpha smoke path that can create a
reviewable Gmail draft without any send capability.

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
- `LocalDashboard`: loopback-only HTML and JSON status on `localhost`, with optional token-protected controls.
- `EngineHost`: composition root for counters, scheduler, and dashboard.

## Alpha Executable Modes

- Demo, offline and repeatable:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- demo --once`
- Demo with persistent SQLite state and audit evidence:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- demo --once --db .appdata/careerseeker-demo.db`
- Alpha Gmail smoke, live but one-shot:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- alpha --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db`
- Import real BYOK provider keys into the local DPAPI vault:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- import-byok --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi`
- Live BYOK provider smoke for Anthropic, Gemini, Tailor, Gate, and accounting:
  `dotnet run -c Release --project tests/ByokLiveHarness/ByokLiveHarness.csproj -- --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi`
- Alpha Gmail smoke with real BYOK Tailor and Gate calls:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- alpha --llm byok --gate-semantic-candidates 3 --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db`
- Bounded BYOK alpha smoke for routine validation:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- alpha --llm byok --fast-smoke --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db`
- Disconnect Gmail, revoking OAuth and deleting the local vault:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- disconnect-gmail --vault .appdata/oauth/gmail-token.dpapi`
- Clear imported BYOK provider keys from the local vault:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- clear-byok --key-vault .appdata/secrets/byok-keys.dpapi`

The alpha mode uses SQLite for engine state, the DPAPI token vault for Gmail OAuth, and the real
`GmailDraftClient`. Demo mode is in-memory by default, but `demo --db <path>` runs the same local
dashboard/cycle shell against `SqliteSeekerStore` so testers can inspect persistent status and audit
evidence without touching Gmail. Alpha preflights Gmail draft access before creating anything, renders an
ATS-clean PDF resume attachment, then intentionally runs one cycle and creates one self-addressed L1 draft
so early testing cannot accidentally produce drafts on a timer.

By default it uses fake inference. Pass `--llm byok` to use local Anthropic/Gemini keys from the DPAPI
provider-key vault, environment variables, or `secrets/env.secrets`. `--email` is optional when Gmail
profile lookup is available. For quick routine validation, use `--llm byok --fast-smoke`; it performs one
bounded live Gate entailment check, one bounded live Tailor call, then runs the normal Gmail/PDF draft path.
In BYOK alpha mode, the Gate defaults to the top 3 semantically relevant source candidates per claim to
keep live entailment calls bounded; pass `--gate-semantic-candidates 0` for exhaustive comparison.

## Injected Ports

- `IJobFeed`: candidate postings. Production is Scout over ATS feeds; sandbox is a fixed batch.
- `ISemanticScorer`: CV-match and growth sub-scores. Production is the LLM Gateway; sandbox is deterministic.
- `IDocumentRenderer`: production alpha is the deterministic ATS-clean PDF renderer. Future product polish
  can add an HTML/Chromium renderer.

## Verified Status

- `dotnet build CareerSeeker.sln -c Release`: 0 warnings, 0 errors.
- Latest offline harness total: 199 passed, 0 failed.
- `SqliteSeekerStore` is included through `Microsoft.Data.Sqlite`, with `StoreParityHarness` covering
  in-memory/SQLite behavior parity plus the recent-application read model, and `EngineHarness` covering a
  SQLite-backed engine cycle.
- Live connector status: Scout ingestion, Gmail draft creation, BYOK provider calls, full alpha BYOK
  Gmail/PDF draft creation, alpha Gmail/PDF smoke, and dashboard Gmail disconnect wiring are verified.
- Bounded BYOK alpha smoke is verified for live Gate, live Tailor, Gmail draft creation, PDF attachment
  packaging, and SQLite audit.
- Brave web-research adapter source and the `research-company` alpha command are implemented and offline
  verified; live verification is pending a Brave Search key.
- Dashboard `/applications` exposes recent job/application state, scores, draft refs, and safe job/apply
  links; `/evidence` exposes audit-chain status and recent audit event metadata without payload bodies.
- `export-audit` writes a local audit JSON package; payloads are hash-only by default and opt-in with
  `--include-payloads`.

## Not Yet Built

- Live `research-company` verification with a real Brave Search key.
- Windows Service host, tray controls, and richer dashboard application/job drill-down around `EngineHost`.
- Onboarding, WinUI tray, OAuth/CASA, installer, and code signing.
