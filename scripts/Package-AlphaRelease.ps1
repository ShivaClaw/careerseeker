param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $OutputDirectory = "output/release",
    [string] $PackageName = "",
    [switch] $NoPublish,
    [switch] $NoDocs
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$engineProject = "src/Engine/SeekerSvc.Engine.csproj"
$publishDir = "src/Engine/bin/$Configuration/net8.0/$Runtime/publish"
$exeName = if ($Runtime.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) {
    "SeekerSvc.Engine.exe"
} else {
    "SeekerSvc.Engine"
}
$setupExeName = "START HERE - CareerSeeker Setup.exe"

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

function Assert-UnderRoot {
    param([string] $Path)

    $fullRoot = [System.IO.Path]::GetFullPath($repoRoot)
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
    $prefix = $fullRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to write outside the repository: $fullPath"
    }
    return $fullPath
}

function Assert-SafePackageName {
    param([string] $Name)

    if ([string]::IsNullOrWhiteSpace($Name)) {
        throw "Package name is required."
    }
    if ([System.IO.Path]::IsPathRooted($Name) -or
        $Name.IndexOfAny([char[]]@('\', '/', ':')) -ge 0 -or
        [System.IO.Path]::GetFileName($Name) -ne $Name) {
        throw "Package name must be a plain .zip file name, not a path: $Name"
    }
    if ($Name.Equals(".zip", [System.StringComparison]::OrdinalIgnoreCase) -or
        $Name.Equals(".", [System.StringComparison]::Ordinal) -or
        $Name.Equals("..", [System.StringComparison]::Ordinal)) {
        throw "Package name must include a non-empty base name."
    }
    return $Name
}

function Get-GitValue {
    param([string[]] $Arguments)

    $output = & git @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $null
    }
    return ($output -join "`n").Trim()
}

Push-Location $repoRoot
try {
    if ([string]::IsNullOrWhiteSpace($PackageName)) {
        $PackageName = "CareerSeeker-alpha-$Runtime.zip"
    }
    if (-not $PackageName.EndsWith(".zip", [System.StringComparison]::OrdinalIgnoreCase)) {
        $PackageName += ".zip"
    }
    $PackageName = Assert-SafePackageName $PackageName
    $outDir = Assert-UnderRoot $OutputDirectory

    if (-not $NoPublish) {
        Invoke-Checked "dotnet" @(
            "publish",
            $engineProject,
            "-c", $Configuration,
            "-r", $Runtime,
            "--self-contained", "true",
            "/p:PublishSingleFile=true"
        )
    }

    $exePath = Join-Path $repoRoot (Join-Path $publishDir $exeName)
    if (-not (Test-Path -LiteralPath $exePath)) {
        throw "Published executable not found: $exePath"
    }

    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    $stageDir = Join-Path $outDir "_stage"
    if (Test-Path -LiteralPath $stageDir) {
        Remove-Item -LiteralPath $stageDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

    Get-ChildItem -LiteralPath (Join-Path $repoRoot $publishDir) -File |
        Where-Object { $_.Extension -ne ".pdb" } |
        ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stageDir $_.Name)
        }
    if ($Runtime.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase) -and
        -not (Test-Path -LiteralPath (Join-Path $stageDir "e_sqlite3.dll"))) {
        $nativeSqlite = Get-ChildItem -LiteralPath (Join-Path $env:USERPROFILE ".nuget/packages/sqlitepclraw.lib.e_sqlite3") `
                -Filter "e_sqlite3.dll" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*\runtimes\$Runtime\native\e_sqlite3.dll" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($null -eq $nativeSqlite) {
            throw "Published output did not include e_sqlite3.dll and the restored $Runtime native SQLite DLL was not found under the NuGet package cache."
        }
        Copy-Item -LiteralPath $nativeSqlite.FullName -Destination (Join-Path $stageDir "e_sqlite3.dll")
    }
    if ($Runtime.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) {
        Copy-Item -LiteralPath (Join-Path $stageDir $exeName) -Destination (Join-Path $stageDir $setupExeName)
    }

    $resourcesDir = Join-Path $stageDir "resources"
    New-Item -ItemType Directory -Force -Path $resourcesDir | Out-Null
    $oauthSource = @(
        "config/google-client.json",
        "config/google-oauth-client.json",
        "secrets/google-oauth-client.json",
        "client_secret.json"
    ) | ForEach-Object { Join-Path $repoRoot $_ } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    $oauthPackaged = $false
    if ($null -ne $oauthSource) {
        Copy-Item -LiteralPath $oauthSource -Destination (Join-Path $resourcesDir "google-client.json")
        $oauthPackaged = $true
    }

    $quickstart = @"
CareerSeeker Alpha

This package contains the local-first L1 Drafts alpha executable.

Start here:

  Double-click START HERE - CareerSeeker Setup.exe.

  It creates the local workspace, stores your Gemini key in the Windows user vault, can extract a profile
  from your resume after consent, asks you to review/approve the profile before import, connects Gmail
  through CareerSeeker's packaged OAuth client, runs readiness checks, and opens the local dashboard.

  Setup creates no Gmail draft. Live draft creation still requires an explicit LIVE confirmation later.

Manual / advanced flow:

  Double-click Advanced Tools/Verify-CareerSeeker-Alpha.cmd to verify the extracted release package before setup.
  Double-click Advanced Tools/Setup-CareerSeeker-Alpha.cmd to create the local workspace.
  Double-click Advanced Tools/Run-CareerSeeker-Demo.cmd to create local demo evidence without Gmail.
    Do this one first. It needs no profile, no API keys, and no Gmail connection, so it is the
    quickest way to see a full cycle end to end before you set anything else up.
  Double-click Advanced Tools/Import-CareerSeeker-Profile.cmd after editing .appdata\profile.template.json.
  Double-click Advanced Tools/Connect-CareerSeeker-Providers.cmd to import AI provider keys without printing them.
    Provider key file: secrets\env.secrets accepts ANTHROPIC_API_KEY, GEMINI_API_KEY or GOOGLE_API_KEY, and optional Brave Search as BRAVE_SEARCH_API_KEY, BRAVE_SEARCH_API, or CAREERSEEKER_BRAVE_SEARCH_API_KEY.
  Double-click Advanced Tools/Connect-CareerSeeker-Gmail.cmd to connect Gmail without creating a draft.
  Double-click Advanced Tools/Check-CareerSeeker-LiveReadiness.cmd to confirm live Gmail/BYOK readiness.
  Double-click Advanced Tools/Run-CareerSeeker-Scout.cmd to discover jobs from public ATS boards without Gmail.
  Double-click Advanced Tools/Research-CareerSeeker-Company.cmd to run live Brave/BYOK company research without Gmail.
  Double-click Advanced Tools/Draft-CareerSeeker-Job.cmd after choosing a job id in the dashboard; it defaults to a no-Gmail dry-run preview and requires typing LIVE before creating a Gmail draft.
    If the selected job was flagged for prompt-injection signals, review it manually and type REVIEWED only when you intentionally want to override the refusal.
  Double-click Advanced Tools/Run-CareerSeeker-Live.cmd for a no-Gmail dry-run preview, or type LIVE there to create one Gmail draft for review.
  Double-click Advanced Tools/Export-CareerSeeker-Audit.cmd to export hash-only audit JSON for review.
  Double-click Advanced Tools/Export-CareerSeeker-Evidence.cmd to package local evidence for review.
  Double-click Advanced Tools/Import-CareerSeeker-Package.cmd to restore a local evidence package into .appdata\imported.
  Double-click Advanced Tools/Start-CareerSeeker-Alpha.cmd to open the local dashboard.
  Double-click Advanced Tools/Install-CareerSeeker-DashboardTask.cmd and type INSTALL to start the dashboard when you sign in.
  Double-click Advanced Tools/Status-CareerSeeker-DashboardTask.cmd to check the dashboard logon task.

Local off-ramps:

  Double-click Advanced Tools/Clear-CareerSeeker-Providers.cmd and type CLEAR to delete the local provider-key vault.
  Double-click Advanced Tools/Disconnect-CareerSeeker-Gmail.cmd and type DISCONNECT to revoke Gmail and delete the local token vault.
  Double-click Advanced Tools/Uninstall-CareerSeeker-DashboardTask.cmd and type UNINSTALL to remove the dashboard logon task.

Command equivalents:

  powershell -ExecutionPolicy Bypass -File .\scripts\Initialize-AlphaWorkspace.ps1
  notepad .appdata\profile.template.json
  powershell -ExecutionPolicy Bypass -File .\scripts\Import-AlphaProfile.ps1 -Published
  powershell -ExecutionPolicy Bypass -File .\scripts\Connect-AlphaProviders.ps1 -Published
  .\$exeName connect-gmail --client secrets\google-oauth-client.json --vault .appdata\oauth\gmail-token.dpapi
  powershell -ExecutionPolicy Bypass -File .\scripts\Check-AlphaLiveReadiness.ps1 -Published -RequireGmail -RequireByok
  powershell -ExecutionPolicy Bypass -File .\scripts\Run-AlphaDemoCycle.ps1 -Published
  powershell -ExecutionPolicy Bypass -File .\scripts\Run-AlphaScoutBoards.ps1 -Published
  powershell -ExecutionPolicy Bypass -File .\scripts\Run-AlphaCompanyResearch.ps1 -Published -Company GitLab -Domain gitlab.com -PreviewOnly
  powershell -ExecutionPolicy Bypass -File .\scripts\Draft-AlphaJob.ps1 -Published -PreviewOnly
  powershell -ExecutionPolicy Bypass -File .\scripts\Run-AlphaLiveCycle.ps1 -Published -DryRun
  powershell -ExecutionPolicy Bypass -File .\scripts\Export-AlphaAudit.ps1 -Published
  powershell -ExecutionPolicy Bypass -File .\scripts\Export-AlphaEvidencePackage.ps1 -Published
  powershell -ExecutionPolicy Bypass -File .\scripts\Import-AlphaPackage.ps1 -Published -PreviewOnly
  powershell -ExecutionPolicy Bypass -File .\scripts\Test-AlphaReleasePackage.ps1 -RunDashboardSmoke
  .\$exeName doctor --db .appdata\careerseeker-alpha.db --artifacts .appdata\artifacts
  powershell -ExecutionPolicy Bypass -File .\scripts\Start-AlphaDashboard.ps1 -Published -Once -NoGmailControl
  powershell -ExecutionPolicy Bypass -File .\scripts\Start-AlphaDashboard.ps1 -Published
  powershell -ExecutionPolicy Bypass -File .\scripts\Manage-AlphaDashboardTask.ps1 -Action Install -Published -DryRun
  powershell -ExecutionPolicy Bypass -File .\scripts\Manage-AlphaDashboardTask.ps1 -Action Status
  powershell -ExecutionPolicy Bypass -File .\scripts\Manage-AlphaDashboardTask.ps1 -Action Uninstall -DryRun
  .\$exeName export-alpha-package --db .appdata\careerseeker-alpha.db --out output\careerseeker-alpha-package.zip
  .\$exeName import-alpha-package --package output\careerseeker-alpha-package.zip

Off-ramp command equivalents:

  .\$exeName clear-byok --key-vault .appdata\secrets\byok-keys.dpapi
  .\$exeName disconnect-gmail --client secrets\google-oauth-client.json --vault .appdata\oauth\gmail-token.dpapi
  powershell -ExecutionPolicy Bypass -File .\scripts\Manage-AlphaDashboardTask.ps1 -Action Uninstall

The L1 alpha creates Gmail drafts only. It has no send path.

The Draft-CareerSeeker-Job.cmd and Run-CareerSeeker-Live.cmd double-click helpers default to no-Gmail dry-run previews and require typing LIVE before creating a Gmail draft.

Draft-CareerSeeker-Job.cmd requires typing REVIEWED before overriding prompt-injection flagged job refusal.

The Gmail and provider off-ramp double-click helpers require typed confirmation before deleting local vaults.

The dashboard logon-task double-click helpers require typed confirmation before changing Windows startup.

For company research, add Brave Search as BRAVE_SEARCH_API_KEY, BRAVE_SEARCH_API, or CAREERSEEKER_BRAVE_SEARCH_API_KEY in secrets\env.secrets.

Read docs\Alpha-Tester-Walkthrough.md for the intended first-run order, safety rails, and evidence locations.
Read docs\CareerSeeker-Alpha2-Onboarding-Spec.md for the Alpha 2.0 Bridge setup contract.

Do not place OAuth tokens, provider keys, resumes, local databases, or generated artifacts in source control.
"@
    Set-Content -LiteralPath (Join-Path $stageDir "README-alpha.txt") -Value $quickstart -Encoding UTF8
    $startHere = @"
CareerSeeker Alpha 2.0 Bridge

First click:

  START HERE - CareerSeeker Setup.exe

What setup does:

  - creates the local CareerSeeker workspace
  - asks for a Gemini API key and stores it in the Windows user vault
  - asks for a recent resume and sends it to Gemini only after consent
  - opens the extracted profile so you can review/edit it before import
  - connects Gmail through CareerSeeker's packaged OAuth client
  - checks readiness and opens the localhost dashboard

Safety:

  - CareerSeeker creates Gmail drafts only
  - setup creates no Gmail draft
  - CareerSeeker has no send path in this alpha
  - API keys and Gmail tokens are stored locally with Windows user protection

Advanced tools are available in the Advanced Tools folder, but most testers should not need them.
"@
    Set-Content -LiteralPath (Join-Path $stageDir "README - Start Here.txt") -Value $startHere -Encoding UTF8

    $advancedDir = Join-Path $stageDir "Advanced Tools"
    New-Item -ItemType Directory -Force -Path $advancedDir | Out-Null
    foreach ($launcher in @(
        "Start-CareerSeeker-Alpha.cmd",
        "Setup-CareerSeeker-Alpha.cmd",
        "Import-CareerSeeker-Profile.cmd",
        "Connect-CareerSeeker-Providers.cmd",
        "Connect-CareerSeeker-Gmail.cmd",
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
        "Uninstall-CareerSeeker-DashboardTask.cmd"
    )) {
        $sourceLauncher = Join-Path $repoRoot $launcher
        $advancedLauncher = Join-Path $advancedDir $launcher
        $content = Get-Content -LiteralPath $sourceLauncher -Raw
        $content = $content.Replace("%~dp0", "%~dp0..\")
        Set-Content -LiteralPath $advancedLauncher -Value $content -Encoding ASCII
    }

    $scriptsDir = Join-Path $stageDir "scripts"
    New-Item -ItemType Directory -Force -Path $scriptsDir | Out-Null
    foreach ($script in @(
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
        Copy-Item -LiteralPath (Join-Path $repoRoot $script) -Destination $scriptsDir
    }

    if (-not $NoDocs) {
        $docsDir = Join-Path $stageDir "docs"
        New-Item -ItemType Directory -Force -Path $docsDir | Out-Null
        foreach ($doc in @(
            "docs/External-Audit-Handoff.md",
            "docs/Alpha-Tester-Walkthrough.md",
            "docs/CareerSeeker-Alpha2-Onboarding-Spec.md",
            "docs/CareerSeeker-Alpha-Build-Checklist.md",
            "docs/Privacy-Policy.md",
            "docs/Support.md",
            "docs/Autonomy-Contract.md"
        )) {
            Copy-Item -LiteralPath (Join-Path $repoRoot $doc) -Destination $docsDir
        }
    }

    $gitStatus = Get-GitValue @("status", "--short")
    $generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    $sourceBranch = Get-GitValue @("rev-parse", "--abbrev-ref", "HEAD")
    $sourceCommit = Get-GitValue @("rev-parse", "HEAD")
    $sourceShortCommit = Get-GitValue @("rev-parse", "--short", "HEAD")
    $sourceDirty = -not [string]::IsNullOrWhiteSpace($gitStatus)

    $auditSnapshot = @"
CareerSeeker Alpha Audit Snapshot

Generated UTC: $generatedAtUtc
Package: $PackageName
Runtime: $Runtime
Source branch: $sourceBranch
Source commit: $sourceCommit
Dirty working tree: $sourceDirty

Package-local verification commands:

  powershell -ExecutionPolicy Bypass -File .\scripts\Test-AlphaReleasePackage.ps1
  powershell -ExecutionPolicy Bypass -File .\scripts\Test-AlphaReleasePackage.ps1 -RunDashboardSmoke
  & ".\$setupExeName" --smoke
  powershell -ExecutionPolicy Bypass -File .\scripts\Start-AlphaDashboard.ps1 -Published -Once -NoGmailControl

Double-click helper entrypoints:

  START HERE - CareerSeeker Setup.exe
  Advanced Tools/Setup-CareerSeeker-Alpha.cmd
  Advanced Tools/Import-CareerSeeker-Profile.cmd
  Advanced Tools/Connect-CareerSeeker-Providers.cmd
  Advanced Tools/Connect-CareerSeeker-Gmail.cmd
  Advanced Tools/Check-CareerSeeker-LiveReadiness.cmd
  Advanced Tools/Clear-CareerSeeker-Providers.cmd
  Advanced Tools/Disconnect-CareerSeeker-Gmail.cmd
  Advanced Tools/Run-CareerSeeker-Demo.cmd
  Advanced Tools/Run-CareerSeeker-Scout.cmd
  Advanced Tools/Research-CareerSeeker-Company.cmd
  Advanced Tools/Draft-CareerSeeker-Job.cmd
  Advanced Tools/Run-CareerSeeker-Live.cmd
  Advanced Tools/Export-CareerSeeker-Audit.cmd
  Advanced Tools/Export-CareerSeeker-Evidence.cmd
  Advanced Tools/Import-CareerSeeker-Package.cmd
  Advanced Tools/Verify-CareerSeeker-Alpha.cmd
  Advanced Tools/Start-CareerSeeker-Alpha.cmd
  Advanced Tools/Install-CareerSeeker-DashboardTask.cmd
  Advanced Tools/Status-CareerSeeker-DashboardTask.cmd
  Advanced Tools/Uninstall-CareerSeeker-DashboardTask.cmd

Safety boundaries:

  L1 creates Gmail drafts only. There is no Gmail send path in the alpha application.
  The release package excludes local SQLite databases, OAuth tokens, DPAPI vaults, provider API keys, resumes, and generated artifacts.
  Secret values are not included in this package or printed by provider, Gmail, or verification scripts.

Cross-checks:

  RELEASE-MANIFEST.json records the packaged files and source commit.
  SHA256SUMS.txt records per-file SHA-256 checksums for the packaged payload.
  docs/Alpha-Tester-Walkthrough.md gives the intended first-run order, safety rails, and evidence locations.
  docs/CareerSeeker-Alpha2-Onboarding-Spec.md gives the Alpha 2.0 Bridge setup contract.
  docs/External-Audit-Handoff.md contains the source-side audit map and repeatable verifier commands.
"@
    Set-Content -LiteralPath (Join-Path $stageDir "AUDIT-SNAPSHOT.txt") -Value $auditSnapshot -Encoding UTF8

    $manifest = [ordered]@{
        format = "careerseeker-alpha-release-v1"
        generatedAtUtc = $generatedAtUtc
        runtime = $Runtime
        packageName = $PackageName
        source = [ordered]@{
            branch = $sourceBranch
            commit = $sourceCommit
            shortCommit = $sourceShortCommit
            dirty = $sourceDirty
        }
        alpha2Bridge = [ordered]@{
            setupExecutable = $setupExeName
            startHere = "README - Start Here.txt"
            appOwnedOAuthClientPackaged = $oauthPackaged
            oauthClientPath = if ($oauthPackaged) { "resources/google-client.json" } else { $null }
        }
        includes = [ordered]@{
            executable = $exeName
            setupExecutable = if ($Runtime.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) { $setupExeName } else { $null }
            startHere = "README - Start Here.txt"
            auditSnapshot = "AUDIT-SNAPSHOT.txt"
            resources = @(Get-ChildItem -LiteralPath $resourcesDir -File |
                Sort-Object Name |
                ForEach-Object { "resources/$($_.Name)" })
            nativeRuntimeDependencies = @(Get-ChildItem -LiteralPath $stageDir -File |
                Where-Object { $_.Name -ne $exeName -and $_.Extension -eq ".dll" } |
                Sort-Object Name |
                ForEach-Object { $_.Name })
            scripts = @(Get-ChildItem -LiteralPath $scriptsDir -File |
                Sort-Object Name |
                ForEach-Object { "scripts/$($_.Name)" })
            launchers = @(Get-ChildItem -LiteralPath $advancedDir -File |
                Sort-Object Name |
                ForEach-Object { "Advanced Tools/$($_.Name)" })
            advancedTools = @(Get-ChildItem -LiteralPath $advancedDir -File |
                Sort-Object Name |
                ForEach-Object { "Advanced Tools/$($_.Name)" })
            docs = if ($NoDocs) { @() } else { @(Get-ChildItem -LiteralPath $docsDir -File |
                Sort-Object Name |
                ForEach-Object { "docs/$($_.Name)" }) }
            checksums = "SHA256SUMS.txt"
        }
        excludes = @(
            "local SQLite databases",
            "OAuth tokens and DPAPI vaults",
            "provider API keys",
            "resumes and generated draft artifacts",
            "debug symbols"
        )
    }
    $manifest |
        ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath (Join-Path $stageDir "RELEASE-MANIFEST.json") -Encoding UTF8

    $stageFull = [System.IO.Path]::GetFullPath($stageDir).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $checksumLines = Get-ChildItem -LiteralPath $stageDir -Recurse -File |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($stageFull.Length).Replace("\", "/")
            $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName
            "$($hash.Hash.ToLowerInvariant())  $relative"
        }
    Set-Content -LiteralPath (Join-Path $stageDir "SHA256SUMS.txt") -Value $checksumLines -Encoding ASCII

    $packagePath = Join-Path $outDir $PackageName
    if (Test-Path -LiteralPath $packagePath) {
        Remove-Item -LiteralPath $packagePath -Force
    }
    Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $packagePath -Force

    Write-Host "CareerSeeker alpha release package"
    Write-Host "  executable: $exePath"
    Write-Host "  package: $packagePath"
    Write-Host "  bytes: $((Get-Item -LiteralPath $packagePath).Length)"
    $contents = "executable, Alpha 2.0 setup executable, native runtime dependencies, double-click launchers, README-alpha.txt, README - Start Here.txt, AUDIT-SNAPSHOT.txt, RELEASE-MANIFEST.json, SHA256SUMS.txt"
    if (-not $NoDocs) {
        $contents += ", docs"
    }
    $contents += ", scripts"
    Write-Host "  contents: $contents"
}
finally {
    Pop-Location
}
