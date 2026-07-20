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

function Invoke-CheckedOutput {
    param(
        [string] $Command,
        [string[]] $Arguments
    )

    $output = & $Command @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) {
        throw "Command failed: $Command $($Arguments -join ' ')"
    }
    return ($output -join "`n")
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
        "Check-CareerSeeker-LiveReadiness.cmd",
        "Clear-CareerSeeker-Providers.cmd",
        "Disconnect-CareerSeeker-Gmail.cmd",
        "Import-CareerSeeker-Profile.cmd",
        "Setup-CareerSeeker-Alpha.cmd",
        "Run-CareerSeeker-Demo.cmd",
        "Run-CareerSeeker-Scout.cmd",
        "Research-CareerSeeker-Company.cmd",
        "Draft-CareerSeeker-Job.cmd",
        "Run-CareerSeeker-Live.cmd",
        "Export-CareerSeeker-Audit.cmd",
        "Export-CareerSeeker-Evidence.cmd",
        "Import-CareerSeeker-Package.cmd",
        "Verify-CareerSeeker-Alpha.cmd",
        "Start-CareerSeeker-Alpha.cmd",
        "Install-CareerSeeker-DashboardTask.cmd",
        "Status-CareerSeeker-DashboardTask.cmd",
        "Uninstall-CareerSeeker-DashboardTask.cmd",
        "e_sqlite3.dll",
        "README-alpha.txt",
        "AUDIT-SNAPSHOT.txt",
        "RELEASE-MANIFEST.json",
        "SHA256SUMS.txt",
        "docs/Alpha-Tester-Walkthrough.md",
        "scripts/Check-AlphaLiveReadiness.ps1",
        "scripts/Connect-AlphaProviders.ps1",
        "scripts/Draft-AlphaJob.ps1",
        "scripts/Export-AlphaAudit.ps1",
        "scripts/Export-AlphaEvidencePackage.ps1",
        "scripts/Import-AlphaPackage.ps1",
        "scripts/Import-AlphaProfile.ps1",
        "scripts/Run-AlphaDemoCycle.ps1",
        "scripts/Run-AlphaScoutBoards.ps1",
        "scripts/Run-AlphaCompanyResearch.ps1",
        "scripts/Run-AlphaLiveCycle.ps1",
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
    if ([string]::IsNullOrWhiteSpace($manifest.source.branch)) {
        throw "Release manifest does not record a source branch."
    }
    if (-not ($manifest.source.commit -match "^[0-9a-f]{40}$")) {
        throw "Release manifest does not record a full 40-character source commit."
    }
    if (-not ($manifest.source.shortCommit -match "^[0-9a-f]{7,40}$")) {
        throw "Release manifest does not record a source short commit."
    }
    if (-not $manifest.source.commit.StartsWith($manifest.source.shortCommit, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Release manifest source short commit does not match the full commit."
    }
    if ($manifest.source.dirty -isnot [bool]) {
        throw "Release manifest source dirty flag is not boolean."
    }
    if ($manifest.includes.nativeRuntimeDependencies -notcontains "e_sqlite3.dll") {
        throw "Release manifest does not list e_sqlite3.dll."
    }
    if ($manifest.includes.auditSnapshot -ne "AUDIT-SNAPSHOT.txt") {
        throw "Release manifest does not reference AUDIT-SNAPSHOT.txt."
    }
    if ($manifest.includes.docs -notcontains "docs/Alpha-Tester-Walkthrough.md") {
        throw "Release manifest does not list the alpha tester walkthrough."
    }
    if ($manifest.includes.scripts -notcontains "scripts/Start-AlphaDashboard.ps1") {
        throw "Release manifest does not list the dashboard launcher."
    }
    if ($manifest.includes.scripts -notcontains "scripts/Check-AlphaLiveReadiness.ps1") {
        throw "Release manifest does not list the live readiness helper."
    }
    if ($manifest.includes.scripts -notcontains "scripts/Connect-AlphaProviders.ps1") {
        throw "Release manifest does not list the provider connect helper."
    }
    if ($manifest.includes.scripts -notcontains "scripts/Draft-AlphaJob.ps1") {
        throw "Release manifest does not list the selected-job draft helper."
    }
    if ($manifest.includes.scripts -notcontains "scripts/Import-AlphaProfile.ps1") {
        throw "Release manifest does not list the profile import helper."
    }
    if ($manifest.includes.scripts -notcontains "scripts/Run-AlphaDemoCycle.ps1") {
        throw "Release manifest does not list the demo cycle helper."
    }
    if ($manifest.includes.scripts -notcontains "scripts/Run-AlphaScoutBoards.ps1") {
        throw "Release manifest does not list the Scout ingest helper."
    }
    if ($manifest.includes.scripts -notcontains "scripts/Run-AlphaCompanyResearch.ps1") {
        throw "Release manifest does not list the company research helper."
    }
    if ($manifest.includes.scripts -notcontains "scripts/Run-AlphaLiveCycle.ps1") {
        throw "Release manifest does not list the live alpha helper."
    }
    if ($manifest.includes.scripts -notcontains "scripts/Export-AlphaEvidencePackage.ps1") {
        throw "Release manifest does not list the evidence export helper."
    }
    if ($manifest.includes.scripts -notcontains "scripts/Export-AlphaAudit.ps1") {
        throw "Release manifest does not list the audit export helper."
    }
    if ($manifest.includes.scripts -notcontains "scripts/Import-AlphaPackage.ps1") {
        throw "Release manifest does not list the alpha package import helper."
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
    if ($manifest.includes.launchers -notcontains "Import-CareerSeeker-Profile.cmd") {
        throw "Release manifest does not list the double-click profile import launcher."
    }
    if ($manifest.includes.launchers -notcontains "Connect-CareerSeeker-Providers.cmd") {
        throw "Release manifest does not list the double-click provider connect launcher."
    }
    if ($manifest.includes.launchers -notcontains "Connect-CareerSeeker-Gmail.cmd") {
        throw "Release manifest does not list the double-click Gmail connect launcher."
    }
    if ($manifest.includes.launchers -notcontains "Check-CareerSeeker-LiveReadiness.cmd") {
        throw "Release manifest does not list the double-click live readiness launcher."
    }
    if ($manifest.includes.launchers -notcontains "Clear-CareerSeeker-Providers.cmd") {
        throw "Release manifest does not list the double-click provider clear launcher."
    }
    if ($manifest.includes.launchers -notcontains "Disconnect-CareerSeeker-Gmail.cmd") {
        throw "Release manifest does not list the double-click Gmail disconnect launcher."
    }
    if ($manifest.includes.launchers -notcontains "Run-CareerSeeker-Demo.cmd") {
        throw "Release manifest does not list the double-click demo cycle launcher."
    }
    if ($manifest.includes.launchers -notcontains "Run-CareerSeeker-Scout.cmd") {
        throw "Release manifest does not list the double-click Scout ingest launcher."
    }
    if ($manifest.includes.launchers -notcontains "Research-CareerSeeker-Company.cmd") {
        throw "Release manifest does not list the double-click company research launcher."
    }
    if ($manifest.includes.launchers -notcontains "Draft-CareerSeeker-Job.cmd") {
        throw "Release manifest does not list the double-click selected-job draft launcher."
    }
    if ($manifest.includes.launchers -notcontains "Run-CareerSeeker-Live.cmd") {
        throw "Release manifest does not list the double-click live alpha launcher."
    }
    if ($manifest.includes.launchers -notcontains "Export-CareerSeeker-Evidence.cmd") {
        throw "Release manifest does not list the double-click evidence export launcher."
    }
    if ($manifest.includes.launchers -notcontains "Export-CareerSeeker-Audit.cmd") {
        throw "Release manifest does not list the double-click audit export launcher."
    }
    if ($manifest.includes.launchers -notcontains "Import-CareerSeeker-Package.cmd") {
        throw "Release manifest does not list the double-click alpha package import launcher."
    }
    if ($manifest.includes.launchers -notcontains "Verify-CareerSeeker-Alpha.cmd") {
        throw "Release manifest does not list the double-click release verification launcher."
    }
    if ($manifest.includes.launchers -notcontains "Install-CareerSeeker-DashboardTask.cmd") {
        throw "Release manifest does not list the double-click dashboard task install launcher."
    }
    if ($manifest.includes.launchers -notcontains "Status-CareerSeeker-DashboardTask.cmd") {
        throw "Release manifest does not list the double-click dashboard task status launcher."
    }
    if ($manifest.includes.launchers -notcontains "Uninstall-CareerSeeker-DashboardTask.cmd") {
        throw "Release manifest does not list the double-click dashboard task uninstall launcher."
    }
    if ($manifest.includes.checksums -ne "SHA256SUMS.txt") {
        throw "Release manifest does not reference SHA256SUMS.txt."
    }

    $readme = Get-Content -LiteralPath (Resolve-RootPath "README-alpha.txt") -Raw
    foreach ($snippet in @(
        "First-run flow",
        "Off-ramp command equivalents",
        "Verify-CareerSeeker-Alpha.cmd",
        "Install-CareerSeeker-DashboardTask.cmd",
        "Status-CareerSeeker-DashboardTask.cmd",
        "Uninstall-CareerSeeker-DashboardTask.cmd",
        "Manage-AlphaDashboardTask.ps1",
        "Research-CareerSeeker-Company.cmd",
        "Run-AlphaCompanyResearch.ps1",
        "Import-CareerSeeker-Package.cmd",
        "Import-AlphaPackage.ps1",
        "Export-CareerSeeker-Audit.cmd",
        "Export-AlphaAudit.ps1",
        "Check-CareerSeeker-LiveReadiness.cmd",
        "requires typing LIVE before creating a Gmail draft",
        "Provider key file: secrets\env.secrets accepts ANTHROPIC_API_KEY, GEMINI_API_KEY or GOOGLE_API_KEY",
        "For company research, add Brave Search",
        "BRAVE_SEARCH_API",
        "clear-byok",
        "disconnect-gmail",
        "type CLEAR",
        "type DISCONNECT",
        "type INSTALL",
        "type UNINSTALL"
    )) {
        if (-not $readme.Contains($snippet)) {
            throw "README-alpha.txt missing '$snippet'."
        }
    }

    $liveLauncher = Get-Content -LiteralPath (Resolve-RootPath "Run-CareerSeeker-Live.cmd") -Raw
    foreach ($snippet in @(
        "Type LIVE to create one Gmail draft for review",
        "CAREERSEEKER_LIVE_MODE",
        "-Published -DryRun",
        "No Gmail draft was created"
    )) {
        if (-not $liveLauncher.Contains($snippet)) {
            throw "Run-CareerSeeker-Live.cmd missing '$snippet'."
        }
    }

    $providerClearLauncher = Get-Content -LiteralPath (Resolve-RootPath "Clear-CareerSeeker-Providers.cmd") -Raw
    foreach ($snippet in @(
        "Type CLEAR to delete the local provider-key vault",
        "CAREERSEEKER_PROVIDER_CLEAR_MODE",
        "Provider-key clear cancelled"
    )) {
        if (-not $providerClearLauncher.Contains($snippet)) {
            throw "Clear-CareerSeeker-Providers.cmd missing '$snippet'."
        }
    }

    $gmailDisconnectLauncher = Get-Content -LiteralPath (Resolve-RootPath "Disconnect-CareerSeeker-Gmail.cmd") -Raw
    foreach ($snippet in @(
        "Type DISCONNECT to revoke Gmail access",
        "CAREERSEEKER_GMAIL_DISCONNECT_MODE",
        "Gmail disconnect cancelled"
    )) {
        if (-not $gmailDisconnectLauncher.Contains($snippet)) {
            throw "Disconnect-CareerSeeker-Gmail.cmd missing '$snippet'."
        }
    }

    $dashboardTaskInstallLauncher = Get-Content -LiteralPath (Resolve-RootPath "Install-CareerSeeker-DashboardTask.cmd") -Raw
    foreach ($snippet in @(
        "Type INSTALL to register the per-user dashboard logon task",
        "CAREERSEEKER_DASHBOARD_TASK_MODE",
        "Dashboard task install cancelled"
    )) {
        if (-not $dashboardTaskInstallLauncher.Contains($snippet)) {
            throw "Install-CareerSeeker-DashboardTask.cmd missing '$snippet'."
        }
    }

    $dashboardTaskUninstallLauncher = Get-Content -LiteralPath (Resolve-RootPath "Uninstall-CareerSeeker-DashboardTask.cmd") -Raw
    foreach ($snippet in @(
        "Type UNINSTALL to remove the per-user dashboard logon task",
        "CAREERSEEKER_DASHBOARD_TASK_MODE",
        "Dashboard task uninstall cancelled"
    )) {
        if (-not $dashboardTaskUninstallLauncher.Contains($snippet)) {
            throw "Uninstall-CareerSeeker-DashboardTask.cmd missing '$snippet'."
        }
    }

    $setupLauncher = Get-Content -LiteralPath (Resolve-RootPath "Setup-CareerSeeker-Alpha.cmd") -Raw
    foreach ($snippet in @(
        "Fill secrets\env.secrets locally with ANTHROPIC_API_KEY and GEMINI_API_KEY or GOOGLE_API_KEY",
        "BRAVE_SEARCH_API_KEY, BRAVE_SEARCH_API, or CAREERSEEKER_BRAVE_SEARCH_API_KEY",
        "Connect-CareerSeeker-Providers.cmd"
    )) {
        if (-not $setupLauncher.Contains($snippet)) {
            throw "Setup-CareerSeeker-Alpha.cmd missing '$snippet'."
        }
    }

    $auditSnapshot = Get-Content -LiteralPath (Resolve-RootPath "AUDIT-SNAPSHOT.txt") -Raw
    foreach ($snippet in @(
        "CareerSeeker Alpha Audit Snapshot",
        "Source branch:",
        "Source commit:",
        "Dirty working tree:",
        "Package-local verification commands",
        "Import-CareerSeeker-Profile.cmd",
        "Connect-CareerSeeker-Providers.cmd",
        "Check-CareerSeeker-LiveReadiness.cmd",
        "Clear-CareerSeeker-Providers.cmd",
        "Disconnect-CareerSeeker-Gmail.cmd",
        "Run-CareerSeeker-Demo.cmd",
        "Run-CareerSeeker-Scout.cmd",
        "Research-CareerSeeker-Company.cmd",
        "Draft-CareerSeeker-Job.cmd",
        "Run-CareerSeeker-Live.cmd",
        "Export-CareerSeeker-Audit.cmd",
        "Export-CareerSeeker-Evidence.cmd",
        "Import-CareerSeeker-Package.cmd",
        "Verify-CareerSeeker-Alpha.cmd",
        "Install-CareerSeeker-DashboardTask.cmd",
        "Status-CareerSeeker-DashboardTask.cmd",
        "Uninstall-CareerSeeker-DashboardTask.cmd",
        "L1 creates Gmail drafts only",
        "Secret values are not included",
        "RELEASE-MANIFEST.json records the packaged files and source commit",
        "docs/Alpha-Tester-Walkthrough.md"
    )) {
        if (-not $auditSnapshot.Contains($snippet)) {
            throw "Audit snapshot missing '$snippet'."
        }
    }

    $walkthrough = Get-Content -LiteralPath (Resolve-RootPath "docs/Alpha-Tester-Walkthrough.md") -Raw
    foreach ($snippet in @(
        "CareerSeeker Alpha Tester Walkthrough",
        "Verify-CareerSeeker-Alpha.cmd",
        "Connect-CareerSeeker-Providers.cmd",
        "Check-CareerSeeker-LiveReadiness.cmd",
        "Clear-CareerSeeker-Providers.cmd",
        "Disconnect-CareerSeeker-Gmail.cmd",
        "BRAVE_SEARCH_API",
        "Research-CareerSeeker-Company.cmd",
        "Draft-CareerSeeker-Job.cmd",
        'type `LIVE`',
        "L1 alpha does not send applications",
        "Secret values are not packaged",
        "prompt-injection signals"
    )) {
        if (-not $walkthrough.Contains($snippet)) {
            throw "Alpha tester walkthrough missing '$snippet'."
        }
    }

    $support = Get-Content -LiteralPath (Resolve-RootPath "docs/Support.md") -Raw
    foreach ($snippet in @(
        "Disconnect-CareerSeeker-Gmail.cmd",
        "Clear-CareerSeeker-Providers.cmd",
        "Export-CareerSeeker-Audit.cmd",
        "Export-CareerSeeker-Evidence.cmd",
        "Import-CareerSeeker-Package.cmd",
        "export-audit",
        "export-alpha-package",
        "import-alpha-package"
    )) {
        if (-not $support.Contains($snippet)) {
            throw "Support doc missing '$snippet'."
        }
    }

    $privacy = Get-Content -LiteralPath (Resolve-RootPath "docs/Privacy-Policy.md") -Raw
    foreach ($snippet in @(
        "disconnect-gmail",
        "Clear-CareerSeeker-Providers.cmd",
        "Export-CareerSeeker-Audit.cmd",
        "Export-CareerSeeker-Evidence.cmd",
        "Import-CareerSeeker-Package.cmd",
        "export-alpha-package",
        "raw event payloads are opt-in"
    )) {
        if (-not $privacy.Contains($snippet)) {
            throw "Privacy policy missing '$snippet'."
        }
    }

    $autonomy = Get-Content -LiteralPath (Resolve-RootPath "docs/Autonomy-Contract.md") -Raw
    foreach ($snippet in @(
        "disconnect-gmail",
        "packaged disconnect helper",
        "Export-CareerSeeker-Audit.cmd",
        "Export-CareerSeeker-Evidence.cmd",
        "Import-CareerSeeker-Package.cmd",
        "export-alpha-package",
        "import-alpha-package"
    )) {
        if (-not $autonomy.Contains($snippet)) {
            throw "Autonomy contract missing '$snippet'."
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
        $dashboardOutput = Invoke-CheckedOutput (Resolve-RootPath "SeekerSvc.Engine.exe") @(
            "dashboard",
            "--once",
            "--db", $DashboardSmokeDbPath
        )
        foreach ($snippet in @("audit export control: available", "alpha package export control: available")) {
            if ($dashboardOutput -notlike "*$snippet*") {
                throw "Dashboard smoke did not report '$snippet'."
            }
        }
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
