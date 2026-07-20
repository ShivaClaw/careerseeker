param(
    [switch] $Published,
    [switch] $PublishIfMissing,
    [switch] $DryRun,
    [string] $Configuration = "Release",
    [string] $DbPath = ".appdata/careerseeker-alpha.db",
    [string] $ArtifactsPath = ".appdata/artifacts",
    [string] $SecretsPath = "secrets/env.secrets",
    [string] $ByokVaultPath = ".appdata/secrets/byok-keys.dpapi",
    [string] $GmailClientPath = "secrets/google-oauth-client.json",
    [string] $GmailVaultPath = ".appdata/oauth/gmail-token.dpapi",
    [string] $Email = "",
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
        "alpha",
        "--llm", "byok",
        "--fast-smoke",
        "--gate-semantic-candidates", $GateSemanticCandidates.ToString(),
        "--http-timeout-seconds", $HttpTimeoutSeconds.ToString(),
        "--secrets", $SecretsPath,
        "--key-vault", $ByokVaultPath,
        "--client", $GmailClientPath,
        "--vault", $GmailVaultPath,
        "--db", $DbPath,
        "--artifacts", $ArtifactsPath
    )
    if (-not [string]::IsNullOrWhiteSpace($Email)) {
        $args += @("--email", $Email)
    }

    Write-Host "CareerSeeker Alpha live L1 draft cycle"
    Write-Host "This creates a Gmail draft for review only. It does not send email."
    Write-Host "Provider and OAuth secret values will not be printed."
    Write-Host ""

    if ($DryRun) {
        Write-Host "Dry run: command was assembled but not executed."
        Write-Host "  command: $command"
        Write-Host "  mode: alpha --llm byok --fast-smoke"
        Write-Host "  db: $DbPath"
        Write-Host "  artifacts: $ArtifactsPath"
        Write-Host "  OAuth client: $GmailClientPath"
        Write-Host "  Gmail vault: $GmailVaultPath"
        Write-Host "  BYOK vault: $ByokVaultPath"
        return
    }

    if (-not (Test-Path -LiteralPath $GmailClientPath -PathType Leaf)) {
        throw "Gmail OAuth client JSON not found at '$GmailClientPath'. Run Setup-CareerSeeker-Alpha.cmd, add your Google OAuth client JSON, then run Connect-CareerSeeker-Gmail.cmd first."
    }
    $providerConfigured =
        (Test-Path -LiteralPath $ByokVaultPath -PathType Leaf) -or
        (Test-ConfiguredSecret $SecretsPath "ANTHROPIC_API_KEY") -or
        (Test-ConfiguredSecret $SecretsPath "GEMINI_API_KEY") -or
        (Test-ConfiguredSecret $SecretsPath "GOOGLE_API_KEY")

    if (-not $providerConfigured) {
        throw "Provider keys were not found in '$SecretsPath' or '$ByokVaultPath'. Run Connect-CareerSeeker-Providers.cmd first."
    }

    Invoke-Checked $command $args

    Write-Host ""
    Write-Host "CareerSeeker Alpha live cycle complete."
    Write-Host "Open Gmail Drafts to review the created draft, or open the dashboard to inspect local evidence."
}
finally {
    Pop-Location
}
