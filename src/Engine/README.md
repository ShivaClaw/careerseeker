# CareerSeeker Engine Host

`src/Engine` is the runnable composition root for the local-first CareerSeeker Windows Beta. It hosts the
real scheduled engine, loopback dashboard, browser onboarding, one-shot diagnostic modes, and the bounded
legacy Alpha Gmail smoke. The L1 Dispatcher can create Gmail drafts but has no send implementation.

## Production path

`run` is the only mode that continuously discovers real jobs:

```powershell
dotnet run -c Release --project src\Engine\SeekerSvc.Engine.csproj -- `
  run --db .appdata\careerseeker-alpha.db --llm byok
```

Important switches:

- `--dry-run`: discovery, persistence, telemetry, and ranking only; no application or simulated draft.
- `--once`: one bounded sweep.
- `--board greenhouse:<handle>`, `lever:<handle>`, or `ashby:<handle>`: choose public ATS boards.
- `--max-drafts-per-cycle <n>`: cap acted postings; the default is 10.
- `--service-host`: stay alive without console input and honor local control files.

Implicit activation from the installed MSIX is stricter. Before onboarding it opens `setup`; afterward it
starts `run` in both discovery-only and service-host modes. Installation or startup enablement never counts
as consent to create a Gmail draft.

The default `lexical-v2` ranker is deterministic and local. It compares the active source profile with
untrusted posting text using job-side coverage, so unrelated additions to a richer profile cannot lower
the same posting. It emphasizes title and Skill/Title overlap, persists CV match and other score
components, and records a matched-term rationale. `/jobs` orders scored rows by total and shows those
components.

## Browser onboarding

`setup` opens a loopback-only ten-step local browser flow:

1. welcome and the local draft-only/no-send boundary;
2. package identity/checksum verification;
3. bounded PDF/DOCX/TXT/Markdown resume selection and local extraction;
4. provider selection and key test;
5. explicit consent before resume text reaches that provider;
6. claim-by-claim accept/edit/drop review with source/evidence and visible `stated` cap;
7. Gmail consent and installed/Desktop OAuth-client validation;
8. final doctor;
9. discovery-only first-run choice;
10. completion.

Provider keys are stored in the per-user DPAPI vault and never printed. A failed test preserves an existing
vault. Resume and posting content are encoded as untrusted data. Setup creates no Gmail draft.

The previous console flow remains available as:

```powershell
dotnet run -c Release --project src\Engine\SeekerSvc.Engine.csproj -- setup --console
```

## Package runtime

`PackagedRuntime` detects real Windows package identity through `GetCurrentPackageFullName`; an unpacked
executable cannot claim package identity through a flag. Package activation changes the working directory
to `%LOCALAPPDATA%\CareerSeeker`, where the existing relative paths create:

- `.appdata\careerseeker-alpha.db`
- `.appdata\artifacts`
- `.appdata\job-descriptions`
- `.appdata\oauth\gmail-token.dpapi`
- `.appdata\secrets\byok-keys.dpapi`
- `.appdata\onboarding.completed`
- `output`

The public installed/Desktop OAuth client metadata is copied once from immutable package resources and never
overwrites an existing user copy. Normal MSIX removal does not delete this external workspace.

Build and non-installing package verification:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1 -IncludePublish -IncludePackage
```

This produces `output\release\CareerSeeker-beta-win-x64.msix`, unpacks it with the locked Microsoft SDK
tool, asserts exactly one `CareerSeeker.exe`, and runs the no-console onboarding smoke with zero provider
and Gmail calls. Signing and real installation are separate human actions documented in
`docs/Beta-Windows-Package-Runbook.md`.

## Crash recovery and continuous host

Before startup work and before every scheduled discovery tick, the engine reconciles applications stranded
in `READY` or `SUBMITTING`:

