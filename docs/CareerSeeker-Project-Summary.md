# CareerSeeker Project Summary

Updated: 2026-07-19
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

Overall status: technical Windows alpha path implemented; SQLite source restoration, SQLite-backed demo/alpha executable paths, Gmail disconnect, dashboard disconnect control, Gmail API preflight, BYOK alpha wiring with DPAPI provider-key import, full BYOK alpha Gmail/PDF drafting, ATS-clean PDF rendering, Brave web-research adapter source, and parity coverage verified.

Completed:

- B1 live Scout ATS ingestion verified against real Greenhouse, Lever, and Ashby APIs.
- B5 Gmail draft client verified with a real Google OAuth token and a real Gmail draft in the test account.
- OAuth token storage works through a local DPAPI-backed token vault.
- Gmail disconnect can revoke the OAuth token and delete the local DPAPI vault via the alpha executable.
- The localhost dashboard can expose a token-protected Gmail disconnect control wired to the same revoke/delete path.
- The localhost dashboard exposes recent application state, scores, draft refs, generated resume/cover document links, and safe job/apply links at `/applications`.
- The localhost dashboard exposes audit-chain status and recent audit event metadata at `/evidence`.
- The alpha executable can export a local audit JSON package; raw payloads are opt-in.
- OAuth client JSON handling is ignored by Git via `client_secret*.json`.
- Gmail live smoke and alpha mode preflight the Gmail drafts API before creating a draft.
- Alpha mode can run Tailor and Gate through real BYOK Anthropic/Gemini providers with `--llm byok`.
- BYOK provider keys can be imported from environment/env.secrets into a local DPAPI vault.
- Live BYOK provider smoke passes for Anthropic, Gemini, Tailor, Gate entailment, and Gateway accounting.
- Demo mode can run the dashboard/cycle shell against a persistent SQLite database with exportable audit evidence and local draft artifacts.
- Bounded alpha `--llm byok --fast-smoke` passes live Gate preflight, live Tailor smoke, Gmail draft creation, PDF attachment packaging, and SQLite audit in one routine command.
- Alpha BYOK Gate verification defaults to top-3 semantic source candidates per tailored claim to bound live entailment calls while still failing closed.
- Unconstrained alpha `--llm byok` passes with live Tailor, live Gate, Gmail draft creation, PDF attachment packaging, and SQLite audit.
- Dispatcher has a real deterministic ATS-clean PDF renderer for resume attachments, with optional cover PDFs.
- Researcher has a real Brave Search adapter that fetches public result pages before docs can ground dossier facts.
- Engine has a `research-company` alpha command that composes Brave Search, BYOK Gateway dossier modeling, and the grounding filter when a Brave key is available.
- L1 compose-only correction is in place: custom Gmail labels are skipped by default because label management requires broader Gmail scope than `gmail.compose`.
- SQLite provider source is restored to the Store project and covered by `StoreParityHarness`, including the recent-application and artifact-metadata read model.
- Gateway pinned-Gate and Dispatcher no-send invariants now have named offline harnesses.
- Gate outages now fail closed into `GATE_UNAVAILABLE` instead of being mislabeled as fabrication.

Not complete yet:

- Live real-company Researcher verification with a Brave Search key.
- Headless Chromium/HTML document renderer polish beyond the current ATS-clean text PDF renderer.
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
| LLM providers | Full alpha BYOK path verified | `--llm byok` reads local DPAPI/env/env.secrets keys and registers Anthropic/Gemini providers for Tailor and Gate; BYOK alpha defaults to top-3 Gate semantic candidates per claim; `--fast-smoke` remains a cheaper routine validator. |
| Research web | Fake/offline only | Planned search API plus web fetch. |
| Document renderer | Offline verified | Deterministic single-column ATS-clean PDF renderer writes selectable resume text and attaches PDFs to drafts; Chromium/HTML polish remains future work. |
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

Total: 201 passed, 0 failed.

| Harness | Result |
| --- | --- |
| `Slice` | 28 passed, 0 failed |
| `EngineHarness` | 30 passed, 0 failed |
| `ResearcherHarness` | 26 passed, 0 failed |
| `HookHarness` | 10 passed, 0 failed |
| `StoreParityHarness` | 14 passed, 0 failed |
| `GatewayGateHarness` | 29 passed, 0 failed |
| `DispatcherNoSendHarness` | 21 passed, 0 failed |
| `LifecycleHarness` | 37 passed, 0 failed |
| `RendererHarness` | 6 passed, 0 failed |

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

