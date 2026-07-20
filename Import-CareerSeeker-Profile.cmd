@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0scripts\Import-AlphaProfile.ps1" (
  echo CareerSeeker profile import could not find scripts\Import-AlphaProfile.ps1.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

if not exist "%~dp0.appdata\profile.template.json" (
  echo CareerSeeker profile import could not find .appdata\profile.template.json.
  echo Run Setup-CareerSeeker-Alpha.cmd, edit the generated profile template, then run this again.
  pause
  exit /b 1
)

echo Importing CareerSeeker Alpha profile...
echo This replaces the local Tailor/Gate source-of-truth profile in SQLite.
echo Profile contents will not be printed.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Import-AlphaProfile.ps1" -Published
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo Profile import stopped with exit code %status%.
  pause
  exit /b %status%
)

echo.
echo CareerSeeker Alpha profile is imported.
pause
