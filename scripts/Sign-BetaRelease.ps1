param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,
    [Parameter(Mandatory = $true)]
    [string] $CertificatePath,
    [string] $TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$toolsProject = Join-Path $repoRoot "tools/WindowsSdkTools/WindowsSdkTools.csproj"
$sdkBuildToolsVersion = "10.0.26100.7705"

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

& $signTool sign /fd SHA256 /td SHA256 /tr $TimestampUrl /f $CertificatePath `
    /p $env:CAREERSEEKER_SIGNING_PASSWORD $PackagePath
if ($LASTEXITCODE -ne 0) {
    throw "MSIX signing failed. No password value was printed."
}
Write-Host "Signed MSIX: $([System.IO.Path]::GetFileName($PackagePath))"
