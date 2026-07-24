@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0scripts\Initialize-AlphaWorkspace.ps1" (
  echo CareerSeeker alpha setup could not find scripts\Initialize-AlphaWorkspace.ps1.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

echo Setting up CareerSeeker Alpha local workspace...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Initialize-AlphaWorkspace.ps1"
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo CareerSeeker Alpha setup stopped with exit code %status%.
  pause
  exit /b %status%
)

echo.
echo Local alpha workspace is ready.
if exist "%~dp0.appdata\profile.template.json" (
  echo Opening .appdata\profile.template.json so it is ready when you need it (step 3 below).
  start "" notepad "%~dp0.appdata\profile.template.json"
)

echo.
echo Next:
echo   1. Double-click Run-CareerSeeker-Demo.cmd to create safe local demo evidence.
echo      Start here. It needs no profile, no API keys, and no Gmail - it just runs.
echo   2. Double-click Start-CareerSeeker-Alpha.cmd to open the local dashboard and inspect that evidence.
echo.
echo   Then, when you want it working on real jobs with your own history:
echo   3. Edit and save .appdata\profile.template.json.
echo   4. Double-click Import-CareerSeeker-Profile.cmd.
echo   5. Fill secrets\env.secrets locally with ANTHROPIC_API_KEY and GEMINI_API_KEY or GOOGLE_API_KEY.
echo      Optional company research also accepts BRAVE_SEARCH_API_KEY, BRAVE_SEARCH_API, or CAREERSEEKER_BRAVE_SEARCH_API_KEY.
echo      Then double-click Connect-CareerSeeker-Providers.cmd.
echo   6. Double-click Connect-CareerSeeker-Gmail.cmd. Alpha 2.0 packages should include resources\google-client.json,
echo      so testers do not need to create or download Google OAuth JSON.
echo   7. Double-click Check-CareerSeeker-LiveReadiness.cmd to confirm live Gmail/BYOK readiness.
echo.
pause
