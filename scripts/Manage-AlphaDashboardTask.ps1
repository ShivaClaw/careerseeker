param(
    [ValidateSet("Install", "Uninstall", "Start", "Stop", "Pause", "Resume", "Status")]
    [string] $Action = "Status",
    [switch] $DryRun,
    [switch] $Published,
    [switch] $PublishIfMissing,
    [string] $TaskName = "CareerSeeker Beta Engine",
    [int] $Port = 7777,
    [int] $IntervalSeconds = 900,
    [int] $MaxBackoffSeconds = 3600,
    [string] $Configuration = "Release",
    [string] $DbPath = ".appdata/careerseeker-alpha.db",
    [string] $JobDescriptionDirectory = ".appdata/job-descriptions",
    [string] $ArtifactsPath = ".appdata/artifacts",
    [string] $ByokVaultPath = ".appdata/secrets/byok-keys.dpapi",
    [string] $AuditOutPath = "output/careerseeker-audit.json",
    [string] $GmailClientPath = "resources/google-client.json",
    [string] $GmailVaultPath = ".appdata/oauth/gmail-token.dpapi",
    [string] $ControlDirectory = ".appdata/engine-control",
    [string] $LogDirectory = ".appdata/logs",
    [int] $StopTimeoutSeconds = 45
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$launcherPath = Join-Path $repoRoot "scripts/Start-BetaEngineHost.ps1"
$controlPath = Join-Path $repoRoot $ControlDirectory
$pauseRequest = Join-Path $controlPath "pause.request"
$stopRequest = Join-Path $controlPath "stop.request"

function Format-CommandArgument {
    param([string] $Value)
    if ($Value -match '[\s"]') {
        return '"' + ($Value -replace '"', '\"') + '"'
    }
    return $Value
}

function Join-CommandArguments {
    param([string[]] $Arguments)
    return ($Arguments | ForEach-Object { Format-CommandArgument $_ }) -join " "
}

function Get-LauncherArguments {
    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $launcherPath,
        "-Port", $Port.ToString(),
        "-IntervalSeconds", $IntervalSeconds.ToString(),
        "-MaxBackoffSeconds", $MaxBackoffSeconds.ToString(),
        "-Configuration", $Configuration,
        "-DbPath", $DbPath,
        "-JobDescriptionDirectory", $JobDescriptionDirectory,
        "-ArtifactsPath", $ArtifactsPath,
        "-ByokVaultPath", $ByokVaultPath,
        "-AuditOutPath", $AuditOutPath,
        "-GmailClientPath", $GmailClientPath,
        "-GmailVaultPath", $GmailVaultPath,
        "-ControlDirectory", $ControlDirectory,
        "-LogDirectory", $LogDirectory
    )
    if ($Published) { $arguments += "-Published" }
    if ($PublishIfMissing) { $arguments += "-PublishIfMissing" }
    return $arguments
}

function Get-ExistingTask {
    Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
}

function Write-TaskCommand {
    param([string[]] $Arguments)
    Write-Host "Task: $TaskName"
    Write-Host "Program: powershell.exe"
    Write-Host "Arguments: $(Join-CommandArguments $Arguments)"
}

function Set-ControlRequest {
    param([string] $Path)
    New-Item -ItemType Directory -Force -Path $controlPath | Out-Null
    Set-Content -LiteralPath $Path -Value (Get-Date -Format "o") -Encoding Ascii
}

function Remove-ControlRequest {
    param([string] $Path)
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Force
    }
}

function Request-CleanStop {
    $task = Get-ExistingTask
    if (-not $task -or $task.State -ne "Running") {
        return
    }
    Set-ControlRequest $stopRequest
    $deadline = (Get-Date).AddSeconds([Math]::Max(1, $StopTimeoutSeconds))
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $task = Get-ExistingTask
        if (-not $task -or $task.State -ne "Running") {
            return
        }
    }
    throw "Engine did not acknowledge the clean stop request within $StopTimeoutSeconds seconds. It was not force-terminated."
}

if (-not (Get-Module -ListAvailable -Name ScheduledTasks)) {
    throw "Windows ScheduledTasks module is not available on this machine."
}
if (-not (Test-Path -LiteralPath $launcherPath)) {
    throw "Beta engine host launcher not found: $launcherPath"
}

$launcherArgs = Get-LauncherArguments