- Result: 5 passed, 0 failed.
- Scope: `gmail.compose`
- Send method present: false
- Token: access token available from DPAPI vault
- Gmail drafts API preflight: reachable
- Labels: skipped under compose-only L1
- Real draft created: true
- Draft ID: redacted real draft ID

Live BYOK harness, 2026-07-19:

- Result: 7 passed, 0 failed.
- Provider-key source: local DPAPI vault imported from `secrets/env.secrets`.
- Anthropic direct completion returned text and usage.
- Gemini direct completion returned text and usage.
- Gateway Tailor live call returned a parseable draft.
- Gateway Gate live entailment returned a supported verdict.
- Gateway accounting recorded Gate spend.
- Gateway Tailor live draft passed bounded Gate verification.

Alpha Gmail/PDF smoke, 2026-07-19:

- Result: passed with fake inference.
- Gmail OAuth token available from DPAPI vault.
- Gmail drafts API preflight reachable.
- Gmail profile lookup available, so `--email` is optional for this path.
- One self-addressed L1 Gmail draft created.
- ATS-clean resume PDF attached by the real document renderer.
- SQLite audit chain verified.

Bounded BYOK alpha smoke, 2026-07-19:

- Result: passed with `--llm byok --fast-smoke`.
- Gmail OAuth token available from DPAPI vault.
- Gmail drafts API preflight reachable.
- Gmail profile lookup available, so `--email` is optional for this path.
- BYOK provider keys loaded from local DPAPI vault.
- Live Gate preflight returned supported entailment through the pinned VerifierEntailment stage.
- Live Tailor smoke returned an exact source-backed draft through Anthropic `claude-sonnet-4-6`.
- One self-addressed L1 Gmail draft created with the real PDF attachment path.
- SQLite audit chain verified.

Unconstrained BYOK alpha smoke, 2026-07-19:

- Result: passed with `--llm byok`.
- Gmail OAuth token available from DPAPI vault.
- Gmail drafts API preflight reachable.
- BYOK provider keys loaded from local DPAPI vault.
- Live Tailor produced a Gate-supported alpha draft after conservative prompt hardening.
- Live Gate verified the generated tailored claims with top-3 semantic source candidates per claim.
- One self-addressed L1 Gmail draft created with the real PDF attachment path.
- SQLite audit chain verified.

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
- Gmail disconnect added to revoke refresh tokens and delete local DPAPI token material.
- Local dashboard Gmail disconnect control added with per-process form token plus loopback, Host, Origin, and Referer checks.
- Local dashboard evidence endpoint added; `/evidence` verifies the audit chain and returns recent event metadata without payload bodies.
- Local `export-audit` command added; default export includes audit-chain status, event metadata, hashes, and payload lengths, while raw payloads require `--include-payloads`.
- Brave Search web-research adapter added; it uses search results only to select URLs, fetches public result pages, strips HTML/script noise, skips localhost/private/non-text results, and leaves final trust to the grounding filter.
- `research-company` command added for live Brave + BYOK dossier runs when `BRAVE_SEARCH_API_KEY` is available.
- Gmail draft API preflight added before live draft creation.
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
- BYOK alpha wiring added for real Anthropic/Gemini Tailor and Gate calls from local environment or `env.secrets` keys.
- BYOK provider-key import/clear commands added for a local DPAPI vault; alpha `--llm byok` prefers the vault.
- Bounded `alpha --llm byok --fast-smoke` added for routine live Tailor + Gate + Gmail + PDF validation.
- Gate verification options added so callers can bound semantic source-candidate checks; alpha BYOK defaults to 3 candidates per claim and `0` keeps exhaustive comparison.
- Content token normalization now trims sentence-ending dots so exact source-backed claims do not fail only because rendered prose includes terminal punctuation.
- Claim mapping now parses structured percent/money values from persisted metric text so exact quantified profile claims can satisfy the Gate.
- Gate failure audit events include local violation samples to make blocked alpha runs diagnosable without sending data off-device.
- Tailor prompt hardened toward conservative source-backed wording; unconstrained BYOK alpha draft creation now passes live Gate.
- ATS-clean PDF renderer added and wired into alpha draft packaging; sample PDFs render and text extracts cleanly.

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
  - Direct Anthropic, Gemini, Tailor, Gate entailment, and Gateway accounting smoke is verified.
  - Bounded alpha `--llm byok --fast-smoke` is verified for routine Gmail + PDF + BYOK validation.
  - Unconstrained alpha `--llm byok` is verified for live Gmail + PDF + Tailor + Gate.
  - Gate semantic candidate minimization is implemented for alpha BYOK.
  - Confirm StrongCloud failover under real provider outage conditions.
