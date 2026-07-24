param(
    [switch] $Published,
    [switch] $PublishIfMissing,
    [switch] $RequireGmail,
    [switch] $RequireByok,
    [string] $Configuration = "Release",
    [string] $DbPath = ".appdata/careerseeker-alpha.db",
    [string] $ArtifactsPath = ".appdata/artifacts",
    [string] $SecretsPath = "secrets/env.secrets",
    [string] $ByokVaultPath = ".appdata/secrets/byok-keys.dpapi",
    [string] $GmailClientPath = "resources/google-client.json",
    [string] $GmailVaultPath = ".appdata/oauth/gmail-token.dpapi"
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

    $doctorArgs = @(
        "doctor",
        "--db", $DbPath,
        "--artifacts", $ArtifactsPath,
        "--secrets", $SecretsPath,
        "--key-vault", $ByokVaultPath,
        "--client", $GmailClientPath,
        "--vault", $GmailVaultPath
    )
    if ($RequireGmail) { $doctorArgs += "--require-gmail" }
    if ($RequireByok) { $doctorArgs += "--require-byok" }

    Write-Host "Checking CareerSeeker Alpha live readiness..."
    Write-Host "  db: $DbPath"
    Write-Host "  artifacts: $ArtifactsPath"
    Write-Host "  secrets: $SecretsPath"
    Write-Host "  BYOK vault: $ByokVaultPath"
    Write-Host "  Gmail client: $GmailClientPath"
    Write-Host "  Gmail vault: $GmailVaultPath"
    Write-Host "  require Gmail: $RequireGmail"
    Write-Host "  require BYOK: $RequireByok"
    Write-Host "  secret values will not be printed."
    Write-Host ""

    Invoke-Checked $command ($prefixArgs + $doctorArgs)
}
finally {
    Pop-Location
}
