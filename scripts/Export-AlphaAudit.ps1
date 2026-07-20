param(
    [switch] $Published,
    [switch] $PublishIfMissing,
    [switch] $IncludePayloads,
    [string] $Configuration = "Release",
    [string] $DbPath = ".appdata/careerseeker-alpha.db",
    [string] $OutputPath = "output/careerseeker-alpha-audit.json"
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
    if (-not (Test-Path -LiteralPath $DbPath -PathType Leaf)) {
        throw "CareerSeeker audit export could not find '$DbPath'. Run Run-CareerSeeker-Demo.cmd or a live alpha cycle first, then run this again."
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
        "export-audit",
        "--db", $DbPath,
        "--out", $OutputPath
    )
    if ($IncludePayloads) {
        $args += "--include-payloads"
    }

    Write-Host "Exporting CareerSeeker Alpha audit JSON..."
    Write-Host "Payloads are hashes only unless explicitly included."
    Write-Host ""

    Invoke-Checked $command $args

    Write-Host ""
    Write-Host "CareerSeeker Alpha audit JSON exported."
    Write-Host "  output: $OutputPath"
    Write-Host "  payloads: $(if ($IncludePayloads) { "included" } else { "hashes only" })"
}
finally {
    Pop-Location
}
