[CmdletBinding()]
param(
    [string] $OutputPath = "docs/Dependency-SBOM.spdx.json",
    [ValidatePattern('^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$')]
    [string] $SnapshotUtc = "2026-08-08T03:01:03Z",
    [switch] $NoRestore,
    [switch] $ValidateOnly
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-RepoPath {
    param([string] $Path)

    $root = [System.IO.Path]::GetFullPath($repoRoot)
    $full = if ([System.IO.Path]::IsPathRooted($Path)) {
        [System.IO.Path]::GetFullPath($Path)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
    }
    $prefix = $root.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing a path outside the repository: $full"
    }
    return $full
}

function Get-RepoRelativePath {
    param([string] $Path)

    $root = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $full = [System.IO.Path]::GetFullPath($Path)
    $prefix = $root + [System.IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Dependency report returned a project outside the repository: $full"
    }
    return $full.Substring($prefix.Length).Replace('\', '/')
}

function Invoke-Dotnet {
    param([string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed: dotnet $($Arguments -join ' ')"
    }
}

function Invoke-DotnetJson {
    param([string[]] $Arguments)

    $output = & dotnet @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed: dotnet $($Arguments -join ' ')`n$($output -join [Environment]::NewLine)"
    }
    return (($output -join "`n") | ConvertFrom-Json)
}

function Get-Sha512Hex {
    param([string] $Base64)

    $bytes = [System.Convert]::FromBase64String($Base64.Trim())
    return (($bytes | ForEach-Object { $_.ToString('x2') }) -join '').ToUpperInvariant()
}

function Get-Sha256Hex {
    param([string] $Value)

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
        return (($algorithm.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join '').ToUpperInvariant()
    } finally {
        $algorithm.Dispose()
    }
}

function Get-SpdxId {
    param([string] $Id, [string] $Version)

    return 'SPDXRef-Package-' + (($Id + '-' + $Version) -replace '[^A-Za-z0-9.-]', '-')
}

function Get-LocalPackageMetadata {
    param(
        [string] $GlobalPackages,
        [string] $Id,
        [string] $Version,
        [string] $FallbackContentHash,
        [string] $FallbackLicenseUrl
    )

    $lowerId = $Id.ToLowerInvariant()
    $directory = Join-Path $GlobalPackages (Join-Path $lowerId $Version.ToLowerInvariant())
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        if ([string]::IsNullOrWhiteSpace($FallbackContentHash)) {
            throw "Restored NuGet package is absent from the global package cache: $Id $Version"
        }
        return [pscustomobject]@{
            License = 'NOASSERTION'
            LicenseUrl = $FallbackLicenseUrl
            Sha512 = Get-Sha512Hex $FallbackContentHash
        }
    }

    $nuspec = Get-ChildItem -LiteralPath $directory -Filter '*.nuspec' -File | Select-Object -First 1
    if ($null -eq $nuspec) {
        throw "NuGet metadata is missing for $Id $Version."
    }

    [xml] $xml = Get-Content -LiteralPath $nuspec.FullName -Raw
    $licenseNode = $xml.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='license']")
    $licenseUrlNode = $xml.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='licenseUrl']")
    $license = 'NOASSERTION'
    if ($null -ne $licenseNode -and
        $licenseNode.Attributes['type'] -and
        $licenseNode.Attributes['type'].Value.Equals('expression', [System.StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::IsNullOrWhiteSpace($licenseNode.InnerText)) {
        $license = $licenseNode.InnerText.Trim()
    }

    $hashPath = Join-Path $directory "$lowerId.$($Version.ToLowerInvariant()).nupkg.sha512"
    if (-not (Test-Path -LiteralPath $hashPath -PathType Leaf)) {
        throw "NuGet content hash is missing for $Id $Version."
    }

    return [pscustomobject]@{
        License = $license
        LicenseUrl = if ($null -eq $licenseUrlNode) { '' } else { $licenseUrlNode.InnerText.Trim() }
        Sha512 = Get-Sha512Hex (Get-Content -LiteralPath $hashPath -Raw)
    }
}

$output = Resolve-RepoPath $OutputPath
if (-not $output.EndsWith('.json', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath must name a JSON file."
}

if (-not $NoRestore) {
    Invoke-Dotnet @('restore', 'CareerSeeker.sln')
    Invoke-Dotnet @('restore', 'tools/WindowsSdkTools/WindowsSdkTools.csproj', '--locked-mode')
}

$toolProject = Resolve-RepoPath 'tools/WindowsSdkTools/WindowsSdkTools.csproj'
$toolAssets = Resolve-RepoPath 'tools/WindowsSdkTools/obj/project.assets.json'
$toolLockPath = Resolve-RepoPath 'tools/WindowsSdkTools/packages.lock.json'
$toolLock = Get-Content -LiteralPath $toolLockPath -Raw | ConvertFrom-Json
$toolDependencies = $toolLock.dependencies.'net8.0'.PSObject.Properties
$lockedContentHashes = @{}
foreach ($dependency in $toolDependencies) {
    $lockedContentHashes["$($dependency.Name.ToLowerInvariant())/$($dependency.Value.resolved.ToLowerInvariant())"] =
        $dependency.Value.contentHash
}

$toolReport = if (-not $NoRestore -or (Test-Path -LiteralPath $toolAssets -PathType Leaf)) {
    Invoke-DotnetJson @('list', 'tools/WindowsSdkTools/WindowsSdkTools.csproj', 'package', '--include-transitive', '--format', 'json')
} else {
    $topLevel = @()
    $transitive = @()
    foreach ($dependency in $toolDependencies) {
        $item = [pscustomobject]@{
            id = $dependency.Name
            resolvedVersion = $dependency.Value.resolved
        }
        if ($dependency.Value.type -eq 'Direct') {
            $topLevel += $item
        } else {
            $transitive += $item
        }
    }
    [pscustomobject]@{
        projects = @(
            [pscustomobject]@{
                path = $toolProject
                frameworks = @(
                    [pscustomobject]@{
                        framework = 'net8.0'
                        topLevelPackages = $topLevel
                        transitivePackages = $transitive
                    }
                )
            }
        )
    }
}

$reports = @(
    [pscustomobject]@{
        Kind = 'runtime'
        Value = Invoke-DotnetJson @('list', 'CareerSeeker.sln', 'package', '--include-transitive', '--format', 'json')
    },
    [pscustomobject]@{
        Kind = 'build'
        Value = $toolReport
    }
)

$packageRecords = @{}
foreach ($report in $reports) {
    foreach ($project in @($report.Value.projects)) {
        $projectPath = Get-RepoRelativePath $project.path
        foreach ($framework in @($project.frameworks)) {
            foreach ($package in @($framework.topLevelPackages | Where-Object { $null -ne $_ })) {
                $key = "$($package.id.ToLowerInvariant())/$($package.resolvedVersion.ToLowerInvariant())"
                if (-not $packageRecords.ContainsKey($key)) {
                    $packageRecords[$key] = [pscustomobject]@{
                        Id = $package.id
                        Version = $package.resolvedVersion
                        RuntimeDirect = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
                        RuntimeTransitive = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
                        BuildDirect = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
                        BuildTransitive = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
                    }
                }
                if ($report.Kind -eq 'build') {
                    [void] $packageRecords[$key].BuildDirect.Add($projectPath)
                } else {
                    [void] $packageRecords[$key].RuntimeDirect.Add($projectPath)
                }
            }
            foreach ($package in @($framework.transitivePackages | Where-Object { $null -ne $_ })) {
                $key = "$($package.id.ToLowerInvariant())/$($package.resolvedVersion.ToLowerInvariant())"
                if (-not $packageRecords.ContainsKey($key)) {
                    $packageRecords[$key] = [pscustomobject]@{
                        Id = $package.id
                        Version = $package.resolvedVersion
                        RuntimeDirect = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
                        RuntimeTransitive = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
                        BuildDirect = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
                        BuildTransitive = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
                    }
                }
                if ($report.Kind -eq 'build') {
                    [void] $packageRecords[$key].BuildTransitive.Add($projectPath)
                } else {
                    [void] $packageRecords[$key].RuntimeTransitive.Add($projectPath)
                }
            }
        }
    }
}

$globalOutput = & dotnet nuget locals global-packages --list 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Could not resolve the NuGet global package cache: $($globalOutput -join [Environment]::NewLine)"
}
$globalLine = ($globalOutput | Where-Object { "$_" -match '^global-packages:\s*(.+)$' } | Select-Object -First 1)
if ($null -eq $globalLine -or "$globalLine" -notmatch '^global-packages:\s*(.+)$') {
    throw "Unexpected dotnet nuget locals output."
}
$globalPackages = [System.IO.Path]::GetFullPath($Matches[1].Trim())

$components = @()
$legacyLicenseUrls = @{
    'system.memory/4.5.3' = 'https://github.com/dotnet/corefx/blob/master/LICENSE.TXT'
    'microsoft.windows.sdk.buildtools/10.0.26100.7705' = 'https://aka.ms/WinSDKLicenseURL'
}
foreach ($record in @($packageRecords.Values | Sort-Object Id, Version)) {
    $key = "$($record.Id.ToLowerInvariant())/$($record.Version.ToLowerInvariant())"
    $fallbackHash = if ($lockedContentHashes.ContainsKey($key)) { $lockedContentHashes[$key] } else { '' }
    $fallbackLicenseUrl = if ($legacyLicenseUrls.ContainsKey($key)) { $legacyLicenseUrls[$key] } else { '' }
    $metadata = Get-LocalPackageMetadata $globalPackages $record.Id $record.Version $fallbackHash $fallbackLicenseUrl
    $scope = if ($record.RuntimeDirect.Count -gt 0) {
        'direct-runtime'
    } elseif ($record.RuntimeTransitive.Count -gt 0) {
        'transitive-runtime'
    } else {
        'build-only'
    }
    $projects = @(
        @($record.RuntimeDirect) +
        @($record.RuntimeTransitive) +
        @($record.BuildDirect) +
        @($record.BuildTransitive)
    ) | Sort-Object -Unique
    $components += [pscustomobject]@{
        Id = $record.Id
        Version = $record.Version
        Scope = $scope
        Projects = @($projects)
        License = $metadata.License
        LicenseUrl = $metadata.LicenseUrl
        Sha512 = $metadata.Sha512
    }
}

if ($components.Count -eq 0) {
    throw "The resolved dependency graph is empty."
}

$graphIdentity = ($components | ForEach-Object { "$($_.Id.ToLowerInvariant())/$($_.Version)/$($_.Sha512)" }) -join "`n"
$namespaceHash = Get-Sha256Hex $graphIdentity
$packages = @()
$relationships = @()
foreach ($component in $components) {
    $spdxId = Get-SpdxId $component.Id $component.Version
    $projectText = $component.Projects -join ', '
    $licenseUrlText = if ([string]::IsNullOrWhiteSpace($component.LicenseUrl)) {
        'none recorded in the local nuspec'
    } else {
        $component.LicenseUrl
    }
    $packages += [ordered]@{
        name = $component.Id
        SPDXID = $spdxId
        versionInfo = $component.Version
        downloadLocation = "https://api.nuget.org/v3-flatcontainer/$($component.Id.ToLowerInvariant())/$($component.Version.ToLowerInvariant())/$($component.Id.ToLowerInvariant()).$($component.Version.ToLowerInvariant()).nupkg"
        filesAnalyzed = $false
        licenseConcluded = 'NOASSERTION'
        licenseDeclared = $component.License
        copyrightText = 'NOASSERTION'
        checksums = @(
            [ordered]@{ algorithm = 'SHA512'; checksumValue = $component.Sha512 }
        )
        externalRefs = @(
            [ordered]@{
                referenceCategory = 'PACKAGE-MANAGER'
                referenceType = 'purl'
                referenceLocator = "pkg:nuget/$([System.Uri]::EscapeDataString($component.Id))@$([System.Uri]::EscapeDataString($component.Version))"
            }
        )
        primaryPackagePurpose = if ($component.Scope -eq 'build-only') { 'BUILD_TOOL' } else { 'LIBRARY' }
        comment = "CareerSeeker scope: $($component.Scope). Projects: $projectText. NuGet license URL: $licenseUrlText."
    }
    $relationships += [ordered]@{
        spdxElementId = 'SPDXRef-DOCUMENT'
        relationshipType = 'DESCRIBES'
        relatedSpdxElement = $spdxId
    }
}

$document = [ordered]@{
    spdxVersion = 'SPDX-2.3'
    dataLicense = 'CC0-1.0'
    SPDXID = 'SPDXRef-DOCUMENT'
    name = 'CareerSeeker NuGet dependency inventory'
    documentNamespace = "https://github.com/ShivaClaw/careerseeker/sbom/nuget/$namespaceHash"
    creationInfo = [ordered]@{
        created = $SnapshotUtc
        creators = @('Tool: scripts/New-DependencySbom.ps1')
        comment = 'Generated from restored project.assets.json data exposed by dotnet list package and locally cached NuGet nuspec/hash metadata.'
    }
    documentDescribes = @($packages | ForEach-Object { $_.SPDXID })
    packages = $packages
    relationships = $relationships
}

$json = (($document | ConvertTo-Json -Depth 12).Replace("`r`n", "`n").Replace("`r", "`n")) + "`n"
if ($ValidateOnly) {
    if (-not (Test-Path -LiteralPath $output -PathType Leaf)) {
        throw "Committed SPDX inventory is missing: $output"
    }
    $expected = (Get-Content -LiteralPath $output -Raw).Replace("`r`n", "`n")
    $actual = $json.Replace("`r`n", "`n")
    if (-not $expected.Equals($actual, [System.StringComparison]::Ordinal)) {
        throw "Dependency graph, local NuGet metadata, or committed SPDX inventory drifted. Regenerate $OutputPath."
    }
    Write-Host "CareerSeeker dependency SBOM validation passed."
} else {
    $parent = Split-Path -Parent $output
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($output, $json, [System.Text.UTF8Encoding]::new($false))
    Write-Host "CareerSeeker dependency SBOM generated."
}

$directRuntime = @($components | Where-Object Scope -eq 'direct-runtime').Count
$transitiveRuntime = @($components | Where-Object Scope -eq 'transitive-runtime').Count
$buildOnly = @($components | Where-Object Scope -eq 'build-only').Count
$noAssertion = @($components | Where-Object License -eq 'NOASSERTION').Count
Write-Host "  packages: $($components.Count)"
Write-Host "  direct runtime: $directRuntime"
Write-Host "  transitive runtime: $transitiveRuntime"
Write-Host "  build-only: $buildOnly"
Write-Host "  license NOASSERTION: $noAssertion"
Write-Host "  output: $output"
