# CareerSeeker Alpha 2.0 Onboarding Spec

Updated: 2026-07-23
Status: proposed product/build spec
Audience: future Codex/Claude sessions, auditors, and implementation owners

## Purpose

Alpha 1 proves the core product path works, but the tester setup path is still technical. Alpha 2.0 turns setup into a guided Windows onboarding experience for non-technical testers.

The goal is simple: a tester downloads CareerSeeker, clicks one obvious thing, connects AI and Gmail, reviews their extracted profile, and lands in the dashboard without editing JSON, opening command prompts, or finding hidden folders.

## Current Alpha 1 Friction

The current trusted-tester path asks users to:

- unzip a release package and choose the right executable or command helper
- edit `.appdata\profile.template.json`
- manually add provider keys to `secrets\env.secrets`
- place a Google OAuth desktop client JSON at `secrets\google-oauth-client.json`
- run setup, import, provider, Gmail, readiness, and dashboard launch steps in order

That is acceptable for technical validation. It is not acceptable for a broad alpha cohort.

## Alpha 2.0 User Promise

The tester experience should feel like this:

1. Download CareerSeeker.
2. Open `CareerSeeker Setup`.
3. Choose a recent resume.
4. Paste a Gemini API key.
5. Sign in with Gmail.
6. Review and approve extracted profile facts.
7. Start the dashboard.

No command prompt required. No JSON editing. No OAuth client setup. No secret files.

## Key Product Decisions

### Ship CareerSeeker's OAuth Client

Do not ask testers to create or download a Google OAuth desktop client JSON.

CareerSeeker should own the Google Cloud project and desktop OAuth client. Testers should only sign in with Gmail. For a closed alpha, they must be added as OAuth test users while the app is in testing mode.

Rationale:

- Google treats native/desktop apps as public clients that cannot securely store secrets on user devices.
- Asking each tester to create a Google Cloud project is the largest avoidable drop-off in the current flow.
- A single project gives us one consent screen, one scope set, one support path, and one verification path.

Alpha caveat:

- Google OAuth apps in testing mode can issue refresh tokens that expire after 7 days for non-basic scopes, so the wizard and dashboard must handle "Reconnect Gmail" gracefully.

### Keep L1 Drafts Only

Alpha 2.0 remains L1 Drafts.

The app may create Gmail drafts after explicit user confirmation. It must not send emails. The codebase should still have no Gmail send implementation.

Use only the minimum Gmail scope needed for draft creation. Today that is `gmail.compose`, with clear consent copy explaining that Google's scope can allow compose/send capability even though CareerSeeker implements draft creation only.

### Resume Extraction Is Assisted, Not Trusted Blindly

Gemini can extract candidate profile facts from a resume, but the user must approve them before they become CareerSeeker source-of-truth claims.

Every imported claim should have:

- claim kind
- normalized value
- confidence
- source document id
- evidence excerpt or location when available
- user approval state

The wizard should make review fast, but never silently promote unsupported or guessed facts.

### Store Secrets Directly In DPAPI

The wizard should not write `secrets\env.secrets` as the normal path.

Provider keys and OAuth tokens should be stored directly in the existing per-user DPAPI vaults. Any temporary secret material should be avoided or deleted immediately after import.

`secrets\env.secrets` can remain an advanced/manual fallback for technical testers and diagnostics.

## Target Setup Artifact

There are two viable distribution stages.

### Alpha 2.0 Bridge

Keep the ZIP package for now, but make the top-level contents non-technical:

- `START HERE - CareerSeeker Setup.exe`
- `README - Start Here.txt`
- `CareerSeeker.exe`
- `Advanced Tools\...`
- `docs\...`

The setup wizard performs the current first-run sequence internally.

This is the fastest path because it builds on the existing package system.

### Alpha 2.0 Proper

Move to a real per-user installer:

- `CareerSeeker-Alpha-Setup.exe`
- installs to a standard per-user app location
- creates Start Menu entry
- launches onboarding after install
- writes app data under `%LOCALAPPDATA%\CareerSeeker`
- supports uninstall
- is signed when the signing path is ready

This is the right shape for broader beta. It also removes the unzip decision point entirely.

## Wizard Flow

### 1. Welcome And Safety

Show a plain-language welcome screen:

- CareerSeeker runs locally.
- It creates Gmail drafts only.
- It never sends applications.
- Resume contents may be sent to the selected AI provider only after consent.
- The user can disconnect Gmail and clear API keys later.

Primary action: `Start setup`

Secondary action: `Advanced manual setup`

### 2. Package Verification And Workspace Init

The wizard should run the current Alpha 1 verification and initialization automatically:

- package manifest/checksum validation
- local workspace creation
- database/artifact folder creation
- startup doctor checks that do not require Gmail or BYOK yet

