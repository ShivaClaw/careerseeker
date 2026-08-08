# CareerSeeker Public Claims Register

Updated: 2026-08-07
Status: Beta skeleton for Brandon review before any public-copy deployment

This register maps material public sentences to the invariant, harness, and source line that supports them.
It is deliberately conservative. `PROVEN` means the repository contains executed harness evidence.
`POLICY` means it is a declared operating/data-practice commitment that code alone cannot prove.
`**UNPROVEN**` means the sentence must not be promoted as a verified product result until the listed evidence
exists.

Line references were refreshed for the R5 source snapshot based on `5661342` and should be refreshed whenever
the supporting file changes.

## Product and autonomy claims

| ID | Public sentence | Status | Invariant / executed evidence | Proof source |
|---|---|---|---|---|
| P01 | “CareerSeeker is a local-first Windows L1 Drafts beta.” | PROVEN | Package runtime and default paths are per-user local; external services are optional. | `src/Engine/PackagedRuntime.cs:19`, `src/Engine/Program.cs:279` |
| P02 | “CareerSeeker discovers jobs from public ATS boards.” | PROVEN | Identified Scout feed fixtures plus bounded public Greenhouse/Lever/Ashby runs. | `src/Engine/ScoutJobFeed.cs:56`, `tests/EngineHarness/Program.cs:763` |
| P03 | “CareerSeeker ranks jobs against your local source-of-truth profile.” | PROVEN | Job-side lexical coverage is non-decreasing across nested 10/50/200-term profiles; 120-posting calibration, explanation, persistence, and dashboard assertions pass. | `src/Engine/LexicalSemanticScorer.cs:74`, `tests/EngineHarness/Program.cs:1022`, `docs/Scoring-Calibration.md` |
| P04 | “CareerSeeker researches employers from public sources.” | PROVEN | Brave adapter and grounded dossier harness; historical live GitLab evidence. | `src/Researcher/Researcher.cs:73`, `tests/ResearcherHarness/Program.cs:35` |
| P05 | “CareerSeeker prepares tailored application materials.” | PROVEN | Tailor-to-Gate vertical slice and ATS PDF renderer harnesses. | `tests/Slice/Program.cs:231`, `tests/RendererHarness/Program.cs:57` |
| P06 | “CareerSeeker creates reviewable Gmail drafts.” | PROVEN | Draft-only interface/harness and historical Gmail live smoke. | `src/Dispatcher/Dispatch.cs:73`, `tests/DispatcherNoSendHarness/Program.cs:50` |
| P07 | “CareerSeeker does not send email.” | PROVEN | No public send-capable Dispatcher method; L1 submit throws. | `src/Dispatcher/Dispatcher.cs:83`, `tests/DispatcherNoSendHarness/Program.cs:38` |
| P08 | “CareerSeeker does not submit applications.” | PROVEN | Pipeline submission port reaches an intentionally unsupported L1 Dispatcher method. | `src/Dispatcher/Dispatcher.cs:83`, `tests/DispatcherNoSendHarness/Program.cs:69` |
| P09 | “You review. You send. CareerSeeker prepares.” | PROVEN | L1 has draft creation but no send/submit implementation. | `tests/DispatcherNoSendHarness/Program.cs:38`, `tests/DispatcherNoSendHarness/Program.cs:49` |
| P10 | “No higher autonomy level is activated by default.” | PROVEN | No L2/L3 dispatch implementation; package default is discovery-only. | `src/Engine/Program.cs:279`, `src/Dispatcher/Dispatcher.cs:83` |

## Truth and safety claims

| ID | Public sentence | Status | Invariant / executed evidence | Proof source |
|---|---|---|---|---|
| S01 | “Unsupported generated claims are blocked before draft creation.” | PROVEN | Fabrication fixture ends `BLOCKED_FABRICATION` with zero draft. | `src/Pipeline/ApplicationPipeline.cs:105`, `tests/Slice/Program.cs:284` |
| S02 | “If verification is unavailable, the application is deferred and no draft is created.” | PROVEN | Matcher-outage fixture ends `GATE_UNAVAILABLE`, zero draft. | `src/Pipeline/ApplicationPipeline.cs:129`, `tests/Slice/Program.cs:303` |
| S03 | “The Fabrication Gate is never budget-throttled or downgraded.” | PROVEN | Pinned tier and 100%/105%/500% budget harness assertions. | `src/Gateway/Stages.cs:68`, `tests/GatewayGateHarness/Program.cs:79` |
| S04 | “Ungrounded employer facts are dropped.” | PROVEN | Retrieved-URL/text support filter plus grounding fixtures. | `src/Researcher/Researcher.cs:73`, `tests/ResearcherHarness/Program.cs:36` |
| S05 | “Prompt-injection signals quarantine a posting before model/action work.” | PROVEN | Quarantine branch precedes scorer/cap work; identified-feed harness. | `src/Engine/EngineCore.cs:240`, `tests/EngineHarness/Program.cs:782` |
| S06 | “Job postings, resumes, and web pages are treated as data, not instructions.” | PROVEN | Prompt quarantine encoding across Tailor, onboarding, Researcher, and verifier; adversarial fixtures. | `src/Tailor/GatewayTailorModel.cs:83`, `src/Engine/BetaSetupWebFlow.cs:1009` |
| S07 | “A successful draft is not repeated after a crash-window lost commit.” | PROVEN | Fresh-process and scheduled-tick recovery assertions. | `src/Pipeline/ApplicationPipeline.cs:288`, `tests/EngineHarness/Program.cs:598`, `tests/EngineHarness/Program.cs:637` |
| S08 | “Dashboard status does not claim a viewer is a running engine.” | PROVEN | Viewer-only string and state-transition assertions. | `src/Engine/Host.cs:274`, `tests/EngineHarness/Program.cs:657` |

