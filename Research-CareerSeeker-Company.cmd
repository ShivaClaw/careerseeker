@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0scripts\Run-AlphaCompanyResearch.ps1" (
  echo CareerSeeker company research could not find scripts\Run-AlphaCompanyResearch.ps1.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

echo CareerSeeker Alpha company research.
echo This reads public web pages through Brave Search and BYOK dossier modeling.
echo It creates no Gmail draft and sends nothing.
echo.
set "CAREERSEEKER_RESEARCH_COMPANY="
set /p CAREERSEEKER_RESEARCH_COMPANY=Company name:
if not defined CAREERSEEKER_RESEARCH_COMPANY (
  echo A company name is required.
  pause
  exit /b 1
)

set "CAREERSEEKER_RESEARCH_DOMAIN="
set /p CAREERSEEKER_RESEARCH_DOMAIN=Optional company domain:
echo.

set "CAREERSEEKER_RESEARCH_SCRIPT=%~dp0scripts\Run-AlphaCompanyResearch.ps1"
powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $company = $env:CAREERSEEKER_RESEARCH_COMPANY; if ([string]::IsNullOrWhiteSpace($company)) { Write-Host 'A company name is required.'; exit 1 }; $researchArgs = @('-Published', '-Company', $company.Trim()); if (-not [string]::IsNullOrWhiteSpace($env:CAREERSEEKER_RESEARCH_DOMAIN)) { $researchArgs += @('-Domain', $env:CAREERSEEKER_RESEARCH_DOMAIN.Trim()) }; & $env:CAREERSEEKER_RESEARCH_SCRIPT @researchArgs }"
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo Company research stopped with exit code %status%.
  echo Check provider keys, Brave Search setup, and your network connection.
  pause
  exit /b %status%
)

echo.
echo CareerSeeker company research complete.
pause
