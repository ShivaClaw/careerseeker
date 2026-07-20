param(
    [switch] $IncludeLive,
    [switch] $IncludePublish,
    [switch] $IncludePackage,
    [switch] $IncludeResearch,
    [string] $Configuration = "Release",
    [string] $PackageOutputDirectory = "output/release",
    [string] $DbPath = ".appdata/careerseeker-alpha.db",
    [string] $ArtifactsPath = ".appdata/artifacts",
    [string] $SecretsPath = "secrets/env.secrets",
    [string] $ByokVaultPath = ".appdata/secrets/byok-keys.dpapi",
    [string] $GmailClientPath = "secrets/google-oauth-client.json",
    [string] $GmailVaultPath = ".appdata/oauth/gmail-token.dpapi",
    [string] $ResearchCompany = "GitLab",
    [string] $ResearchDomain = "gitlab.com",
    [int] $ResearchMaxDocsPerQuery = 5,
    [int] $ResearchAttempts = 3
)

$ErrorActionPreference = "Stop"

function Invoke-Step {
    param(
        [string] $Name,
        [scriptblock] $Script
    )

    Write-Host ""
    Write-Host "=== $Name ==="
    & $Script
}

function Invoke-Dotnet {
    param([string[]] $DotnetArgs)

    & dotnet @DotnetArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed: dotnet $($DotnetArgs -join ' ')"
    }
}