switch ($Action) {
    "Install" {
        Write-TaskCommand $launcherArgs
        Write-Host "Trigger: at current user logon"
        Write-Host "Restart policy: 12 attempts, one-minute task-level interval; child supervisor uses capped exponential backoff"
        Write-Host "Multiple instances: IgnoreNew"

        $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $taskAction = New-ScheduledTaskAction -Execute "powershell.exe" -Argument (Join-CommandArguments $launcherArgs)
        $trigger = New-ScheduledTaskTrigger -AtLogOn -User $currentUser
        $settings = New-ScheduledTaskSettingsSet `
            -AllowStartIfOnBatteries `
            -DontStopIfGoingOnBatteries `
            -MultipleInstances IgnoreNew `
            -ExecutionTimeLimit ([TimeSpan]::Zero) `
            -RestartCount 12 `
            -RestartInterval (New-TimeSpan -Minutes 1) `
            -StartWhenAvailable
        $principal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel Limited
        $definition = New-ScheduledTask `
            -Action $taskAction `
            -Trigger $trigger `
            -Settings $settings `
            -Principal $principal `
            -Description "Runs the local CareerSeeker Beta engine at logon with clean controls, logging, and restart backoff."

        if ($DryRun) {
            Write-Host "Task definition validated by Windows Task Scheduler cmdlets."
            Write-Host "  restart count: $($definition.Settings.RestartCount)"
            Write-Host "  restart interval: $($definition.Settings.RestartInterval)"
            Write-Host "  multiple instances: $($definition.Settings.MultipleInstances)"
            Write-Host "Dry run only; scheduled task was not registered."
            break
        }

        Register-ScheduledTask `
            -TaskName $TaskName `
            -InputObject $definition `
            -Force | Out-Null
        Write-Host "Installed scheduled task: $TaskName"
    }

    "Uninstall" {
        if (-not (Get-ExistingTask)) {
            Write-Host "Scheduled task not found: $TaskName"
            break
        }
        if ($DryRun) {
            Write-Host "Dry run only; scheduled task would be cleanly stopped and removed: $TaskName"
            break
        }
        Request-CleanStop
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "Removed scheduled task. Local database, vaults, logs, and artifacts were preserved."
    }

    "Start" {
        if ($DryRun) {
            Write-Host "Dry run only; scheduled task would be started: $TaskName"
            break
        }
        if (-not (Get-ExistingTask)) { throw "Scheduled task not found: $TaskName" }
        Remove-ControlRequest $stopRequest
        Remove-ControlRequest $pauseRequest
        Start-ScheduledTask -TaskName $TaskName
        Write-Host "Started scheduled task: $TaskName"
    }

    "Stop" {
        if ($DryRun) {
            Write-Host "Dry run only; a clean stop would be requested: $TaskName"
            break
        }
        if (-not (Get-ExistingTask)) { throw "Scheduled task not found: $TaskName" }
        Request-CleanStop
        Write-Host "Stopped scheduled task cleanly: $TaskName"
    }

    "Pause" {
        if ($DryRun) {
            Write-Host "Dry run only; pause request would be written: $pauseRequest"
            break
        }
        Set-ControlRequest $pauseRequest
        Write-Host "Paused discovery cycles; dashboard and local controls remain available."
    }

    "Resume" {
        if ($DryRun) {
            Write-Host "Dry run only; pause request would be removed: $pauseRequest"
            break
        }
        Remove-ControlRequest $pauseRequest
        Write-Host "Resumed discovery cycles."
    }

    "Status" {
        $task = Get-ExistingTask
        if (-not $task) {
            Write-Host "Scheduled task not installed: $TaskName"
            break
        }
        $info = Get-ScheduledTaskInfo -TaskName $TaskName
        Write-Host "Scheduled task: $TaskName"
        Write-Host "  state: $($task.State)"
        Write-Host "  cycle control: $(if (Test-Path -LiteralPath $pauseRequest) { 'paused' } else { 'active' })"
        Write-Host "  last run: $($info.LastRunTime)"
        Write-Host "  next run: $($info.NextRunTime)"
        Write-Host "  last result: $($info.LastTaskResult)"
        Write-Host "  logs: $(Join-Path $repoRoot $LogDirectory)"
        Write-Host "  dashboard: http://localhost:$Port/"
    }
}
