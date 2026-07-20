param(
    [switch] $Published,
    [switch] $PublishIfMissing,
    [switch] $DryRun,
    [string] $Configuration = "Release",
    [string] $DbPath = ".appdata/careerseeker-alpha.db",
    [string] $JobDescriptionDirectory = ".appdata/job-descriptions",
    [string[]] $Board = @("greenhouse:remotecom", "lever:mistral"),
    [int] $TimeoutSeconds = 240
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

    $args = $prefixArgs + @(
        "scout-boards",
        "--db", $DbPath,
        "--jd-dir", $JobDescriptionDirectory,
        "--timeout-seconds", $TimeoutSeconds.ToString()
    )
    foreach ($boardName in $Board) {
        if (-not [string]::IsNullOrWhiteSpace($boardName)) {
            $args += @("--board", $boardName)
        }
    }

    Write-Host "Running CareerSeeker Alpha Scout board ingest..."
    Write-Host "This reads public ATS boards, saves local posting evidence, and creates no Gmail draft."
    Write-Host ""

    if ($DryRun) {
        Write-Host "Dry run: command was assembled but not executed."
        Write-Host "  command: $command"
        Write-Host "  mode: scout-boards"
        Write-Host "  db: $DbPath"
        Write-Host "  job descriptions: $JobDescriptionDirectory"
        Write-Host "  boards: $($Board -join ', ')"
        return
    }

    Invoke-Checked $command $args

    Write-Host ""
    Write-Host "CareerSeeker Alpha Scout ingest complete."
    Write-Host "Open the dashboard to review discovered jobs, then export evidence when ready."
}
finally {
    Pop-Location
}
