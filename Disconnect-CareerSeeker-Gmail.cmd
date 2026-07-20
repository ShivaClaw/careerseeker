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
