# CareerSeeker

Autonomous job-search engine: free Windows service (.exe) that discovers, verifies, tailors, and drafts
job applications, plus a paid Android dashboard. Spec: `docs/CareerSeeker-Spec.md` (authoritative,
v0.9). Sequencing: `docs/CareerSeeker-Integration-Windows-Roadmap.md`. Current handoff:
`docs/CareerSeeker-Project-Summary.md`. External audit quickstart: `docs/External-Audit-Handoff.md`.

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
  `EngineHarness` (57), `ResearcherHarness` (29), `HookHarness` (12), `StoreParityHarness` (17),
  `GatewayGateHarness` (29), `DispatcherNoSendHarness` (21), `LifecycleHarness` (37), and
  `RendererHarness` (6). Latest offline total: 236 assertions. Run each with
  `dotnet run -c Release`.
- `scripts/Verify-Alpha.ps1`: repeatable alpha verification entrypoint. It builds, runs the initializer dry run,
  source-mode SQLite demo smoke, and offline harness suite. Add `-IncludeLive` for local BYOK/Gmail checks,
  `-IncludePublish` for the win-x64 single-file publish smoke, `-IncludePackage` for the trusted-tester release
  ZIP, and `-IncludeResearch` for the live Brave/BYOK company-research smoke.
- `scripts/Package-AlphaRelease.ps1`: builds a self-contained trusted-tester ZIP with the alpha executable,
  native runtime dependencies, workspace initializer, dashboard helper scripts, quickstart, checksums, and
  selected docs; it does not package local databases, vaults, or generated artifacts.
- `scripts/Initialize-AlphaWorkspace.ps1`: creates ignored local alpha directories, a starter profile
  template, and a blank env-secrets placeholder, with an optional startup doctor run.
- `scripts/Start-AlphaDashboard.ps1`: Windows-friendly alpha dashboard launcher. Use `-Once` for a
  one-shot smoke check or `-Published -PublishIfMissing` to run the self-contained executable.
- `scripts/Manage-AlphaDashboardTask.ps1`: optional per-user Windows startup task helper for the alpha
  dashboard. Use `-Action Install -DryRun` to preview it before registering anything.
- `.github/workflows/ci.yml`: GitHub CI runs the Release warnings-as-errors build plus the same offline
  alpha verification script used locally.

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
