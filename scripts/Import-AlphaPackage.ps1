param(
    [switch] $Published,
    [switch] $PublishIfMissing,
    [switch] $PreviewOnly,
    [switch] $Overwrite,
    [string] $Configuration = "Release",
    [string] $PackagePath = "output/careerseeker-alpha-evidence.zip",
    [string] $TargetRoot = ".appdata/imported",
    [string] $DbPath = "",
    [string] $ArtifactsPath = "",
    [string] $JobDescriptionDirectory = ""
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
    if ([string]::IsNullOrWhiteSpace($PackagePath)) {
        throw "A package path is required."
    }
    if ([string]::IsNullOrWhiteSpace($TargetRoot)) {
        throw "A target import folder is required."
    }

    if ([string]::IsNullOrWhiteSpace($DbPath)) {
        $DbPath = Join-Path $TargetRoot "careerseeker-alpha.db"
    }
    if ([string]::IsNullOrWhiteSpace($ArtifactsPath)) {
        $ArtifactsPath = Join-Path $TargetRoot "artifacts"
    }
    if ([string]::IsNullOrWhiteSpace($JobDescriptionDirectory)) {
        $JobDescriptionDirectory = Join-Path $TargetRoot "job-descriptions"
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
        "import-alpha-package",
        "--package", $PackagePath,
        "--target", $TargetRoot,
        "--db", $DbPath,
        "--artifacts", $ArtifactsPath,
        "--jd-dir", $JobDescriptionDirectory
    )
    if ($Overwrite) {
        $args += "--overwrite"
    }

    Write-Host "Importing CareerSeeker Alpha evidence package..."
    Write-Host "Existing files are preserved unless overwrite is explicitly enabled."
    Write-Host ""

    if ($PreviewOnly) {
        Write-Host "Preview only: command was assembled but not executed."
        Write-Host "  command: $command"
        Write-Host "  mode: import-alpha-package"
        Write-Host "  package: $PackagePath"
        Write-Host "  target: $TargetRoot"
        Write-Host "  db: $DbPath"
        Write-Host "  artifacts: $ArtifactsPath"
        Write-Host "  job descriptions: $JobDescriptionDirectory"
        Write-Host "  overwrite: $(if ($Overwrite) { "yes" } else { "no" })"
        return
    }

    if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
        throw "CareerSeeker could not find package '$PackagePath'. Export evidence first, or enter the path to an existing CareerSeeker alpha package."
    }

    Invoke-Checked $command $args

    Write-Host ""
    Write-Host "CareerSeeker Alpha evidence package imported."
    Write-Host "  target: $TargetRoot"
}
finally {
    Pop-Location
}