If Windows SmartScreen appears because the build is unsigned or newly signed, the download email and setup UI should prepare users for what they may see.

### 3. Resume Selection

Ask the tester to choose a recent resume:

- PDF preferred
- DOCX acceptable if supported
- TXT/Markdown acceptable as fallback

Store the source file metadata locally:

- original filename
- content hash
- imported timestamp
- source document id

Do not permanently copy the resume outside the local workspace unless the user agrees.

### 4. AI Provider Connection

For Alpha 2.0, default to Gemini BYOK:

- accepted key names: `GEMINI_API_KEY` or `GOOGLE_API_KEY`
- setup field label: `Gemini API key`
- default extraction model: `gemini-2.5-flash-lite`
- model must be configurable so the alpha can move to a newer Flash-Lite model without changing the wizard

The wizard should:

- link to Google AI Studio key creation
- provide paste-and-test
- mask the key after entry
- store the key directly in DPAPI
- run a low-cost provider doctor
- never print or log the secret value

Manual fallback:

- user can skip AI extraction and fill profile facts manually in a form
- technical users can still use `secrets\env.secrets` through advanced setup

### 5. Resume Extraction

After explicit consent, send only the resume text/content needed for extraction to Gemini.

The extraction prompt should request structured JSON with:

- contact facts
- work history
- education
- skills
- projects
- certifications
- target roles or preferences, if present
- evidence snippets tied to each fact
- uncertainty markers
- facts that require user confirmation

The extractor must not infer sensitive or unsupported facts. Missing information should stay missing.

Resume text must be treated as untrusted data in the extraction prompt, not as instruction text.

### 6. Profile Review

Show a review screen before import:

- facts grouped by category
- evidence excerpt visible beside each fact
- confidence indicator
- approve/edit/remove controls
- "needs source" warning for unsupported facts
- AI-extracted claims capped below `verified` unless a separate human-authored attestation path promotes them

The user must explicitly approve the set before CareerSeeker imports it into SQLite.

Approved facts become source-of-truth claims. AI-extracted facts must not be imported as `verified`; they should be capped at `stated` with `sourceDoc: "resume-ai"` unless a future review UI captures stronger human attestation. Unapproved extracted facts are discarded or stored only as local onboarding draft data, not as trusted profile evidence.

### 7. Gmail Connection

CareerSeeker should open the browser for Google OAuth using the app-owned desktop OAuth client.

The wizard should:

- request only the Gmail compose/draft scope required for L1
- explain draft-only behavior in plain language
- store tokens in the existing DPAPI Gmail vault
- run Gmail preflight without creating a draft
- show a clear reconnect path when tokens expire or are revoked

The tester should never handle `google-oauth-client.json`.

### 8. Readiness Check

Run the Alpha 1 readiness sequence internally:

- profile exists
- provider key works
- Gmail OAuth works
- database and artifacts are writable
- dashboard can start
- no required setup step is missing

Show one friendly result:

- `Ready`
- `Needs attention`
- `Advanced details`

### 9. Dashboard Launch

Start the localhost dashboard and open it in the user's browser.

If the dashboard is already running, reuse it.

Provide a visible control for:

- disconnect Gmail
- clear AI key
- pause/kill current activity
- export evidence package
- open support guide

### 10. Optional First Smoke

Offer one optional guided first smoke test:

- demo cycle with no Gmail draft
- live L1 draft only after explicit confirmation

Default should be the no-Gmail demo cycle.

## Email Strategy

Do not paste the full Alpha 1 technical walkthrough into the download-code email.

Use the email to do three things:

- tell testers exactly what to click first
- set expectations about Windows warnings, Gmail consent, AI key, and resume upload
- link to the full walkthrough for advanced/manual setup

The email should be short enough to read on a phone.

Recommended structure:

- download link and code
- "After download, open `START HERE - CareerSeeker Setup.exe`"
- prerequisites: Windows, Gmail account, Gemini API key, recent resume
- safety promise: creates drafts only, sends nothing
- support link
- advanced/manual guide link

## Data And Privacy Requirements

Alpha 2.0 must preserve the local-first alpha promise:

- SQLite, OAuth tokens, provider keys, generated drafts, evidence packages, and resume-derived profile data stay local by default.
- Resume content is sent to Gemini only after explicit consent.
- AI-extracted profile claims are tagged with AI/resume provenance and capped at `stated`.
- API keys are stored in per-user DPAPI.
- Gmail tokens are stored in per-user DPAPI.
- No secrets are included in evidence exports or release packages.
- Logs must redact secrets and avoid raw resume dumps by default.
- Payload-inclusive exports must remain explicit opt-in.

## Implementation Workstreams

### A. App-Owned OAuth

