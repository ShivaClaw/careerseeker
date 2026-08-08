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
    [string] $GmailClientPath = "resources/google-client.json",
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

# The pinned offline assertion total. CI runs this whole file on windows-latest, so the real-SQLite
# harnesses above (EngineHarness, StoreParityHarness) are exercised on every push/PR - not just locally.
# This number is the measured total's expected value: the run below fails if the actual sum drifts from
# it, so a dropped harness or deleted assertion can no longer regress silently while the doc-smoke grep
# still finds the stale count. Bump it in lockstep with the per-harness/doc counts (see the drift trap in
# CLAUDE.md). Seven onboarding/provider assertions were added for Alpha 2.0.1. Twenty-three more were
# added when the engine gained a real board-backed feed: 3 scheduler-state, 7 dashboard-status-honesty,
# 10 for the Scout-backed identified feed and its prompt-injection quarantine rail, 2 for the per-cycle
# action cap, and 1 pinning quarantine accounting against that cap. Five adversarial-review assertions
# pin per-job application lookup parity, periodic no-redraft/idempotent cap advancement, and the honest
# discovery-only path. Four crash-recovery assertions pin startup/periodic self-healing and idempotent
# manual-review audit evidence. Six lexical-ranking assertions pin deterministic ordering,
# persistence, and dashboard explanation. Five calibration assertions pin 10/50/200-term profile
# monotonicity, a 120-posting eligibility band, targeted separation at the derived 4.0 threshold, and
# honest job-side rationale/versioning plus the healthy demo's Act decision. One store-parity assertion
# positively pins score detail reads.
# Four engine assertions pin persisted cycle counters/reasons across the engine, dashboard, and audit
# export; one store-parity assertion pins the new telemetry table in memory and SQLite.
# Twelve engine assertions pin adaptive backoff and its board-failure input, clean pause/resume,
# single-instance locking, local control files, honest runtime status, and service-host doctor checks.
$ExpectedOfflineTotal = 412

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
        -GmailClientPath "tmp/verify-alpha-init/resources/google-client.json" `
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

Invoke-Step "Alpha release packaging path safety smoke" {
    $rejected = $false
    try {
        & (Join-Path $PSScriptRoot "Package-AlphaRelease.ps1") `
            -NoPublish `
            -OutputDirectory "tmp/verify-alpha-package-safety" `
            -PackageName "..\escape"
    }
    catch {
        $rejected = $_.Exception.Message.Contains("plain .zip file name")
    }

    if (-not $rejected) {
        throw "Alpha release packaging accepted an unsafe package name."
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
        Assert-Contains $content $snippets $relative
        if ($relative -like "*privacy*") {
            Assert-Contains $content @(
                "Google user data to train generalized AI or ML models",
                "L1 Drafts beta",
                "%LOCALAPPDATA%\CareerSeeker",
                "current Beta MSIX is unsigned"
            ) $relative
        }
        if ($relative -like "*support*") {
            Assert-Contains $content @(
                "Current Beta Actions",
                "%LOCALAPPDATA%\CareerSeeker",
                "Do not combine app uninstall and user-data deletion"
            ) $relative
        }
        if ($relative -like "*autonomy*") {
            Assert-Contains $content @(
                "L1 Drafts beta",
                "Current L1 beta path",
                "%LOCALAPPDATA%\CareerSeeker"
            ) $relative
        }
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

Invoke-Step "Service-grade scheduled task source smoke" {
    $manager = Get-Content -LiteralPath "scripts/Manage-AlphaDashboardTask.ps1" -Raw
    Assert-Contains $manager @(
        '"Pause", "Resume"',
        '-MultipleInstances IgnoreNew',
        '-RestartCount 12',
        '-RunLevel Limited',
        'Request-CleanStop',
        'It was not force-terminated',
        'Local database, vaults, logs, and artifacts were preserved'
    ) "scripts/Manage-AlphaDashboardTask.ps1"

    $supervisor = Get-Content -LiteralPath "scripts/Start-BetaEngineHost.ps1" -Raw
    Assert-Contains $supervisor @(
        '"--service-host"',
        'SupervisorSelfTest',
        '"--max-backoff-seconds"',
        '"--control-dir"',
        'Tee-Object -FilePath $logPath -Append',
        'engine stopped cleanly; supervisor exiting',
        'restart $restart in $delay seconds'
    ) "scripts/Start-BetaEngineHost.ps1"

    $hostSource = Get-Content -LiteralPath "src/Engine/ServiceGradeHost.cs" -Raw
    Assert-Contains $hostSource @(
        'FileShare.None',
        'SingleInstanceLease',
        'pause.request',
        'stop.request'
    ) "src/Engine/ServiceGradeHost.cs"
}

Invoke-Step "Public README and harness count smoke" {
    $readme = Get-Content -LiteralPath "README.md" -Raw
    Assert-Contains $readme @(
        'local-first Windows L1 Drafts beta',
        'one unsigned `win-x64` MSIX',
        'optional startup task that is disabled by default',
        'implicit package activation is discovery-only',
        'Alpha `.cmd` helpers',
        'no open-source license',
        'all rights are reserved',
        '| EngineHarness | 164 |',
        '| ResearcherHarness | 57 |',
        '| HookHarness | 16 |',
        '| GatewayGateHarness | 36 |',
        '| **Total** | **412** |',
        'No implicit draft consent'
    ) "README.md"
    Assert-DoesNotContain $readme @(
        'trusted-tester release ZIP, and',
        'native Windows service/tray packaging'
    ) "README.md"

    $summary = Get-Content -LiteralPath "docs/CareerSeeker-Project-Summary.md" -Raw
    # The harness-count rows live in a Markdown table whose columns may be alignment-padded (a linter
    # re-pads them); collapse runs of spaces so the row assertions tolerate that padding.
    $summaryCollapsed = [regex]::Replace($summary, '[ \t]+', ' ')
    Assert-Contains $summary @(
        'B0-B8 Windows ladder is implemented',
        '| **Total** | **412** |',
        'deterministic local `lexical-v2`',
        'one unsigned MSIX',
        '`%LOCALAPPDATA%\CareerSeeker`',
        'No detector threshold was changed',
        '## Human-only work remaining'
    ) "docs/CareerSeeker-Project-Summary.md"
    Assert-Contains $summaryCollapsed @(
        '| EngineHarness | 164 |',
        '| ResearcherHarness | 57 |',
        '| HookHarness | 16 |',
        '| StoreParityHarness | 25 |',
        '| GatewayGateHarness | 36 |',
        '| LifecycleHarness | 45 |'
    ) "docs/CareerSeeker-Project-Summary.md (harness table, whitespace-normalized)"

    $engineReadme = Get-Content -LiteralPath "src/Engine/README.md" -Raw
    Assert-Contains $engineReadme @(
        '| **Total** | **412** |',
        'default `lexical-v2` ranker is deterministic and local',
        'Final counters distinguish `scored` and `act-eligible`',
        '--migration-output tmp\rehearsal\careerseeker.db',
        'Implicit activation from the installed MSIX is stricter',
        'one `CareerSeeker.exe`',
        '`%LOCALAPPDATA%\CareerSeeker`',
        'Native SCM Windows Service and tray UI are not built'
    ) "src/Engine/README.md"

    $engineCore = Get-Content -LiteralPath "src/Engine/EngineCore.cs" -Raw
    Assert-Contains $engineCore @(
        'public long Scored => Interlocked.Read(ref _scored);',
        'public long ActEligible => Interlocked.Read(ref _actEligible);',
        '_counters.IncScored();',
        '_counters.IncActEligible();'
    ) "src/Engine/EngineCore.cs"

    $engineProgram = Get-Content -LiteralPath "src/Engine/Program.cs" -Raw
    Assert-Contains $engineProgram @(
        'Console.WriteLine($"  scored: {counters.Scored}");',
        'Console.WriteLine($"  act-eligible: {counters.ActEligible}");'
    ) "src/Engine/Program.cs"

    $storeParity = Get-Content -LiteralPath "tests/StoreParityHarness/Program.cs" -Raw
    Assert-Contains $storeParity @(
        '--migration-output',
        'SqliteOpenMode.ReadOnly',
        'sourceHashBefore.SequenceEqual(sourceHashAfter)',
        'Migration output already exists; refusing to overwrite it.'
    ) "tests/StoreParityHarness/Program.cs"

    $handoff = Get-Content -LiteralPath "docs/External-Audit-Handoff.md" -Raw
    Assert-Contains $handoff @(
        'Pinned offline verifier: **412 passed, 0 failed**',
        'B0-B8 work did not repeat Gmail/provider live calls',
        '## Invariant map',
        'Injection signals quarantine before action/model work',
        'Crash-window recovery does not repeat a successful effect',
        'MSIX has one exe and external user data',
        '## Safety surfaces to inspect adversarially',
        'The MSIX is unsigned',
        'Historical Alpha `.cmd` launchers',
        'Local evidence export/import still uses ZIP'
    ) "docs/External-Audit-Handoff.md"

    $calibration = Get-Content -LiteralPath "docs/Scoring-Calibration.md" -Raw
    Assert-Contains $calibration @(
        'strictly nested profiles with',
        '0/120 with both richer supersets',
        '`lexical-v2` formula',
        '| 200 | 1.50–3.88 | 2.99–4.20 | 2.99 | 4.20 | 4.20 | 3.20 | 8/120 (6.7%) |',
        'existing default Act threshold of 4.0 remains inside that gap',
        '164 passed, 0 failed'
    ) "docs/Scoring-Calibration.md"

    $historicalAudit = Get-Content -LiteralPath "docs/repo-audit-2026-07-13.md" -Raw
    Assert-Contains $historicalAudit @(
        'Current-status note, 2026-07-20',
        'this is preserved as historical audit input, not as current status for',
        'the default verifier reports 341 passed / 0 failed'
    ) "docs/repo-audit-2026-07-13.md"

    Assert-Contains $summary @(
        'historical live provider evidence exists'
    ) "docs/CareerSeeker-Project-Summary.md"

    $positioning = Get-Content -LiteralPath "docs/Positioning.md" -Raw
    Assert-Contains $positioning @(
        'Public Claims Register',
        '`**UNPROVEN**`',
        'The installer is signed and trusted by Windows.',
        'CareerSeeker survives reboot unattended.',
        'CareerSeeker is production-ready.',
        'Updating this register never authorizes a deploy.'
    ) "docs/Positioning.md"

    $betaRunbook = Get-Content -LiteralPath "docs/Beta-Runbook.md" -Raw
    Assert-Contains $betaRunbook @(
        'single ordered Sunday list',
        'Deploy the truth copy',
        'Protect `/api/signup`',
        'Process the current OAuth test-user queue',
        'Submit OAuth production verification',
        'Configure production MSIX signing',
        'Disposable Windows installer matrix',
        'Anything not executed remains `PENDING`'
    ) "docs/Beta-Runbook.md"

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
        'const string pricingAsOf = "2026-07-23"',
        'gemini-3.1-flash-lite',
        'claude-sonnet-5',
        'claude-sonnet-4-6',
        'gemini-3.1-pro-preview',
        'https://platform.claude.com/docs/en/about-claude/pricing',
        'https://ai.google.dev/gemini-api/docs/gemini-3'
    ) "src/Gateway/Routing.cs"
}

