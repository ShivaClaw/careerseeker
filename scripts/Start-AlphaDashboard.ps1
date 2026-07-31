param(
    [switch] $Published,
    [switch] $PublishIfMissing,
    [switch] $Once,
    [switch] $NoOpen,
    [switch] $NoGmailControl,
    # Start the engine (discovery -> scoring -> gate -> draft on a timer) instead of the read-only
    # viewer. Without this the dashboard shows only what earlier runs already stored.
    [switch] $Engine,
    [int] $Port = 7777,
    [int] $IntervalSeconds = 900,
    [string] $Configuration = "Release",
    [string] $DbPath = ".appdata/careerseeker-alpha.db",
    [string] $JobDescriptionDirectory = ".appdata/job-descriptions",
    [string] $ArtifactsPath = ".appdata/artifacts",
    [string] $ByokVaultPath = ".appdata/secrets/byok-keys.dpapi",
    [string] $AuditOutPath = "output/careerseeker-audit.json",
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

    if ($Engine) {
        $byokReady = Test-Path -LiteralPath $ByokVaultPath
        $engineArgs = @(
            "run",
            "--db", $DbPath,
            "--artifacts", $ArtifactsPath,
            "--jd-dir", $JobDescriptionDirectory,
            "--audit-out", $AuditOutPath,
            "--port", $Port.ToString(),
            "--interval-seconds", $IntervalSeconds.ToString(),
            "--key-vault", $ByokVaultPath,
            # No provider key means no tailoring model; the discovery-only loop still stores and scores.
            "--llm", $(if ($byokReady) { "byok" } else { "fake" })
        )

        # Drafting needs both Gmail files and a real BYOK provider. With any piece missing, keep the
        # engine useful but discovery-only: never pair the demo/fake model with a real Gmail account,
        # and never record a simulated Gmail operation as DRAFTED.
        $gmailReady = (Test-Path -LiteralPath $GmailVaultPath) -and (Test-Path -LiteralPath $GmailClientPath)
        $draftingReady = $gmailReady -and $byokReady
        if (-not $draftingReady) {
            $engineArgs += "--dry-run"
        }
    }
    else {
        $engineArgs = @(
            "dashboard",
            "--db", $DbPath,
            "--audit-out", $AuditOutPath,
            "--port", $Port.ToString()
        )
    }

    if ($Once) {
        $engineArgs += "--once"
    }

    if (-not $NoGmailControl -and -not ($Engine -and $engineArgs -contains "--dry-run")) {
        $engineArgs += @(
            "--gmail-control",
            "--client", $GmailClientPath,
            "--vault", $GmailVaultPath
        )
    }

    if ($Once) {
        Invoke-Checked $command ($prefixArgs + $engineArgs)
        return
    }

    $url = "http://localhost:$Port/"
    if ($Engine) {
        Write-Host "Starting CareerSeeker engine..."
        Write-Host "The first sweep runs immediately; discovery can take a minute before jobs appear."
    }
    else {
        Write-Host "Starting CareerSeeker alpha dashboard (read-only view; no engine attached)..."
    }
    Write-Host "Dashboard: $url"
    Write-Host "SQLite db: $DbPath"
    Write-Host "Press Enter or Ctrl+C to stop."

    if (-not $NoOpen) {
        Start-Job -ScriptBlock {
            param([string] $Url)
            Start-Sleep -Seconds 1
            Start-Process $Url
        } -ArgumentList $url | Out-Null
    }

    Invoke-Checked $command ($prefixArgs + $engineArgs)
}
finally {
    Pop-Location
}
