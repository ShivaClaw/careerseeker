[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,
    [Parameter(Mandatory = $true)]
    [string] $CertificatePath,
    [string] $TimestampUrl = "https://timestamp.digicert.com",
    [switch] $ValidateOnly
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$toolsProject = Join-Path $repoRoot "tools/WindowsSdkTools/WindowsSdkTools.csproj"
$sdkBuildToolsVersion = "10.0.26100.7705"

function Resolve-InputPath {
    param([string] $Path)
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

$fullPackage = Resolve-InputPath $PackagePath
$fullCertificate = Resolve-InputPath $CertificatePath
if (-not (Test-Path -LiteralPath $fullPackage -PathType Leaf)) {
    throw "MSIX package does not exist: $fullPackage"
}
if (-not $fullPackage.EndsWith(".msix", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "PackagePath must name an .msix file."
}
if (-not $fullCertificate.EndsWith(".pfx", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "CertificatePath must name a .pfx file."
}

$timestamp = $null
if (-not [System.Uri]::TryCreate($TimestampUrl, [System.UriKind]::Absolute, [ref] $timestamp) -or
    $timestamp.Scheme -ne [System.Uri]::UriSchemeHttps) {
    throw "TimestampUrl must be an absolute HTTPS URL."
}

if ($ValidateOnly) {
    Write-Host "Beta signing flow validation passed."
    Write-Host "  mode: validation only; no signing or certificate read"
    Write-Host "  package: $([System.IO.Path]::GetFileName($fullPackage))"
    Write-Host "  certificate input: $([System.IO.Path]::GetFileName($fullCertificate))"
    Write-Host "  timestamp: $($timestamp.AbsoluteUri)"
    Write-Host "  password read: no"
    return
}

if (-not (Test-Path -LiteralPath $fullCertificate -PathType Leaf)) {
    throw "PFX certificate does not exist: $fullCertificate"
}
if ([string]::IsNullOrWhiteSpace($env:CAREERSEEKER_SIGNING_PASSWORD)) {
    throw "Set CAREERSEEKER_SIGNING_PASSWORD in this process. The script never prints it."
}

& dotnet restore $toolsProject --locked-mode
if ($LASTEXITCODE -ne 0) { throw "Locked Windows SDK tool restore failed." }
$globalPackagesLine = (& dotnet nuget locals global-packages --list) -join ""
if ($LASTEXITCODE -ne 0 -or $globalPackagesLine -notmatch "global-packages:\s*(.+)$") {
    throw "Could not locate the NuGet global-packages directory."
}
$signTool = Join-Path $Matches[1].Trim() `
    "microsoft.windows.sdk.buildtools/$sdkBuildToolsVersion/bin/10.0.26100.0/x64/signtool.exe"
if (-not (Test-Path -LiteralPath $signTool -PathType Leaf)) {
    throw "SignTool.exe is unavailable after locked restore."
}

& $signTool sign /fd SHA256 /td SHA256 /tr $timestamp.AbsoluteUri /f $fullCertificate `
    /p $env:CAREERSEEKER_SIGNING_PASSWORD $fullPackage
if ($LASTEXITCODE -ne 0) {
    throw "MSIX signing failed. No password value was printed."
}
& $signTool verify /pa /all $fullPackage
if ($LASTEXITCODE -ne 0) {
    throw "MSIX signature verification failed after signing."
}
Write-Host "Signed and verified MSIX: $([System.IO.Path]::GetFileName($fullPackage))"