Invoke-Step "Alpha 2.0 provider onboarding smoke" {
    $setupBridge = Get-Content -LiteralPath "src/Engine/AlphaSetupBridge.cs" -Raw
    Assert-Contains $setupBridge @(
        'AI resume provider',
        'Gemini',
        'Anthropic',
        'Continue without AI',
        'Retesting the saved credential before any resume content can be sent.',
        'The rejected saved credential was removed from the local vault.',
        'Save this credential for a later retry?',
        'ResumeTextExtractor.ExtractAsync',
        'Send the extracted resume text to',
        'gemini-3.1-flash-lite',
        'claude-haiku-4-5'
    ) "src/Engine/AlphaSetupBridge.cs"
    Assert-DoesNotContain $setupBridge @(
        'Save this key anyway?',
        'gemini-2.5-flash-lite',
        'Send this resume to Gemini'
    ) "src/Engine/AlphaSetupBridge.cs"

    $providerDiagnostics = Get-Content -LiteralPath "src/Engine/AlphaProviderDiagnostics.cs" -Raw
    Assert-Contains $providerDiagnostics @(
        'ACCESS_TOKEN_TYPE_UNSUPPORTED',
        'HttpStatusCode.Unauthorized',
        'HttpStatusCode.TooManyRequests',
        'HttpStatusCode.RequestTimeout',
        'CanSaveWithoutSuccessfulTest'
    ) "src/Engine/AlphaProviderDiagnostics.cs"
}

