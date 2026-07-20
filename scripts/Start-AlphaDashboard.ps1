param(
    [switch] $Published,
    [switch] $PublishIfMissing,
    [switch] $Once,
    [switch] $NoOpen,
    [switch] $NoGmailControl,
    [int] $Port = 7777,
    [string] $Configuration = "Release",
    [string] $DbPath = ".appdata/careerseeker-alpha.db",
    [string] $GmailClientPath = "secrets/google-oauth-client.json",
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

    $engineArgs = @(
        "dashboard",
        "--db", $DbPath,
        "--port", $Port.ToString()
    )

    if ($Once) {
        $engineArgs += "--once"
    }

    if (-not $NoGmailControl) {
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
    Write-Host "Starting CareerSeeker alpha dashboard..."
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
