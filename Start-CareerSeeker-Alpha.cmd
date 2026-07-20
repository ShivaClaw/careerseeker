@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0scripts\Start-AlphaDashboard.ps1" (
  echo CareerSeeker alpha launcher could not find scripts\Start-AlphaDashboard.ps1.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

echo Starting CareerSeeker Alpha...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-AlphaDashboard.ps1" -Published
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo CareerSeeker Alpha stopped with exit code %status%.
  pause
)

exit /b %status%
