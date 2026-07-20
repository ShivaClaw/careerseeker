@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0scripts\Run-AlphaDemoCycle.ps1" (
  echo CareerSeeker demo cycle could not find scripts\Run-AlphaDemoCycle.ps1.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

echo Running one safe CareerSeeker Alpha demo cycle...
echo This uses demo data, writes local SQLite/artifact evidence, and creates no Gmail draft.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Run-AlphaDemoCycle.ps1" -Published
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo Demo cycle stopped with exit code %status%.
  pause
  exit /b %status%
)

echo.
echo CareerSeeker Alpha demo cycle complete.
echo Double-click Start-CareerSeeker-Alpha.cmd to inspect it in the local dashboard.
pause
