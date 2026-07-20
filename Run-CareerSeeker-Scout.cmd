@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0scripts\Run-AlphaScoutBoards.ps1" (
  echo CareerSeeker Scout ingest could not find scripts\Run-AlphaScoutBoards.ps1.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

echo Running CareerSeeker Alpha Scout board ingest...
echo This reads public ATS boards, saves local posting evidence, and creates no Gmail draft.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Run-AlphaScoutBoards.ps1" -Published
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo Scout ingest stopped with exit code %status%.
  echo Check your network connection, then try again.
  pause
  exit /b %status%
)

echo.
echo CareerSeeker Alpha Scout ingest complete.
echo Double-click Start-CareerSeeker-Alpha.cmd to review discovered jobs.
pause
