param(
    [switch] $Published,
    [switch] $PublishIfMissing,
    [switch] $SupervisorSelfTest,
    [int] $Port = 7777,
    [int] $IntervalSeconds = 900,
    [int] $MaxBackoffSeconds = 3600,
    [int] $MaximumRestartDelaySeconds = 300,
    [string] $Configuration = "Release",
    [string] $DbPath = ".appdata/careerseeker-alpha.db",
    [string] $JobDescriptionDirectory = ".appdata/job-descriptions",
    [string] $ArtifactsPath = ".appdata/artifacts",
    [string] $ByokVaultPath = ".appdata/secrets/byok-keys.dpapi",
    [string] $AuditOutPath = "output/careerseeker-audit.json",
    [string] $GmailClientPath = "resources/google-client.json",
    [string] $GmailVaultPath = ".appdata/oauth/gmail-token.dpapi",
    [string] $ControlDirectory = ".appdata/engine-control",
    [string] $LogDirectory = ".appdata/logs"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$engineProject = "src/Engine/SeekerSvc.Engine.csproj"
$packagedExe = "SeekerSvc.Engine.exe"
$publishExe = "src/Engine/bin/$Configuration/net8.0/win-x64/publish/SeekerSvc.Engine.exe"

function Test-CommandAvailable {
    param([string] $Command)
    return $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
}

function Invoke-EnginePublish {
    if (-not (Test-CommandAvailable "dotnet")) {
        throw "dotnet is required to publish the missing engine executable."
    }
    & dotnet publish $engineProject -c $Configuration -r win-x64 --self-contained true /p:PublishSingleFile=true
    if ($LASTEXITCODE -ne 0) {
        throw "CareerSeeker engine publish failed with exit code $LASTEXITCODE."
    }
}

function Wait-RestartDelay {
    param(
        [int] $Seconds,
        [string] $StopRequestPath
    )
    for ($remaining = $Seconds; $remaining -gt 0; $remaining--) {
        if (Test-Path -LiteralPath $StopRequestPath) {
            Remove-Item -LiteralPath $StopRequestPath -Force
            return $false
        }
        Start-Sleep -Seconds 1
    }
    return $true
}

Push-Location $repoRoot
try {
    New-Item -ItemType Directory -Force -Path $ControlDirectory, $LogDirectory | Out-Null
    $stopRequest = Join-Path $ControlDirectory "stop.request"
    if (Test-Path -LiteralPath $stopRequest) {
        Remove-Item -LiteralPath $stopRequest -Force
    }

    if ($SupervisorSelfTest) {
        $command = $null
        $prefixArgs = @()
    }
    elseif ($Published -or $PublishIfMissing) {
        $packagedExePath = Join-Path $repoRoot $packagedExe
        $publishExePath = Join-Path $repoRoot $publishExe
        $command = if (Test-Path -LiteralPath $packagedExePath) { $packagedExePath } else { $publishExePath }
        if (-not (Test-Path -LiteralPath $command)) {
            if (-not $PublishIfMissing) {
                throw "Published engine executable not found. Re-run with -PublishIfMissing or publish first."
            }
            Invoke-EnginePublish
            $command = $publishExePath
        }
        $prefixArgs = @()
    }
    else {
        if (-not (Test-CommandAvailable "dotnet")) {
            throw "dotnet is required in source mode. Use -Published for the packaged executable."
        }
        $command = "dotnet"
        $prefixArgs = @("run", "-c", $Configuration, "--project", $engineProject, "--")
    }

    $byokReady = Test-Path -LiteralPath $ByokVaultPath
    $gmailReady = (Test-Path -LiteralPath $GmailVaultPath) -and (Test-Path -LiteralPath $GmailClientPath)
    $engineArgs = @(
        "run",
        "--service-host",
        "--db", $DbPath,
        "--artifacts", $ArtifactsPath,
        "--jd-dir", $JobDescriptionDirectory,
        "--audit-out", $AuditOutPath,
        "--port", $Port.ToString(),
        "--interval-seconds", $IntervalSeconds.ToString(),
        "--max-backoff-seconds", $MaxBackoffSeconds.ToString(),
        "--control-dir", $ControlDirectory,
        "--key-vault", $ByokVaultPath,
        "--llm", $(if ($byokReady) { "byok" } else { "fake" })
    )

    if (-not ($byokReady -and $gmailReady)) {
        $engineArgs += "--dry-run"
    }
    else {
        $engineArgs += @("--client", $GmailClientPath, "--vault", $GmailVaultPath)
    }

    $restart = 0
    while ($true) {
        $logPath = Join-Path $LogDirectory ("engine-" + (Get-Date -Format "yyyy-MM-dd") + ".log")
        if (Test-Path -LiteralPath $stopRequest) {
            Remove-Item -LiteralPath $stopRequest -Force
            "[$(Get-Date -Format "o")] supervisor stop request received; exiting cleanly" |
                Tee-Object -FilePath $logPath -Append
            exit 0
        }
        $stamp = Get-Date -Format "o"
        "[$stamp] starting CareerSeeker scheduled-task engine host" | Tee-Object -FilePath $logPath -Append
        if ($SupervisorSelfTest) {
            "self-test child exited 7" | Tee-Object -FilePath $logPath -Append
            $exitCode = 7
        }
        else {
            & $command @($prefixArgs + $engineArgs) 2>&1 | Tee-Object -FilePath $logPath -Append
            $exitCode = $LASTEXITCODE
        }

        if ($exitCode -eq 0) {
            "[$(Get-Date -Format "o")] engine stopped cleanly; supervisor exiting" |
                Tee-Object -FilePath $logPath -Append
            exit 0
        }

        $restart++
        $delay = [Math]::Min(
            [Math]::Max(1, $MaximumRestartDelaySeconds),
            [Math]::Min(300, 5 * [Math]::Pow(2, [Math]::Min(6, $restart - 1))))
        "[$(Get-Date -Format "o")] engine exited $exitCode; restart $restart in $delay seconds" |
            Tee-Object -FilePath $logPath -Append
        if ($SupervisorSelfTest) {
            Set-Content -LiteralPath $stopRequest -Value "self-test stop" -Encoding Ascii
        }
        if (-not (Wait-RestartDelay -Seconds $delay -StopRequestPath $stopRequest)) {
            "[$(Get-Date -Format "o")] supervisor stop request received during backoff; exiting cleanly" |
                Tee-Object -FilePath $logPath -Append
            exit 0
        }
    }
}
finally {
    Pop-Location
}
