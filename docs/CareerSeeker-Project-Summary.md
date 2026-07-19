# CareerSeeker Project Summary

Updated: 2026-07-18
Audience: LLM agents, coding agents, test harnesses, planning agents
Source repo: repository root
Primary branch: `main`
Status: living handoff
Sensitivity: internal project context

Do not commit or echo secrets, OAuth client JSON, refresh tokens, provider keys, resumes, or user profile data.

## Purpose

CareerSeeker is a local-first Windows career-search engine. It discovers jobs, evaluates fit and legitimacy, researches employers, tailors application materials, verifies every candidate claim against the user's source-of-truth profile, and creates reviewable Gmail drafts for the user.

The first launch mode is L1 Drafts: CareerSeeker may prepare work, but the human reviews and sends. The product is built to increase application throughput without compromising user agency, truthfulness, account safety, or privacy.

The authoritative product spec is [CareerSeeker-Spec.md](./CareerSeeker-Spec.md). Treat this file as a high-signal repo and handoff map, not the authoritative spec.

## Current Status

Overall status: technical Windows alpha path implemented; SQLite source restoration and parity coverage verified.

Completed:

- B1 live Scout ATS ingestion verified against real Greenhouse, Lever, and Ashby APIs.
- B5 Gmail draft client verified with a real Google OAuth token and a real Gmail draft in the test account.
- OAuth token storage works through a local DPAPI-backed token vault.
- OAuth client JSON handling is ignored by Git via `client_secret*.json`.
- L1 compose-only correction is in place: custom Gmail labels are skipped by default because label management requires broader Gmail scope than `gmail.compose`.
- SQLite provider source is restored to the Store project and covered by `StoreParityHarness`.
- Gateway pinned-Gate and Dispatcher no-send invariants now have named offline harnesses.
- Gate outages now fail closed into `GATE_UNAVAILABLE` instead of being mislabeled as fabrication.

Not complete yet:

- BYOK Anthropic/Gemini provider key wiring and live inference smoke.
- Real web research adapter.
- Headless Chromium document renderer.
- Windows Service, tray, installer, and code signing.
- OAuth production verification and CASA assessment.
- Android blind relay and dashboard.

## Founding Decisions

- Local first: the pipeline, SQLite store, OAuth tokens, provider API keys, generated documents, resume, profile, and audit trail live on the user's Windows machine.
- No hosted pipeline: do not move the engine, Gmail draft creation, OAuth tokens, resume, or user data to Cloud Run or any hosted backend.
- Only planned server component: a future blind relay for Android dashboard sync. Relay content must be end-to-end encrypted and unreadable by the server.
- L1 scope: request `gmail.compose` only.
- No send path in L1: `gmail.compose` can authorize sends, but the L1 Gmail interface exposes no send method and the app has no send implementation. Sending belongs to later L2/L3 scope decisions.
- CASA/OAuth cost control: local-first architecture and minimal scopes are deliberate verification cost controls.
- Vendor plurality: strong-tier failover is structural. The pinned Gate must not depend on a single vendor.
- Free Windows engine: the Windows engine is intended to be a free `.exe`; paid surfaces are the Android dashboard or future managed inference.
- User agency: the Autonomy Contract and kill switch are product primitives, not marketing copy.

## Repository Layout

- [CareerSeeker.sln](../CareerSeeker.sln): .NET 8 solution.
- [README.md](../README.md): concise orientation and safety invariants.
- [nuget.config](../nuget.config): restores `Microsoft.Data.Sqlite` from nuget.org.
- [docs/](./): spec, roadmap, active privacy/support/autonomy trust docs, and this Markdown handoff.
- [src/](../src): production projects and ports.
- [tests/](../tests): console harnesses; no xUnit dependency.

Important docs:

