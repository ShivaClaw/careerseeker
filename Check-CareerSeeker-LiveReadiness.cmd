@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0scripts\Check-AlphaLiveReadiness.ps1" (
  echo CareerSeeker live readiness check could not find scripts\Check-AlphaLiveReadiness.ps1.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

echo Checking CareerSeeker Alpha live readiness...
echo This verifies local SQLite/artifact access plus required Gmail and BYOK configuration.
echo Secret values will not be printed.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Check-AlphaLiveReadiness.ps1" -Published -RequireGmail -RequireByok
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo Live readiness check stopped with exit code %status%.
  pause
  exit /b %status%
)

echo.
echo CareerSeeker Alpha live path is ready.
pause
