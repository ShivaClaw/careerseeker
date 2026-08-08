[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ArtifactPath,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string] $ExpectedSha256,
    [Parameter(Mandatory = $true)]
    [string] $ExpectedPublisher,
    [string] $OutputPath = "output/release/Beta-VM-Install-Matrix.md",
    [switch] $ValidateOnly,
    [switch] $Overwrite
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$unsignedPublisherOid = "OID.2.25.311729368913984317654407730594956997722=1"

function Resolve-RepoPath {
    param([string] $Path)
    $root = [System.IO.Path]::GetFullPath($repoRoot)
    $full = if ([System.IO.Path]::IsPathRooted($Path)) {
        [System.IO.Path]::GetFullPath($Path)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
    }
    $prefix = $root.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing a path outside the repository: $full"
    }
    return $full
}

$artifact = Resolve-RepoPath $ArtifactPath
$output = Resolve-RepoPath $OutputPath
if (-not (Test-Path -LiteralPath $artifact -PathType Leaf) -or
    -not $artifact.EndsWith(".msix", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "ArtifactPath must identify an existing repository-local .msix file."
}
if (-not $output.EndsWith(".md", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath must name a Markdown file."
}
if ([string]::IsNullOrWhiteSpace($ExpectedPublisher) -or
    $ExpectedPublisher.IndexOfAny([char[]] "`r`n``") -ge 0) {
    throw "ExpectedPublisher must be a single non-empty manifest subject."
}
if ($ExpectedPublisher.IndexOf($unsignedPublisherOid, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
    throw "ExpectedPublisher must not contain the unsigned-package OID."
}

$actualSha256 = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
if (-not $actualSha256.Equals($ExpectedSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Artifact SHA-256 does not match ExpectedSha256."
}

$steps = @(
    [pscustomobject]@{ Id = "VM01"; Title = "Baseline and signed artifact"; Expected = "Clean disposable Windows 11 VM; no CareerSeeker registration; signature, publisher, bytes, and SHA-256 recorded." },
    [pscustomobject]@{ Id = "VM02"; Title = "Install exact signed MSIX"; Expected = "Installation succeeds without -AllowUnsigned and registers CareerSeeker.LocalBeta." },
    [pscustomobject]@{ Id = "VM03"; Title = "Start menu and startup default"; Expected = "Name/icon render correctly and CareerSeeker is disabled in Startup Apps." },
    [pscustomobject]@{ Id = "VM04"; Title = "First launch and offline onboarding"; Expected = "No console; synthetic resume/manual provider/Gmail skip completes with zero provider or draft calls." },
    [pscustomobject]@{ Id = "VM05"; Title = "External workspace"; Expected = "%LOCALAPPDATA%\CareerSeeker is created; database, logs, and vault paths remain outside the package." },
    [pscustomobject]@{ Id = "VM06"; Title = "Relaunch discovery-only"; Expected = "Relaunch reports honest discovery-only state and creates no Gmail draft." },
    [pscustomobject]@{ Id = "VM07"; Title = "Startup, reboot, and single instance"; Expected = "After manual enablement and reboot, exactly one engine instance runs and local logging is present." },
    [pscustomobject]@{ Id = "VM08"; Title = "Pause, resume, and stop controls"; Expected = "Each local control produces the documented state transition and evidence." },
    [pscustomobject]@{ Id = "VM09"; Title = "Uninstall and preservation"; Expected = "Application/startup registration is removed while the external workspace sentinel remains." },
    [pscustomobject]@{ Id = "VM10"; Title = "Separately confirmed full-data deletion"; Expected = "Only after explicit confirmation, the exact resolved workspace is removed and verified absent." },
    [pscustomobject]@{ Id = "VM11"; Title = "Upgrade-in-place"; Expected = "When a prior signed Beta exists, upgrade preserves data and starts the new version once." }
)

if ($ValidateOnly) {
    Write-Host "Beta disposable-VM matrix validation passed."
    Write-Host "  mode: validation only; no install, signature check, or output write"
    Write-Host "  artifact: $([System.IO.Path]::GetFileName($artifact))"
    Write-Host "  SHA-256: $actualSha256"
    Write-Host "  expected publisher: $ExpectedPublisher"
    Write-Host "  checklist steps: $($steps.Count) ($($steps.Id -join ', '))"
    return
}

if ((Test-Path -LiteralPath $output) -and -not $Overwrite) {
    throw "Matrix output already exists; use -Overwrite only after preserving prior evidence."
}

& (Join-Path $PSScriptRoot "Test-BetaReleasePackage.ps1") `
    -PackagePath $artifact `
    -ExpectedPublisher $ExpectedPublisher `
    -RequireSigned
if ($LASTEXITCODE -ne 0) {
    throw "Signed package verification failed; VM matrix was not initialized."
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# CareerSeeker disposable Windows VM install matrix")
$lines.Add("")
$lines.Add("- Artifact: ``$([System.IO.Path]::GetFileName($artifact))``")
$lines.Add("- SHA-256: ``$actualSha256``")
$lines.Add("- Expected publisher: ``$($ExpectedPublisher.Replace('|', '\|'))``")
$lines.Add("- Generated UTC: ``$([DateTimeOffset]::UtcNow.ToString('O'))``")
$lines.Add("- VM image/build: ``<RECORD>``")
$lines.Add("- VM snapshot ID: ``<RECORD>``")
$lines.Add("- Operator: ``<RECORD>``")
$lines.Add("")
$lines.Add("This checklist records human execution. Generating it does not install, reboot, enable startup, uninstall, or delete data.")
$lines.Add("")
foreach ($step in $steps) {
    $lines.Add("## $($step.Id) - $($step.Title)")
    $lines.Add("")
    $lines.Add("Expected: $($step.Expected)")
    $lines.Add("")
    $lines.Add("- Result: ``PENDING``")
    $lines.Add("- Executed UTC: ``<RECORD>``")
    $lines.Add("- Evidence file/screenshot IDs: ``<RECORD>``")
    $lines.Add("- Notes: ``<RECORD>``")
    $lines.Add("- Recorded output:")
    $lines.Add("")
    $lines.Add('```text')
    $lines.Add("<PASTE COMMAND OR OBSERVATION OUTPUT HERE>")
    $lines.Add('```')
    $lines.Add("")
}
$lines.Add("## Matrix conclusion")
$lines.Add("")
$lines.Add("- Overall result: ``PENDING``")
$lines.Add("- Blocking step IDs: ``<RECORD>``")
$lines.Add("- Signed artifact retained at: ``<RECORD>``")
$lines.Add("- Boundary exceptions or deviations: ``<RECORD>``")

$parent = Split-Path -Parent $output
New-Item -ItemType Directory -Force -Path $parent | Out-Null
[System.IO.File]::WriteAllLines($output, $lines, [System.Text.UTF8Encoding]::new($false))
Write-Host "Disposable-VM matrix initialized: $output"
Write-Host "  steps: $($steps.Count)"
Write-Host "  all results: PENDING"
