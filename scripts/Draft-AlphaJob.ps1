param(
    [switch] $Published,
    [switch] $PublishIfMissing,
    [switch] $PreviewOnly,
    [switch] $Live,
    [switch] $AllowInjected,
    [string] $Configuration = "Release",
    [string] $DbPath = ".appdata/careerseeker-alpha.db",
    [string] $ArtifactsPath = ".appdata/artifacts",
    [string] $SecretsPath = "secrets/env.secrets",
    [string] $ByokVaultPath = ".appdata/secrets/byok-keys.dpapi",
    [string] $GmailClientPath = "secrets/google-oauth-client.json",
    [string] $GmailVaultPath = ".appdata/oauth/gmail-token.dpapi",
    [string] $Email = "",
    [string] $LlmMode = "byok",
    [long] $JobId = 0,
    [int] $GateSemanticCandidates = 3,
    [int] $HttpTimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$engineProject = "src/Engine/SeekerSvc.Engine.csproj"
$packagedExe = "SeekerSvc.Engine.exe"
$publishExe = "src/Engine/bin/$Configuration/net8.0/win-x64/publish/SeekerSvc.Engine.exe"

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

function Test-CommandAvailable {
    param([string] $Command)

    return $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
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

function Test-ConfiguredSecret {
    param(
        [string] $Path,
        [string] $Name
    )

    $envValue = [Environment]::GetEnvironmentVariable($Name)
    return -not [string]::IsNullOrWhiteSpace($envValue) -or (Test-SecretName $Path $Name)
}

Push-Location $repoRoot
try {
    if ($JobId -le 0 -and $PreviewOnly) {
        $JobId = 123
    }
    if ($JobId -le 0) {
        $entered = Read-Host "Enter a job id from the dashboard Jobs page"
        if (-not [long]::TryParse($entered, [ref] $JobId) -or $JobId -le 0) {
            throw "A positive job id is required. Open the dashboard Jobs page, choose a job id, then run this again."
        }
    }

    if ($Published -or $PublishIfMissing) {
        $packagedExePath = Join-Path $repoRoot $packagedExe
        $publishExePath = Join-Path $repoRoot $publishExe
        $exePath = if (Test-Path -LiteralPath $packagedExePath) { $packagedExePath } else { $publishExePath }
        if (-not (Test-Path -LiteralPath $exePath)) {
            if (-not $PublishIfMissing) {
                throw "Published alpha executable not found at '$packagedExe' or '$publishExe'. Re-run with -PublishIfMissing or publish it first."
            }

            if (-not (Test-CommandAvailable "dotnet")) {
                throw "The published executable is missing and dotnet is not available to build it."
            }

            Write-Host "Publishing CareerSeeker alpha executable..."
            Invoke-Checked "dotnet" @(
                "publish",
                $engineProject,
                "-c", $Configuration,
                "-r", "win-x64",
                "--self-contained", "true",
                "/p:PublishSingleFile=true"
            )
            $exePath = $publishExePath
        }

        $command = $exePath
        $prefixArgs = @()
    }
    else {
        if (-not (Test-CommandAvailable "dotnet")) {
            throw "dotnet is required when running from source. Use -Published after publishing the alpha executable."
        }

        $command = "dotnet"
        $prefixArgs = @(
            "run",
            "-c", $Configuration,
            "--project", $engineProject,
            "--"
        )
    }

    $args = $prefixArgs + @(
        "draft-job",
        "--job-id", $JobId.ToString(),
        "--llm", $LlmMode,
        "--gate-semantic-candidates", $GateSemanticCandidates.ToString(),
        "--http-timeout-seconds", $HttpTimeoutSeconds.ToString(),
        "--db", $DbPath,
        "--artifacts", $ArtifactsPath,
        "--secrets", $SecretsPath,
        "--key-vault", $ByokVaultPath
    )
    if (-not $Live) {
        $args += "--dry-run"
    }
    else {
        $args += @("--client", $GmailClientPath, "--vault", $GmailVaultPath)
    }
    if (-not [string]::IsNullOrWhiteSpace($Email)) {
        $args += @("--email", $Email)
    }
    if ($AllowInjected) {
        $args += "--allow-injected"
    }

    Write-Host "CareerSeeker Alpha selected-job draft"
    Write-Host "Default mode is a local dry-run package. Live mode creates a Gmail draft for review only."
    Write-Host "It does not send email."
    Write-Host ""

    if ($PreviewOnly) {
        Write-Host "Preview only: command was assembled but not executed."
        Write-Host "  command: $command"
        Write-Host "  mode: draft-job"
        Write-Host "  job id: $JobId"
        Write-Host "  db: $DbPath"
        Write-Host "  artifacts: $ArtifactsPath"
        Write-Host "  live Gmail draft: $(if ($Live) { "yes" } else { "no" })"
        return
    }

    if (-not (Test-Path -LiteralPath $DbPath -PathType Leaf)) {
        throw "CareerSeeker could not find '$DbPath'. Run Run-CareerSeeker-Scout.cmd first, then choose a job id from the dashboard Jobs page."
    }
    if ($LlmMode.Equals("byok", [System.StringComparison]::OrdinalIgnoreCase)) {
        $providerConfigured =
            (Test-Path -LiteralPath $ByokVaultPath -PathType Leaf) -or
            (Test-ConfiguredSecret $SecretsPath "ANTHROPIC_API_KEY") -or
            (Test-ConfiguredSecret $SecretsPath "GEMINI_API_KEY") -or
            (Test-ConfiguredSecret $SecretsPath "GOOGLE_API_KEY")

        if (-not $providerConfigured) {
            throw "Provider keys were not found in '$SecretsPath' or '$ByokVaultPath'. Run Connect-CareerSeeker-Providers.cmd first."
        }
    }
    if ($Live -and -not (Test-Path -LiteralPath $GmailClientPath -PathType Leaf)) {
        throw "Gmail OAuth client JSON not found at '$GmailClientPath'. Run Connect-CareerSeeker-Gmail.cmd first."
    }

    Invoke-Checked $command $args

    Write-Host ""
    Write-Host "CareerSeeker Alpha selected-job draft complete."
    Write-Host "$(if ($Live) { "Open Gmail Drafts to review it." } else { "Open the dashboard or artifacts folder to review the local dry-run package." })"
}
finally {
    Pop-Location
}
