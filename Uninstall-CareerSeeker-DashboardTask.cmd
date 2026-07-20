@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0scripts\Manage-AlphaDashboardTask.ps1" (
  echo CareerSeeker dashboard task helper could not find scripts\Manage-AlphaDashboardTask.ps1.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

echo Removing CareerSeeker Alpha dashboard logon task...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Manage-AlphaDashboardTask.ps1" -Action Uninstall
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo Dashboard task uninstall stopped with exit code %status%.
  pause
  exit /b %status%
)

echo.
echo CareerSeeker Alpha dashboard logon task removed or was not installed.
pause
