@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0scripts\Export-AlphaAudit.ps1" (
  echo CareerSeeker audit export could not find scripts\Export-AlphaAudit.ps1.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

echo Exporting CareerSeeker Alpha audit JSON...
echo Default mode writes payload hashes only.
echo.
echo Press Enter for hashes only.
echo Type PAYLOADS only when you intentionally want raw event payloads in the JSON.
set /p CAREERSEEKER_AUDIT_MODE=Mode:
echo.

if /I "%CAREERSEEKER_AUDIT_MODE%"=="PAYLOADS" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Export-AlphaAudit.ps1" -Published -IncludePayloads
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Export-AlphaAudit.ps1" -Published
)
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo Audit export stopped with exit code %status%.
  echo Run Run-CareerSeeker-Demo.cmd or a live alpha cycle first, then try again.
  pause
  exit /b %status%
)

echo.
echo CareerSeeker Alpha audit JSON exported to output\careerseeker-alpha-audit.json.
pause
