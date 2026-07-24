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
        "START HERE - CareerSeeker Setup.exe",
        "README - Start Here.txt",
        "Advanced Tools/Connect-CareerSeeker-Providers.cmd",
        "Advanced Tools/Connect-CareerSeeker-Gmail.cmd",
        "Advanced Tools/Check-CareerSeeker-LiveReadiness.cmd",
        "Advanced Tools/Clear-CareerSeeker-Providers.cmd",
        "Advanced Tools/Disconnect-CareerSeeker-Gmail.cmd",
        "Advanced Tools/Import-CareerSeeker-Profile.cmd",
        "Advanced Tools/Setup-CareerSeeker-Alpha.cmd",
        "Advanced Tools/Run-CareerSeeker-Demo.cmd",
        "Advanced Tools/Run-CareerSeeker-Scout.cmd",
        "Advanced Tools/Research-CareerSeeker-Company.cmd",
        "Advanced Tools/Draft-CareerSeeker-Job.cmd",
        "Advanced Tools/Run-CareerSeeker-Live.cmd",
        "Advanced Tools/Export-CareerSeeker-Audit.cmd",
        "Advanced Tools/Export-CareerSeeker-Evidence.cmd",
        "Advanced Tools/Import-CareerSeeker-Package.cmd",
        "Advanced Tools/Verify-CareerSeeker-Alpha.cmd",
        "Advanced Tools/Start-CareerSeeker-Alpha.cmd",
        "Advanced Tools/Install-CareerSeeker-DashboardTask.cmd",
        "Advanced Tools/Status-CareerSeeker-DashboardTask.cmd",
        "Advanced Tools/Uninstall-CareerSeeker-DashboardTask.cmd",
        "e_sqlite3.dll",
        "README-alpha.txt",
        "AUDIT-SNAPSHOT.txt",
        "RELEASE-MANIFEST.json",
        "SHA256SUMS.txt",
        "resources/google-client.json",
        "docs/Alpha-Tester-Walkthrough.md",
        "docs/CareerSeeker-Alpha2-Onboarding-Spec.md",
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
    if ($manifest.alpha2Bridge.setupExecutable -ne "START HERE - CareerSeeker Setup.exe") {
        throw "Release manifest does not list the Alpha 2.0 setup executable."
    }
    if ($manifest.includes.setupExecutable -ne "START HERE - CareerSeeker Setup.exe") {
        throw "Release manifest includes block does not list the setup executable."
    }
    if ($manifest.includes.startHere -ne "README - Start Here.txt") {
        throw "Release manifest does not reference README - Start Here.txt."
    }
    if ($manifest.alpha2Bridge.appOwnedOAuthClientPackaged -ne $true) {
        throw "Release manifest does not confirm packaged app-owned OAuth client metadata."
    }
    if ($manifest.alpha2Bridge.oauthClientPath -ne "resources/google-client.json") {
        throw "Release manifest does not point to resources/google-client.json for OAuth client metadata."
    }
    if ($manifest.includes.resources -notcontains "resources/google-client.json") {
        throw "Release manifest does not list resources/google-client.json."
    }
    $oauthClient = Get-Content -LiteralPath (Resolve-RootPath "resources/google-client.json") -Raw | ConvertFrom-Json
    if ($null -eq $oauthClient.installed) {
        throw "Packaged Google OAuth client must be an installed/Desktop client."
    }
    if ($null -ne $oauthClient.web) {
        throw "Packaged Google OAuth client must not be a Web client."
    }
    if ([string]::IsNullOrWhiteSpace($oauthClient.installed.client_id)) {
        throw "Packaged installed OAuth client is missing client_id."
    }
    if ([string]::IsNullOrWhiteSpace($oauthClient.installed.auth_uri) -or
        [string]::IsNullOrWhiteSpace($oauthClient.installed.token_uri)) {
        throw "Packaged installed OAuth client is missing auth_uri or token_uri."
    }
    if ($manifest.includes.docs -notcontains "docs/Alpha-Tester-Walkthrough.md") {
        throw "Release manifest does not list the alpha tester walkthrough."
    }
    if ($manifest.includes.docs -notcontains "docs/CareerSeeker-Alpha2-Onboarding-Spec.md") {
        throw "Release manifest does not list the Alpha 2.0 onboarding spec."
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
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Start-CareerSeeker-Alpha.cmd") {
        throw "Release manifest does not list the double-click alpha launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Setup-CareerSeeker-Alpha.cmd") {
        throw "Release manifest does not list the double-click setup launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Import-CareerSeeker-Profile.cmd") {
        throw "Release manifest does not list the double-click profile import launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Connect-CareerSeeker-Providers.cmd") {
        throw "Release manifest does not list the double-click provider connect launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Connect-CareerSeeker-Gmail.cmd") {
        throw "Release manifest does not list the double-click Gmail connect launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Check-CareerSeeker-LiveReadiness.cmd") {
        throw "Release manifest does not list the double-click live readiness launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Clear-CareerSeeker-Providers.cmd") {
        throw "Release manifest does not list the double-click provider clear launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Disconnect-CareerSeeker-Gmail.cmd") {
        throw "Release manifest does not list the double-click Gmail disconnect launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Run-CareerSeeker-Demo.cmd") {
        throw "Release manifest does not list the double-click demo cycle launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Run-CareerSeeker-Scout.cmd") {
        throw "Release manifest does not list the double-click Scout ingest launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Research-CareerSeeker-Company.cmd") {
        throw "Release manifest does not list the double-click company research launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Draft-CareerSeeker-Job.cmd") {
        throw "Release manifest does not list the double-click selected-job draft launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Run-CareerSeeker-Live.cmd") {
        throw "Release manifest does not list the double-click live alpha launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Export-CareerSeeker-Evidence.cmd") {
        throw "Release manifest does not list the double-click evidence export launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Export-CareerSeeker-Audit.cmd") {
        throw "Release manifest does not list the double-click audit export launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Import-CareerSeeker-Package.cmd") {
        throw "Release manifest does not list the double-click alpha package import launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Verify-CareerSeeker-Alpha.cmd") {
        throw "Release manifest does not list the double-click release verification launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Install-CareerSeeker-DashboardTask.cmd") {
        throw "Release manifest does not list the double-click dashboard task install launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Status-CareerSeeker-DashboardTask.cmd") {
        throw "Release manifest does not list the double-click dashboard task status launcher."
    }
    if ($manifest.includes.launchers -notcontains "Advanced Tools/Uninstall-CareerSeeker-DashboardTask.cmd") {
        throw "Release manifest does not list the double-click dashboard task uninstall launcher."
    }
    if ($manifest.includes.checksums -ne "SHA256SUMS.txt") {
        throw "Release manifest does not reference SHA256SUMS.txt."
    }

    $readme = Get-Content -LiteralPath (Resolve-RootPath "README-alpha.txt") -Raw
    foreach ($snippet in @(
        "Start here",
        "START HERE - CareerSeeker Setup.exe",
        "Setup creates no Gmail draft",
        "Off-ramp command equivalents",
        "Advanced Tools/Verify-CareerSeeker-Alpha.cmd",
        "Advanced Tools/Install-CareerSeeker-DashboardTask.cmd",
        "Advanced Tools/Status-CareerSeeker-DashboardTask.cmd",
        "Advanced Tools/Uninstall-CareerSeeker-DashboardTask.cmd",
        "Manage-AlphaDashboardTask.ps1",
        "Advanced Tools/Research-CareerSeeker-Company.cmd",
        "Run-AlphaCompanyResearch.ps1",
        "Draft-CareerSeeker-Job.cmd after choosing a job id in the dashboard; it defaults to a no-Gmail dry-run preview",
        "type REVIEWED only when you intentionally want to override the refusal",
        "Advanced Tools/Import-CareerSeeker-Package.cmd",
        "Import-AlphaPackage.ps1",
        "Advanced Tools/Export-CareerSeeker-Audit.cmd",
        "Export-AlphaAudit.ps1",
        "Advanced Tools/Check-CareerSeeker-LiveReadiness.cmd",
        "requires typing LIVE before creating a Gmail draft",
        "Draft-CareerSeeker-Job.cmd and Run-CareerSeeker-Live.cmd double-click helpers default to no-Gmail dry-run previews",
        "powershell -ExecutionPolicy Bypass -File .\scripts\Run-AlphaLiveCycle.ps1 -Published -DryRun",
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
    if ($readme.Contains("powershell -ExecutionPolicy Bypass -File .\scripts\Run-AlphaLiveCycle.ps1 -Published`r`n") -or
        $readme.Contains("powershell -ExecutionPolicy Bypass -File .\scripts\Run-AlphaLiveCycle.ps1 -Published`n")) {
        throw "README-alpha.txt shows the live alpha command equivalent without -DryRun."
    }

    $startHere = Get-Content -LiteralPath (Resolve-RootPath "README - Start Here.txt") -Raw
    foreach ($snippet in @(
        "CareerSeeker Alpha 2.0 Bridge",
        "START HERE - CareerSeeker Setup.exe",
        "Gemini, Anthropic, or manual profile setup",
        "tests provider credentials before use",
        "extracts resume text locally",
        "review/edit it before import",
        "packaged OAuth client",
        "setup creates no Gmail draft",
        "has no send path"
    )) {
        if (-not $startHere.Contains($snippet)) {
            throw "README - Start Here.txt missing '$snippet'."
        }
    }

    $liveLauncher = Get-Content -LiteralPath (Resolve-RootPath "Advanced Tools/Run-CareerSeeker-Live.cmd") -Raw
    foreach ($snippet in @(
        "Type LIVE to create one Gmail draft for review",
        "set `"CAREERSEEKER_LIVE_MODE=`"",
        "CAREERSEEKER_LIVE_MODE",
        '$env:CAREERSEEKER_LIVE_MODE -ieq ''LIVE''',
        "CAREERSEEKER_LIVE_WAS_LIVE",
        "-Published -DryRun",
        "No Gmail draft was created"
    )) {
        if (-not $liveLauncher.Contains($snippet)) {
            throw "Run-CareerSeeker-Live.cmd missing '$snippet'."
        }
    }

    $providerClearLauncher = Get-Content -LiteralPath (Resolve-RootPath "Advanced Tools/Clear-CareerSeeker-Providers.cmd") -Raw
    foreach ($snippet in @(
        "Type CLEAR to delete the local provider-key vault",
        "set `"CAREERSEEKER_PROVIDER_CLEAR_MODE=`"",
        "CAREERSEEKER_PROVIDER_CLEAR_MODE",
        '$env:CAREERSEEKER_PROVIDER_CLEAR_MODE -ieq ''CLEAR''',
        "Provider-key clear cancelled"
    )) {
        if (-not $providerClearLauncher.Contains($snippet)) {
            throw "Clear-CareerSeeker-Providers.cmd missing '$snippet'."
        }
    }

    $gmailDisconnectLauncher = Get-Content -LiteralPath (Resolve-RootPath "Advanced Tools/Disconnect-CareerSeeker-Gmail.cmd") -Raw
    foreach ($snippet in @(
        "Type DISCONNECT to revoke Gmail access",
        "set `"CAREERSEEKER_GMAIL_DISCONNECT_MODE=`"",
        "CAREERSEEKER_GMAIL_DISCONNECT_MODE",
        '$env:CAREERSEEKER_GMAIL_DISCONNECT_MODE -ieq ''DISCONNECT''',
        "Gmail disconnect cancelled"
    )) {
        if (-not $gmailDisconnectLauncher.Contains($snippet)) {
            throw "Disconnect-CareerSeeker-Gmail.cmd missing '$snippet'."
        }
    }

    $dashboardTaskInstallLauncher = Get-Content -LiteralPath (Resolve-RootPath "Advanced Tools/Install-CareerSeeker-DashboardTask.cmd") -Raw
    foreach ($snippet in @(
        "Type INSTALL to register the per-user dashboard logon task",
        "set `"CAREERSEEKER_DASHBOARD_TASK_MODE=`"",
        "CAREERSEEKER_DASHBOARD_TASK_MODE",
        '$env:CAREERSEEKER_DASHBOARD_TASK_MODE -ieq ''INSTALL''',
        "Dashboard task install cancelled"
    )) {
        if (-not $dashboardTaskInstallLauncher.Contains($snippet)) {
            throw "Install-CareerSeeker-DashboardTask.cmd missing '$snippet'."
        }
    }

    $dashboardTaskUninstallLauncher = Get-Content -LiteralPath (Resolve-RootPath "Advanced Tools/Uninstall-CareerSeeker-DashboardTask.cmd") -Raw
    foreach ($snippet in @(
        "Type UNINSTALL to remove the per-user dashboard logon task",
        "set `"CAREERSEEKER_DASHBOARD_TASK_MODE=`"",
        "CAREERSEEKER_DASHBOARD_TASK_MODE",
        '$env:CAREERSEEKER_DASHBOARD_TASK_MODE -ieq ''UNINSTALL''',
        "Dashboard task uninstall cancelled"
    )) {
        if (-not $dashboardTaskUninstallLauncher.Contains($snippet)) {
            throw "Uninstall-CareerSeeker-DashboardTask.cmd missing '$snippet'."
        }
    }

    $auditExportLauncher = Get-Content -LiteralPath (Resolve-RootPath "Advanced Tools/Export-CareerSeeker-Audit.cmd") -Raw
    foreach ($snippet in @(
        "set `"CAREERSEEKER_AUDIT_MODE=`"",
        '$env:CAREERSEEKER_AUDIT_MODE -ieq ''PAYLOADS''',
        "-Published -IncludePayloads"
    )) {
        if (-not $auditExportLauncher.Contains($snippet)) {
            throw "Export-CareerSeeker-Audit.cmd missing hardened input snippet '$snippet'."
        }
    }

    $draftJobLauncher = Get-Content -LiteralPath (Resolve-RootPath "Advanced Tools/Draft-CareerSeeker-Job.cmd") -Raw
    foreach ($snippet in @(
        "set `"CAREERSEEKER_JOB_ID=`"",
        "set `"CAREERSEEKER_INJECTION_MODE=`"",
        '$jobIdText = $env:CAREERSEEKER_JOB_ID',
        '[int]::TryParse($jobIdText.Trim(), [ref]$jobId)',
        "[string]::IsNullOrWhiteSpace(`$jobIdText)",
        '$draftArgs = @(''-Published'', ''-JobId'', $jobId)',
        '$env:CAREERSEEKER_DRAFT_MODE -ieq ''LIVE''',
        '$env:CAREERSEEKER_INJECTION_MODE -ieq ''REVIEWED''',
        "'-AllowInjected'",
        '$env:CAREERSEEKER_DRAFT_SCRIPT'
    )) {
        if (-not $draftJobLauncher.Contains($snippet)) {
            throw "Draft-CareerSeeker-Job.cmd missing hardened input snippet '$snippet'."
        }
    }
    if ($draftJobLauncher.Contains('-JobId "%CAREERSEEKER_JOB_ID%"')) {
        throw "Draft-CareerSeeker-Job.cmd still interpolates the job id directly into the batch command line."
    }

    $companyResearchLauncher = Get-Content -LiteralPath (Resolve-RootPath "Advanced Tools/Research-CareerSeeker-Company.cmd") -Raw
    foreach ($snippet in @(
        "set `"CAREERSEEKER_RESEARCH_COMPANY=`"",
        '$company = $env:CAREERSEEKER_RESEARCH_COMPANY',
        "[string]::IsNullOrWhiteSpace(`$company)",
        "Write-Host 'A company name is required.'; exit 1",
        '$researchArgs = @(''-Published'', ''-Company'', $company.Trim())',
        '$env:CAREERSEEKER_RESEARCH_DOMAIN',
        '$env:CAREERSEEKER_RESEARCH_SCRIPT'
    )) {
        if (-not $companyResearchLauncher.Contains($snippet)) {
            throw "Research-CareerSeeker-Company.cmd missing hardened input snippet '$snippet'."
        }
    }
    if ($companyResearchLauncher.Contains('-Company "%CAREERSEEKER_RESEARCH_COMPANY%"') -or
        $companyResearchLauncher.Contains('-Domain "%CAREERSEEKER_RESEARCH_DOMAIN%"')) {
        throw "Research-CareerSeeker-Company.cmd still interpolates typed research values directly into the batch command line."
    }

    $packageImportLauncher = Get-Content -LiteralPath (Resolve-RootPath "Advanced Tools/Import-CareerSeeker-Package.cmd") -Raw
    foreach ($snippet in @(
        "set `"CAREERSEEKER_IMPORT_PACKAGE=`"",
        '$packagePath = if ([string]::IsNullOrWhiteSpace($env:CAREERSEEKER_IMPORT_PACKAGE))',
        '$targetRoot = if ([string]::IsNullOrWhiteSpace($env:CAREERSEEKER_IMPORT_TARGET))',
        '$importArgs = @(''-Published'', ''-PackagePath'', $packagePath, ''-TargetRoot'', $targetRoot)',
        '$env:CAREERSEEKER_IMPORT_MODE -ieq ''OVERWRITE''',
        '$env:CAREERSEEKER_IMPORT_SCRIPT'
    )) {
        if (-not $packageImportLauncher.Contains($snippet)) {
            throw "Import-CareerSeeker-Package.cmd missing hardened input snippet '$snippet'."
        }
    }
    if ($packageImportLauncher.Contains('-PackagePath "%CAREERSEEKER_IMPORT_PACKAGE%"') -or
        $packageImportLauncher.Contains('-TargetRoot "%CAREERSEEKER_IMPORT_TARGET%"')) {
        throw "Import-CareerSeeker-Package.cmd still interpolates typed import paths directly into the batch command line."
    }

    $setupLauncher = Get-Content -LiteralPath (Resolve-RootPath "Advanced Tools/Setup-CareerSeeker-Alpha.cmd") -Raw
    foreach ($snippet in @(
        "Fill secrets\env.secrets locally with ANTHROPIC_API_KEY and GEMINI_API_KEY or GOOGLE_API_KEY",
        "resources\google-client.json",
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
        "START HERE - CareerSeeker Setup.exe",
        "CareerSeeker-Alpha2-Onboarding-Spec.md",
        "Source branch:",
        "Source commit:",
        "Dirty working tree:",
        "Package-local verification commands",
        "Advanced Tools/Import-CareerSeeker-Profile.cmd",
        "Advanced Tools/Connect-CareerSeeker-Providers.cmd",
        "Advanced Tools/Check-CareerSeeker-LiveReadiness.cmd",
        "Advanced Tools/Clear-CareerSeeker-Providers.cmd",
        "Advanced Tools/Disconnect-CareerSeeker-Gmail.cmd",
        "Advanced Tools/Run-CareerSeeker-Demo.cmd",
        "Advanced Tools/Run-CareerSeeker-Scout.cmd",
        "Advanced Tools/Research-CareerSeeker-Company.cmd",
        "Advanced Tools/Draft-CareerSeeker-Job.cmd",
        "Advanced Tools/Run-CareerSeeker-Live.cmd",
        "Advanced Tools/Export-CareerSeeker-Audit.cmd",
        "Advanced Tools/Export-CareerSeeker-Evidence.cmd",
        "Advanced Tools/Import-CareerSeeker-Package.cmd",
        "Advanced Tools/Verify-CareerSeeker-Alpha.cmd",
        "Advanced Tools/Install-CareerSeeker-DashboardTask.cmd",
        "Advanced Tools/Status-CareerSeeker-DashboardTask.cmd",
        "Advanced Tools/Uninstall-CareerSeeker-DashboardTask.cmd",
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
        "First Run (Alpha 2.0 Bridge)",
        "START HERE - CareerSeeker Setup.exe",
        "testers should not create or download Google OAuth JSON",
        'sourceDoc: "resume-ai"',
        "claim-by-claim review",
        "Advanced Tools/Verify-CareerSeeker-Alpha.cmd",
        "Advanced Tools/Connect-CareerSeeker-Providers.cmd",
        "Advanced Tools/Check-CareerSeeker-LiveReadiness.cmd",
        "Advanced Tools/Clear-CareerSeeker-Providers.cmd",
        "Advanced Tools/Disconnect-CareerSeeker-Gmail.cmd",
        "BRAVE_SEARCH_API",
        "Advanced Tools/Research-CareerSeeker-Company.cmd",
        "Advanced Tools/Draft-CareerSeeker-Job.cmd",
        "/evidence.html",
        'type `LIVE`',
        "L1 alpha does not send applications",
        "Secret values are not packaged",
        "prompt-injection signals",
        'type `REVIEWED`'
    )) {
        if (-not $walkthrough.Contains($snippet)) {
            throw "Alpha tester walkthrough missing '$snippet'."
        }
    }

    $alpha2Spec = Get-Content -LiteralPath (Resolve-RootPath "docs/CareerSeeker-Alpha2-Onboarding-Spec.md") -Raw
    foreach ($snippet in @(
        "CareerSeeker Alpha 2.0 Onboarding Spec",
        "Alpha 2.0 Bridge",
        "START HERE - CareerSeeker Setup.exe",
        "Store Secrets Directly In DPAPI",
        'capped at `stated`',
        "untrusted data",
        "Acceptance Criteria"
    )) {
        if (-not $alpha2Spec.Contains($snippet)) {
            throw "Alpha 2.0 spec missing '$snippet'."
        }
    }

    $support = Get-Content -LiteralPath (Resolve-RootPath "docs/Support.md") -Raw
    foreach ($snippet in @(
        "Advanced Tools/Disconnect-CareerSeeker-Gmail.cmd",
        "Advanced Tools/Clear-CareerSeeker-Providers.cmd",
        "Advanced Tools/Export-CareerSeeker-Audit.cmd",
        "Advanced Tools/Export-CareerSeeker-Evidence.cmd",
        "Advanced Tools/Import-CareerSeeker-Package.cmd",
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
        "Advanced Tools/Clear-CareerSeeker-Providers.cmd",
        "Advanced Tools/Export-CareerSeeker-Audit.cmd",
        "Advanced Tools/Export-CareerSeeker-Evidence.cmd",
        "Advanced Tools/Import-CareerSeeker-Package.cmd",
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
        "Advanced Tools/Export-CareerSeeker-Audit.cmd",
        "Advanced Tools/Export-CareerSeeker-Evidence.cmd",
        "Advanced Tools/Import-CareerSeeker-Package.cmd",
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

    $setupOutput = Invoke-CheckedOutput (Resolve-RootPath "START HERE - CareerSeeker Setup.exe") @("--smoke")
    foreach ($snippet in @(
        "CareerSeeker Alpha 2.0 Bridge Setup",
        "Setup smoke completed"
    )) {
        if ($setupOutput -notlike "*$snippet*") {
            throw "Alpha 2.0 setup smoke did not report '$snippet'."
        }
    }

    Write-Host "CareerSeeker alpha release package self-check"
    Write-Host "  root: $Root"
    Write-Host "  manifest: ok"
    Write-Host "  OAuth client type: installed/Desktop"
    Write-Host "  checksums: $checksumCount verified"
    Write-Host "  dashboard smoke: $(if ($RunDashboardSmoke) { "passed" } else { "skipped" })"
    Write-Host "  Alpha 2.0 setup smoke: passed"
}
finally {
    Pop-Location
}