- recorded `SUCCEEDED` draft: commit `READY -> DRAFTED` without another Gmail call;
- recorded `SUCCEEDED` submission: commit the local settled states without another external effect;
- recorded `FAILED`: leave retry available;
- `PENDING`/unknown outcome: preserve state and record one durable manual-review event.

A store-level reconciliation failure aborts the cycle. A malformed individual row is isolated and counted
so other recoverable rows can still be inspected.

The shipped service-grade fallback is a per-user Scheduled Task supervised by
`scripts/Start-BetaEngineHost.ps1`. It supplies:

- capped crash-restart and cycle-error backoff;
- database-scoped single-instance lock;
- per-user local logs;
- control-file pause, resume, stop, and status;
- honest scheduler state on the loopback dashboard.

Task registration is always an explicit human action. Native SCM Windows Service and tray UI are not built.

## Modes

| Mode | Purpose | External-effect boundary |
|---|---|---|
| `setup` | Browser onboarding | Provider/Gmail only after separate consent; no draft |
| `setup --console` | Advanced fallback wizard | Same safety boundaries |
| `run` | Real scheduled discovery and drafting engine | Drafts only when explicitly configured; no send |
| `run --dry-run` | Real discovery/ranking/telemetry | No draft |
| `dashboard` | Viewer/controls over stored state | No engine attached unless another host is running |
| `demo` | Invented deterministic postings | Local fake draft artifact only |
| `scout-boards` | One public ATS ingest | No Gmail |
| `draft-job` | Selected stored-job package/draft | `--dry-run` avoids Gmail |
| `research-company` | Brave/BYOK grounded dossier | No Gmail |
| `doctor` | Local readiness checks | No draft/provider completion |
| `connect-gmail` | OAuth plus draft-access preflight | No draft |
| `disconnect-gmail` | Revoke token and delete local vault | Explicit local/account action |
| `import-byok` / `clear-byok` | Manage local DPAPI provider vault | Never prints key values |
| `export-audit` | Hash-only audit export by default | Payloads require opt-in |
| `export-alpha-package` / `import-alpha-package` | Local evidence ZIP | Separate from the app installer |
| `alpha` | Historical bounded live smoke | One explicit self-addressed draft |

## Injected ports

- `IJobFeed`: `ScoutJobFeed` is the real identified public-board implementation. Injection-signaled postings
  are stored as evidence and quarantined before scorer/model/action work.
- `ISemanticScorer`: Beta uses local `LexicalSemanticScorer`; fixtures may inject fixed values.
- `IDocumentRenderer`: deterministic ATS-clean PDF renderer.
- `IGmailDraftClient`: draft-only port. Label management is a separate capability.

## Verified evidence

Current pinned offline results:

| Harness | Assertions |
|---|---:|
| Slice | 28 |
| EngineHarness | 164 |
| ResearcherHarness | 57 |
| HookHarness | 16 |
| StoreParityHarness | 25 |
| GatewayGateHarness | 36 |
| DispatcherNoSendHarness | 35 |
| LifecycleHarness | 45 |
| RendererHarness | 6 |
| **Total** | **412** |

The engine-specific assertions cover browser onboarding, crash reconciliation, SQLite composition,
single-instance protection, scheduler pause/resume/backoff, honest dashboard state, identified Scout feeds,
quarantine-before-action, action-cap advancement, no-redraft idempotency, discovery-only behavior,
deterministic ranking, loopback/CSRF/security headers, audit/evidence exports, safe evidence ZIP
import/export, profile import, doctor, pinned Gate, and Dispatcher no-send.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Verify-Alpha.ps1
```

## Deliberate gaps

- MSIX production signature / SmartScreen reputation.
- Real installer registration, uninstall UI, Startup Apps UI, and reboot verification on a disposable tester
  machine.
- OAuth production verification and the Google-directed CASA assessment.
- Native Windows Service, tray, and in-app startup toggle.
- Public-site deployment of the repository truth-copy changes.
- Any higher autonomy, Gmail send/modify/read scope, calendar access, or ATS submission.
