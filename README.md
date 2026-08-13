# CareerSeeker

CareerSeeker is a local-first Windows L1 Drafts beta. It discovers jobs from public ATS boards, ranks
them against a local source-of-truth profile, researches employers, tailors materials, verifies generated
claims, and can create reviewable Gmail drafts. The L1 application contains no email-send or ATS-submit
implementation.

The Windows engine is implemented and packaged for closed-beta testing. Public distribution is not ready:
the MSIX is unsigned, OAuth production verification/CASA are pending, and the native tray/Windows Service
shell is not built. The shipped service-grade fallback is a hardened per-user Scheduled Task host.

Authoritative product constraints live in [docs/CareerSeeker-Spec.md](docs/CareerSeeker-Spec.md). Current
implementation evidence lives in
[docs/CareerSeeker-Project-Summary.md](docs/CareerSeeker-Project-Summary.md),
[docs/External-Audit-Handoff.md](docs/External-Audit-Handoff.md), and
[docs/BETA-AUDIT-REQUEST.md](docs/BETA-AUDIT-REQUEST.md).

This repository has no open-source license; all rights are reserved unless a future `LICENSE` says otherwise.

## Current tester path

The Beta package command produces one unsigned `win-x64` MSIX containing exactly one executable:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage
```

Artifact: `output\release\CareerSeeker-beta-win-x64.msix`.

The MSIX manifest provides the Start-menu application and an optional startup task that is disabled by default.
A package-identity launch uses `%LOCALAPPDATA%\CareerSeeker` for mutable state, so normal app
removal does not delete the database, generated materials, or DPAPI vaults. The first launch opens the
ten-step loopback browser onboarding flow. After onboarding, implicit package activation is discovery-only;
drafting still requires an explicit configured run.

The built-in `delete-all-data` mode is separate from package removal. Its first invocation only prints the
resolved `%LOCALAPPDATA%\CareerSeeker` target and a path-bound confirmation phrase; deletion occurs only
when that exact phrase is supplied on a later invocation, and the command reports whether the target was
removed or was already absent.

The previous console onboarding remains available as `setup --console`. Alpha `.cmd` helpers and the old
`Package-AlphaRelease.ps1` ZIP builder remain as source-level historical/advanced utilities, but they are not
the artifact produced by `-IncludePackage`.

See [docs/Beta-Windows-Package-Runbook.md](docs/Beta-Windows-Package-Runbook.md) for unsigned testing,
signing, and uninstall boundaries.

## Repository layout

- `src/`: 11 .NET 8 production projects.
- `tests/`: plain console assertion harnesses; no xUnit dependency.
- `scripts/Verify-Alpha.ps1`: the pinned verification entrypoint. The historical filename remains for
  compatibility; it verifies the current Beta tree.
- `scripts/Package-BetaRelease.ps1`: locked Microsoft-SDK MSIX builder.
- `scripts/Test-BetaReleasePackage.ps1`: non-installing manifest, payload, onboarding, and data-preservation
  self-check.
- `scripts/Start-BetaEngineHost.ps1` and `scripts/Manage-AlphaDashboardTask.ps1`: supervised per-user
  Scheduled Task fallback with restart backoff, local controls/logs, and a database-scoped single-instance
  rail.
- `docs-site/`: canonical repository copy for public privacy, support, and autonomy text. Deployment is a
  separate human action.

## Build and verify

```powershell
dotnet build CareerSeeker.sln -c Release
powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1
```

The current pinned breakdown is:

| Harness | Assertions |
|---|---:|
| Slice | 28 |
| EngineHarness | 228 |
| ResearcherHarness | 57 |
| HookHarness | 16 |
| StoreParityHarness | 28 |
| GatewayGateHarness | 36 |
| DispatcherNoSendHarness | 35 |
| LifecycleHarness | 45 |
| RendererHarness | 6 |
| SyncHarness | 130 |
| **Total** | **609** |

CI runs the warnings-as-errors Release build and the same offline verifier. Optional live switches use
already-configured local credentials and are not part of the default gate:

- `-IncludeLive`: bounded BYOK/Gmail draft smoke.
- `-IncludeResearch`: bounded Brave/BYOK company research.
- `-IncludePublish`: self-contained `win-x64` executable smoke.
- `-IncludePackage`: the Beta MSIX build and non-installing self-check.

Never put keys, OAuth client files, resumes, `.appdata`, or `output` in source control. Current package
dependencies are locked; audit them with:

```powershell
dotnet list CareerSeeker.sln package --vulnerable --include-transitive
dotnet list tools\WindowsSdkTools\WindowsSdkTools.csproj package --vulnerable --include-transitive
```

## Engine behavior

The production engine command is `run`. It performs real public-board discovery on a timer, stores and
ranks jobs, quarantines injection-signaled postings before model work, reconciles crash-window state, and
can draft only when Gmail and BYOK are explicitly configured.

Useful safe commands:

```powershell
# One discovery-only sweep: no provider or Gmail draft.
dotnet run -c Release --project src\Engine\SeekerSvc.Engine.csproj -- run --once --dry-run --llm fake

# Read-only dashboard over an existing local database.
dotnet run -c Release --project src\Engine\SeekerSvc.Engine.csproj -- dashboard --db .appdata\careerseeker-alpha.db

# Local doctor. Add --require-gmail / --require-byok only when those resources are intentionally configured.
dotnet run -c Release --project src\Engine\SeekerSvc.Engine.csproj -- doctor
```

The persistent engine reconciles recorded external-effect outcomes at startup and before every discovery
cycle. A recorded provider success completes the missing local transition without repeating the external
effect. An unknown pending outcome is left for manual review.

Ranking defaults to deterministic local `lexical-v2`. It measures job-side term coverage so adding
unrelated verified profile evidence cannot lower a posting's score, weights title and Skill/Title overlap,
persists the components and matched-term rationale, and orders `/jobs` by the composed score. It does not
require an inference provider.

## Safety invariants

- **Fabrication Gate:** unsupported generated claims block before draft creation.
- **Pinned Gate:** `Stage.VerifierEntailment` stays StrongCloud, is never budget-throttled or downgraded, and
  fails closed when unavailable.
- **No send:** Dispatcher exposes draft creation only; `SubmitAsync` throws. `gmail.compose` itself is
  send-capable, so this is an application-code guarantee, not a token limitation.
- **Local first:** databases, generated documents, OAuth tokens, and provider keys are local. External
  provider data transfer occurs only for explicitly connected services and disclosed prompts.
- **Untrusted-data quarantine:** job postings, resumes, and retrieved web content are data, never commands.
- **Grounded research:** ungrounded dossier facts are dropped.
- **Idempotent recovery:** persisted effect attempts prevent a successful Gmail draft from being repeated
  after a crash-window lost commit.
- **Honest status:** a viewer never claims the engine is running; starting, running, paused, faulted, and
  stopped states come from the attached scheduler.
- **No implicit draft consent:** packaged automatic activation is discovery-only.

Trust and control documents:

- [Privacy Policy](docs/Privacy-Policy.md)
- [Support](docs/Support.md)
- [Autonomy Contract](docs/Autonomy-Contract.md)