- [CareerSeeker-Spec.md](./CareerSeeker-Spec.md): authoritative v0.9 product and architecture spec.
- [CareerSeeker-Integration-Windows-Roadmap.md](./CareerSeeker-Integration-Windows-Roadmap.md): integration sequencing.
- [CareerSeeker-Spec-5_6-LLM-Gateway.md](./CareerSeeker-Spec-5_6-LLM-Gateway.md): gateway economics and routing addendum.
- [Privacy-Policy.md](./Privacy-Policy.md): current L1 alpha privacy and Google Limited Use language.
- [Support.md](./Support.md): current alpha support and account-disconnect guidance.
- [Autonomy-Contract.md](./Autonomy-Contract.md): current L1 autonomy boundaries and no-send/label split.

## Module Map

| Module | Purpose | Key Files |
| --- | --- | --- |
| `src/Scout` | Ingest public ATS feeds, normalize jobs, detect remote/compensation/injection signals, deduplicate. | `Ats.cs`, `Scout.cs`, `Providers.cs`, `Http.cs`, `Dedup.cs`, `Canon.cs`, `Internal.cs` |
| `src/Store` | Single source of truth, SQLite schema, in-memory test store, audit chain. | `Schema.cs`, `ISeekerStore.cs`, `InMemorySeekerStore.cs`, `SqliteSeekerStore.cs`, `Ingest.cs`, `Audit.cs` |
| `src/Scorer` | Deterministic fit and legitimacy scoring; scam floor and red flag multiplier. | `Components.cs`, `Scoring.cs`, `Scorer.cs` |
| `src/Researcher` | Retrieve company docs, derive grounded dossiers and positive-only legitimacy signals. | `Researcher.cs`, `Grounding.cs`, `Dossier.cs`, `Signals.cs`, `GatewayDossierModel.cs` |
| `src/Gateway` | LLM routing, budgets, provider contracts, pinned Gate policy, fake and HTTP providers. | `Routing.cs`, `Stages.cs`, `Gateway.cs`, `Budget.cs`, `Accounting.cs`, `Contracts.cs`, `ProvidersHttp.cs` |
| `src/Tailor` | Generate resume/cover/answers, decompose candidate claims, apply hook safety. | `Tailor.cs`, `Drafting.cs`, `Decomposer.cs`, `GatewayTailorModel.cs`, `Hook.cs`, `Policy.cs` |
| `src/TailorHookBridge` | Bridge Researcher dossier hooks into Tailor without direct cyclic dependency. | `DossierHookProvider.cs` |
| `src/Verifier` | Fabrication Gate claim entailment and exact/semantic verification. | `FabricationGate.cs`, `Claims.cs`, `Matchers.cs` |
| `src/Pipeline` | Application lifecycle state machine and orchestration ports. | `ApplicationPipeline.cs`, `Lifecycle.cs`, `States.cs`, `Ports.cs`, `ClaimMapping.cs` |
| `src/Dispatcher` | Package verified applications, build MIME, create Gmail drafts, detect channels, run local OAuth token flow. | `Dispatch.cs`, `Dispatcher.cs`, `Mime.cs`, `Packaging.cs`, `Recipients.cs`, `Providers.cs`, `GoogleOAuth.cs` |
| `src/Engine` | Cycle scheduler, counters, localhost dashboard. | `EngineCore.cs`, `Host.cs` |

## Architecture

### Local-First Boundary

```text
+----------------------------------------------------------------------------------+
| USER WINDOWS MACHINE                                                             |
|                                                                                  |
|  +---------------------+      +----------------------+      +------------------+  |
|  | Windows Service     | ---> | Engine/Pipeline      | ---> | Local Dashboard  |  |
|  | future host         |      | cycle orchestration   |      | localhost:7777   |  |
|  +---------------------+      +----------------------+      +------------------+  |
|            |                              |                            |          |
|            v                              v                            v          |
|  +---------------------+      +----------------------+      +------------------+  |
|  | SQLite store        |      | DPAPI vault          |      | Generated docs   |  |
|  | seeker.db, WAL      |      | OAuth/API keys       |      | PDFs/dossiers    |  |
|  | hash-chain events   |      | local user scope     |      | local paths      |  |
|  +---------------------+      +----------------------+      +------------------+  |
|            |                              |                                       |
+------------|------------------------------|---------------------------------------+
             |                              |
             v                              v
  +--------------------------+      +---------------------------+
  | Public external APIs     |      | User-authorized services  |
  | ATS feeds, LLM providers |      | Gmail OAuth compose only  |
  | web search/fetch         |      | future calendar/inbox     |
  +--------------------------+      +---------------------------+
```

