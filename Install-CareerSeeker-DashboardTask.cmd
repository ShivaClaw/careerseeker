@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0scripts\Manage-AlphaDashboardTask.ps1" (
  echo CareerSeeker engine task helper could not find scripts\Manage-AlphaDashboardTask.ps1.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

echo Installing CareerSeeker Beta engine logon task...
echo This starts the real local engine and dashboard when you sign in.
echo It creates drafts only when existing Gmail and BYOK setup is ready; it never sends email.
echo.
echo Type INSTALL to register the per-user engine logon task.
echo Press Enter to cancel without changing engine startup.
set "CAREERSEEKER_DASHBOARD_TASK_MODE="
set /p CAREERSEEKER_DASHBOARD_TASK_MODE=Mode:
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command "& { if ($env:CAREERSEEKER_DASHBOARD_TASK_MODE -ieq 'INSTALL') { exit 0 }; exit 1 }"
if errorlevel 1 (
  echo Engine task install cancelled. Engine startup was not changed.
  pause
  exit /b 0
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Manage-AlphaDashboardTask.ps1" -Action Install -Published
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo Engine task install stopped with exit code %status%.
  pause
  exit /b %status%
)

echo.
echo CareerSeeker Beta engine logon task installed.
pause
