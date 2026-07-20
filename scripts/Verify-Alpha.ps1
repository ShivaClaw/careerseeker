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

function Assert-Contains {
    param(
        [string] $Content,
        [string[]] $Snippets,
        [string] $Label
    )

    foreach ($snippet in $Snippets) {
        if (-not $Content.Contains($snippet)) {
            throw "$Label missing '$snippet'."
        }
    }
}

function Assert-DoesNotContain {
    param(
        [string] $Content,
        [string[]] $Snippets,
        [string] $Label
    )

    foreach ($snippet in $Snippets) {
        if ($Content.Contains($snippet)) {
            throw "$Label still contains stale wording '$snippet'."
        }
    }
}

function Get-GitValue {
    param([string[]] $Arguments)

    $output = & git @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $null
    }
    return ($output -join "`n").Trim()
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

Invoke-Step "Source-control hygiene smoke" {
    $gitignore = Get-Content -LiteralPath ".gitignore" -Raw
    Assert-Contains $gitignore @(
        'secrets/',
        '.appdata/',
        'tmp/',
        'output/'
    ) ".gitignore"

    $tracked = & git ls-files -- secrets .appdata tmp output 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git ls-files failed while checking generated/local-secret paths: $tracked"
    }

    $trackedText = (($tracked | ForEach-Object { "$_" }) -join "`n").Trim()
    if ($trackedText.Length -ne 0) {
        throw "Generated/local-secret paths are tracked and must be removed from source control:`n$trackedText"
    }

    foreach ($sample in @(
        "secrets/env.secrets",
        ".appdata/oauth/gmail-token.dpapi",
        ".appdata/secrets/byok-keys.dpapi",
        "tmp/verify-alpha-demo/demo.db",
        "output/release/CareerSeeker-alpha-win-x64.zip"
    )) {
        & git check-ignore -q -- $sample
        if ($LASTEXITCODE -ne 0) {
            throw "Expected git to ignore local/generated path '$sample'."
        }
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

Invoke-Step "Docs-site trust copy smoke" {
    $trustSnippets = @(
        "Google user data to train generalized AI or ML models",
        "Export-CareerSeeker-Audit.cmd",
        "Export-CareerSeeker-Evidence.cmd",
        "Import-CareerSeeker-Package.cmd",
        "export-audit",
        "export-alpha-package",
        "import-alpha-package"
    )

    foreach ($relative in @(
        "docs-site/privacy.md",
        "docs-site/privacy.html",
        "docs-site/support.md",
        "docs-site/support.html",
        "docs-site/autonomy-contract.md",
        "docs-site/autonomy-contract.html"
    )) {
        $content = Get-Content -LiteralPath $relative -Raw
        $snippets = $trustSnippets
        if ($relative -like "*support*") {
            $snippets = $trustSnippets | Where-Object { $_ -ne "Google user data to train generalized AI or ML models" }
        }
        if ($relative -like "*autonomy*") {
            $snippets = $trustSnippets | Where-Object { $_ -ne "Google user data to train generalized AI or ML models" }
        }
        Assert-Contains $content $snippets $relative
    }

    $index = Get-Content -LiteralPath "docs-site/index.html" -Raw
    Assert-Contains $index @("privacy.html", "support.html", "autonomy-contract.html") "docs-site/index.html"
}

Invoke-Step "Trust wording smoke" {
    foreach ($relative in @(
        "README.md",
        "src/Engine/README.md",
        "docs/Privacy-Policy.md",
        "docs/Autonomy-Contract.md",
        "docs/External-Audit-Handoff.md",
        "docs-site/privacy.md",
        "docs-site/autonomy-contract.md"
    )) {
        $content = Get-Content -LiteralPath $relative -Raw
        Assert-DoesNotContain $content @("without any send capability") $relative
    }
}

Invoke-Step "Public README and harness count smoke" {
    $readme = Get-Content -LiteralPath "README.md" -Raw
    Assert-Contains $readme @(
        'free local Windows alpha executable',
        'Windows service/tray packaging and the paid Android dashboard still future',
        'no open-source license',
        'all rights are reserved',
        'EngineHarness` (68)',
        'GatewayGateHarness` (34)',
        'admitted hooks stay prompt',
        'Latest offline total: 258 assertions'
    ) "README.md"
    Assert-DoesNotContain $readme @(
        'free Windows service (.exe)'
    ) "README.md"

    $summary = Get-Content -LiteralPath "docs/CareerSeeker-Project-Summary.md" -Raw
    Assert-Contains $summary @(
        'Total: 258 passed, 0 failed.',
        '| `EngineHarness` | 68 passed, 0 failed |',
        '`/evidence.html`',
        '| `GatewayGateHarness` | 34 passed, 0 failed |',
        'typed confirmations for `LIVE`, `CLEAR`, `DISCONNECT`, `INSTALL`, and `UNINSTALL`',
        'typed confirmation for live/dangerous/persistent actions',
        'admitted company hooks stay prompt'
    ) "docs/CareerSeeker-Project-Summary.md"

    $engineReadme = Get-Content -LiteralPath "src/Engine/README.md" -Raw
    Assert-Contains $engineReadme @(
        'Latest offline harness total: 258 passed, 0 failed.',
        '`/evidence.html` exposes a human audit-chain page',
        'visible job ids for selected-job drafting',
        '`INSTALL`',
        '`UNINSTALL`',
        '`CLEAR`',
        '`DISCONNECT`',
        '`LIVE` before creating a Gmail draft'
    ) "src/Engine/README.md"

    $handoff = Get-Content -LiteralPath "docs/External-Audit-Handoff.md" -Raw
    Assert-Contains $handoff @(
        'Latest local offline verifier: `258 passed, 0 failed`.',
        'Verify-Alpha.ps1 -IncludeLive -IncludePublish -IncludeResearch',
        'Fresh live Scout harness, 2026-07-20',
        'BYOK live provider smoke',
        'live Brave/BYOK company research',
        'Cloudflare Email Routing MX records',
        'does not prove the final',
        '## Evidence Map',
        'ATS-clean resume PDF is rendered and attached to Gmail drafts',
        'Real BYOK Tailor and Gate providers are wired through the Gateway',
        'Live ATS board ingest discovers and stores real jobs',
        'Brave Search company research is grounded and fails closed on missing keys',
        'source-control hygiene smoke',
        'Trusted-tester ZIP carries source provenance, payload checksums, and provider-key quickstart guidance',
        'README-alpha provider-key checks',
        'Dashboard controls are loopback, token-protected, and evidence-oriented',
        'controls are hidden for terminal rows',
        '`/evidence.html`',
        'audit payload export requires `PAYLOADS`',
        'Confirmation variables are cleared before prompting and evaluated through',
        'environment-backed PowerShell checks',
        'Free-form tester inputs in selected-job draft, company research, and package import launchers are forwarded',
        'through environment-backed PowerShell argument arrays instead of interpolated directly into batch command lines',
        'Historical audit context, with a supersession note at the top'
    ) "docs/External-Audit-Handoff.md"

    $historicalAudit = Get-Content -LiteralPath "docs/repo-audit-2026-07-13.md" -Raw
    Assert-Contains $historicalAudit @(
        'Current-status note, 2026-07-20',
        'this is preserved as historical audit input, not as current status for',
        'the default verifier reports 258 passed / 0 failed'
    ) "docs/repo-audit-2026-07-13.md"

    Assert-Contains $summary @(
        'Live BYOK harness, 2026-07-20:'
    ) "docs/CareerSeeker-Project-Summary.md"

    $engineProgram = Get-Content -LiteralPath "src/Engine/Program.cs" -Raw
    Assert-Contains $engineProgram @(
        'draft-job --job-id 123',
        '[--secrets secrets/env.secrets]',
        '[--key-vault .appdata/secrets/byok-keys.dpapi]',
        '[--gate-semantic-candidates 3]'
    ) "src/Engine/Program.cs"

    $tailorModel = Get-Content -LiteralPath "src/Tailor/GatewayTailorModel.cs" -Raw
    Assert-Contains $tailorModel @(
        'Do not quote, paraphrase,',
        'It is not candidate evidence.'
    ) "src/Tailor/GatewayTailorModel.cs"

    $packaging = Get-Content -LiteralPath "src/Dispatcher/Packaging.cs" -Raw
    Assert-Contains $packaging @(
        'sb.AppendLine("- " + step);',
        'Review the form, complete any remaining fields, and submit when ready.'
    ) "src/Dispatcher/Packaging.cs"
    if ($packaging.Contains('sb.AppendLine("' + [char]0x2022 + ' " + step);')) {
        throw "src/Dispatcher/Packaging.cs reintroduced a non-ASCII manual-finish bullet."
    }
    if ($packaging.Contains('Review the auto-filled fields and submit.')) {
        throw "src/Dispatcher/Packaging.cs overclaims ATS auto-fill in L1 manual draft instructions."
    }

    $dispatchContracts = Get-Content -LiteralPath "src/Dispatcher/Dispatch.cs" -Raw
    Assert-Contains $dispatchContracts @(
        'string SubjectTemplate = "Application for {title} at {company}"'
    ) "src/Dispatcher/Dispatch.cs"
}

Invoke-Step "Local API security spec smoke" {
    $spec = Get-Content -LiteralPath "docs/CareerSeeker-Spec.md" -Raw
    Assert-Contains $spec @(
        'Local API security is load-bearing',
        'loopback only',
        'per-install control token',
        'validate `Host`, `Origin`, and `Referer`',
        'Content-Type: application/json',
        'no unauthenticated localhost approval or control POST'
    ) "docs/CareerSeeker-Spec.md"
}

Invoke-Step "L2 Gmail relay scope smoke" {
    $spec = Get-Content -LiteralPath "docs/CareerSeeker-Spec.md" -Raw
    Assert-Contains $spec @(
        'any email digest is a separately scoped relay feature',
        'separately scoped email digest only if that L2 relay channel has been enabled',
        'Cloud Pub/Sub topic in our Google project',
        'Gmail address, `historyId`, and timing metadata',
        'gmail.metadata` or `gmail.readonly',
        'gmail.send` only for user-approved L2/L3 sends'
    ) "docs/CareerSeeker-Spec.md"
    Assert-DoesNotContain $spec @(
        'replying STOP to any digest email',
        'First digest tomorrow'
    ) "docs/CareerSeeker-Spec.md"
}

Invoke-Step "LLM provider registry smoke" {
    $spec = Get-Content -LiteralPath "docs/CareerSeeker-Spec.md" -Raw
    Assert-Contains $spec @(
        'Anthropic/Gemini (Google) API key'
    ) "docs/CareerSeeker-Spec.md"
    Assert-DoesNotContain $spec @(
        'Anthropic/OpenAI API key'
    ) "docs/CareerSeeker-Spec.md"

    $gatewayAddendum = Get-Content -LiteralPath "docs/CareerSeeker-Spec-5_6-LLM-Gateway.md" -Raw
    Assert-Contains $gatewayAddendum @(
        'Anthropic / Gemini (Google) key'
    ) "docs/CareerSeeker-Spec-5_6-LLM-Gateway.md"
    Assert-DoesNotContain $gatewayAddendum @(
        'Anthropic / OpenAI / Google key'
    ) "docs/CareerSeeker-Spec-5_6-LLM-Gateway.md"

    $routing = Get-Content -LiteralPath "src/Gateway/Routing.cs" -Raw
    Assert-Contains $routing @(
        'const string pricingAsOf = "2026-07-20"',
        'claude-sonnet-5',
        'claude-sonnet-4-6',
        'gemini-3.1-pro-preview',
        'https://platform.claude.com/docs/en/about-claude/pricing',
        'https://ai.google.dev/gemini-api/docs/gemini-3'
    ) "src/Gateway/Routing.cs"
}

Invoke-Step "Code-signing guidance smoke" {
    $spec = Get-Content -LiteralPath "docs/CareerSeeker-Spec.md" -Raw
    Assert-Contains $spec @(
        'prefer Azure Artifact Signing',
        'EV certificates are no longer a SmartScreen shortcut',
        'Azure Artifact Signing when eligible, otherwise an OV certificate fallback'
    ) "docs/CareerSeeker-Spec.md"
    Assert-DoesNotContain $spec @('Azure Artifact Signing/OV/EV') "docs/CareerSeeker-Spec.md"
}

Invoke-Step "Per-user storage guidance smoke" {
    $spec = Get-Content -LiteralPath "docs/CareerSeeker-Spec.md" -Raw
    Assert-Contains $spec @(
        '%LOCALAPPDATA%\CareerSeeker\seeker.db',
        'must not default to a machine-global `%ProgramData%` path',
        'per-user DPAPI vaults'
    ) "docs/CareerSeeker-Spec.md"

    $roadmap = Get-Content -LiteralPath "docs/CareerSeeker-Integration-Windows-Roadmap.md" -Raw
    Assert-Contains $roadmap @(
        'explicit per-user identity/task model',
        '%LOCALAPPDATA%\CareerSeeker\seeker.db',
        'not a machine-global `%ProgramData%` default'
    ) "docs/CareerSeeker-Integration-Windows-Roadmap.md"
}

Invoke-Step "Alpha secrets checklist smoke" {
    $checklist = Get-Content -LiteralPath "docs/CareerSeeker-Alpha-Build-Checklist.md" -Raw
    Assert-Contains $checklist @(
        'Suggested entries for the current alpha verification path:',
        'ANTHROPIC_API_KEY=...',
        'GEMINI_API_KEY=...',
        'BRAVE_SEARCH_API_KEY=...',
        '`research-company` also accepts `BRAVE_SEARCH_API` and `CAREERSEEKER_BRAVE_SEARCH_API_KEY` as local aliases.'
    ) "docs/CareerSeeker-Alpha-Build-Checklist.md"
    Assert-DoesNotContain $checklist @(
        'CLOUDFLARE_API_TOKEN=...',
        'CLOUDFLARE_ZONE_NAME=careerseeker.app',
        'CAREERSEEKER_GMAIL_TEST_EMAIL=...'
    ) "docs/CareerSeeker-Alpha-Build-Checklist.md"
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
            foreach ($snippet in @("Setup-CareerSeeker-Alpha.cmd", "Import-CareerSeeker-Profile.cmd", "Connect-CareerSeeker-Providers.cmd", "Connect-CareerSeeker-Gmail.cmd", "Check-CareerSeeker-LiveReadiness.cmd", "Clear-CareerSeeker-Providers.cmd", "Disconnect-CareerSeeker-Gmail.cmd", "Run-CareerSeeker-Demo.cmd", "Run-CareerSeeker-Scout.cmd", "Research-CareerSeeker-Company.cmd", "Draft-CareerSeeker-Job.cmd", "Run-CareerSeeker-Live.cmd", "Export-CareerSeeker-Audit.cmd", "Export-CareerSeeker-Evidence.cmd", "Import-CareerSeeker-Package.cmd", "Verify-CareerSeeker-Alpha.cmd", "Start-CareerSeeker-Alpha.cmd", "Install-CareerSeeker-DashboardTask.cmd", "Status-CareerSeeker-DashboardTask.cmd", "Uninstall-CareerSeeker-DashboardTask.cmd", "Manage-AlphaDashboardTask.ps1", "Import-AlphaProfile.ps1", "Import-AlphaPackage.ps1", "Check-AlphaLiveReadiness.ps1", "Connect-AlphaProviders.ps1", "Run-AlphaDemoCycle.ps1", "Run-AlphaScoutBoards.ps1", "Run-AlphaCompanyResearch.ps1", "Draft-AlphaJob.ps1", "Run-AlphaLiveCycle.ps1", "Export-AlphaAudit.ps1", "Export-AlphaEvidencePackage.ps1", "Off-ramp command equivalents", "BRAVE_SEARCH_API", "connect-gmail", "clear-byok", "disconnect-gmail", "Test-AlphaReleasePackage.ps1", "Start-AlphaDashboard.ps1", "NoGmailControl", "Alpha-Tester-Walkthrough.md")) {
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
            foreach ($snippet in @("CareerSeeker Alpha Audit Snapshot", "Package-local verification commands", "Import-CareerSeeker-Profile.cmd", "Connect-CareerSeeker-Providers.cmd", "Check-CareerSeeker-LiveReadiness.cmd", "Clear-CareerSeeker-Providers.cmd", "Disconnect-CareerSeeker-Gmail.cmd", "Run-CareerSeeker-Demo.cmd", "Run-CareerSeeker-Scout.cmd", "Research-CareerSeeker-Company.cmd", "Draft-CareerSeeker-Job.cmd", "Run-CareerSeeker-Live.cmd", "Export-CareerSeeker-Audit.cmd", "Export-CareerSeeker-Evidence.cmd", "Import-CareerSeeker-Package.cmd", "Verify-CareerSeeker-Alpha.cmd", "Install-CareerSeeker-DashboardTask.cmd", "Status-CareerSeeker-DashboardTask.cmd", "Uninstall-CareerSeeker-DashboardTask.cmd", "L1 creates Gmail drafts only", "Secret values are not included", "docs/Alpha-Tester-Walkthrough.md")) {
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
            $expectedSourceCommit = Get-GitValue @("rev-parse", "HEAD")
            if (-not [string]::IsNullOrWhiteSpace($expectedSourceCommit) -and
                -not $manifest.source.commit.Equals($expectedSourceCommit, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Alpha release manifest source commit '$($manifest.source.commit)' does not match current HEAD '$expectedSourceCommit'."
            }
            if ($manifest.source.dirty -ne $false) {
                throw "Alpha release manifest was generated from a dirty working tree."
            }
            if ($manifest.includes.nativeRuntimeDependencies -notcontains "e_sqlite3.dll") {
                throw "Alpha release manifest missing native SQLite dependency."
            }
            if ($manifest.includes.auditSnapshot -ne "AUDIT-SNAPSHOT.txt") {
                throw "Alpha release manifest missing audit snapshot reference."
            }
            if ($manifest.includes.docs -notcontains "docs/Alpha-Tester-Walkthrough.md") {
                throw "Alpha release manifest missing alpha tester walkthrough."
            }
            if ($manifest.includes.scripts -notcontains "scripts/Start-AlphaDashboard.ps1") {
                throw "Alpha release manifest missing dashboard launcher script."
            }
            if ($manifest.includes.scripts -notcontains "scripts/Check-AlphaLiveReadiness.ps1") {
                throw "Alpha release manifest missing live readiness helper script."
            }
            if ($manifest.includes.scripts -notcontains "scripts/Connect-AlphaProviders.ps1") {
                throw "Alpha release manifest missing provider connect helper script."
            }
            if ($manifest.includes.scripts -notcontains "scripts/Draft-AlphaJob.ps1") {
                throw "Alpha release manifest missing selected-job draft helper script."
            }
            if ($manifest.includes.scripts -notcontains "scripts/Import-AlphaProfile.ps1") {
                throw "Alpha release manifest missing profile import helper script."
            }
            if ($manifest.includes.scripts -notcontains "scripts/Run-AlphaDemoCycle.ps1") {
                throw "Alpha release manifest missing demo cycle helper script."
            }
            if ($manifest.includes.scripts -notcontains "scripts/Run-AlphaScoutBoards.ps1") {
                throw "Alpha release manifest missing Scout ingest helper script."
            }
            if ($manifest.includes.scripts -notcontains "scripts/Run-AlphaCompanyResearch.ps1") {
                throw "Alpha release manifest missing company research helper script."
            }
            if ($manifest.includes.scripts -notcontains "scripts/Run-AlphaLiveCycle.ps1") {
                throw "Alpha release manifest missing live alpha helper script."
            }
            if ($manifest.includes.scripts -notcontains "scripts/Export-AlphaEvidencePackage.ps1") {
                throw "Alpha release manifest missing evidence export helper script."
            }
            if ($manifest.includes.scripts -notcontains "scripts/Export-AlphaAudit.ps1") {
                throw "Alpha release manifest missing audit export helper script."
            }
            if ($manifest.includes.scripts -notcontains "scripts/Import-AlphaPackage.ps1") {
                throw "Alpha release manifest missing alpha package import helper script."
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
            if ($manifest.includes.launchers -notcontains "Check-CareerSeeker-LiveReadiness.cmd") {
                throw "Alpha release manifest missing double-click live readiness launcher."
            }
            if ($manifest.includes.launchers -notcontains "Clear-CareerSeeker-Providers.cmd") {
                throw "Alpha release manifest missing double-click provider clear launcher."
            }
            if ($manifest.includes.launchers -notcontains "Disconnect-CareerSeeker-Gmail.cmd") {
                throw "Alpha release manifest missing double-click Gmail disconnect launcher."
            }
            if ($manifest.includes.launchers -notcontains "Run-CareerSeeker-Demo.cmd") {
                throw "Alpha release manifest missing double-click demo cycle launcher."
            }
            if ($manifest.includes.launchers -notcontains "Run-CareerSeeker-Scout.cmd") {
                throw "Alpha release manifest missing double-click Scout ingest launcher."
            }
            if ($manifest.includes.launchers -notcontains "Research-CareerSeeker-Company.cmd") {
                throw "Alpha release manifest missing double-click company research launcher."
            }
            if ($manifest.includes.launchers -notcontains "Draft-CareerSeeker-Job.cmd") {
                throw "Alpha release manifest missing double-click selected-job draft launcher."
            }
            if ($manifest.includes.launchers -notcontains "Run-CareerSeeker-Live.cmd") {
                throw "Alpha release manifest missing double-click live alpha launcher."
            }
            if ($manifest.includes.launchers -notcontains "Export-CareerSeeker-Evidence.cmd") {
                throw "Alpha release manifest missing double-click evidence export launcher."
            }
            if ($manifest.includes.launchers -notcontains "Export-CareerSeeker-Audit.cmd") {
                throw "Alpha release manifest missing double-click audit export launcher."
            }
            if ($manifest.includes.launchers -notcontains "Import-CareerSeeker-Package.cmd") {
                throw "Alpha release manifest missing double-click alpha package import launcher."
            }
            if ($manifest.includes.launchers -notcontains "Verify-CareerSeeker-Alpha.cmd") {
                throw "Alpha release manifest missing double-click release verification launcher."
            }
            if ($manifest.includes.launchers -notcontains "Install-CareerSeeker-DashboardTask.cmd") {
                throw "Alpha release manifest missing double-click dashboard task install launcher."
            }
            if ($manifest.includes.launchers -notcontains "Status-CareerSeeker-DashboardTask.cmd") {
                throw "Alpha release manifest missing double-click dashboard task status launcher."
            }
            if ($manifest.includes.launchers -notcontains "Uninstall-CareerSeeker-DashboardTask.cmd") {
                throw "Alpha release manifest missing double-click dashboard task uninstall launcher."
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

        & (Join-Path $extractRoot "scripts/Manage-AlphaDashboardTask.ps1") `
            -Action Install `
            -Published `
            -DryRun `
            -TaskName "CareerSeeker Alpha Dashboard Package Smoke" `
            -DbPath ".appdata/package-task-smoke.db"
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged dashboard task install dry run failed."
        }

        & (Join-Path $extractRoot "scripts/Manage-AlphaDashboardTask.ps1") `
            -Action Status `
            -TaskName "CareerSeeker Alpha Dashboard Package Smoke"
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged dashboard task status smoke failed."
        }

        & (Join-Path $extractRoot "scripts/Manage-AlphaDashboardTask.ps1") `
            -Action Uninstall `
            -DryRun `
            -TaskName "CareerSeeker Alpha Dashboard Package Smoke"
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged dashboard task uninstall dry run failed."
        }

        & (Join-Path $extractRoot "scripts/Check-AlphaLiveReadiness.ps1") `
            -Published `
            -DbPath ".appdata/package-readiness-smoke.db" `
            -ArtifactsPath ".appdata/package-readiness-artifacts" `
            -SecretsPath "secrets/env.secrets" `
            -ByokVaultPath ".appdata/package-readiness-secrets/byok-keys.dpapi" `
            -GmailClientPath "secrets/google-oauth-client.json" `
            -GmailVaultPath ".appdata/package-readiness-oauth/gmail-token.dpapi"
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged live readiness helper smoke failed."
        }

        & (Join-Path $extractRoot "scripts/Run-AlphaDemoCycle.ps1") `
            -Published `
            -DbPath ".appdata/package-evidence-smoke.db" `
            -ArtifactsPath ".appdata/package-evidence-artifacts" `
            -PackageOutPath "output/package-evidence-internal.zip"
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged demo helper smoke failed."
        }

        & (Join-Path $extractRoot "scripts/Run-AlphaScoutBoards.ps1") `
            -Published `
            -DryRun `
            -DbPath ".appdata/package-scout-smoke.db" `
            -JobDescriptionDirectory ".appdata/package-scout-jds"
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged Scout ingest helper dry run failed."
        }

        & (Join-Path $extractRoot "scripts/Run-AlphaCompanyResearch.ps1") `
            -Published `
            -PreviewOnly `
            -Company "GitLab" `
            -Domain "gitlab.com" `
            -SecretsPath "secrets/env.secrets" `
            -ByokVaultPath ".appdata/package-research-secrets/byok-keys.dpapi"
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged company research helper preview failed."
        }

        & (Join-Path $extractRoot "scripts/Draft-AlphaJob.ps1") `
            -Published `
            -PreviewOnly `
            -JobId 123 `
            -DbPath ".appdata/package-selected-job-smoke.db" `
            -ArtifactsPath ".appdata/package-selected-job-artifacts"
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged selected-job draft helper preview failed."
        }

        & (Join-Path $extractRoot "scripts/Run-AlphaLiveCycle.ps1") `
            -Published `
            -DryRun `
            -DbPath ".appdata/package-live-smoke.db" `
            -ArtifactsPath ".appdata/package-live-artifacts"
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged live alpha helper dry run failed."
        }

        & (Join-Path $extractRoot "scripts/Export-AlphaAudit.ps1") `
            -Published `
            -DbPath ".appdata/package-evidence-smoke.db" `
            -OutputPath "output/package-audit-smoke.json"
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged audit export helper smoke failed."
        }

        $offrampSecretsDir = Join-Path $extractRoot ".appdata/package-offramp-secrets"
        $offrampOauthDir = Join-Path $extractRoot ".appdata/package-offramp-oauth"
        New-Item -ItemType Directory -Force -Path $offrampSecretsDir | Out-Null
        New-Item -ItemType Directory -Force -Path $offrampOauthDir | Out-Null
        & (Join-Path $extractRoot "SeekerSvc.Engine.exe") `
            "clear-byok" `
            "--key-vault" (Join-Path $offrampSecretsDir "byok-keys.dpapi")
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged provider-key clear command smoke failed."
        }

        & (Join-Path $extractRoot "SeekerSvc.Engine.exe") `
            "disconnect-gmail" `
            "--client" (Join-Path $extractRoot "secrets/google-oauth-client.json") `
            "--vault" (Join-Path $offrampOauthDir "gmail-token.dpapi")
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged Gmail disconnect command smoke failed."
        }

        & (Join-Path $extractRoot "scripts/Export-AlphaEvidencePackage.ps1") `
            -Published `
            -DbPath ".appdata/package-evidence-smoke.db" `
            -ArtifactsPath ".appdata/package-evidence-artifacts" `
            -JobDescriptionDirectory ".appdata/job-descriptions" `
            -OutputPath "output/package-evidence-smoke.zip"
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged evidence export helper smoke failed."
        }

        & (Join-Path $extractRoot "scripts/Import-AlphaPackage.ps1") `
            -Published `
            -PackagePath "output/package-evidence-smoke.zip" `
            -TargetRoot ".appdata/package-import-smoke"
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged alpha package import helper smoke failed."
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
