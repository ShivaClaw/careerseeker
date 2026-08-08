# CareerSeeker Beta Windows package runbook

Updated: 2026-08-07

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

## Offline production-flow validation

R4 adds validation-only paths that exercise parameter and control flow without
reading a certificate, signing, installing, or writing a VM evidence file.
Use an already-built disposable MSIX; the PFX path is intentionally allowed to
be absent in validation-only mode.

```powershell
$Artifact = 'output\release\CareerSeeker-beta-win-x64.msix'
$Publisher = 'CN=EXACT CERTIFICATE SUBJECT'
$Hash = (Get-FileHash -LiteralPath $Artifact -Algorithm SHA256).Hash

powershell -ExecutionPolicy Bypass -File scripts\Sign-BetaRelease.ps1 `
  -PackagePath $Artifact `
  -CertificatePath C:\secure\publisher.pfx `
  -TimestampUrl https://timestamp.digicert.com `
  -ValidateOnly

powershell -ExecutionPolicy Bypass -File scripts\New-BetaVmInstallMatrix.ps1 `
  -ArtifactPath $Artifact `
  -ExpectedSha256 $Hash `
  -ExpectedPublisher $Publisher `
  -ValidateOnly
```

`-ValidateOnly` checks the package suffix/existence, HTTPS timestamp URL,
expected SHA-256, publisher shape, repository-local paths, and all eleven VM
step definitions. It does not prove a signature, publisher identity, install,
startup, reboot, upgrade, uninstall, or deletion result.

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

powershell -ExecutionPolicy Bypass -File scripts\Test-BetaReleasePackage.ps1 `
  -PackagePath output\release\CareerSeeker-beta-win-x64.msix `
  -ExpectedPublisher 'CN=EXACT CERTIFICATE SUBJECT' `
  -RequireSigned
```

The signing hook now verifies the result with SignTool before returning. The
package verifier independently requires an exact manifest-publisher match,
rejects Microsoft's unsigned-package OID, and requires SignTool policy
verification when `-RequireSigned` is supplied.

For public Beta, prefer Azure Artifact Signing in CI after human account and
identity setup. `docs/autonomy/HUMAN-QUEUE.md` Q03 records the current Azure
CLI resource/RBAC commands and the manual GitHub OIDC signing-action shape. A
human must choose billing, complete portal-only identity validation, approve
repository permissions, and execute the signing workflow. This agent
intentionally performs none of those live-service actions.

Microsoft references:

- https://learn.microsoft.com/windows/msix/package/signing-package-overview
- https://learn.microsoft.com/windows/msix/package/unsigned-package
- https://learn.microsoft.com/windows/msix/desktop/tamper-protection
- https://learn.microsoft.com/windows/apps/desktop/modernize/desktop-to-uwp-extensions
- https://learn.microsoft.com/azure/artifact-signing/quickstart
- https://learn.microsoft.com/azure/artifact-signing/tutorial-assign-roles
- https://github.com/Azure/artifact-signing-action

## Verification and removal

`Test-BetaReleasePackage.ps1` unpacks without installing and asserts:

- manifest identity, full-trust entry point, package integrity, and disabled startup declaration;
- exactly one executable and no duplicate setup launcher;
- no `.appdata`, output, DPAPI vault, token, or plaintext secret payload;
- the unpacked executable traverses the ten-step offline setup smoke with zero provider/Gmail calls;
- deleting the unpacked package tree leaves a synthetic external user vault untouched.

After a real signed artifact passes `-ExpectedPublisher ... -RequireSigned`,
initialize a recorded-output checklist (still without installing anything):

```powershell
$Artifact = 'output\release\CareerSeeker-beta-win-x64.msix'
$Hash = (Get-FileHash -LiteralPath $Artifact -Algorithm SHA256).Hash
powershell -ExecutionPolicy Bypass -File scripts\New-BetaVmInstallMatrix.ps1 `
  -ArtifactPath $Artifact `
  -ExpectedSha256 $Hash `
  -ExpectedPublisher 'CN=EXACT CERTIFICATE SUBJECT' `
  -OutputPath output\release\Beta-VM-Install-Matrix.md
```

The generator first performs the signed-package verification and then writes
eleven `PENDING` steps with slots for timestamps, commands/observations,
screenshots, and notes. A human executes those steps on a disposable Windows
VM. The generator itself never installs, registers, reboots, uninstalls, or
deletes data.

While the package is still installed, resolve its executable and run the
separately confirmed data off-ramp first without a confirmation:

```powershell
$Package = Get-AppxPackage CareerSeeker.LocalBeta
$CareerSeeker = Join-Path $Package.InstallLocation 'CareerSeeker.exe'
& $CareerSeeker delete-all-data
```

That invocation must report `NOT DELETED` and print the exact resolved path-bound
phrase. Close every CareerSeeker process, then copy that phrase exactly:

```powershell
& $CareerSeeker delete-all-data `
  --confirm-delete-all-data 'DELETE ALL CAREERSEEKER DATA AT <COPY EXACT DISPLAYED PATH>'
```

The command must report `target exists after: no`. It refuses an arbitrary or
volume-root target and does not follow nested directory links. To test package
removal separately, relaunch once to recreate a synthetic workspace sentinel,
then remove only the application:

```powershell
Get-AppxPackage CareerSeeker.LocalBeta | Remove-AppxPackage
```

If the package was already removed, the in-app command is unavailable; the
manual fallback remains `Remove-Item -LiteralPath
"$env:LOCALAPPDATA\CareerSeeker" -Recurse -Force` after resolving and inspecting
that exact path. Never combine application removal with data deletion.
