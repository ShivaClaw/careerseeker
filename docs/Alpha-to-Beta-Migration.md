# Alpha-to-Beta migration

Updated: 2026-08-07

Use the existing evidence-package export/import path to move local Alpha data
into the Beta workspace. Keep the exported ZIP until the Beta data has been
reviewed. These commands do not install an MSIX and do not change package
registration. The packaged Beta workspace is rooted at
`%LOCALAPPDATA%\CareerSeeker`.

## 1. Export from Alpha

Close the Alpha engine first so its SQLite database is not changing. From the
Alpha repository or unpacked Alpha folder, run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Export-AlphaEvidencePackage.ps1 `
  -DbPath .appdata\careerseeker-alpha.db `
  -ArtifactsPath .appdata\artifacts `
  -JobDescriptionDirectory .appdata\job-descriptions `
  -OutputPath output\careerseeker-alpha-evidence.zip
```

The default export excludes raw provider/request payloads and secret-like
paths. Do not add `-IncludePayloads` for an ordinary migration. Retain the
reported archive hash with the ZIP.

## 2. Import into the Beta workspace

Run this from the Beta repository or unpacked support tree before the first
drafting cycle:

```powershell
$BetaData = Join-Path $env:LOCALAPPDATA 'CareerSeeker\.appdata'
powershell -ExecutionPolicy Bypass -File scripts\Import-AlphaPackage.ps1 `
  -PackagePath output\careerseeker-alpha-evidence.zip `
  -TargetRoot $BetaData `
  -DbPath (Join-Path $BetaData 'careerseeker-alpha.db') `
  -ArtifactsPath (Join-Path $BetaData 'artifacts') `
  -JobDescriptionDirectory (Join-Path $BetaData 'job-descriptions')
```

Import preserves existing files by default. Do not add `-Overwrite` during a
normal migration. If the target already contains data, stop and review the
reported preserved-file counts instead of replacing it.

The importer rejects unsafe ZIP paths, verifies the evidence manifest and
audit chain, and restores the database, generated artifacts, and saved job
descriptions. Provider-key and Gmail OAuth vaults are deliberately excluded;
reconnect those integrations separately after reviewing the local data.

## 3. Review before enabling integrations

1. Open the local dashboard and confirm historical rows and artifacts are
   present.
2. Run discovery-only first and confirm the engine reports its state honestly.
3. Reconnect an inference provider only if wanted.
4. Reconnect Gmail only if wanted. CareerSeeker's L1 surface can create and
   retain drafts, but contains no send path.
5. Keep the evidence ZIP until the migrated workspace has been reviewed and
   backed up according to your own retention policy.

## Why this path is used

The export/import harness covers manifest and audit verification, unsafe-entry
rejection, and preservation-by-default. Separately, the R2 read-only backup
rehearsal copied the retained Alpha database through
`StoreParityHarness --migration-copy`, migrated the copy twice, and verified
that the source stayed byte-identical: 172,032 bytes and SHA-256
`0A5605288D04302443A129289E03E5B62DA1C7B535FE124B0935455238E18192` before
and after. That measured result is engineering evidence for copy safety; the
export/import steps above remain the tester migration procedure.

## Removal boundary

Removing the app package and deleting local user data are separate actions.
Package removal must not be presented as data deletion. While the app remains
available, run `CareerSeeker.exe delete-all-data` once to display the exact
installed-workspace phrase, close CareerSeeker, and supply that exact phrase
with `--confirm-delete-all-data`. Configured source/test workspaces and exports
saved elsewhere remain separate paths. The manual post-uninstall fallback is
documented in `docs/Beta-Windows-Package-Runbook.md`.
