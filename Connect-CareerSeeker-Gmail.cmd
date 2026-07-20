@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0SeekerSvc.Engine.exe" (
  echo CareerSeeker Gmail connect could not find SeekerSvc.Engine.exe.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

if not exist "%~dp0secrets\google-oauth-client.json" (
  echo CareerSeeker Gmail connect could not find secrets\google-oauth-client.json.
  echo Add your Google OAuth client JSON there, then run this again.
  pause
  exit /b 1
)

echo Connecting CareerSeeker Alpha to Gmail...
echo This checks gmail.compose draft access and creates no draft.
echo.
"%~dp0SeekerSvc.Engine.exe" connect-gmail --client secrets\google-oauth-client.json --vault .appdata\oauth\gmail-token.dpapi
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo Gmail connection stopped with exit code %status%.
  pause
  exit /b %status%
)

echo.
echo Gmail is connected for CareerSeeker Alpha.
pause