Boundary rules:

- Resume/profile/tokens/database never leave the Windows machine.
- LLM calls receive only the minimum task data needed; job descriptions remain untrusted data.
- Gmail drafts are created in the user's own Gmail account via OAuth.
- Future Android reads state through a blind E2E relay only.

### Pipeline

```text
Live ATS boards
    |
    v
Scout --> DiscoveredJob --> Store.Ingest --> companies/jobs
    |                         |
    |                         v
    |                    Scorer + semantic scores
    |                         |
    v                         v
Researcher ------------> dossier/signals
    |                         |
    v                         v
Tailor <----------- DossierHookBridge
    |
    v
Decomposer --> TailoredClaims
    |
    v
Verifier / Fabrication Gate
    | pass                         | fail
    v                              v
VERIFIED -> READY              BLOCKED_FABRICATION
                           or  GATE_UNAVAILABLE
    |
    v
Dispatcher -> MIME -> GmailDraftClient -> Gmail Draft
```

L1 state path:

1. `DISCOVERED`
2. `SCREENED`
3. `EVALUATED`
4. `TAILORED`
5. `VERIFIED`
6. `READY`
7. `DRAFTED`

Safety assertion: `READY` is reachable only from `VERIFIED`, and `DRAFTED` is reachable only from `READY`. Therefore a Gmail draft cannot be created without passing the Fabrication Gate.

### Module Dependencies

- Leaves: `Verifier`, `Scout`, `Gateway`.
- Core: `Store` depends on `Scout`; `Scorer` depends on `Scout`.
- Orchestration: `Pipeline` depends on `Store`, `Scorer`, and `Verifier`; `Tailor` depends on `Gateway`, `Pipeline`, and `Verifier`; `Dispatcher` depends on `Pipeline`; `Engine` depends directly on `Pipeline`, `Tailor`, and `Dispatcher`.
- Content: `Researcher` depends on `Gateway`; `TailorHookBridge` connects `Researcher` to `Tailor`.
- Delivery: Gmail OAuth and REST draft client live in `Dispatcher`.

Dependency principles:

- Ports isolate network/OS edges.
- Test harnesses use fakes.
- Live harnesses exercise real adapters one connector at a time.

## Data And Secrets

```text
+-------------------+        +-------------------+        +----------------------+
| Profile/resume    | -----> | Source claims     | -----> | Fabrication Gate     |
| local onboarding  |        | Store.claims      |        | no unsupported prose |
+-------------------+        +-------------------+        +----------------------+

+-------------------+        +-------------------+        +----------------------+
| OAuth client JSON | -----> | Google OAuth flow | -----> | DPAPI token vault    |
| ignored by Git    |        | loopback browser  |        | ignored local file   |
+-------------------+        +-------------------+        +----------------------+

+-------------------+        +-------------------+        +----------------------+
| Events table      | -----> | prev_hash/hash    | -----> | tamper-evidence      |
| actor/kind/entity |        | chain             |        | VerifyAuditAsync     |
+-------------------+        +-------------------+        +----------------------+
```

Secret rules:

- `client_secret*.json` is ignored.
- `token*.json` is ignored.
- `.appdata/` is ignored.
- Provider keys must not be committed.
- DPAPI token vault is local user-profile scoped.

## Store Schema Summary

