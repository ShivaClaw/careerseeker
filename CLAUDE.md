# CLAUDE.md

Repo-root guidance for coding agents working on CareerSeeker. Read this before
making changes. It exists to prevent two recurring failure modes: silently
weakening a safety invariant, and shipping doc/verifier drift (a doc-count
assertion in the verifier that nobody updated to match a doc that changed).

For full architecture, module map, and roadmap, see
[docs/CareerSeeker-Project-Summary.md](docs/CareerSeeker-Project-Summary.md).
For the audit-facing summary, see
[docs/External-Audit-Handoff.md](docs/External-Audit-Handoff.md).

## Non-negotiable invariants

Do not weaken, bypass, or refactor around these without an explicit user
decision to change product scope. If a change would touch any of these files,
stop and confirm intent first.

- **Fabrication Gate is never bypassed.** `READY` is reachable only from
  `VERIFIED`, and `VERIFIED` is reachable only by passing entailment in
  [src/Verifier/FabricationGate.cs](src/Verifier/FabricationGate.cs). No code
  path may hand-wave a claim into a verified state.
- **`Stage.VerifierEntailment` is pinned to `StrongCloud` and fails closed.**
  Pin lives in [src/Gateway/Routing.cs:89](src/Gateway/Routing.cs) and
  [src/Gateway/Stages.cs:68](src/Gateway/Stages.cs) (`PinnedStages`). Budget
  throttling must never downgrade or skip this stage. Provider outages on this
  stage must fail into `GATE_UNAVAILABLE`, never be mislabeled as a passed
  verification or silently treated as fabrication-cleared.
- **The L1 Dispatcher has no send path.** `Dispatcher.SubmitAsync` throws
  `NotSupportedException` at
  [src/Dispatcher/Dispatcher.cs:83-86](src/Dispatcher/Dispatcher.cs). Do not
  add a send method to `IGmailDraftClient` or otherwise give the Dispatcher a
  way to transmit anything beyond creating a Gmail draft
  (`gmail.compose` scope only). Sending is an L2/L3 decision, not this repo's
  to make unilaterally.
- **Local-first, no hosted pipeline.** SQLite store, OAuth tokens, provider
  API keys, generated documents (resumes/covers/PDFs), and the audit trail
  stay on the user's Windows machine. Do not add Cloud Run, a hosted backend,
  or any code path that moves this data off-device. The only planned server
  component is a future blind, end-to-end-encrypted relay for an Android
  dashboard — nothing else.
- **Do not expand Gmail scope casually.** L1 requests `gmail.compose` only.
  Custom label management is deliberately deferred (it needs broader scope) —
  do not reintroduce it into default L1 behavior.
- **Job descriptions and other external content are untrusted data**, never
  instruction context. Keep them inside quarantined/XML-style untrusted-data
  blocks in prompts (Tailor, Researcher). Don't let Scout/Researcher inputs
  influence control flow as if they were instructions.

## Verification ritual

Run after every change, before considering work done:

```powershell
scripts\Verify-Alpha.ps1
```

For a higher-signal pass before anything audit-facing or before merging
non-trivial changes, add the relevant flags:

```powershell
scripts\Verify-Alpha.ps1 -IncludeLive       # live BYOK/Gmail/Gateway smoke
scripts\Verify-Alpha.ps1 -IncludePackage    # trusted-tester ZIP build + packaged helper smokes
scripts\Verify-Alpha.ps1 -IncludeResearch   # live Brave + BYOK company research
scripts\Verify-Alpha.ps1 -IncludePublish    # win-x64 single-file publish + smoke
```

Never assert that a harness or verifier passed unless you actually ran it in
this session. "Should pass" is not evidence — cite the actual command output,
and reference specific file:line locations when describing what changed.

## The doc/verifier drift trap

`scripts/Verify-Alpha.ps1` contains hard-coded string/content assertions
against specific docs — e.g. harness pass counts and phrase checks against
`README.md`, `src/Engine/README.md`, `docs/CareerSeeker-Project-Summary.md`,
`docs/External-Audit-Handoff.md`, `docs/repo-audit-2026-07-13.md`, and the
`docs-site/` trust pages (see roughly
[scripts/Verify-Alpha.ps1:245-463](scripts/Verify-Alpha.ps1)).

**If you change harness counts, evidence numbers, or wording that those docs
report, update the corresponding `Assert-Contains`/`Assert-DoesNotContain`
expectations in `Verify-Alpha.ps1` in the same change.** A doc edit that
drifts from what the verifier expects will fail the next `Verify-Alpha.ps1`
run for someone else, and a verifier expectation that drifts from the doc
will silently stop testing what it claims to test. Treat doc content and
verifier expectations as one unit that changes together.

## Secrets — never print

Never print, log, or echo the contents of:

- `secrets/env.secrets`
- `secrets/google-oauth-client.json` or any `client_secret*.json`
- Any DPAPI vault under `.appdata/` (OAuth token vault, BYOK key vault)
- Any `token*.json`

These are already gitignored. If a task requires referencing them, refer to
the path only, never the contents. If you must confirm a key/token is present,
check for existence/non-emptiness, not the value.

## Practical notes

- Solution: `dotnet build CareerSeeker.sln -c Release` (0 warnings / 0 errors
  is the baseline).
- Tests are console harnesses under `tests/`, no xUnit — run via
  `dotnet run --project tests/<Harness>/<Harness>.csproj -c Release --no-build`.
- Keep offline harnesses (`Slice`, `EngineHarness`, `ResearcherHarness`,
  `HookHarness`, `StoreParityHarness`, `GatewayGateHarness`,
  `DispatcherNoSendHarness`, `LifecycleHarness`, `RendererHarness`) green
  before merging — `scripts/Verify-Alpha.ps1` runs these by default.
- `docs/CareerSeeker-Project-Summary.md` is the living handoff/status doc;
  update it (and the verifier expectations, per above) when status changes.
