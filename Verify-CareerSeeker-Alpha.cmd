@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0scripts\Test-AlphaReleasePackage.ps1" (
  echo CareerSeeker alpha verification could not find scripts\Test-AlphaReleasePackage.ps1.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

echo Verifying CareerSeeker Alpha release package...
echo This checks the release manifest, SHA-256 checksums, secret-path exclusions, and dashboard smoke.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Test-AlphaReleasePackage.ps1" -RunDashboardSmoke
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo CareerSeeker Alpha verification stopped with exit code %status%.
  pause
  exit /b %status%
)

echo.
echo CareerSeeker Alpha release package verified.
pause
