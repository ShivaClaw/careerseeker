param(
    [string] $Root = "",
    [switch] $RunDashboardSmoke,
    [string] $DashboardSmokeDbPath = ".appdata/package-self-check.db"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path -Parent $PSScriptRoot
}

function Resolve-RootPath {
    param([string] $Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $Root $Path))
}

function Assert-SafeRelativePath {
    param([string] $RelativePath)

    $normalized = $RelativePath.Replace("\", "/")
    if ([string]::IsNullOrWhiteSpace($normalized) -or
        [System.IO.Path]::IsPathRooted($normalized) -or
        $normalized.Contains("../") -or
        $normalized.StartsWith("..") -or
        $normalized.Contains(":")) {
        throw "Unsafe checksum path '$RelativePath'."
    }
    return $normalized
}

function Test-SecretLookingPath {
    param([string] $RelativePath)

    $parts = $RelativePath.Replace("\", "/").Split("/")
    if (@($parts | Where-Object { $_ -ieq "secrets" -or $_ -ieq "oauth" }).Count -gt 0) {
        return $true
    }

    $name = [System.IO.Path]::GetFileName($RelativePath)
    return $name.EndsWith(".dpapi", [System.StringComparison]::OrdinalIgnoreCase) -or
           $name.IndexOf("token", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
           $name.IndexOf("secret", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
           $name.IndexOf("key", [System.StringComparison]::OrdinalIgnoreCase) -ge 0
}

function Invoke-Checked {
    param(
        [string] $Command,
        [string[]] $Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $Command $($Arguments -join ' ')"
    }
}

$Root = [System.IO.Path]::GetFullPath($Root)
if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
    throw "Release package root not found: $Root"
}

Push-Location $Root
try {
    foreach ($required in @(
        "SeekerSvc.Engine.exe",
        "Connect-CareerSeeker-Providers.cmd",
        "Connect-CareerSeeker-Gmail.cmd",
        "Setup-CareerSeeker-Alpha.cmd",
        "Start-CareerSeeker-Alpha.cmd",
        "e_sqlite3.dll",
        "README-alpha.txt",
        "AUDIT-SNAPSHOT.txt",
        "RELEASE-MANIFEST.json",
        "SHA256SUMS.txt",
        "scripts/Connect-AlphaProviders.ps1",
        "scripts/Initialize-AlphaWorkspace.ps1",
        "scripts/Start-AlphaDashboard.ps1",
        "scripts/Manage-AlphaDashboardTask.ps1",
        "scripts/Test-AlphaReleasePackage.ps1"
    )) {
        if (-not (Test-Path -LiteralPath (Resolve-RootPath $required) -PathType Leaf)) {
            throw "Required release file missing: $required"
        }
    }

    $manifest = Get-Content -LiteralPath (Resolve-RootPath "RELEASE-MANIFEST.json") -Raw | ConvertFrom-Json
    if ($manifest.format -ne "careerseeker-alpha-release-v1") {
        throw "Unexpected release manifest format '$($manifest.format)'."
    }
    if ($manifest.includes.nativeRuntimeDependencies -notcontains "e_sqlite3.dll") {
        throw "Release manifest does not list e_sqlite3.dll."
    }
    if ($manifest.includes.auditSnapshot -ne "AUDIT-SNAPSHOT.txt") {
        throw "Release manifest does not reference AUDIT-SNAPSHOT.txt."
    }
    if ($manifest.includes.scripts -notcontains "scripts/Start-AlphaDashboard.ps1") {
        throw "Release manifest does not list the dashboard launcher."
    }
    if ($manifest.includes.scripts -notcontains "scripts/Connect-AlphaProviders.ps1") {
        throw "Release manifest does not list the provider connect helper."
    }
    if ($manifest.includes.scripts -notcontains "scripts/Test-AlphaReleasePackage.ps1") {
        throw "Release manifest does not list the package self-check script."
    }
    if ($manifest.includes.launchers -notcontains "Start-CareerSeeker-Alpha.cmd") {
        throw "Release manifest does not list the double-click alpha launcher."
    }
    if ($manifest.includes.launchers -notcontains "Setup-CareerSeeker-Alpha.cmd") {
        throw "Release manifest does not list the double-click setup launcher."
    }
    if ($manifest.includes.launchers -notcontains "Connect-CareerSeeker-Providers.cmd") {
        throw "Release manifest does not list the double-click provider connect launcher."
    }
    if ($manifest.includes.launchers -notcontains "Connect-CareerSeeker-Gmail.cmd") {
        throw "Release manifest does not list the double-click Gmail connect launcher."
    }
    if ($manifest.includes.checksums -ne "SHA256SUMS.txt") {
        throw "Release manifest does not reference SHA256SUMS.txt."
    }

    $auditSnapshot = Get-Content -LiteralPath (Resolve-RootPath "AUDIT-SNAPSHOT.txt") -Raw
    foreach ($snippet in @(
        "CareerSeeker Alpha Audit Snapshot",
        "Package-local verification commands",
        "Connect-CareerSeeker-Providers.cmd",
        "L1 creates Gmail drafts only",
        "Secret values are not included"
    )) {
        if (-not $auditSnapshot.Contains($snippet)) {
            throw "Audit snapshot missing '$snippet'."
        }
    }

    $checksumCount = 0
    foreach ($line in Get-Content -LiteralPath (Resolve-RootPath "SHA256SUMS.txt")) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -notmatch "^([0-9a-f]{64})  (.+)$") {
            throw "Malformed checksum line: $line"
        }

        $expected = $Matches[1]
        $relative = Assert-SafeRelativePath $Matches[2]
        if (Test-SecretLookingPath $relative) {
            throw "Release package contains secret-looking path '$relative'."
        }

        $filePath = Resolve-RootPath $relative
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            throw "Checksum target missing: $relative"
        }

        $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $filePath).Hash.ToLowerInvariant()
        if ($actual -ne $expected) {
            throw "Checksum mismatch for '$relative'."
        }
        $checksumCount++
    }

    if ($checksumCount -lt 1) {
        throw "No checksums were verified."
    }

    if ($RunDashboardSmoke) {
        Invoke-Checked (Resolve-RootPath "SeekerSvc.Engine.exe") @(
            "dashboard",
            "--once",
            "--db", $DashboardSmokeDbPath
        )
    }

    Write-Host "CareerSeeker alpha release package self-check"
    Write-Host "  root: $Root"
    Write-Host "  manifest: ok"
    Write-Host "  checksums: $checksumCount verified"
    Write-Host "  dashboard smoke: $(if ($RunDashboardSmoke) { "passed" } else { "skipped" })"
}
finally {
    Pop-Location
}
