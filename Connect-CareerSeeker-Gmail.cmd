@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0SeekerSvc.Engine.exe" (
  echo CareerSeeker Gmail connect could not find SeekerSvc.Engine.exe.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

set "CAREERSEEKER_GOOGLE_CLIENT=%~dp0resources\google-client.json"
if not exist "%CAREERSEEKER_GOOGLE_CLIENT%" (
  set "CAREERSEEKER_GOOGLE_CLIENT=%~dp0secrets\google-oauth-client.json"
)
if not exist "%CAREERSEEKER_GOOGLE_CLIENT%" (
  echo CareerSeeker Gmail connect could not find the packaged Google client metadata.
  echo Ask the alpha owner for a package that includes resources\google-client.json.
  pause
  exit /b 1
)

echo Connecting CareerSeeker Alpha to Gmail...
echo This checks gmail.compose draft access and creates no draft.
echo.
"%~dp0SeekerSvc.Engine.exe" connect-gmail --client "%CAREERSEEKER_GOOGLE_CLIENT%" --vault .appdata\oauth\gmail-token.dpapi
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
