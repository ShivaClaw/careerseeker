# CareerSeeker Alpha Tester Walkthrough

Updated: 2026-07-20

This walkthrough is the intended first-run path for the local Windows L1 Drafts alpha. It is written for
trusted testers and external auditors who want to verify the working product path without reading the source
first.

## What This Alpha Does

CareerSeeker runs locally. It can initialize a private workspace, import a source-of-truth profile, ingest jobs
from public ATS boards, score and filter those jobs, tailor draft materials through BYOK LLM providers, verify
draft claims against local profile evidence, render an ATS-clean resume PDF, create reviewable Gmail drafts, show
local state in a localhost dashboard, and export a local evidence package.

The L1 alpha does not send applications. It creates Gmail drafts only. The application code has no Gmail send
path.

## First Run

1. Unzip the release package into a local folder.
2. Double-click `Verify-CareerSeeker-Alpha.cmd` once. This checks the extracted package before you trust it.
3. Double-click `Setup-CareerSeeker-Alpha.cmd`. This creates ignored local folders under `.appdata`, `secrets`,
   and `output`.
4. Edit `.appdata\profile.template.json` with your profile facts and source evidence.
5. Double-click `Import-CareerSeeker-Profile.cmd` to import that local profile into SQLite.
6. Add provider keys to `secrets\env.secrets`. Accepted names are `ANTHROPIC_API_KEY`, `GEMINI_API_KEY` or
   `GOOGLE_API_KEY`, and optional Brave Search as `BRAVE_SEARCH_API_KEY`, `BRAVE_SEARCH_API`, or
   `CAREERSEEKER_BRAVE_SEARCH_API_KEY`.
7. Double-click `Connect-CareerSeeker-Providers.cmd`. It imports provider keys into a local DPAPI vault and runs
   a provider doctor without printing secret values.
8. Put your OAuth desktop client JSON at `secrets\google-oauth-client.json`.
9. Double-click `Connect-CareerSeeker-Gmail.cmd`. It opens Gmail OAuth and preflights draft access without
   creating a draft.
10. Double-click `Check-CareerSeeker-LiveReadiness.cmd` to confirm Gmail/BYOK readiness for live draft paths.
11. Double-click `Start-CareerSeeker-Alpha.cmd` to open the localhost dashboard.

To disconnect later, double-click `Disconnect-CareerSeeker-Gmail.cmd` to revoke Gmail and delete the local token
vault. Double-click `Clear-CareerSeeker-Providers.cmd` to delete the local provider-key vault.
If you want the dashboard to start when you sign in, double-click `Install-CareerSeeker-DashboardTask.cmd`.
Double-click `Status-CareerSeeker-DashboardTask.cmd` to check it, or
`Uninstall-CareerSeeker-DashboardTask.cmd` to remove that logon task.

## Choose A Test Path

Use the demo path when you want deterministic local evidence without Gmail:

1. Double-click `Run-CareerSeeker-Demo.cmd`.
2. Open the dashboard and inspect Applications, Jobs, Documents, and Evidence.
3. Double-click `Export-CareerSeeker-Evidence.cmd` to package local evidence for review.

Use the Scout path when you want real public ATS job discovery:

1. Double-click `Run-CareerSeeker-Scout.cmd`.
2. Open the dashboard Jobs page and choose a job id.
3. Double-click `Draft-CareerSeeker-Job.cmd`.
4. Press Enter for preview/dry-run mode, or type `LIVE` only when you intentionally want a Gmail draft.
5. Double-click `Export-CareerSeeker-Evidence.cmd` after reviewing the result.

Use the live alpha path when you want one end-to-end Gmail draft smoke:

1. Confirm provider keys and Gmail OAuth are connected.
2. Double-click `Check-CareerSeeker-LiveReadiness.cmd`.
3. Double-click `Run-CareerSeeker-Live.cmd`.
4. Review the created Gmail draft manually before doing anything with it.
5. Double-click `Export-CareerSeeker-Evidence.cmd`.

## Safety Rails

- Gmail is compose-only in L1. The app creates drafts and has no send implementation.
- Secret values are not packaged and are not printed by setup, provider, Gmail, verifier, or evidence scripts.
- `secrets`, `.appdata`, `output`, OAuth vaults, BYOK vaults, SQLite databases, resumes, and generated artifacts
  are local-only.
- Gmail and provider off-ramps are local too: `Disconnect-CareerSeeker-Gmail.cmd` clears the Gmail vault, and
  `Clear-CareerSeeker-Providers.cmd` clears the provider-key vault.
- The optional dashboard logon task is per-user, inspectable with `Status-CareerSeeker-DashboardTask.cmd`, and
  removable with `Uninstall-CareerSeeker-DashboardTask.cmd`.
- Job descriptions and researched web pages are untrusted data. The app records prompt-injection signals, and
  selected-job drafting refuses flagged jobs unless explicitly allowed after manual review.
- The Fabrication Gate is fail-closed. Unsupported claims block; unavailable verification defers.
- Evidence packages omit payloads by default. Use payload-inclusive exports only when you intentionally want to
  share local content for audit.

## Useful Files

- `README-alpha.txt`: package-local quickstart.
- `AUDIT-SNAPSHOT.txt`: package-local source commit, verification commands, and safety boundaries.
- `RELEASE-MANIFEST.json`: packaged file inventory.
- `SHA256SUMS.txt`: packaged file checksums.
- `docs/External-Audit-Handoff.md`: source-side audit map and repeatable verifier commands.
