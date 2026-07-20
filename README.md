# CareerSeeker

Autonomous job-search engine: free Windows service (.exe) that discovers, verifies, tailors, and drafts
job applications, plus a paid Android dashboard. Spec: `docs/CareerSeeker-Spec.md` (authoritative,
v0.9). Sequencing: `docs/CareerSeeker-Integration-Windows-Roadmap.md`. Current handoff:
`docs/CareerSeeker-Project-Summary.md`. Trusted-tester walkthrough: `docs/Alpha-Tester-Walkthrough.md`.
External audit quickstart: `docs/External-Audit-Handoff.md`.

License notice: this repository currently has no open-source license; all rights are reserved unless a
future `LICENSE` file says otherwise.

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
  `EngineHarness` (66), `ResearcherHarness` (29), `HookHarness` (12), `StoreParityHarness` (17),
  `GatewayGateHarness` (34), `DispatcherNoSendHarness` (22), `LifecycleHarness` (37), and
  `RendererHarness` (6). Latest offline total: 251 assertions. Run each with
  `dotnet run -c Release`.
- `scripts/Verify-Alpha.ps1`: repeatable alpha verification entrypoint. It builds, runs the initializer dry run,
  source-mode SQLite demo smoke, and offline harness suite. Add `-IncludeLive` for local BYOK/Gmail checks,
  `-IncludePublish` for the win-x64 single-file publish smoke, `-IncludePackage` for the trusted-tester release
  ZIP, and `-IncludeResearch` for the live Brave/BYOK company-research smoke.
- `scripts/Package-AlphaRelease.ps1`: builds a self-contained trusted-tester ZIP with the alpha executable,
  native runtime dependencies, workspace initializer, dashboard/helper self-check scripts, quickstart, audit
  snapshot, tester walkthrough, release manifest, checksums, and selected docs; it does not package local
  databases, vaults, or generated artifacts.
- `scripts/Initialize-AlphaWorkspace.ps1`: creates ignored local alpha directories, a starter profile
  template, and a blank env-secrets placeholder, with an optional startup doctor run.
- `scripts/Start-AlphaDashboard.ps1`: Windows-friendly alpha dashboard launcher. Use `-Once` for a
  one-shot smoke check or `-Published -PublishIfMissing` to run the self-contained executable.
- `Setup-CareerSeeker-Alpha.cmd`: double-click local workspace setup helper copied into the trusted-tester
  release ZIP.
- `Import-CareerSeeker-Profile.cmd`: double-click local source-of-truth profile import helper copied into the
  trusted-tester release ZIP.
- `Connect-CareerSeeker-Providers.cmd`: double-click BYOK provider-key import and doctor helper copied into
  the trusted-tester release ZIP.
- `Connect-CareerSeeker-Gmail.cmd`: double-click Gmail OAuth helper copied into the trusted-tester release ZIP.
- `Check-CareerSeeker-LiveReadiness.cmd`: double-click live Gmail/BYOK readiness doctor copied into the
  trusted-tester release ZIP.
- `Clear-CareerSeeker-Providers.cmd`: double-click local provider-key vault clear helper copied into the
  trusted-tester release ZIP. It requires typing `CLEAR`.
- `Disconnect-CareerSeeker-Gmail.cmd`: double-click Gmail revoke/local token-vault clear helper copied into the
  trusted-tester release ZIP. It requires typing `DISCONNECT`.
- `Run-CareerSeeker-Demo.cmd`: double-click safe local demo cycle helper copied into the trusted-tester
  release ZIP.
- `Run-CareerSeeker-Scout.cmd`: double-click public ATS board ingest helper copied into the trusted-tester
  release ZIP.
- `Research-CareerSeeker-Company.cmd`: double-click Brave/BYOK company research helper copied into the
  trusted-tester release ZIP. It creates no Gmail draft.
- `Draft-CareerSeeker-Job.cmd`: double-click selected-job draft helper copied into the trusted-tester release
  ZIP.
- `Run-CareerSeeker-Live.cmd`: double-click live L1 helper copied into the trusted-tester release ZIP. It
  defaults to a no-Gmail dry-run preview and requires typing `LIVE` before creating a draft.
- `Export-CareerSeeker-Audit.cmd`: double-click hash-only audit JSON export helper copied into the
  trusted-tester release ZIP.
- `Export-CareerSeeker-Evidence.cmd`: double-click local evidence package helper copied into the trusted-tester
  release ZIP.
- `Import-CareerSeeker-Package.cmd`: double-click local evidence package restore helper copied into the
  trusted-tester release ZIP.
- `Verify-CareerSeeker-Alpha.cmd`: double-click release package self-check copied into the trusted-tester
  release ZIP.
- `Start-CareerSeeker-Alpha.cmd`: double-click dashboard launcher copied into the trusted-tester release ZIP.
- `Install-CareerSeeker-DashboardTask.cmd`: double-click per-user dashboard logon-task install helper copied
  into the trusted-tester release ZIP. It requires typing `INSTALL`.
- `Status-CareerSeeker-DashboardTask.cmd`: double-click dashboard logon-task status helper copied into the
  trusted-tester release ZIP.
- `Uninstall-CareerSeeker-DashboardTask.cmd`: double-click dashboard logon-task removal helper copied into the
  trusted-tester release ZIP. It requires typing `UNINSTALL`.
- `connect-gmail`: first-class alpha command that opens Gmail OAuth, stores the local DPAPI token, and
  preflights draft access without creating a draft.
- `scripts/Manage-AlphaDashboardTask.ps1`: optional per-user Windows startup task helper for the alpha
  dashboard. Use `-Action Install -DryRun` to preview it before registering anything.
- `.github/workflows/ci.yml`: GitHub CI runs the Release warnings-as-errors build plus the same offline
  alpha verification script used locally.

## Build
`dotnet build CareerSeeker.sln -c Release`

Note: `src/Store/SeekerSvc.Store.csproj` includes `Microsoft.Data.Sqlite`, the only external
dependency in the tree. `nuget.config` restores it from nuget.org, so first-time builds require network
access or a warmed NuGet cache.

For local BYOK/live research checks, keep provider keys in ignored `secrets/env.secrets`. The alpha commands
accept `ANTHROPIC_API_KEY`, `GEMINI_API_KEY` or `GOOGLE_API_KEY`, and Brave Search via
`BRAVE_SEARCH_API_KEY`, `BRAVE_SEARCH_API`, or `CAREERSEEKER_BRAVE_SEARCH_API_KEY`.

## Safety Invariants
- Fabrication Gate: no application state is reachable except through VERIFIED; unsupported claims block.
- Gateway pinned-Gate: `Stage.VerifierEntailment` is never throttled, never downgraded, fails closed.
- Dispatcher L1: the Gmail draft port exposes only `CreateDraftAsync`; label management is a separate
  capability and no send method exists in the L1 application, even though `gmail.compose` can authorize sends.
- Researcher: dossier facts are grounded-or-dropped; signals are positive-only and deterministic.
- HookGuard: a cover-letter hook carrying any candidate-claim pattern is omitted, never risked.
- Scorer: `total = min(fit, legitimacy) * red_flags`; a scam can never outrank its worst axis.
- Store: hash-chained audit log.