| Table | Purpose |
| --- | --- |
| `profile` | Canonical user profile JSON and version. |
| `claims` | Atomic source-of-truth profile claims; Gate oracle. |
| `companies` | ATS handle, company metadata, dossier path. |
| `jobs` | Normalized discovered postings, compensation, remote state, injection signals, simhash. |
| `scores` | Fit, legitimacy, red flag multiplier, total. |
| `applications` | Lifecycle state, autonomy level, artifacts, channel. |
| `gates` | Human approval gates for apply/reply/calendar/lesson. |
| `threads` | Gmail thread association for future correspondence. |
| `events` | Hash-chained audit log. |
| `lessons` | Opt-in campaign learning. |
| `stories` | STAR+R story bank. |
| `config` | Local settings and rails. |

SQLite pragmas:

- `journal_mode=WAL`
- `foreign_keys=ON`
- `synchronous=NORMAL`
- `busy_timeout=5000`

## Safety Invariants

- Fabrication Gate: no action state is reachable except through `VERIFIED`.
- Pinned Gate: `Stage.VerifierEntailment` is `StrongCloud`, never downgraded, never throttled.
- Dispatcher L1: Gmail draft port has no send method.
- Gmail scope: L1 requests `gmail.compose` only.
- Compose-only labels: custom label management is disabled by default because it requires broader Gmail scope.
- Researcher: facts are grounded-or-dropped; no ungrounded dossier fact survives.
- HookGuard: hooks that resemble candidate claims are omitted.
- Scorer: `total = min(fit, legitimacy) * red_flag_multiplier`.
- Legitimacy floor: low-legitimacy jobs may be shown but not acted on.
- Scout: job description is untrusted data and may signal prompt injection but is never instruction context.
- Store: events form a tamper-evident hash chain.
- Server boundary: no hosted pipeline; future server relay is blind and E2E encrypted.

## Connector Status

| Connector | Status | Notes |
| --- | --- | --- |
| Scout | Live verified | Greenhouse, Lever, and Ashby public APIs. Board-level failures are isolated. |
| Gmail OAuth | Live verified | `gmail.compose`; DPAPI local token vault; real draft created; custom labels skipped for L1. |
| LLM providers | Compile verified only | BYOK Anthropic and Gemini planned first, managed proxy later. |
| Research web | Fake/offline only | Planned search API plus web fetch. |
| Document renderer | Fake/offline only | Planned headless Chromium renderer to ATS-clean PDF. |
| SQLite | Source restored | `Microsoft.Data.Sqlite` PackageReference active; `StoreParityHarness` passed. |
| Windows service/tray | Engine shell only | Service/tray not yet implemented. |
| Android relay | Not implemented | Intentionally deferred. |

## Verification Log

Environment:

- OS: Windows
- Framework: .NET 8 SDK/runtime
- Build command: `dotnet build CareerSeeker.sln -c Release`
- Networked tests require explicit approval.
- Local build caches ignored: `.appdata`, `.dotnet`, `.nuget`, `bin`, `obj`

Latest build:

- Date: 2026-07-19
- Result: passed
- Restore: `Microsoft.Data.Sqlite` restored from nuget.org.
- Warnings: 0
- Errors: 0
- Alpha DB migration check: copied `.appdata/careerseeker-alpha.db`, initialized with current SQLite
  store, verified `applications.paused_from`, and passed a pause/resume round-trip on the copy.

Latest offline harnesses:

Total: 145 passed, 0 failed.

| Harness | Result |
| --- | --- |
| `Slice` | 22 passed, 0 failed |
| `EngineHarness` | 13 passed, 0 failed |
| `ResearcherHarness` | 21 passed, 0 failed |
| `HookHarness` | 10 passed, 0 failed |
| `StoreParityHarness` | 12 passed, 0 failed |
| `GatewayGateHarness` | 21 passed, 0 failed |
| `DispatcherNoSendHarness` | 9 passed, 0 failed |
| `LifecycleHarness` | 37 passed, 0 failed |

