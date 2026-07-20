@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0scripts\Connect-AlphaProviders.ps1" (
  echo CareerSeeker provider connect could not find scripts\Connect-AlphaProviders.ps1.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

if not exist "%~dp0secrets\env.secrets" (
  echo CareerSeeker provider connect could not find secrets\env.secrets.
  echo Run Setup-CareerSeeker-Alpha.cmd, fill in provider keys locally, then run this again.
  pause
  exit /b 1
)

echo Connecting CareerSeeker Alpha AI providers...
echo This imports Anthropic/Gemini keys into the local DPAPI vault and verifies BYOK readiness.
echo Secret values will not be printed.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Connect-AlphaProviders.ps1" -Published
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo Provider connection stopped with exit code %status%.
  pause
  exit /b %status%
)

echo.
echo CareerSeeker Alpha AI providers are connected.
pause
