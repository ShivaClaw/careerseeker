param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $OutputDirectory = "output/release",
    [string] $PackageName = "",
    [switch] $NoPublish,
    [switch] $NoDocs
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$engineProject = "src/Engine/SeekerSvc.Engine.csproj"
$publishDir = "src/Engine/bin/$Configuration/net8.0/$Runtime/publish"
$exeName = if ($Runtime.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) {
    "SeekerSvc.Engine.exe"
} else {
    "SeekerSvc.Engine"
}

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

function Assert-UnderRoot {
    param([string] $Path)

    $fullRoot = [System.IO.Path]::GetFullPath($repoRoot)
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
    $prefix = $fullRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to write outside the repository: $fullPath"
    }
    return $fullPath
}

Push-Location $repoRoot
try {
    if (-not $NoPublish) {
        Invoke-Checked "dotnet" @(
            "publish",
            $engineProject,
            "-c", $Configuration,
            "-r", $Runtime,
            "--self-contained", "true",
            "/p:PublishSingleFile=true"
        )
    }

    $exePath = Join-Path $repoRoot (Join-Path $publishDir $exeName)
    if (-not (Test-Path -LiteralPath $exePath)) {
        throw "Published executable not found: $exePath"
    }

    $outDir = Assert-UnderRoot $OutputDirectory
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    if ([string]::IsNullOrWhiteSpace($PackageName)) {
        $PackageName = "CareerSeeker-alpha-$Runtime.zip"
    }
    if (-not $PackageName.EndsWith(".zip", [System.StringComparison]::OrdinalIgnoreCase)) {
        $PackageName += ".zip"
    }

    $stageDir = Join-Path $outDir "_stage"
    if (Test-Path -LiteralPath $stageDir) {
        Remove-Item -LiteralPath $stageDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

    Get-ChildItem -LiteralPath (Join-Path $repoRoot $publishDir) -File |
        Where-Object { $_.Extension -ne ".pdb" } |
        ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stageDir $_.Name)
        }

    $quickstart = @"
CareerSeeker Alpha

This package contains the local-first L1 Drafts alpha executable.

Quick checks:

  powershell -ExecutionPolicy Bypass -File .\scripts\Initialize-AlphaWorkspace.ps1
  notepad .appdata\profile.template.json
  .\$exeName import-profile --profile .appdata\profile.template.json --db .appdata\careerseeker-alpha.db
  .\$exeName doctor --db .appdata\careerseeker-alpha.db --artifacts .appdata\artifacts
  powershell -ExecutionPolicy Bypass -File .\scripts\Start-AlphaDashboard.ps1 -Published -Once -NoGmailControl
  powershell -ExecutionPolicy Bypass -File .\scripts\Start-AlphaDashboard.ps1 -Published
  .\$exeName export-alpha-package --db .appdata\careerseeker-alpha.db --out output\careerseeker-alpha-package.zip
  .\$exeName import-alpha-package --package output\careerseeker-alpha-package.zip

The L1 alpha creates Gmail drafts only. It has no send path.

Do not place OAuth tokens, provider keys, resumes, local databases, or generated artifacts in source control.
"@
    Set-Content -LiteralPath (Join-Path $stageDir "README-alpha.txt") -Value $quickstart -Encoding UTF8

    $scriptsDir = Join-Path $stageDir "scripts"
    New-Item -ItemType Directory -Force -Path $scriptsDir | Out-Null
    foreach ($script in @(
        "scripts/Initialize-AlphaWorkspace.ps1",
        "scripts/Start-AlphaDashboard.ps1",
        "scripts/Manage-AlphaDashboardTask.ps1"
    )) {
        Copy-Item -LiteralPath (Join-Path $repoRoot $script) -Destination $scriptsDir
    }

    if (-not $NoDocs) {
        $docsDir = Join-Path $stageDir "docs"
        New-Item -ItemType Directory -Force -Path $docsDir | Out-Null
        foreach ($doc in @(
            "docs/External-Audit-Handoff.md",
            "docs/CareerSeeker-Alpha-Build-Checklist.md",
            "docs/Privacy-Policy.md",
            "docs/Support.md",
            "docs/Autonomy-Contract.md"
        )) {
            Copy-Item -LiteralPath (Join-Path $repoRoot $doc) -Destination $docsDir
        }
    }

    $stageFull = [System.IO.Path]::GetFullPath($stageDir).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $checksumLines = Get-ChildItem -LiteralPath $stageDir -Recurse -File |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($stageFull.Length).Replace("\", "/")
            $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName
            "$($hash.Hash.ToLowerInvariant())  $relative"
        }
    Set-Content -LiteralPath (Join-Path $stageDir "SHA256SUMS.txt") -Value $checksumLines -Encoding ASCII

    $packagePath = Join-Path $outDir $PackageName
    if (Test-Path -LiteralPath $packagePath) {
        Remove-Item -LiteralPath $packagePath -Force
    }
    Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $packagePath -Force

    Write-Host "CareerSeeker alpha release package"
    Write-Host "  executable: $exePath"
    Write-Host "  package: $packagePath"
    Write-Host "  bytes: $((Get-Item -LiteralPath $packagePath).Length)"
    $contents = "executable, native runtime dependencies, README-alpha.txt, SHA256SUMS.txt"
    if (-not $NoDocs) {
        $contents += ", docs"
    }
    $contents += ", scripts"
    Write-Host "  contents: $contents"
}
finally {
    Pop-Location
}