Live Scout harness, 2026-07-07:

- Result: 29 passed, 0 failed.
- Boards configured:
  - Greenhouse: `remotecom`, `xai`, `grafanalabs`
  - Lever: `mistral`, `gohighlevel`, `rws`, `lever`
  - Ashby: `deel`, `ramp`, `suno`, `notable`
- Observed empty feeds:
  - Lever `lever` returned HTTP 200 with zero jobs.
  - Ashby `deel` returned HTTP 200 with zero jobs through posting API while the public page existed.
- Raw jobs: 1169
- Deduped jobs: 803
- Duplicates collapsed: 366
- Remote or hybrid jobs: 493
- Compensation present: 388
- Structured compensation: 188
- Parsed-from-text compensation: 200
- Prompt-injection signals: 160

Live Gmail harness, 2026-07-08:

- Result: 4 passed, 0 failed.
- Scope: `gmail.compose`
- Send method present: false
- Token: access token available from DPAPI vault
- Labels: skipped under compose-only L1
- Real draft created: true
- Draft ID: redacted real draft ID

## Design Changes Since Original Snapshot

- Repo baseline repaired into structured `src/`, `tests/`, and `docs/` layout.
- B1 live Scout harness added and merged to main.
- Greenhouse public host handling recognizes `job-boards.greenhouse.io` and `boards.greenhouse.io`.
- BoardRegistry parses API URLs for Greenhouse, Lever, and Ashby instead of misreading path prefixes as handles.
- Compensation text parsing hardened:
  - interval detection uses salary-context window instead of entire job description.
  - European thousands like `110.000` normalize correctly.
  - implausibly large hourly parsed ranges are treated as annual.
- Trust/OAuth docs added and cleaned up: Privacy Policy, Support, Autonomy Contract.
- Local OAuth implementation added without external NuGet packages.
- DPAPI token vault added for local OAuth token storage.
- Gmail live harness added.
- L1 Gmail labels deferred to preserve `gmail.compose` only.
- `client_secret*.json` and `token*.json` added to `.gitignore`.
- Main branch includes verified real Gmail draft creation.
- SQLite source restored, `nuget.org` re-enabled, and Store parity harness added.
- Gateway pinned-Gate and Dispatcher no-send harnesses added.
- Lifecycle CAS transitions, durable L2 payloads, side-effect attempt records, pause/resume durability,
  and crash reconciliation added with `LifecycleHarness` coverage.
- Gateway budget/accounting and Pipeline in-flight state hardened for concurrency.
- StrongCloud failover now points at live `gemini-3.1-pro-preview`, and Gate outages defer distinctly from fabrication blocks.
- Tailor and Researcher prompts now mark untrusted data in explicit XML-style blocks.

## Roadmap

### Phase 0: Repo And Invariants

Status: complete.

- Repo normalized.
- Core safety invariants documented.
- Offline harnesses green.

### Phase 1: Real Connector Foundation

Status: substantially complete.

- B1 Scout live ingestion: complete.
- B5 Gmail compose-only draft creation: complete.
- A2 Google OAuth test-mode setup: complete enough for closed testing.
- A5 DPAPI vault: first implementation complete.

### Phase 2: Next Real Connectors

- B2 BYOK LLM providers:
  - Wire Anthropic and Gemini provider clients to local DPAPI-stored keys.
  - Verify real Tailor call and real Gate entailment call.
  - Confirm budget accounting and failover.
- B3 real Researcher:
  - Implement `IWebResearch` with search API plus web fetch.
  - Verify grounding drops unsupported facts on real pages.
- B4 document renderer:
  - Implement `IDocumentRenderer` with headless Chromium/Playwright.
  - Produce ATS-clean resume PDF and optional cover PDF.

### Phase 3: Composition Root And Storage

