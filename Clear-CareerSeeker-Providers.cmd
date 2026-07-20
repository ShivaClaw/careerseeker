@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0SeekerSvc.Engine.exe" (
  echo CareerSeeker provider clear could not find SeekerSvc.Engine.exe.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

echo Clearing CareerSeeker Alpha AI provider keys...
echo This deletes the local DPAPI provider-key vault. Secret values will not be printed.
echo.
"%~dp0SeekerSvc.Engine.exe" clear-byok --key-vault .appdata\secrets\byok-keys.dpapi
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo Provider clear stopped with exit code %status%.
  pause
  exit /b %status%
)

echo.
echo CareerSeeker Alpha AI provider keys are cleared locally.
pause
