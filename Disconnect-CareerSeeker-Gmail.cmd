@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0SeekerSvc.Engine.exe" (
  echo CareerSeeker Gmail disconnect could not find SeekerSvc.Engine.exe.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

echo Disconnecting CareerSeeker Alpha from Gmail...
echo This revokes the local OAuth token when possible and deletes the local DPAPI token vault.
echo.
echo Type DISCONNECT to revoke Gmail access and delete the local token vault.
echo Press Enter to cancel without changing Gmail setup.
set "CAREERSEEKER_GMAIL_DISCONNECT_MODE="
set /p CAREERSEEKER_GMAIL_DISCONNECT_MODE=Mode:
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "& { if ($env:CAREERSEEKER_GMAIL_DISCONNECT_MODE -ieq 'DISCONNECT') { exit 0 }; exit 1 }"
if errorlevel 1 (
  echo Gmail disconnect cancelled. Gmail setup was not changed.
  pause
  exit /b 0
)

"%~dp0SeekerSvc.Engine.exe" disconnect-gmail --client secrets\google-oauth-client.json --vault .appdata\oauth\gmail-token.dpapi
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo Gmail disconnect stopped with exit code %status%.
  pause
  exit /b %status%
)

echo.
echo CareerSeeker Alpha Gmail access is disconnected locally.
pause