- Keep SQLite package restore available in CI or a warmed NuGet cache.
- Keep `StoreParityHarness` green for in-memory/SQLite behavior parity.
- Build composition root that wires:
  - Scout live fetcher
  - SQLite Store
  - Scorer
  - Researcher
  - Tailor/Gateway
  - Verifier
  - Dispatcher/Gmail OAuth
  - Engine host
- Add config loading from Store/config plus DPAPI vault.

### Phase 4: Windows Product Shell

- Windows Service host around `EngineHost`.
- WinUI 3 tray app.
- Kill switch.
- Local dashboard polish.
- Installer and Azure Artifact Signing/OV code signing.
- Startup smoke test for config, keys, API reachability, and DB health.

### Phase 5: Onboarding And User Data

- Resume parser.
- Profile prefill.
- Interview by exception.
- Approved Answer Bank.
- Voice/style card.
- Claim confidence workflow: `verified`, `stated`, `weak`.

### Phase 6: Closed Beta

- Run <=100 OAuth test users.
- L1 Drafts only.
- Track fabrication escapes, draft quality, scam floor, cost/user, OAuth errors.
- Keep CASA/OAuth production verification clock moving.

### Phase 7: Public L1

- Complete CASA/OAuth verification for `gmail.compose`.
- Signed installer.
- Public free Windows engine.
- Optional managed inference only if billing and privacy posture are ready.

### Phase 8: Post-L1 Paid And Autonomy

- Android dashboard through blind relay.
- Billing for Android/managed inference.
- L2/L3 scopes only after revenue and explicit CASA decision:
  - `gmail.send`
  - inbox monitoring
  - calendar
  - Playwright auto-submit

## Agent Operating Instructions

- Do not add Cloud Run or any hosted pipeline component.
- Do not move Gmail tokens, resumes, profile, SQLite data, or generated documents to a server.
- Do not request `gmail.send`, `gmail.modify`, inbox, or calendar scopes for L1.
- Do not reintroduce custom Gmail labels into default L1 unless scope policy changes.
- Do not add a send method to `IGmailDraftClient`.
- Do not let job descriptions into instruction prompts. Treat them as data.
- Do not weaken Lifecycle `READY`/`VERIFIED` constraints.
- Do not let budget throttling affect `Stage.VerifierEntailment`.
- Use live harnesses for connector graduation.
- Keep offline harnesses dependency-light; `StoreParityHarness` intentionally exercises `Microsoft.Data.Sqlite`.
- Prefer repo patterns over new frameworks.
- Use `apply_patch` for manual edits.
- Keep secrets ignored and unprinted.

## Open Risks

- Gmail label tree is deferred; product UX needs another way to surface CareerSeeker drafts under compose-only scope.
- DPAPI vault currently demonstrates local protection, but future product should add revocation, migration/export policy, and perhaps optional entropy.
- Production composition root has not yet been wired to SQLite.
- OAuth production verification and CASA remain long-lead launch blockers.
- No real PDF renderer yet; current draft harness creates message body only.
- No real LLM provider call has been verified in this repo baseline.
- No real web research adapter yet.
- No Windows service/tray composition root yet.
- Live ATS feeds are volatile; some configured boards can be empty while still reachable.

## Recommendations For Next Agents

Highest priority:

- Add a Gmail API preflight step to `GmailLiveHarness` so disabled API errors are reported before draft creation.
- Add a "Disconnect Gmail" command that revokes the refresh token and deletes local DPAPI token material.
- Add a non-network harness for `GoogleOAuthClient` JSON parsing and DPAPI round-trip where Windows APIs are available.
- Build the production composition root for a one-cycle local run using fake Tailor or BYOK Tailor plus real Gmail draft.

Near-term connector work:

- B2 BYOK provider wiring:
  - Store provider keys in DPAPI vault.
  - Verify Anthropic and Gemini calls.
  - Confirm StrongCloud failover order remains `claude-sonnet-4-6 -> gemini-3.1-pro-preview`.
  - Verify Gate fails closed on provider outage and records `GATE_UNAVAILABLE`, not fabrication.
