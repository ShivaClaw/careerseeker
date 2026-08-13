# PowerShell static-analysis policy

Snapshot date: 2026-08-12  
Analyzer: PSScriptAnalyzer 1.25.0  
Scope: every `.ps1`, `.psm1`, and `.psd1` file under `scripts/`

## Reproduce the enforced pass

PSScriptAnalyzer is development tooling, not an application dependency. Install
the pinned version for the current user, then run the repository wrapper:

```powershell
Install-Module PSScriptAnalyzer -RequiredVersion 1.25.0 -Scope CurrentUser
scripts\Test-PowerShellScripts.ps1
```

The wrapper requires exactly 1.25.0, recursively scans `scripts/`, applies
`scripts/PSScriptAnalyzerSettings.psd1`, prints every enforced finding, and
fails if the count is nonzero.

## R6(c) raw inventory and decisions

The first unfiltered recursive scan reported 374 findings: 307 warnings and
67 informational findings. The rule distribution was:

| Rule | Initial count | Decision |
|---|---:|---|
| `PSAvoidUsingWriteHost` | 253 | Documented exception. These are interactive entry-point and verification status messages. `Write-Host` keeps presentation text out of the success-object pipeline consumed by wrapper scripts. |
| `PSAvoidUsingPositionalParameters` | 67 | Informational only, outside the enforced warning/error severity. Every instance was a call to a private script-local assertion helper in `Verify-Alpha.ps1`; none invokes an external command ambiguously. |
| `PSReviewUnusedParameter` | 26 | Documented analyzer limitation/compatibility exception. Twenty-five script parameters are consumed from nested local functions or verification scriptblocks that this rule does not resolve. `Test-BetaReleasePackage.ps1` retains `Configuration` because existing repository callers pass the common packaging interface. |
| `PSAvoidAssignmentToAutomaticVariable` | 17 | Fixed. Seven command wrappers now build `$commandArgs` instead of overwriting PowerShell's automatic `$args`. |
| `PSUseShouldProcessForStateChangingFunctions` | 5 | Documented exception for private helpers. Their public scripts already implement explicit `-DryRun` behavior and perform validation before mutation; the helpers are not exported cmdlets. |
| `PSUseSingularNouns` | 3 | Documented exception for private helpers whose plural names accurately describe argument collections or containment assertions. |
| `PSUseUsingScopeModifierInNewRunspaces` | 2 | Fixed. The dashboard opener now captures its parent URL explicitly with `$using:url`. |
| `PSUseBOMForUnicodeEncodedFile` | 1 | Documented repository-encoding exception. `Verify-Alpha.ps1` is stored as Git-normalized UTF-8 without a BOM and is exercised by Windows PowerShell 5.1 and PowerShell 7 gates. |

The policy excludes only the five reviewed warning families above and enforces
all other warning/error rules. Informational rules remain visible when running
an explicit unfiltered inventory but do not fail the repository wrapper.

After the two actionable rule families were fixed, an explicit unfiltered scan
(overriding automatic settings discovery) reported 355 reviewed findings: 288
warnings and 67 informational findings. The enforced repository wrapper then
reported zero findings:

```powershell
Invoke-ScriptAnalyzer -Path .\scripts -Recurse `
  -Settings @{ Severity = @('Error', 'Warning', 'Information') }
scripts\Test-PowerShellScripts.ps1
```

The remaining unfiltered distribution is 253 `PSAvoidUsingWriteHost`, 67
`PSAvoidUsingPositionalParameters`, 26 `PSReviewUnusedParameter`, 5
`PSUseShouldProcessForStateChangingFunctions`, 3 `PSUseSingularNouns`, and 1
`PSUseBOMForUnicodeEncodedFile`. No finding from an unreviewed warning/error
rule remains outside the enforced policy.
