[CmdletBinding()]
param(
    [string] $SettingsPath
)

$ErrorActionPreference = 'Stop'
$requiredVersion = [Version] '1.25.0'
if ([string]::IsNullOrWhiteSpace($SettingsPath)) {
    $SettingsPath = Join-Path $PSScriptRoot 'PSScriptAnalyzerSettings.psd1'
}
$module = Get-Module -ListAvailable -Name PSScriptAnalyzer |
    Where-Object Version -eq $requiredVersion |
    Select-Object -First 1

if ($null -eq $module) {
    throw "PSScriptAnalyzer $requiredVersion is required. Install it for the current user with: Install-Module PSScriptAnalyzer -RequiredVersion $requiredVersion -Scope CurrentUser"
}
if (-not (Test-Path -LiteralPath $SettingsPath -PathType Leaf)) {
    throw "PSScriptAnalyzer settings file not found: $SettingsPath"
}

Import-Module $module.Path -Force
$findings = @(Invoke-ScriptAnalyzer `
    -Path $PSScriptRoot `
    -Recurse `
    -Settings $SettingsPath)

if ($findings.Count -gt 0) {
    $findings |
        Sort-Object ScriptName, Line, Column, RuleName |
        Format-Table Severity, RuleName, ScriptName, Line, Column, Message -Wrap |
        Out-String |
        Write-Output
    throw "PSScriptAnalyzer reported $($findings.Count) enforced finding(s)."
}

Write-Output "PSScriptAnalyzer $requiredVersion passed."
Write-Output "  path: $PSScriptRoot"
Write-Output '  enforced findings: 0'
