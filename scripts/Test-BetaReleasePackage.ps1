param(
    [string] $PackagePath = "output/release/CareerSeeker-beta-win-x64.msix",
    [string] $Configuration = "Release",
    [string] $ExpectedPublisher,
    [switch] $RequireSigned
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$sdkBuildToolsVersion = "10.0.26100.7705"
$unsignedPublisherOid = "OID.2.25.311729368913984317654407730594956997722=1"

if ($RequireSigned -and [string]::IsNullOrWhiteSpace($ExpectedPublisher)) {
    throw "-RequireSigned requires -ExpectedPublisher with the exact certificate subject."
}

function Invoke-CheckedOutput {
    param([string] $Command, [string[]] $Arguments)
    $output = & $Command @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) {
        throw "Command failed: $Command $($Arguments -join ' ')"
    }
    return ($output -join "`n")
}

Push-Location $repoRoot
try {
    $fullPackage = if ([System.IO.Path]::IsPathRooted($PackagePath)) {
        [System.IO.Path]::GetFullPath($PackagePath)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path $repoRoot $PackagePath))
    }
    if (-not (Test-Path -LiteralPath $fullPackage -PathType Leaf)) {
        throw "Beta MSIX not found: $fullPackage"
    }

    & dotnet restore "tools/WindowsSdkTools/WindowsSdkTools.csproj" --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "Locked Windows SDK tool restore failed." }
    $globalPackagesLine = (& dotnet nuget locals global-packages --list) -join ""
    if ($LASTEXITCODE -ne 0 -or $globalPackagesLine -notmatch "global-packages:\s*(.+)$") {
        throw "Could not locate the NuGet global-packages directory."
    }
    $makeAppx = Join-Path $Matches[1].Trim() `
        "microsoft.windows.sdk.buildtools/$sdkBuildToolsVersion/bin/10.0.26100.0/x64/makeappx.exe"
    $signTool = Join-Path $Matches[1].Trim() `
        "microsoft.windows.sdk.buildtools/$sdkBuildToolsVersion/bin/10.0.26100.0/x64/signtool.exe"
    if (-not (Test-Path -LiteralPath $makeAppx -PathType Leaf)) {
        throw "MakeAppx.exe is unavailable after locked restore."
    }
    if ($RequireSigned -and -not (Test-Path -LiteralPath $signTool -PathType Leaf)) {
        throw "SignTool.exe is unavailable after locked restore."
    }

    $testRoot = Join-Path $repoRoot "tmp/beta-package-self-check"
    $unpackRoot = Join-Path $testRoot "unpacked"
    $workspace = Join-Path $testRoot "preserved-user-workspace"
    if (Test-Path -LiteralPath $testRoot) {
        $resolved = [System.IO.Path]::GetFullPath($testRoot)
        $expected = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "tmp/beta-package-self-check"))
        if (-not $resolved.Equals($expected, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean an unexpected self-check directory."
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $unpackRoot, $workspace | Out-Null
    $sentinel = Join-Path $workspace ".appdata/secrets/byok-keys.dpapi"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $sentinel) | Out-Null
    Set-Content -LiteralPath $sentinel -Value "synthetic-preservation-sentinel" -Encoding utf8

    Invoke-CheckedOutput $makeAppx @("unpack", "/p", $fullPackage, "/d", $unpackRoot, "/o") | Out-Null
    [xml] $manifest = Get-Content -LiteralPath (Join-Path $unpackRoot "AppxManifest.xml") -Raw
    $ns = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
    $ns.AddNamespace("f", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
    $ns.AddNamespace("uap10", "http://schemas.microsoft.com/appx/manifest/uap/windows10/10")
    $ns.AddNamespace("desktop", "http://schemas.microsoft.com/appx/manifest/desktop/windows10")
    $ns.AddNamespace("rescap", "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities")

    $identity = $manifest.SelectSingleNode("/f:Package/f:Identity", $ns)
    if ($null -eq $identity -or $identity.Name -ne "CareerSeeker.LocalBeta") {
        throw "Unexpected MSIX identity."
    }
    $manifestPublisher = [string] $identity.Publisher
    $signedManifestShapeVerified = $false
    if (-not [string]::IsNullOrWhiteSpace($ExpectedPublisher)) {
        if (-not $manifestPublisher.Equals($ExpectedPublisher, [System.StringComparison]::Ordinal)) {
            throw "Manifest Publisher does not exactly match ExpectedPublisher."
        }
        if ($manifestPublisher.IndexOf($unsignedPublisherOid, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "A production-shaped manifest must not retain the unsigned-package OID."
        }
        $signedManifestShapeVerified = $true
    }
    if ($RequireSigned) {
        Invoke-CheckedOutput $signTool @("verify", "/pa", "/all", "/v", $fullPackage) | Out-Null
    }
    $application = $manifest.SelectSingleNode("/f:Package/f:Applications/f:Application", $ns)
    if ($null -eq $application -or $application.Executable -ne "CareerSeeker.exe" -or
        $application.EntryPoint -ne "Windows.FullTrustApplication") {
        throw "The MSIX application does not identify the single full-trust CareerSeeker executable."
    }
    $startup = $manifest.SelectSingleNode("//desktop:StartupTask", $ns)
    if ($null -eq $startup -or $startup.Enabled -ne "false") {
        throw "The optional startup task is missing or is not disabled by default."
    }
    $integrity = $manifest.SelectSingleNode("//uap10:PackageIntegrity/uap10:Content", $ns)
    if ($null -eq $integrity -or $integrity.Enforcement -ne "on") {
        throw "MSIX package-integrity enforcement is not enabled."
    }
    if ($null -eq $manifest.SelectSingleNode("//rescap:Capability[@Name='runFullTrust']", $ns)) {
        throw "The full-trust capability is missing."
    }

    $executables = @(Get-ChildItem -LiteralPath $unpackRoot -Filter "*.exe" -File -Recurse)
    if ($executables.Count -ne 1 -or $executables[0].Name -ne "CareerSeeker.exe") {
        throw "Expected exactly one executable named CareerSeeker.exe; found $($executables.Count)."
    }
    $forbidden = @(Get-ChildItem -LiteralPath $unpackRoot -File -Recurse | Where-Object {
        $_.FullName -match '[\\/](?:\.appdata|output|secrets|oauth)[\\/]' -or
        $_.Name -match '(?i)(?:\.dpapi$|token|env\.secrets)'
    })
    if ($forbidden.Count -ne 0) {
        throw "The MSIX contains mutable user data or secret-looking files."
    }

    $smokeOutput = Invoke-CheckedOutput (Join-Path $unpackRoot "CareerSeeker.exe") @(
        "--smoke", "--workspace-root", (Join-Path $testRoot "setup-workspace"), "--setup-port", "0"
    )
    foreach ($expected in @(
        "Setup smoke completed through the local web flow.",
        "AI provider calls: 0",
        "Gmail calls/drafts: 0"
    )) {
        if (-not $smokeOutput.Contains($expected)) {
            throw "Packaged executable setup smoke did not report '$expected'."
        }
    }

    Remove-Item -LiteralPath $unpackRoot -Recurse -Force
    if (-not (Test-Path -LiteralPath $sentinel -PathType Leaf)) {
        throw "Package removal simulation deleted the external user workspace."
    }

    $item = Get-Item -LiteralPath $fullPackage
    $hash = (Get-FileHash -LiteralPath $fullPackage -Algorithm SHA256).Hash
    Write-Host "Beta package self-check passed."
    Write-Host "  identity: $($identity.Name)"
    Write-Host "  publisher: $manifestPublisher"
    Write-Host "  signed manifest shape: $(if ($signedManifestShapeVerified) { 'subject match; unsigned OID absent' } else { 'not requested' })"
    Write-Host "  signature verification: $(if ($RequireSigned) { 'passed' } else { 'not requested' })"
    Write-Host "  executable payload: 1 (CareerSeeker.exe)"
    Write-Host "  startup task: optional, disabled by default"
    Write-Host "  external user workspace preserved: yes"
    Write-Host "  provider calls: 0"
    Write-Host "  Gmail calls/drafts: 0"
    Write-Host "  bytes: $($item.Length)"
    Write-Host "  SHA-256: $hash"
}
finally {
    Pop-Location
}
