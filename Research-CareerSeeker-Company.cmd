@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0scripts\Run-AlphaCompanyResearch.ps1" (
  echo CareerSeeker company research could not find scripts\Run-AlphaCompanyResearch.ps1.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

echo CareerSeeker Alpha company research.
echo This reads public web pages through Brave Search and BYOK dossier modeling.
echo It creates no Gmail draft and sends nothing.
echo.
set /p CAREERSEEKER_RESEARCH_COMPANY=Company name:
if "%CAREERSEEKER_RESEARCH_COMPANY%"=="" (
  echo A company name is required.
  pause
  exit /b 1
)

set /p CAREERSEEKER_RESEARCH_DOMAIN=Optional company domain:
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Run-AlphaCompanyResearch.ps1" -Published -Company "%CAREERSEEKER_RESEARCH_COMPANY%" -Domain "%CAREERSEEKER_RESEARCH_DOMAIN%"
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo Company research stopped with exit code %status%.
  echo Check provider keys, Brave Search setup, and your network connection.
  pause
  exit /b %status%
)

echo.
echo CareerSeeker company research complete.
pause
