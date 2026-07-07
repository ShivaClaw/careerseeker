# CareerSeeker

Autonomous job-search engine: free Windows service (.exe) that discovers, verifies, tailors, and drafts
job applications, + paid Android dashboard. Spec: `docs/CareerSeeker-Spec.md` (authoritative, v0.9).
Sequencing: `docs/CareerSeeker-Integration-Windows-Roadmap.md`.

## Layout
- `src/` — 11 net8.0 projects. Dependency flow: Verifier/Scout/Gateway (leaves) ← Store/Scorer ←
  Pipeline ← Tailor/Dispatcher ← Engine; `TailorHookBridge` joins Tailor↔Researcher so neither
  references the other.
- `tests/` — offline plain-assertion harnesses (console, no xUnit — NuGet-free): `Slice` (the L1
  vertical slice, 12 scenarios), `EngineHarness` (11), `ResearcherHarness` (21), `HookHarness` (10).
  Run each with `dotnet run -c Release`. Per-module xUnit mirrors live in module `Tests/` folders of the
  original deliverables and run wherever NuGet is available.

## Build
`dotnet build CareerSeeker.sln -c Release`

Note: `nuget.config` clears package sources (offline sandbox origin). Once online, delete it and
re-include `src/Store/SqliteSeekerStore.cs` (excluded in `Store.csproj`) with a
`Microsoft.Data.Sqlite` PackageReference — the only external dependency in the tree.

## Safety invariants (enforced in code, asserted in tests — do not weaken)
- Fabrication Gate: no application state is reachable except through VERIFIED; unsupported claims block.
- Gateway pinned-Gate: `Stage.VerifierEntailment` is never throttled, never downgraded, fails closed.
- Dispatcher L1: the Gmail port has **no send method**; body is the Gate-cleared cover letter verbatim.
- Researcher: dossier facts are grounded-or-dropped; signals are positive-only and deterministic.
- HookGuard: a cover-letter hook carrying any candidate-claim pattern is omitted, never risked.
- Scorer: `total = min(fit, legitimacy) · red_flags` — a scam can never outrank its worst axis.
- Store: hash-chained audit log.
