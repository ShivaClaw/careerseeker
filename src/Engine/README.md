# CareerSeeker — Engine host + L1 vertical slice (C# / .NET 8)

This covers the **Phase 1 capstone** (the whole brain proven to compose end-to-end) and the **Phase 2
shell** (the engine that runs it on a schedule with a local dashboard). BCL-only, so both are verified
offline; the production Windows Service and the real Scout/Gmail network paths drop in at integration.

## What got proven

### P1 — the brain composes (vertical slice)

`integration/VerticalSliceHarness.cs` wires the **real** modules — Store → Scorer → Pipeline →
Fabrication Gate → Tailor (via the LLM-Gateway bridge) → Dispatcher — and drives one job all the way
through. Three end-to-end behaviors, the load-bearing ones, hold *across* module seams:

| Path | Result |
|---|---|
| Clean, profile-supported application | `READY → DRAFTED`, a Gmail draft created, audit chain intact |
| Cover letter with an unsupported metric ("increased revenue 200%") | `BLOCKED_FABRICATION`, **zero drafts** — the Gate holds across the seam |
| Low-legitimacy "scam" posting | `REJECTED_BY_ENGINE`, never tailored, never drafted |

12 assertions, all green. This is the first time all modules run together rather than in isolation.

### Integration gap found and fixed

Assembling the solution surfaced a real break: `ApplicationPipeline` called `ClaimMapping.ToSourceClaims(…)`,
which existed nowhere — the adapter from the Store's persisted `ClaimRow` to the Verifier's `SourceClaim`
(the Gate's oracle) had never been written. `pipeline-fix/ClaimMapping.cs` implements it: string→enum
kind/confidence with safe defaults (`Other`, `Stated`), and a note that the Store schema doesn't yet
carry the structured numeric/employer/year fields (the Gate is strictly more conservative without them,
so this is an enhancement, not a correctness gap). Drop it into the Pipeline module.

### P2 — the engine shell

The host turns the slice into a continuously-running engine, with the **same wiring**, driven on a timer:

- **`EngineCycle`** — one discovery → decision → action pass over a batch: persist each posting, get the
  model sub-scores, run the Scorer, and admit to the Pipeline (which tailors, gates, and drafts). One bad
  posting increments an error counter instead of taking the cycle down. Tallies where each job rests.
- **`PeriodicScheduler`** — runs a tick immediately then every interval (`PeriodicTimer`); a throwing
  tick never kills the loop. Quartz swaps in for cron/misfire at integration.
- **`LocalDashboard`** — the free "nobody is ever blind" view (spec §4): an `HttpListener` on
  `localhost:7777` serving live counters as HTML at `/` and JSON at `/status`. Loopback only, no auth
  surface. The paid Android app is this same data remoted.
- **`EngineHost`** — composition root owning the counters, scheduler, and dashboard.

`Tests/EngineHarness.cs`: a real cycle over a mixed batch (healthy → drafted, scam → rejected,
fabrication → blocked, all counted correctly), the scheduler firing repeatedly and stopping cleanly on
dispose, and the dashboard served over real HTTP (`/status` JSON + `/` HTML). 11 assertions, all green.

## Alpha executable modes

- Demo, offline and repeatable:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- demo --once`
- Alpha Gmail smoke, live but one-shot:
  `dotnet run -c Release --project src/Engine/SeekerSvc.Engine.csproj -- alpha --email you@gmail.com --client secrets/google-oauth-client.json --vault .appdata/oauth/gmail-token.dpapi --db .appdata/careerseeker-alpha.db`

The alpha mode uses SQLite for engine state, the DPAPI token vault for Gmail OAuth, and the real
`GmailDraftClient`. It intentionally runs one cycle and creates one self-addressed L1 draft so early
testing cannot accidentally produce drafts on a timer.

## The two injected ports (production vs sandbox)

- **`IJobFeed`** — candidate postings. Production: the Scout over ATS feeds. Sandbox: a fixed batch.
- **`ISemanticScorer`** — the CV-match / growth sub-scores the Scorer needs. Production: the LLM Gateway's
  QuickScore/FullEvaluation stages. Sandbox: deterministic scores. Everything else the Scorer computes
  without a model.

## Verified status (this sandbox)

- All eight modules (Verifier, Scout, Store, Scorer, Pipeline, Tailor, Gateway, Dispatcher) plus the
  Engine compile together as one solution: `dotnet build` → 0 warnings, 0 errors.
- Vertical slice: **12/12**. Engine host: **11/11** (live HttpListener included).
- Store status: `SqliteSeekerStore` is included through `Microsoft.Data.Sqlite`, with a dedicated
  `StoreParityHarness` for in-memory/SQLite parity. Package restore requires NuGet access or a warmed cache.

## Not yet built (next)

- **Researcher / dossier** (P1 remainder) — feeds the Scorer's `FullEvaluation` and the cover letter's one
  researched company hook.
- **Real connector wiring** (P2 remainder) — the compile-verified Gmail and Scout clients exercised over
  the network; the Windows Service `Program.cs` (Microsoft.Extensions.Hosting) wrapping `EngineHost`.
- Onboarding, WinUI tray, OAuth/CASA — the integration-environment work that can't run in this sandbox.
