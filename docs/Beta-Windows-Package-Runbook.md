# CareerSeeker Beta Windows package runbook

Updated: 2026-07-30

## What B7 produces

`scripts/Package-BetaRelease.ps1` produces one `win-x64` MSIX containing exactly one executable:
`CareerSeeker.exe`. Windows creates the Start-menu entry from the package manifest. The declared startup
task is disabled by default and remains user-configurable in Windows Startup Apps / Task Manager.

Mutable state is not stored in the package. A package-identity launch uses
`%LOCALAPPDATA%\CareerSeeker` for the database, artifacts, job descriptions, DPAPI vaults, and onboarding
marker. Therefore normal MSIX removal removes the application, Start-menu registration, and startup-task
registration while preserving the local workspace. Delete that workspace only after the user separately
confirms that data deletion is intended.

The committed image master was generated with the built-in image tool from this prompt: “Create a simple,
polished CareerSeeker app icon: a cream paper document with a checkmark and upward path motif on deep teal;
flat geometric illustration, restrained coral accent, centered, readable at 44 px, no text, no transparency,
no watermark.” The required 44, 150, and Store tile sizes are deterministic downscales of that inspected
master.

## Reproducible unsigned tester build

The repository pins `Microsoft.Windows.SDK.BuildTools` and its lock file. This downloads build tools into the
NuGet cache; it does not install an SDK machine-wide.

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Package-BetaRelease.ps1
powershell -ExecutionPolicy Bypass -File scripts\Test-BetaReleasePackage.ps1
```

The default manifest publisher includes Microsoft's special unsigned-package OID. On Windows 11, a human
tester may install this explicitly with an elevated PowerShell session:

```powershell
Add-AppxPackage -Path .\CareerSeeker-beta-win-x64.msix -AllowUnsigned
```

Unsigned installation is a bounded tester path, not a public distribution mechanism. This runbook does not
install, remove, or alter any package on behalf of the user.

## Production signing handoff

Windows requires a trusted signature for ordinary MSIX distribution. Before building, set `-Publisher` to
the exact subject of the intended code-signing certificate (and omit the unsigned OID). Then sign the built
MSIX with `scripts/Sign-BetaRelease.ps1`. The PFX password must be supplied only through the
`CAREERSEEKER_SIGNING_PASSWORD` process environment variable; the script never prints it.

```powershell
$env:CAREERSEEKER_SIGNING_PASSWORD = Read-Host -AsSecureString |
  ConvertFrom-SecureString -AsPlainText
powershell -ExecutionPolicy Bypass -File scripts\Package-BetaRelease.ps1 `
  -Publisher 'CN=EXACT CERTIFICATE SUBJECT'
powershell -ExecutionPolicy Bypass -File scripts\Sign-BetaRelease.ps1 `
  -PackagePath output\release\CareerSeeker-beta-win-x64.msix `
  -CertificatePath C:\secure\publisher.pfx
Remove-Item Env:\CAREERSEEKER_SIGNING_PASSWORD
```

For public Beta, prefer Azure Artifact Signing in CI after human account/identity setup. A human must create
that service, choose its billing, approve GitHub permissions/secrets, and configure the signing action. This
agent intentionally performs none of those live-service actions.

Microsoft references:

- https://learn.microsoft.com/windows/msix/package/signing-package-overview
- https://learn.microsoft.com/windows/msix/package/unsigned-package
- https://learn.microsoft.com/windows/msix/desktop/tamper-protection
- https://learn.microsoft.com/windows/apps/desktop/modernize/desktop-to-uwp-extensions

## Verification and removal

`Test-BetaReleasePackage.ps1` unpacks without installing and asserts:

- manifest identity, full-trust entry point, package integrity, and disabled startup declaration;
- exactly one executable and no duplicate setup launcher;
- no `.appdata`, output, DPAPI vault, token, or plaintext secret payload;
- the unpacked executable traverses the ten-step offline setup smoke with zero provider/Gmail calls;
- deleting the unpacked package tree leaves a synthetic external user vault untouched.

For an intentionally installed tester package, a human can remove only the application with:

```powershell
Get-AppxPackage CareerSeeker.LocalBeta | Remove-AppxPackage
```

Only after a separate explicit data-deletion confirmation:

```powershell
Remove-Item -LiteralPath "$env:LOCALAPPDATA\CareerSeeker" -Recurse -Force
```

Resolve and inspect that exact path before deletion. Never combine application removal with data deletion.
