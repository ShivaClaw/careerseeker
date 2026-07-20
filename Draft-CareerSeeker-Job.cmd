@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0scripts\Draft-AlphaJob.ps1" (
  echo CareerSeeker selected-job draft could not find scripts\Draft-AlphaJob.ps1.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

echo Draft a selected CareerSeeker Alpha job.
echo Open the dashboard Jobs page first, then enter the job id shown there.
echo.
set /p CAREERSEEKER_JOB_ID=Job id:
if "%CAREERSEEKER_JOB_ID%"=="" (
  echo A job id is required.
  pause
  exit /b 1
)

echo.
echo Press Enter for a safe local dry-run package.
echo Type LIVE to create a Gmail draft for review.
set /p CAREERSEEKER_DRAFT_MODE=Mode:

if /I "%CAREERSEEKER_DRAFT_MODE%"=="LIVE" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Draft-AlphaJob.ps1" -Published -JobId "%CAREERSEEKER_JOB_ID%" -Live
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Draft-AlphaJob.ps1" -Published -JobId "%CAREERSEEKER_JOB_ID%"
)
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo Selected-job draft stopped with exit code %status%.
  echo Check the job id, provider keys, Gmail setup if LIVE, and prompt-injection warnings.
  pause
  exit /b %status%
)

echo.
echo CareerSeeker selected-job draft complete.
pause