Invoke-Step "Beta onboarding local web flow source smoke" {
    $program = Get-Content -LiteralPath "src/Engine/Program.cs" -Raw
    Assert-Contains $program @(
        'HasFlag("--console")',
        'BetaSetupWebFlow.RunAsync',
        'setup --console'
    ) "src/Engine/Program.cs"

    $webSetup = Get-Content -LiteralPath "src/Engine/BetaSetupWebFlow.cs" -Raw
    Assert-Contains $webSetup @(
        'CareerSeeker runs locally. It creates Gmail drafts only. It never sends applications.',
        'Locally extracted resume text is sent to the selected AI provider only after explicit consent.',
        "Google's gmail.compose scope can allow compose/send capability",
        'Review every claim',
        'Maximum confidence: stated',
        'sourceDoc"] = "resume-ai"',
        'IsInstalledDesktopOAuthClient',
        'CryptographicOperations.FixedTimeEquals',
        'default-src ''none''',
        'PromptQuarantine.Encode(resumeText)',
        'setup --console',
        'ExerciseOfflineSmokeAsync'
    ) "src/Engine/BetaSetupWebFlow.cs"
    Assert-DoesNotContain $webSetup @(
        'Drafts.Send',
        '--sync'
    ) "src/Engine/BetaSetupWebFlow.cs"

    $package = Get-Content -LiteralPath "scripts/Package-AlphaRelease.ps1" -Raw
    Assert-Contains $package @(
        'ten-step local browser flow',
        'accept, edit, and drop controls',
        'caps AI-extracted claims at stated confidence'
    ) "scripts/Package-AlphaRelease.ps1"
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

    $providerConnect = Get-Content -LiteralPath "scripts/Connect-AlphaProviders.ps1" -Raw
    Assert-Contains $providerConnect @(
        'function Test-SecretValue',
        'return -not [string]::IsNullOrWhiteSpace($value)',
        'Brave Search key: $(if ($braveConfigured)'
    ) "scripts/Connect-AlphaProviders.ps1"
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
if ($totalPassed -ne $ExpectedOfflineTotal) {
    throw "Offline assertion total drifted: measured $totalPassed, expected $ExpectedOfflineTotal. A harness or assertion was added/removed without updating `$ExpectedOfflineTotal (and the doc counts). See the drift trap in CLAUDE.md."
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
    Invoke-Step "Package Beta win-x64 MSIX" {
        $packageArgs = @{
            Configuration = $Configuration
            OutputDirectory = $PackageOutputDirectory
        }
        if ($IncludePublish) {
            $packageArgs["NoPublish"] = $true
        }
        & (Join-Path $PSScriptRoot "Package-BetaRelease.ps1") @packageArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Beta MSIX package creation failed."
        }
    }

    Invoke-Step "Beta MSIX structural and executable smoke" {
        $packagePath = Join-Path $PackageOutputDirectory "CareerSeeker-beta-win-x64.msix"
        & (Join-Path $PSScriptRoot "Test-BetaReleasePackage.ps1") `
            -PackagePath $packagePath `
            -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) {
            throw "Beta MSIX package self-check failed."
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