- B4 document rendering:
  - Add Playwright/Chromium renderer.
  - Attach real PDF to Gmail draft.
  - Ensure text is ATS-clean and does not introduce ungated claims.
- SQLite:
  - Wire the production composition root to `SqliteSeekerStore`.
  - Keep package restore available in CI or a warmed NuGet cache.

Product recommendations:

- Keep L1 free, local-first, and reviewable.
- Treat the first public promise as "real drafts, zero sends."
- Make privacy copy concrete: resume and OAuth tokens never leave the device.
- Add visible local dashboard evidence: jobs found, rejected, blocked, drafted, last audit verification.
- Add a prominent "show me everything CareerSeeker did" export early.

Security recommendations:

- Rotate any secrets pasted into chat or tools.
- Keep provider keys and OAuth tokens out of Git and logs.
- Require explicit user action before adding any restricted Gmail scope beyond compose.
- Document scope changes as architecture changes, not implementation details.
- Add red-team cases for prompt injection in job postings.

Testing recommendations:

- Keep all existing offline harnesses green before each merge to main.
- Run `ScoutLiveHarness` when changing Scout, compensation parsing, board registry, or dedup.
- Run `GmailLiveHarness` when changing Dispatcher, OAuth, MIME, or Gmail REST code.
- Record live harness dates and key counts in this file or a future test log.
- Keep `GatewayGateHarness` and `DispatcherNoSendHarness` in the offline merge gate.

## Known Commands

Build:

```powershell
dotnet build CareerSeeker.sln -c Release
```

Offline harnesses:

```powershell
dotnet run --project tests/Slice/Slice.csproj -c Release --no-build
dotnet run --project tests/EngineHarness/EngineHarness.csproj -c Release --no-build
dotnet run --project tests/ResearcherHarness/ResearcherHarness.csproj -c Release --no-build
dotnet run --project tests/HookHarness/HookHarness.csproj -c Release --no-build
dotnet run --project tests/StoreParityHarness/StoreParityHarness.csproj -c Release --no-build
dotnet run --project tests/GatewayGateHarness/GatewayGateHarness.csproj -c Release --no-build
dotnet run --project tests/DispatcherNoSendHarness/DispatcherNoSendHarness.csproj -c Release --no-build
dotnet run --project tests/LifecycleHarness/LifecycleHarness.csproj -c Release --no-build
```

Live harnesses:

```powershell
dotnet run --project tests/ScoutLiveHarness/ScoutLiveHarness.csproj -c Release --no-build
dotnet run --project tests/GmailLiveHarness/GmailLiveHarness.csproj -c Release --no-build -- --email you@gmail.com --client client_secret.json
```

## Current Git Facts

- Active cleanup branch: `agent/repo-cleanup`
- Draft PR: `https://github.com/ShivaClaw/careerseeker/pull/1`
- Main baseline for PR #1: `3fa65f5`
- Current branch SHA changes during cleanup; use `git rev-parse --short HEAD` for the exact value.
- Remote: `https://github.com/ShivaClaw/careerseeker.git`

Ignored local artifacts:

- `client_secret.json`
- `.appdata/`
- `.dotnet/`
- `.nuget/`
- `bin/`
- `obj/`

## Handoff Summary

CareerSeeker is now past three important proof points: real job ingestion, real Gmail draft creation, and restored SQLite source/parity coverage in the working tree. The architecture remains local-first and L1 compose-only. The immediate next engineering work should be BYOK LLM provider wiring, document rendering, and a composition root that turns a real discovered job into a real draft through the full safety path using SQLite.

Do not add hosted pipeline infrastructure. Do not expand Gmail scopes casually. Treat label management as deferred because live testing proved it does not fit `gmail.compose`-only L1.
