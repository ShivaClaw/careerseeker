@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0scripts\Run-AlphaLiveCycle.ps1" (
  echo CareerSeeker live alpha could not find scripts\Run-AlphaLiveCycle.ps1.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

echo Running one CareerSeeker Alpha live L1 draft cycle...
echo This uses BYOK Tailor/Gate checks and creates a Gmail draft for review.
echo It does not send email.
echo.
echo Press Enter for a no-Gmail dry-run preview.
echo Type LIVE to create one Gmail draft for review.
set /p CAREERSEEKER_LIVE_MODE=Mode:
echo.

if /I "%CAREERSEEKER_LIVE_MODE%"=="LIVE" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Run-AlphaLiveCycle.ps1" -Published
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Run-AlphaLiveCycle.ps1" -Published -DryRun
)
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo Live alpha cycle stopped with exit code %status%.
  echo Check setup, provider keys, Gmail connection, then try again.
  pause
  exit /b %status%
)

echo.
if /I "%CAREERSEEKER_LIVE_MODE%"=="LIVE" (
  echo CareerSeeker Alpha live cycle complete.
  echo Open Gmail Drafts to review the created draft.
) else (
  echo CareerSeeker Alpha live dry-run preview complete.
  echo No Gmail draft was created.
)
pause
