# CareerSeeker Beta changelog

Updated: 2026-08-07

This changelog compares the shipped Alpha ZIP with the repository-staged Beta
MSIX candidate. It does not announce a public Beta download.

## Baseline

The shipped Alpha was built from commit `7018ff9`:

- `CareerSeeker-alpha2-bridge-win-x64-2026-07-24-7018ff9.zip`
- 64,937,092 bytes
- SHA-256 `3A4251F65AEF530BC5D73387422CD53556294970EC546C0112B6EF1BA4E900F2`

The Beta candidate is packaged as `CareerSeeker-beta-win-x64.msix`, version
`0.7.0.0`, for x64 Windows. Candidate package bytes and hashes are build
specific until the final artifact is signed and selected for release.

## What changes in Beta

- The ZIP and launcher collection becomes one MSIX containing one
  `CareerSeeker.exe`.
- Start-menu and optional startup activation remain discovery-only. The
  optional startup task is disabled by default.
- Runtime state lives outside the package under
  `%LOCALAPPDATA%\CareerSeeker`, so package removal is structurally separate
  from user-data deletion.
- Public ATS discovery, prompt-injection quarantine, cycle telemetry, crash
  recovery, and local onboarding are integrated into the periodic engine.
- Lexical ranking uses job-side coverage. Its calibrated 4.0 Act threshold is
  non-decreasing as a profile becomes richer and produced a 6.7% eligible band
  for each 10/50/200-term fixture profile across a 120-posting corpus.
- Alpha evidence packages can be exported and imported with path-safety,
  preservation-by-default, and audit verification.

## Safety boundaries retained

- CareerSeeker creates reviewable Gmail drafts; it has no email-send path and
  does not submit applications.
- The Fabrication Gate and pinned `Stage.VerifierEntailment` routing remain
  mandatory before draft creation.
- Job postings, resumes, and retrieved web text are treated as untrusted data,
  not instructions.
- Gmail drafting and inference providers remain optional. The default packaged
  activation performs discovery only.

## Migration

Follow [Alpha-to-Beta migration](Alpha-to-Beta-Migration.md). The supported
transfer path is export from Alpha, retain the evidence ZIP as a backup, and
import into the Beta workspace without overwrite.

Provider keys and Gmail OAuth tokens are not included in the evidence ZIP.
Reconnect or reconfigure those integrations after migration if wanted.

## Release gates still open

- The repository candidate is unsigned.
- The disposable Windows install/upgrade/removal matrix has not been executed.
- No public Beta artifact or download URL has been published.
- The R2 real-profile rehearsal is blocked after two bounded public-board
  attempts produced zero Act-eligible postings; the authorized live Gmail
  drafting allowance therefore remains unused.
- In-app confirmed full data deletion is scheduled for R6. Until then, use the
  documented exact-path manual removal workflow.
