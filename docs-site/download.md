# CareerSeeker Beta download

**Status: Beta download is not yet available.**

The repository candidate is `CareerSeeker-beta-win-x64.msix` for x64 Windows.
It is currently unsigned. Human signing, signature verification, and a
disposable Windows install/upgrade/removal matrix remain release gates. No
public Beta artifact URL is staged on this page.

## What testers can expect

- One MSIX containing one `CareerSeeker.exe`.
- A local-first Windows L1 Drafts beta: CareerSeeker prepares reviewable Gmail
  drafts and has no email-send or application-submit path.
- Public ATS discovery and local job ranking against the source-of-truth
  profile.
- Prompt-injection quarantine and the Fabrication Gate before any draft.
- Local runtime state under `%LOCALAPPDATA%\CareerSeeker`.
- An optional startup task that is disabled by default; package activation is
  discovery-only.

The previously shipped Alpha remains the last published artifact:
`CareerSeeker-alpha2-bridge-win-x64-2026-07-24-7018ff9.zip`, 64,937,092 bytes,
SHA-256 `3A4251F65AEF530BC5D73387422CD53556294970EC546C0112B6EF1BA4E900F2`.

## Moving from Alpha

The Beta transition uses the existing `export-alpha-package` and
`import-alpha-package` workflow. Export first, keep the evidence ZIP as a
backup, and import without overwrite. Provider keys and Gmail OAuth tokens are
not transferred; reconnect them separately if wanted.

See the repository's [Beta changelog](../docs/Beta-Changelog.md) and
[Alpha-to-Beta migration guide](../docs/Alpha-to-Beta-Migration.md).

The evidence ZIP can be checked with `export-audit` before import. Removing the
app package and deleting `%LOCALAPPDATA%\CareerSeeker` remain separate,
explicit actions.

