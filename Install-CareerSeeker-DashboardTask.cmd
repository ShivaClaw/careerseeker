@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0scripts\Manage-AlphaDashboardTask.ps1" (
  echo CareerSeeker dashboard task helper could not find scripts\Manage-AlphaDashboardTask.ps1.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

echo Installing CareerSeeker Alpha dashboard logon task...
echo This starts the local dashboard when you sign in. It does not create Gmail drafts or send email.
echo.
echo Type INSTALL to register the per-user dashboard logon task.
echo Press Enter to cancel without changing dashboard startup.
set "CAREERSEEKER_DASHBOARD_TASK_MODE="
set /p CAREERSEEKER_DASHBOARD_TASK_MODE=Mode:
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "& { if ($env:CAREERSEEKER_DASHBOARD_TASK_MODE -ieq 'INSTALL') { exit 0 }; exit 1 }"
if errorlevel 1 (
  echo Dashboard task install cancelled. Dashboard startup was not changed.
  pause
  exit /b 0
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Manage-AlphaDashboardTask.ps1" -Action Install -Published
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo Dashboard task install stopped with exit code %status%.
  pause
  exit /b %status%
)

echo.
echo CareerSeeker Alpha dashboard logon task installed.
pause