- B3 real Researcher:
  - Implement `IWebResearch` with search API plus web fetch.
  - Verify grounding drops unsupported facts on real pages.
- B4 document renderer:
  - Keep the current ATS-clean text PDF renderer for alpha.
  - Add headless Chromium/Playwright when HTML template polish is needed.

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
- Broader dashboard polish beyond the current local status, evidence, application, and document-link views.
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
- DPAPI vault now supports local deletion and Gmail token revocation; future product should add migration/export policy and perhaps optional entropy.
- The executable demo and alpha paths are wired to SQLite; no Windows service/tray composition root exists yet.
- OAuth production verification and CASA remain long-lead launch blockers.
- Current PDF renderer is ATS-clean text; not yet a polished HTML/Chromium resume template.
- Live real-company web research has not been run yet because no Brave Search key is present locally.
- No Windows service/tray composition root yet.
- Live ATS feeds are volatile; some configured boards can be empty while still reachable.

## Recommendations For Next Agents

Highest priority:

- Live-verify `research-company` with a real Brave Search key so company hooks and legitimacy signals can graduate from source-present to live-proven.
- Live-verify dashboard document links in an interactive hosted demo session before wider tester distribution.

Near-term connector work:

- B2 BYOK provider wiring:
  - Keep provider keys in the local DPAPI vault after import.
  - Keep `ByokLiveHarness` green for Anthropic, Gemini, Tailor, Gate, and accounting.
  - Confirm StrongCloud failover order remains `claude-sonnet-4-6 -> gemini-3.1-pro-preview`.
  - Verify Gate fails closed on provider outage and records `GATE_UNAVAILABLE`, not fabrication.
- B4 document rendering:
  - Add Playwright/Chromium renderer when visual template polish matters.
  - Keep the current renderer's selectable text and no-new-claims behavior.
- SQLite:
  - Keep executable demo/alpha SQLite composition covered as dashboard and service hosting evolve.
  - Keep package restore available in CI or a warmed NuGet cache.

Product recommendations:

- Keep L1 free, local-first, and reviewable.
- Treat the first public promise as "real drafts, zero sends."
- Make privacy copy concrete: resume and OAuth tokens never leave the device.
- Live-verify dashboard document links in an interactive hosted demo session before wider tester distribution.
- Add import/migration packaging around the audit export when the workspace export story broadens.

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
dotnet run --project tests/RendererHarness/RendererHarness.csproj -c Release --no-build
```

Live harnesses:

```powershell
dotnet run --project tests/ScoutLiveHarness/ScoutLiveHarness.csproj -c Release --no-build
dotnet run --project tests/GmailLiveHarness/GmailLiveHarness.csproj -c Release --no-build -- --email you@gmail.com --client client_secret.json
dotnet run --project src/Engine/SeekerSvc.Engine.csproj -c Release --no-build -- import-byok --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi
dotnet run --project tests/ByokLiveHarness/ByokLiveHarness.csproj -c Release --no-build -- --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi
dotnet run --project src/Engine/SeekerSvc.Engine.csproj -c Release --no-build -- alpha --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db
dotnet run --project src/Engine/SeekerSvc.Engine.csproj -c Release --no-build -- alpha --llm byok --fast-smoke --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db
dotnet run --project src/Engine/SeekerSvc.Engine.csproj -c Release --no-build -- alpha --llm byok --gate-semantic-candidates 3 --secrets secrets/env.secrets --key-vault .appdata/secrets/byok-keys.dpapi --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db
dotnet run --project src/Engine/SeekerSvc.Engine.csproj -c Release --no-build -- disconnect-gmail --vault .appdata/oauth/gmail-token.dpapi
dotnet run --project src/Engine/SeekerSvc.Engine.csproj -c Release --no-build -- clear-byok --key-vault .appdata/secrets/byok-keys.dpapi
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

CareerSeeker is now past twelve important proof points: real job ingestion, real Gmail draft creation, restored SQLite source/parity coverage, SQLite-backed executable demo/alpha composition, local draft artifact persistence, live BYOK provider calls, local DPAPI provider-key import, bounded BYOK alpha validation, full BYOK alpha Gmail/PDF drafting, real ATS-clean PDF draft attachments, a dashboard-accessible Gmail disconnect control, and offline-verified real web-research adapter code. The architecture remains local-first and L1 compose-only. The immediate next engineering work should be live Brave-key verification for `research-company` and then broader local evidence surfaces.

Do not add hosted pipeline infrastructure. Do not expand Gmail scopes casually. Treat label management as deferred because live testing proved it does not fit `gmail.compose`-only L1.