function Test-SecretName {
    param(
        [string] $Path,
        [string] $Name
    )

    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith("#")) { continue }
        $idx = $trimmed.IndexOf("=")
        if ($idx -le 0) { continue }
        if ($trimmed.Substring(0, $idx).Trim().Equals($Name, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

$offlineProjects = @(
    "tests/Slice/Slice.csproj",
    "tests/EngineHarness/EngineHarness.csproj",
    "tests/ResearcherHarness/ResearcherHarness.csproj",
    "tests/HookHarness/HookHarness.csproj",
    "tests/StoreParityHarness/StoreParityHarness.csproj",
    "tests/GatewayGateHarness/GatewayGateHarness.csproj",
    "tests/DispatcherNoSendHarness/DispatcherNoSendHarness.csproj",
    "tests/LifecycleHarness/LifecycleHarness.csproj",
    "tests/RendererHarness/RendererHarness.csproj"
)

Invoke-Step "Build solution" {
    Invoke-Dotnet @("build", "CareerSeeker.sln", "-c", $Configuration)
}

Invoke-Step "Alpha workspace initializer dry run" {
    & (Join-Path $PSScriptRoot "Initialize-AlphaWorkspace.ps1") `
        -DryRun `
        -DbPath "tmp/verify-alpha-init/alpha.db" `
        -ArtifactsPath "tmp/verify-alpha-init/artifacts" `
        -JobDescriptionDirectory "tmp/verify-alpha-init/job-descriptions" `
        -ProfileTemplatePath "tmp/verify-alpha-init/profile.template.json" `
        -SecretsPath "tmp/verify-alpha-init/secrets/env.secrets" `
        -GmailClientPath "tmp/verify-alpha-init/secrets/google-oauth-client.json" `
        -GmailVaultPath "tmp/verify-alpha-init/oauth/gmail-token.dpapi" `
        -ByokVaultPath "tmp/verify-alpha-init/secrets/byok-keys.dpapi" `
        -OutputDirectory "tmp/verify-alpha-init/output"
    if ($LASTEXITCODE -ne 0) {
        throw "Alpha workspace initializer dry run failed."
    }
}

Invoke-Step "Engine SQLite demo smoke" {
    Invoke-Dotnet @(
        "run",
        "--project", "src/Engine/SeekerSvc.Engine.csproj",
        "-c", $Configuration,
        "--no-build",
        "--",
        "demo",
        "--once",
        "--db", "tmp/verify-alpha-demo/demo.db",
        "--artifacts", "tmp/verify-alpha-demo/artifacts"
    )
}

$totalPassed = 0
$totalFailed = 0
foreach ($project in $offlineProjects) {
    Invoke-Step "Offline harness: $project" {
        $output = & dotnet run --project $project -c $Configuration --no-build 2>&1
        $output | Write-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Offline harness failed: $project"
        }

        $summary = $output | Select-String -Pattern "===\s+(\d+) passed,\s+(\d+) failed\s+===" | Select-Object -Last 1
        if (-not $summary) {
            throw "Offline harness did not print a pass/fail summary: $project"
        }

        $script:totalPassed += [int] $summary.Matches[0].Groups[1].Value
        $script:totalFailed += [int] $summary.Matches[0].Groups[2].Value
    }
}

Write-Host ""
Write-Host "=== Offline total: $totalPassed passed, $totalFailed failed ==="
if ($totalFailed -ne 0) {
    throw "Offline harness failures were reported."
}

if ($IncludePublish) {
    Invoke-Step "Publish win-x64 single-file executable" {
        Invoke-Dotnet @(
            "publish",
            "src/Engine/SeekerSvc.Engine.csproj",
            "-c", $Configuration,
            "-r", "win-x64",
            "--self-contained", "true",
            "/p:PublishSingleFile=true"
        )
    }

    Invoke-Step "Published executable demo smoke" {
        $exe = "src/Engine/bin/$Configuration/net8.0/win-x64/publish/SeekerSvc.Engine.exe"
        if (-not (Test-Path -LiteralPath $exe)) {
            throw "Published executable not found: $exe"
        }
        & $exe demo --once --db ".appdata/publish-smoke.db" --artifacts ".appdata/publish-smoke-artifacts"
        if ($LASTEXITCODE -ne 0) {
            throw "Published executable demo smoke failed."
        }
    }
}

if ($IncludePackage) {
    Invoke-Step "Package trusted-tester alpha ZIP" {
        $packageArgs = @{
            Configuration = $Configuration
            OutputDirectory = $PackageOutputDirectory
        }
        if ($IncludePublish) {
            $packageArgs["NoPublish"] = $true
        }

        & (Join-Path $PSScriptRoot "Package-AlphaRelease.ps1") @packageArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Alpha release package creation failed."
        }

        $packagePath = Join-Path $PackageOutputDirectory "CareerSeeker-alpha-win-x64.zip"
        if (-not (Test-Path -LiteralPath $packagePath)) {
            throw "Alpha release package was not created: $packagePath"
        }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zipPath = (Resolve-Path -LiteralPath $packagePath).Path
        $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
        try {
            $entries = @($zip.Entries | ForEach-Object { $_.FullName.Replace("\", "/") })
            foreach ($required in @(
                "SeekerSvc.Engine.exe",
                "Connect-CareerSeeker-Providers.cmd",
                "Connect-CareerSeeker-Gmail.cmd",
                "Import-CareerSeeker-Profile.cmd",
                "Setup-CareerSeeker-Alpha.cmd",
                "Start-CareerSeeker-Alpha.cmd",
                "e_sqlite3.dll",
                "README-alpha.txt",
                "AUDIT-SNAPSHOT.txt",
                "RELEASE-MANIFEST.json",
                "SHA256SUMS.txt",
                "scripts/Connect-AlphaProviders.ps1",
                "scripts/Import-AlphaProfile.ps1",
                "scripts/Initialize-AlphaWorkspace.ps1",
                "scripts/Start-AlphaDashboard.ps1",
                "scripts/Manage-AlphaDashboardTask.ps1",
                "scripts/Test-AlphaReleasePackage.ps1"
            )) {
                if ($entries -notcontains $required) {
                    throw "Alpha release package missing '$required'."
                }
            }

            $readmeEntry = $zip.GetEntry("README-alpha.txt")
            if ($null -eq $readmeEntry) {
                throw "Alpha release package missing README-alpha.txt."
            }
            $reader = [System.IO.StreamReader]::new($readmeEntry.Open())
            try {
                $readme = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }
            foreach ($snippet in @("Setup-CareerSeeker-Alpha.cmd", "Import-CareerSeeker-Profile.cmd", "Connect-CareerSeeker-Providers.cmd", "Connect-CareerSeeker-Gmail.cmd", "Start-CareerSeeker-Alpha.cmd", "Import-AlphaProfile.ps1", "Connect-AlphaProviders.ps1", "connect-gmail", "Test-AlphaReleasePackage.ps1", "Start-AlphaDashboard.ps1", "NoGmailControl")) {
                if (-not $readme.Contains($snippet)) {
                    throw "Alpha release quickstart missing '$snippet'."
                }
            }

            $auditSnapshotEntry = $zip.GetEntry("AUDIT-SNAPSHOT.txt")
            if ($null -eq $auditSnapshotEntry) {
                throw "Alpha release package missing AUDIT-SNAPSHOT.txt."
            }
            $auditSnapshotReader = [System.IO.StreamReader]::new($auditSnapshotEntry.Open())
            try {
                $auditSnapshot = $auditSnapshotReader.ReadToEnd()
            }
            finally {
                $auditSnapshotReader.Dispose()
            }
            foreach ($snippet in @("CareerSeeker Alpha Audit Snapshot", "Package-local verification commands", "Import-CareerSeeker-Profile.cmd", "Connect-CareerSeeker-Providers.cmd", "L1 creates Gmail drafts only", "Secret values are not included")) {
                if (-not $auditSnapshot.Contains($snippet)) {
                    throw "Alpha release audit snapshot missing '$snippet'."
                }
            }

            $manifestEntry = $zip.GetEntry("RELEASE-MANIFEST.json")
            if ($null -eq $manifestEntry) {
                throw "Alpha release package missing RELEASE-MANIFEST.json."
            }
            $manifestReader = [System.IO.StreamReader]::new($manifestEntry.Open())
            try {
                $manifest = $manifestReader.ReadToEnd() | ConvertFrom-Json
            }
            finally {
                $manifestReader.Dispose()
            }
            if ($manifest.format -ne "careerseeker-alpha-release-v1") {
                throw "Alpha release manifest has unexpected format '$($manifest.format)'."
            }
            if ($manifest.runtime -ne "win-x64") {
                throw "Alpha release manifest has unexpected runtime '$($manifest.runtime)'."
            }
            if ([string]::IsNullOrWhiteSpace($manifest.source.shortCommit)) {
                throw "Alpha release manifest missing source short commit."
            }
            if ($manifest.includes.nativeRuntimeDependencies -notcontains "e_sqlite3.dll") {
                throw "Alpha release manifest missing native SQLite dependency."
            }
            if ($manifest.includes.auditSnapshot -ne "AUDIT-SNAPSHOT.txt") {
                throw "Alpha release manifest missing audit snapshot reference."
            }
            if ($manifest.includes.scripts -notcontains "scripts/Start-AlphaDashboard.ps1") {
                throw "Alpha release manifest missing dashboard launcher script."
            }
            if ($manifest.includes.scripts -notcontains "scripts/Connect-AlphaProviders.ps1") {
                throw "Alpha release manifest missing provider connect helper script."
            }
            if ($manifest.includes.scripts -notcontains "scripts/Import-AlphaProfile.ps1") {
                throw "Alpha release manifest missing profile import helper script."
            }
            if ($manifest.includes.scripts -notcontains "scripts/Test-AlphaReleasePackage.ps1") {
                throw "Alpha release manifest missing package self-check script."
            }
            if ($manifest.includes.launchers -notcontains "Start-CareerSeeker-Alpha.cmd") {
                throw "Alpha release manifest missing double-click alpha launcher."
            }
            if ($manifest.includes.launchers -notcontains "Setup-CareerSeeker-Alpha.cmd") {
                throw "Alpha release manifest missing double-click setup launcher."
            }
            if ($manifest.includes.launchers -notcontains "Import-CareerSeeker-Profile.cmd") {
                throw "Alpha release manifest missing double-click profile import launcher."
            }
            if ($manifest.includes.launchers -notcontains "Connect-CareerSeeker-Providers.cmd") {
                throw "Alpha release manifest missing double-click provider connect launcher."
            }
            if ($manifest.includes.launchers -notcontains "Connect-CareerSeeker-Gmail.cmd") {
                throw "Alpha release manifest missing double-click Gmail connect launcher."
            }
            if ($manifest.includes.checksums -ne "SHA256SUMS.txt") {
                throw "Alpha release manifest missing checksum reference."
            }
        }
        finally {
            $zip.Dispose()
        }

        $extractRoot = Join-Path "tmp" "verify-alpha-package-run"
        if (Test-Path -LiteralPath $extractRoot) {
            Remove-Item -LiteralPath $extractRoot -Recurse -Force
        }
        New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
        Expand-Archive -LiteralPath $packagePath -DestinationPath $extractRoot -Force

        & (Join-Path $extractRoot "scripts/Test-AlphaReleasePackage.ps1") `
            -RunDashboardSmoke `
            -DashboardSmokeDbPath ".appdata/package-self-check.db"
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged release self-check failed."
        }

        & (Join-Path $extractRoot "scripts/Start-AlphaDashboard.ps1") `
            -Published `
            -Once `
            -NoGmailControl `
            -DbPath ".appdata/package-smoke.db"
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged dashboard launcher smoke failed."
        }
    }
}

if ($IncludeLive) {
    Invoke-Step "Import BYOK provider keys" {
        if (-not (Test-SecretName $SecretsPath "ANTHROPIC_API_KEY")) {
            throw "Missing ANTHROPIC_API_KEY in $SecretsPath"
        }
        if (-not ((Test-SecretName $SecretsPath "GEMINI_API_KEY") -or (Test-SecretName $SecretsPath "GOOGLE_API_KEY"))) {
            throw "Missing GEMINI_API_KEY or GOOGLE_API_KEY in $SecretsPath"
        }
        Invoke-Dotnet @(
            "run", "--project", "src/Engine/SeekerSvc.Engine.csproj",
            "-c", $Configuration, "--no-build", "--",
            "import-byok",
            "--secrets", $SecretsPath,
            "--key-vault", $ByokVaultPath
        )
    }

    Invoke-Step "BYOK live provider smoke" {
        Invoke-Dotnet @(
            "run", "--project", "tests/ByokLiveHarness/ByokLiveHarness.csproj",
            "-c", $Configuration, "--no-build", "--",
            "--secrets", $SecretsPath,
            "--key-vault", $ByokVaultPath
        )
    }

    Invoke-Step "Startup doctor with Gmail and BYOK requirements" {
        Invoke-Dotnet @(
            "run", "--project", "src/Engine/SeekerSvc.Engine.csproj",
            "-c", $Configuration, "--no-build", "--",
            "doctor",
            "--require-gmail",
            "--require-byok",
            "--db", $DbPath,
            "--artifacts", $ArtifactsPath,
            "--secrets", $SecretsPath,
            "--key-vault", $ByokVaultPath,
            "--client", $GmailClientPath,
            "--vault", $GmailVaultPath
        )
    }

    Invoke-Step "Dashboard one-shot smoke" {
        Invoke-Dotnet @(
            "run", "--project", "src/Engine/SeekerSvc.Engine.csproj",
            "-c", $Configuration, "--no-build", "--",
            "dashboard",
            "--once",
            "--db", $DbPath,
            "--gmail-control",
            "--client", $GmailClientPath,
            "--vault", $GmailVaultPath
        )
    }
}

if ($IncludeResearch) {
    Invoke-Step "Live Brave/BYOK company research smoke" {
        if (-not (
            (Test-SecretName $SecretsPath "BRAVE_SEARCH_API_KEY") -or
            (Test-SecretName $SecretsPath "BRAVE_SEARCH_API") -or
            (Test-SecretName $SecretsPath "CAREERSEEKER_BRAVE_SEARCH_API_KEY"))) {
            throw "Missing BRAVE_SEARCH_API_KEY, BRAVE_SEARCH_API, or CAREERSEEKER_BRAVE_SEARCH_API_KEY in $SecretsPath"
        }

        $maxAttempts = [Math]::Max(1, $ResearchAttempts)
        for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
            Write-Host "Research attempt $attempt of $maxAttempts"
            $output = & dotnet run --project "src/Engine/SeekerSvc.Engine.csproj" `
                -c $Configuration --no-build -- `
                "research-company" `
                "--company" $ResearchCompany `
                "--domain" $ResearchDomain `
                "--llm" "byok" `
                "--secrets" $SecretsPath `
                "--key-vault" $ByokVaultPath `
                "--max-docs-per-query" $ResearchMaxDocsPerQuery.ToString() 2>&1
            $output | Write-Host
            if ($LASTEXITCODE -ne 0) {
                if ($attempt -eq $maxAttempts) {
                    throw "Live company research smoke failed."
                }
                continue
            }

            $summary = $output | Select-String -Pattern "facts:\s+(\d+)" | Select-Object -Last 1
            if (-not $summary) {
                if ($attempt -eq $maxAttempts) {
                    throw "Live company research smoke did not print a grounded fact count."
                }
                continue
            }

            $factCount = [int] $summary.Matches[0].Groups[1].Value
            if ($factCount -gt 0) {
                return
            }

            if ($attempt -eq $maxAttempts) {
                throw "Live company research smoke returned zero grounded facts."
            }

            Write-Host "Research returned zero grounded facts; retrying..."
        }
    }
}

Write-Host ""
Write-Host "CareerSeeker alpha verification complete."
