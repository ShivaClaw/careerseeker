# CareerSeeker

Autonomous job-search engine: free Windows service (.exe) that discovers, verifies, tailors, and drafts
job applications, plus a paid Android dashboard. Spec: `docs/CareerSeeker-Spec.md` (authoritative,
v0.9). Sequencing: `docs/CareerSeeker-Integration-Windows-Roadmap.md`. Current handoff:
`docs/CareerSeeker-Project-Summary.md`.

Trust/OAuth prep placeholders:
- `docs/Privacy-Policy.md`
- `docs/Support.md`
- `docs/Autonomy-Contract.md`

## Layout
- `src/`: 11 net8.0 projects. Leaves are Verifier, Scout, and Gateway; Store/Scorer build on Scout;
  Pipeline depends on Store, Scorer, and Verifier; Tailor and Dispatcher sit above Pipeline; Engine
  references Pipeline, Tailor, and Dispatcher. `TailorHookBridge` joins Tailor<->Researcher so neither
  core project references the other.
- `tests/`: plain-assertion harnesses (console, no xUnit): `Slice` (the L1 vertical slice, 12
  scenarios), `EngineHarness` (11), `ResearcherHarness` (21), `HookHarness` (10),
  `StoreParityHarness` (in-memory/SQLite parity), `GatewayGateHarness` (pinned Gate), and
  `DispatcherNoSendHarness` (L1 no-send guard). Run each with `dotnet run -c Release`.
  Per-module xUnit mirrors live in module `Tests/` folders of the original deliverables and run wherever
  NuGet is available.

## Build
`dotnet build CareerSeeker.sln -c Release`

Note: `src/Store/SeekerSvc.Store.csproj` includes `Microsoft.Data.Sqlite`, the only external
dependency in the tree. `nuget.config` restores it from nuget.org, so first-time builds require network
access or a warmed NuGet cache.

## Safety Invariants
- Fabrication Gate: no application state is reachable except through VERIFIED; unsupported claims block.
- Gateway pinned-Gate: `Stage.VerifierEntailment` is never throttled, never downgraded, fails closed.
- Dispatcher L1: the Gmail port has **no send method** (even though `gmail.compose` can authorize sends); body is the Gate-cleared cover letter verbatim.
- Researcher: dossier facts are grounded-or-dropped; signals are positive-only and deterministic.
- HookGuard: a cover-letter hook carrying any candidate-claim pattern is omitted, never risked.
- Scorer: `total = min(fit, legitimacy) * red_flags`; a scam can never outrank its worst axis.
- Store: hash-chained audit log.
