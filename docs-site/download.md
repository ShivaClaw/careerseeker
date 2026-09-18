# CareerSeeker Beta download

**Status: The signed Beta is available to trusted testers.** Published 2026-09-18.

The published artifact is `CareerSeeker-beta-0.7.0-win-x64.msix` for x64 Windows,
signed via Azure Artifact Signing with an RFC-3161 timestamp — the signature
outlives the deliberately short-lived certificate. Verify before installing:

- Object key: `beta/CareerSeeker-beta-0.7.0-win-x64.msix`
- Size: 33,763,432 bytes
- SHA-256: `538E8E647F971B75EBFC99F826BD5302478D253005F8CC21E82A7586DEE89972`
- Publisher: `CN=Applied Autonomy LLC, O=Applied Autonomy LLC, L=Denver, S=Colorado, C=US`

`Get-FileHash` on the downloaded file must print exactly that SHA-256; do not
install a file that differs. The disposable Windows install/upgrade/removal
matrix (VM01-VM11) was executed and recorded on 2026-09-18 before publication.

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

The previously shipped Alpha remains available for existing testers:
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

