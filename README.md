# CareerSeeker

Autonomous job-search engine: free Windows service (.exe) that discovers, verifies, tailors, and drafts
job applications, plus a paid Android dashboard. Spec: `docs/CareerSeeker-Spec.md` (authoritative,
v0.9). Sequencing: `docs/CareerSeeker-Integration-Windows-Roadmap.md`. Current handoff:
`docs/CareerSeeker-Project-Summary.md`.

Trust/OAuth docs:
- `docs/Privacy-Policy.md`
- `docs/Support.md`
- `docs/Autonomy-Contract.md`

## Layout
- `src/`: 11 net8.0 projects. Leaves are Verifier, Scout, and Gateway; Store/Scorer build on Scout;
  Pipeline depends on Store, Scorer, and Verifier; Tailor and Dispatcher sit above Pipeline; Engine
  references Pipeline, Tailor, Dispatcher, and Researcher for alpha composition commands. `TailorHookBridge`
  joins Tailor<->Researcher so neither core project references the other.
- `tests/`: plain-assertion harnesses (console, no xUnit): `Slice` (28 assertions),
  `EngineHarness` (40), `ResearcherHarness` (26), `HookHarness` (10), `StoreParityHarness` (17),
  `GatewayGateHarness` (29), `DispatcherNoSendHarness` (21), `LifecycleHarness` (37), and
  `RendererHarness` (6). Latest offline total: 214 assertions. Run each with
  `dotnet run -c Release`.

## Build
`dotnet build CareerSeeker.sln -c Release`

Note: `src/Store/SeekerSvc.Store.csproj` includes `Microsoft.Data.Sqlite`, the only external
dependency in the tree. `nuget.config` restores it from nuget.org, so first-time builds require network
access or a warmed NuGet cache.

## Safety Invariants
- Fabrication Gate: no application state is reachable except through VERIFIED; unsupported claims block.
- Gateway pinned-Gate: `Stage.VerifierEntailment` is never throttled, never downgraded, fails closed.
- Dispatcher L1: the Gmail draft port exposes only `CreateDraftAsync`; label management is a separate
  capability and no send method exists in the L1 application, even though `gmail.compose` can authorize sends.
- Researcher: dossier facts are grounded-or-dropped; signals are positive-only and deterministic.
- HookGuard: a cover-letter hook carrying any candidate-claim pattern is omitted, never risked.
- Scorer: `total = min(fit, legitimacy) * red_flags`; a scam can never outrank its worst axis.
- Store: hash-chained audit log.
