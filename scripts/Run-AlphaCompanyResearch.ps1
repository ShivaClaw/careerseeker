param(
    [switch] $Published,
    [switch] $PublishIfMissing,
    [switch] $PreviewOnly,
    [string] $Configuration = "Release",
    [string] $Company = "",
    [string] $Domain = "",
    [string] $SecretsPath = "secrets/env.secrets",
    [string] $ByokVaultPath = ".appdata/secrets/byok-keys.dpapi",
    [int] $MaxDocsPerQuery = 5,
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
    if ([string]::IsNullOrWhiteSpace($Company)) {
        if ($PreviewOnly) {
            $Company = "GitLab"
        }
        else {
            $Company = Read-Host "Enter a company name to research"
        }
    }
    if ([string]::IsNullOrWhiteSpace($Company)) {
        throw "A company name is required."
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
        "research-company",
        "--company", $Company,
        "--llm", "byok",
        "--secrets", $SecretsPath,
        "--key-vault", $ByokVaultPath,
        "--max-docs-per-query", $MaxDocsPerQuery.ToString(),
        "--http-timeout-seconds", $HttpTimeoutSeconds.ToString()
    )
    if (-not [string]::IsNullOrWhiteSpace($Domain)) {
        $args += @("--domain", $Domain)
    }

    Write-Host "CareerSeeker Alpha company research"
    Write-Host "This reads public web pages through Brave Search and BYOK dossier modeling."
    Write-Host "It creates no Gmail draft and sends nothing."
    Write-Host "Secret values will not be printed."
    Write-Host ""

    if ($PreviewOnly) {
        Write-Host "Preview only: command was assembled but not executed."
        Write-Host "  command: $command"
        Write-Host "  mode: research-company"
        Write-Host "  company: $Company"
        Write-Host "  domain: $(if ([string]::IsNullOrWhiteSpace($Domain)) { "<none>" } else { $Domain })"
        Write-Host "  secrets: $SecretsPath"
        Write-Host "  BYOK vault: $ByokVaultPath"
        return
    }

    $providerConfigured =
        (Test-Path -LiteralPath $ByokVaultPath -PathType Leaf) -or
        (Test-ConfiguredSecret $SecretsPath "ANTHROPIC_API_KEY") -or
        (Test-ConfiguredSecret $SecretsPath "GEMINI_API_KEY") -or
        (Test-ConfiguredSecret $SecretsPath "GOOGLE_API_KEY")
    if (-not $providerConfigured) {
        throw "Provider keys were not found in '$SecretsPath' or '$ByokVaultPath'. Run Connect-CareerSeeker-Providers.cmd first."
    }

    $braveConfigured =
        (Test-ConfiguredSecret $SecretsPath "BRAVE_SEARCH_API_KEY") -or
        (Test-ConfiguredSecret $SecretsPath "BRAVE_SEARCH_API") -or
        (Test-ConfiguredSecret $SecretsPath "CAREERSEEKER_BRAVE_SEARCH_API_KEY")
    if (-not $braveConfigured) {
        throw "Brave Search was not found in '$SecretsPath' or the process environment. Add BRAVE_SEARCH_API_KEY, BRAVE_SEARCH_API, or CAREERSEEKER_BRAVE_SEARCH_API_KEY."
    }

    Invoke-Checked $command $args

    Write-Host ""
    Write-Host "CareerSeeker Alpha company research complete."
}
finally {
    Pop-Location
}