## Privacy and control claims

| ID | Public sentence | Status | Invariant / executed evidence | Proof source |
|---|---|---|---|---|
| D01 | “SQLite state, generated documents, OAuth tokens, and provider keys are local.” | PROVEN | Relative per-user workspace paths and DPAPI vault implementations. | `src/Engine/PackagedRuntime.cs:19`, `src/Dispatcher/GoogleOAuth.cs:48`, `src/Dispatcher/DpapiSecretVault.cs:10` |
| D02 | “Provider keys and OAuth tokens are encrypted with Windows DPAPI when using the provided vaults.” | PROVEN | Windows round-trip/delete harnesses. | `tests/DispatcherNoSendHarness/Program.cs:309`, `tests/DispatcherNoSendHarness/Program.cs:334` |
| D03 | “CareerSeeker does not read your Gmail inbox.” | PROVEN | Requested scope is `gmail.compose`; draft client surface has draft creation only. | `src/Dispatcher/GoogleOAuth.cs:92`, `tests/DispatcherNoSendHarness/Program.cs:49` |
| D04 | “CareerSeeker does not create Gmail labels by default.” | PROVEN | Label capability is split and absent by default. | `tests/DispatcherNoSendHarness/Program.cs:52` |
| D05 | “Inference providers may receive posting text, selected profile claims, generated text, research snippets, and Gate prompts.” | PROVEN | Prompt builders expose these bounded categories. | `src/Tailor/GatewayTailorModel.cs:83`, `src/Researcher/GatewayDossierModel.cs:50`, `src/Verifier/GatewaySemanticMatcher.cs:43` |
| D06 | “CareerSeeker does not use Google user data to train generalized AI/ML models.” | POLICY | Limited Use commitment; not a repository-executable property. | `docs-site/privacy.md` §5 |
| D07 | “CareerSeeker does not sell or license user data.” | POLICY | Business/data-practice commitment; requires organizational compliance evidence. | `docs-site/privacy.md` §7 |
| D08 | “CareerSeeker uses no analytics, advertising, or tracking services.” | **UNPROVEN** | No whole-binary/network inventory or deployed-site tracker scan is pinned in CI. Keep as a policy statement until both exist. | Needed: SBOM/network audit + deployed-site scan |
| D09 | “All CareerSeeker product data has no server-side retention.” | **UNPROVEN** | Local Windows pipeline is proven, but the operated signup/KV service means the sentence needs precise product-data scope and deployment audit. | Needed: production data-flow/retention inventory |
| D10 | “You can delete all local data.” | PROVEN for installed workspace | `delete-all-data` resolves only `%LOCALAPPDATA%\CareerSeeker`, requires the exact displayed path-bound phrase, refuses broad/arbitrary roots, removes without following directory links, verifies absence, and reports already-absent truthfully. Source/test workspaces and separately saved exports remain explicitly out of scope. | `src/Engine/FullDataDeletion.cs:24`, `src/Engine/FullDataDeletion.cs:33`, `src/Engine/Program.cs:20`, `tests/EngineHarness/Program.cs:224`, `tests/EngineHarness/Program.cs:255` |

## Package and operations claims

| ID | Public sentence | Status | Invariant / executed evidence | Proof source |
|---|---|---|---|---|
| O01 | “The Beta artifact contains one executable.” | PROVEN | MakeAppx unpack self-check found exactly one `CareerSeeker.exe`. | `scripts/Test-BetaReleasePackage.ps1:116` |
| O02 | “The optional startup task is disabled by default.” | PROVEN | Manifest value and XPath self-check. | `scripts/Package-BetaRelease.ps1:185`, `scripts/Test-BetaReleasePackage.ps1:104` |
| O03 | “Normal app removal does not delete local user data.” | PROVEN structurally | Mutable workspace is outside package; removal simulation preserved the external vault sentinel. Real Windows uninstall was not executed. | `src/Engine/PackagedRuntime.cs:19`, `scripts/Test-BetaReleasePackage.ps1:141` |
| O04 | “The installer is signed and trusted by Windows.” | **UNPROVEN** | Current MSIX is explicitly unsigned; signing hook only. Do not publish this claim. | `docs/Beta-Windows-Package-Runbook.md` §Production signing handoff |
| O05 | “CareerSeeker survives reboot unattended.” | **UNPROVEN** | Task definition and process supervisor are tested, but no task registration/reboot test was executed. | `docs/BETA-AUDIT-REQUEST.md` B5 boundary |
| O06 | “CareerSeeker is production-ready.” | **UNPROVEN** | Signing, real installer matrix, OAuth production verification/CASA, and public trust-copy deployment remain. | `docs/Beta-Runbook.md` |
| O07 | “Support responds within 24/48 business hours.” | **UNPROVEN** | Aspirational service target; no operational measurement/SLA. Present it as a target, never a guarantee. | Needed: support operations report |

## Brandon review decisions

Before deployment, Brandon should explicitly accept or revise:

1. D08/D09 wording after a production tracker/data-retention inventory.
2. D10 wording must retain its installed-workspace scope and the separate source/test/export caveat.
3. O03 wording: “structured to preserve” is more precise than claiming a real uninstall test.
4. O05/O06: keep absent from marketing until the installer/reboot/OAuth/signing gates pass.
5. O07: label contact times as targets, not commitments.

The deployment checklist is `docs/Beta-Runbook.md`. Updating this register never authorizes a deploy.
