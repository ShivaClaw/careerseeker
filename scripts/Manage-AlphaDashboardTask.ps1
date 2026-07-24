param(
    [ValidateSet("Install", "Uninstall", "Start", "Stop", "Status")]
    [string] $Action = "Status",
    [switch] $DryRun,
    [switch] $Published,
    [switch] $PublishIfMissing,
    [switch] $NoGmailControl,
    [string] $TaskName = "CareerSeeker Alpha Dashboard",
    [int] $Port = 7777,
    [string] $Configuration = "Release",
    [string] $DbPath = ".appdata/careerseeker-alpha.db",
    [string] $AuditOutPath = "output/careerseeker-audit.json",
    [string] $GmailClientPath = "resources/google-client.json",
    [string] $GmailVaultPath = ".appdata/oauth/gmail-token.dpapi"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$launcherPath = Join-Path $repoRoot "scripts/Start-AlphaDashboard.ps1"

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
    $args = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $launcherPath,
        "-NoOpen",
        "-Port", $Port.ToString(),
        "-Configuration", $Configuration,
        "-DbPath", $DbPath,
        "-AuditOutPath", $AuditOutPath,
        "-GmailClientPath", $GmailClientPath,
        "-GmailVaultPath", $GmailVaultPath
    )

    if ($Published) { $args += "-Published" }
    if ($PublishIfMissing) { $args += "-PublishIfMissing" }
    if ($NoGmailControl) { $args += "-NoGmailControl" }

    return $args
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

if (-not (Get-Module -ListAvailable -Name ScheduledTasks)) {
    throw "Windows ScheduledTasks module is not available on this machine."
}

if (-not (Test-Path -LiteralPath $launcherPath)) {
    throw "Alpha dashboard launcher not found: $launcherPath"
}

$launcherArgs = Get-LauncherArguments

switch ($Action) {
    "Install" {
        Write-TaskCommand $launcherArgs
        Write-Host "Trigger: at current user logon"

        if ($DryRun) {
            Write-Host "Dry run only; scheduled task was not registered."
            break
        }

        $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $taskAction = New-ScheduledTaskAction -Execute "powershell.exe" -Argument (Join-CommandArguments $launcherArgs)
        $trigger = New-ScheduledTaskTrigger -AtLogOn -User $currentUser
        $settings = New-ScheduledTaskSettingsSet `
            -AllowStartIfOnBatteries `
            -DontStopIfGoingOnBatteries `
            -MultipleInstances IgnoreNew `
            -ExecutionTimeLimit ([TimeSpan]::Zero)
        $principal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel LeastPrivilege

        Register-ScheduledTask `
            -TaskName $TaskName `
            -Action $taskAction `
            -Trigger $trigger `
            -Settings $settings `
            -Principal $principal `
            -Description "Starts the local CareerSeeker alpha dashboard at user logon." `
            -Force | Out-Null

        Write-Host "Installed scheduled task: $TaskName"
    }

    "Uninstall" {
        if (-not (Get-ExistingTask)) {
            Write-Host "Scheduled task not found: $TaskName"
            break
        }

        if ($DryRun) {
            Write-Host "Dry run only; scheduled task would be removed: $TaskName"
            break
        }

        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "Removed scheduled task: $TaskName"
    }

    "Start" {
        if (-not (Get-ExistingTask)) {
            throw "Scheduled task not found: $TaskName"
        }

        if ($DryRun) {
            Write-Host "Dry run only; scheduled task would be started: $TaskName"
            break
        }

        Start-ScheduledTask -TaskName $TaskName
        Write-Host "Started scheduled task: $TaskName"
    }

    "Stop" {
        if (-not (Get-ExistingTask)) {
            throw "Scheduled task not found: $TaskName"
        }

        if ($DryRun) {
            Write-Host "Dry run only; scheduled task would be stopped: $TaskName"
            break
        }

        Stop-ScheduledTask -TaskName $TaskName
        Write-Host "Stopped scheduled task: $TaskName"
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
        Write-Host "  last run: $($info.LastRunTime)"
        Write-Host "  next run: $($info.NextRunTime)"
        Write-Host "  last result: $($info.LastTaskResult)"
    }
}
