param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $OutputDirectory = "output/release",
    [string] $PackageName = "CareerSeeker-beta-win-x64.msix",
    [string] $Version = "0.7.0.0",
    [string] $Publisher = "CN=CareerSeeker, OID.2.25.311729368913984317654407730594956997722=1",
    [switch] $NoPublish
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$engineProject = "src/Engine/SeekerSvc.Engine.csproj"
$publishRelative = "src/Engine/bin/$Configuration/net8.0/$Runtime/publish"
$sourceExeName = "SeekerSvc.Engine.exe"
$packageExeName = "CareerSeeker.exe"
$toolsProject = "tools/WindowsSdkTools/WindowsSdkTools.csproj"
$sdkBuildToolsVersion = "10.0.26100.7705"

function Invoke-Checked {
    param([string] $Command, [string[]] $Arguments)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $Command $($Arguments -join ' ')"
    }
}

function Resolve-RepoPath {
    param([string] $RelativePath)
    $root = [System.IO.Path]::GetFullPath($repoRoot)
    $full = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $RelativePath))
    $prefix = $root.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing a path outside the repository: $full"
    }
    return $full
}

function Assert-PlainMsixName {
    param([string] $Name)
    if ([string]::IsNullOrWhiteSpace($Name) -or
        [System.IO.Path]::IsPathRooted($Name) -or
        [System.IO.Path]::GetFileName($Name) -ne $Name -or
        -not $Name.EndsWith(".msix", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "PackageName must be a plain .msix file name."
    }
}

Push-Location $repoRoot
try {
    Assert-PlainMsixName $PackageName
    if ($Runtime -ne "win-x64") {
        throw "The Beta 7 installer currently supports only win-x64."
    }
    if (-not ($Version -match '^\d+\.\d+\.\d+\.\d+$')) {
        throw "MSIX Version must contain four numeric components."
    }

    Invoke-Checked "dotnet" @("restore", $toolsProject, "--locked-mode")
    if (-not $NoPublish) {
        Invoke-Checked "dotnet" @(
            "publish", $engineProject,
            "-c", $Configuration,
            "-r", $Runtime,
            "--self-contained", "true",
            "/p:PublishSingleFile=true"
        )
    }

    $globalPackagesLine = (& dotnet nuget locals global-packages --list) -join ""
    if ($LASTEXITCODE -ne 0 -or $globalPackagesLine -notmatch "global-packages:\s*(.+)$") {
        throw "Could not locate the NuGet global-packages directory."
    }
    $globalPackages = $Matches[1].Trim()
    # Resolve the BuildTools package where NuGet actually placed it. On a machine
    # with Visual Studio -- GitHub's windows-latest runners included -- restore can
    # be satisfied from a machine-wide FALLBACK folder: nothing is downloaded,
    # nothing lands in the global-packages folder, and a lookup hard-wired to
    # `dotnet nuget locals global-packages` finds no package at all, which is how
    # the first sign-beta dispatch failed (2026-09-18). The assets file records
    # every package folder restore actually used; trust it, not an assumption.
    $assetsPath = Resolve-RepoPath "tools/WindowsSdkTools/obj/project.assets.json"
    if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
        throw "Restore left no assets file at $assetsPath, so the BuildTools package cannot be located."
    }
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    $packageFolders = @($assets.packageFolders.PSObject.Properties.Name)
    $sdkPackageDir = $packageFolders |
        ForEach-Object { Join-Path $_ "microsoft.windows.sdk.buildtools/$sdkBuildToolsVersion" } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Container } |
        Select-Object -First 1
    if ($null -eq $sdkPackageDir) {
        throw ("Microsoft.Windows.SDK.BuildTools $sdkBuildToolsVersion was restored but its folder was " +
            "not found under any packageFolders entry: $($packageFolders -join '; ')")
    }
    # Locate MakeAppx.exe by search rather than a hard-coded bin/<sdk-build>/x64
    # segment: the inner folder is named for the SDK build, not the package
    # version, and a wrong guess is indistinguishable from a missing file. The
    # x64 filter keeps the match unambiguous; the error path lists what WAS
    # extracted so a recurrence diagnoses itself.
    $makeAppxItem = Get-ChildItem -Path $sdkPackageDir -Filter "makeappx.exe" -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '[\\/]x64[\\/]' } |
        Select-Object -First 1
    if ($null -eq $makeAppxItem) {
        $foundExes = @(Get-ChildItem -Path $sdkPackageDir -Filter "*.exe" -Recurse -File -ErrorAction SilentlyContinue |
            ForEach-Object { $_.FullName })
        $inventory = if ($foundExes.Count -gt 0) { $foundExes -join "; " } else { "none" }
        throw ("The restored Microsoft Windows SDK BuildTools package did not contain an x64 MakeAppx.exe " +
            "under $sdkPackageDir. Executables actually extracted: $inventory")
    }
    $makeAppx = $makeAppxItem.FullName

    $publishDirectory = Resolve-RepoPath $publishRelative
    $sourceExe = Join-Path $publishDirectory $sourceExeName
    if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
        throw "Published executable not found: $sourceExe"
    }

    $outDir = Resolve-RepoPath $OutputDirectory
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    $stageDir = Join-Path $outDir "_beta-msix-stage"
    if (Test-Path -LiteralPath $stageDir) {
        $resolvedStage = [System.IO.Path]::GetFullPath($stageDir)
        $expectedStage = [System.IO.Path]::GetFullPath((Join-Path $outDir "_beta-msix-stage"))
        if (-not $resolvedStage.Equals($expectedStage, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean an unexpected staging directory."
        }
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

    Get-ChildItem -LiteralPath $publishDirectory -File |
        Where-Object { $_.Extension -ne ".pdb" -and $_.Name -ne $sourceExeName } |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stageDir $_.Name) }
    Copy-Item -LiteralPath $sourceExe -Destination (Join-Path $stageDir $packageExeName)

    $nativeSqlite = Join-Path $stageDir "e_sqlite3.dll"
    if (-not (Test-Path -LiteralPath $nativeSqlite -PathType Leaf)) {
        $sqlitePackage = Get-ChildItem -LiteralPath (Join-Path $globalPackages "sqlitepclraw.lib.e_sqlite3") `
                -Filter "e_sqlite3.dll" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*\runtimes\$Runtime\native\e_sqlite3.dll" } |
            Sort-Object -Descending -Property @{ Expression = {
                $parsed = $null
                if ([System.Version]::TryParse($_.Directory.Parent.Parent.Parent.Name, [ref]$parsed)) {
                    $parsed
                } else {
                    [System.Version]"0.0.0"
                }
            } } |
            Select-Object -First 1
        if ($null -eq $sqlitePackage) {
            throw "The published output and restored packages contain no $Runtime e_sqlite3.dll."
        }
        Copy-Item -LiteralPath $sqlitePackage.FullName -Destination $nativeSqlite
    }

    $assets = Join-Path $stageDir "Assets"
    New-Item -ItemType Directory -Force -Path $assets | Out-Null
    foreach ($assetName in @("Square44x44Logo.png", "Square150x150Logo.png", "StoreLogo.png")) {
        Copy-Item -LiteralPath (Resolve-RepoPath "installer/Assets/$assetName") `
            -Destination (Join-Path $assets $assetName)
    }

    $oauthSource = @(
        "config/google-client.json",
        "config/google-oauth-client.json",
        "secrets/google-oauth-client.json",
        "client_secret.json"
    ) | ForEach-Object { Resolve-RepoPath $_ } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
    if ($null -ne $oauthSource) {
        $resources = Join-Path $stageDir "resources"
        New-Item -ItemType Directory -Force -Path $resources | Out-Null
        Copy-Item -LiteralPath $oauthSource -Destination (Join-Path $resources "google-client.json")
    }

    $escapedPublisher = [System.Security.SecurityElement]::Escape($Publisher)
    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
  xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap uap10 desktop rescap">
  <Identity Name="CareerSeeker.LocalBeta" Publisher="$escapedPublisher" Version="$Version" ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>CareerSeeker Beta</DisplayName>
    <PublisherDisplayName>CareerSeeker</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
    <uap10:PackageIntegrity>
      <uap10:Content Enforcement="on" />
    </uap10:PackageIntegrity>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Resources>
    <Resource Language="en-us" />
  </Resources>
  <Applications>
    <Application Id="CareerSeeker" Executable="$packageExeName" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements
        DisplayName="CareerSeeker Beta"
        Description="Local-first job search engine"
        BackgroundColor="transparent"
        Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png" />
      <Extensions>
        <desktop:Extension
          Category="windows.startupTask"
          Executable="$packageExeName"
          EntryPoint="Windows.FullTrustApplication">
          <desktop:StartupTask TaskId="CareerSeekerEngine" Enabled="false" DisplayName="CareerSeeker Beta" />
        </desktop:Extension>
      </Extensions>
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@
    Set-Content -LiteralPath (Join-Path $stageDir "AppxManifest.xml") -Value $manifest -Encoding utf8

    $packagePath = Join-Path $outDir $PackageName
    if (Test-Path -LiteralPath $packagePath) {
        Remove-Item -LiteralPath $packagePath -Force
    }
    Invoke-Checked $makeAppx @("pack", "/d", $stageDir, "/p", $packagePath, "/o", "/h", "SHA256")

    $item = Get-Item -LiteralPath $packagePath
    $hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
    Write-Host "Beta MSIX: $($item.FullName)"
    Write-Host "Bytes: $($item.Length)"
    Write-Host "SHA-256: $hash"
    Write-Host "Executable payload: $packageExeName (one .exe)"
    Write-Host "Signing state: unsigned build hook; see docs/Beta-Windows-Package-Runbook.md"
}
finally {
    Pop-Location
}
