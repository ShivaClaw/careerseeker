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
  echo Opening .appdata\profile.template.json for editing.
  start "" notepad "%~dp0.appdata\profile.template.json"
)

echo.
echo Next:
echo   1. Edit and save .appdata\profile.template.json.
echo   2. Double-click Import-CareerSeeker-Profile.cmd.
echo   3. Fill secrets\env.secrets locally with ANTHROPIC_API_KEY and GEMINI_API_KEY or GOOGLE_API_KEY.
echo      Optional company research also accepts BRAVE_SEARCH_API_KEY, BRAVE_SEARCH_API, or CAREERSEEKER_BRAVE_SEARCH_API_KEY.
echo      Then double-click Connect-CareerSeeker-Providers.cmd.
echo   4. Double-click Connect-CareerSeeker-Gmail.cmd.
echo   5. Double-click Check-CareerSeeker-LiveReadiness.cmd to confirm live Gmail/BYOK readiness.
echo   6. Double-click Run-CareerSeeker-Demo.cmd to create safe local demo evidence.
echo   7. Double-click Start-CareerSeeker-Alpha.cmd to open the local dashboard.
echo.
pause