- Create or confirm CareerSeeker Google Cloud project.
- Configure OAuth consent screen for L1 draft scope.
- Create desktop OAuth client.
- Add alpha testers to the test-user allowlist.
- Package OAuth client metadata with the app or fetch it from a trusted app resource.
- Remove tester-facing OAuth JSON step from normal onboarding.
- Keep advanced override for local developer testing.

Exit:

- A tester signs in with Gmail without creating a Google Cloud project or moving JSON files.

### B. Setup Wizard Shell

- Add a Windows-friendly setup entrypoint.
- Decide first implementation form:
  - console-free WinUI/WPF setup app, or
  - minimal webview/localhost setup wizard launched by the existing executable
- Hide command windows.
- Reuse existing setup, provider, Gmail, doctor, import, and dashboard commands internally.
- Show clear progress, errors, retry actions, and advanced details.

Exit:

- A non-technical tester can complete Alpha 1 setup steps 1, 2, 6, 7, 8, 9, 10, and 11 through the wizard.

### C. Resume Import And Extraction

- Add resume file picker.
- Add PDF text extraction.
- Add DOCX extraction if feasible for Alpha 2.0; otherwise mark DOCX as Alpha 2.1.
- Add Gemini structured extraction adapter.
- Validate extracted JSON.
- Capture source document metadata and evidence excerpts.
- Create review/edit/approve UI.
- Import approved claims using the existing profile import/store path.

Exit:

- A tester can generate and approve a source-of-truth profile without editing JSON.

### D. Secret Handling Upgrade

- Add direct DPAPI write path from setup UI.
- Keep `env.secrets` as advanced import only.
- Ensure no setup logs include pasted keys.
- Ensure failed provider tests do not persist invalid keys unless user chooses to save anyway.

Exit:

- Normal setup leaves no plaintext provider key file behind.

### E. Packaging

- Update release package layout for the bridge version.
- Put advanced command helpers under `Advanced Tools`.
- Make `START HERE - CareerSeeker Setup.exe` the obvious first click.
- Update `README-alpha.txt`, tester walkthrough, and audit snapshot.
- Add clean-machine package test for wizard setup path.

Exit:

- The release ZIP no longer asks testers to choose from many top-level `.cmd` files.

### F. Installer And Signing

- Choose installer path: MSIX, MSI, or single bootstrapper.
- Prefer per-user install and per-user app data.
- Start signing setup early.
- Expect SmartScreen reputation to build over time even for signed non-Store builds.

Exit:

- Clean Windows machine can install, launch onboarding, uninstall, and leave user data behavior documented.

## Acceptance Criteria

Alpha 2.0 is ready when:

- a non-technical tester can complete setup without opening a terminal
- no tester edits JSON
- no tester creates a Google Cloud OAuth client
- no tester manually places `google-oauth-client.json`
- no normal setup path leaves plaintext API keys in `secrets\env.secrets`
- resume extraction requires explicit consent
- extracted profile facts require user approval
- Gmail connects through app-owned OAuth
- readiness check passes from the wizard
- dashboard opens successfully
- demo cycle runs by default
- live Gmail draft creation still requires explicit confirmation
- evidence export still excludes secrets
- disconnect and key-clear controls are available

## Recommended Build Order

1. Build the bridge setup wizard around the existing command paths.
2. Add direct DPAPI provider-key entry.
3. Package app-owned OAuth client metadata and remove tester OAuth JSON from normal setup.
4. Add Gmail connect screen and reconnect handling.
5. Add resume picker and PDF text extraction.
6. Add Gemini structured extraction.
7. Add profile review/approval UI.
8. Wire approved claims into existing profile import/store path.
9. Update packaging so the top-level ZIP has one obvious start action.
10. Run a clean-machine novice test and revise copy based on where the user hesitates.

## Recommended Ownership

Best path: Codex implements Alpha 2.0, then Claude audits the implementation and user flow.

Reasoning:

- The current codebase already has many command paths, DPAPI vaults, Gmail OAuth, profile import, startup doctor, packaging scripts, and dashboard controls. This implementation should mostly compose and polish existing pieces rather than reinvent them.
- Codex already has the repo context and can keep the work tightly scoped to those existing paths.
- Claude is valuable as a second-pass auditor, especially for UX clarity, threat modeling, setup copy, and "would a normal tester get stuck here?" review.

Good split:

- Codex: implementation, packaging, local verification, clean-machine checklist.
- Claude: audit, UX friction review, privacy wording, OAuth/scope copy, edge-case critique.

If only one agent builds it, choose Codex for continuity. If two agents are available, use Claude as the skeptical reviewer after the first working pass.

## References Checked

- Google OAuth overview: https://developers.google.com/identity/protocols/oauth2
- Google OAuth client management: https://support.google.com/cloud/answer/15549257
- Gemini model list: https://ai.google.dev/gemini-api/docs/models
- Microsoft SmartScreen guidance: https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation
