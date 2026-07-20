param(
    [switch] $Published,
    [switch] $PublishIfMissing,
    [string] $Configuration = "Release",
    [string] $DbPath = ".appdata/careerseeker-alpha.db",
    [string] $ArtifactsPath = ".appdata/artifacts",
    [string] $SecretsPath = "secrets/env.secrets",
    [string] $ByokVaultPath = ".appdata/secrets/byok-keys.dpapi"
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
    if (-not (Test-Path -LiteralPath $SecretsPath -PathType Leaf)) {
        throw "Provider secrets file not found at '$SecretsPath'. Run Setup-CareerSeeker-Alpha.cmd first, then fill in provider keys locally."
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

    Write-Host "Connecting CareerSeeker Alpha provider keys..."
    Write-Host "Secret values will not be printed."
    Write-Host ""

    Invoke-Checked $command ($prefixArgs + @(
        "import-byok",
        "--secrets", $SecretsPath,
        "--key-vault", $ByokVaultPath
    ))

    Write-Host ""
    Invoke-Checked $command ($prefixArgs + @(
        "doctor",
        "--require-byok",
        "--db", $DbPath,
        "--artifacts", $ArtifactsPath,
        "--secrets", $SecretsPath,
        "--key-vault", $ByokVaultPath
    ))

    $braveConfigured =
        (Test-ConfiguredSecret $SecretsPath "BRAVE_SEARCH_API_KEY") -or
        (Test-ConfiguredSecret $SecretsPath "BRAVE_SEARCH_API") -or
        (Test-ConfiguredSecret $SecretsPath "CAREERSEEKER_BRAVE_SEARCH_API_KEY")

    Write-Host ""
    Write-Host "CareerSeeker Alpha providers are ready."
    Write-Host "  BYOK vault: $ByokVaultPath"
    Write-Host "  Brave Search key: $(if ($braveConfigured) { "configured" } else { "not configured (optional for company research)" })"
}
finally {
    Pop-Location
}
