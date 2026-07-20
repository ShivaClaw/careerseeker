@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0scripts\Export-AlphaEvidencePackage.ps1" (
  echo CareerSeeker evidence export could not find scripts\Export-AlphaEvidencePackage.ps1.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

echo Exporting CareerSeeker Alpha evidence package...
echo This packages local audit data, the SQLite snapshot, and generated artifacts for review.
echo Secret-looking paths are excluded.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Export-AlphaEvidencePackage.ps1" -Published
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo Evidence export stopped with exit code %status%.
  echo Run Run-CareerSeeker-Demo.cmd or a live alpha cycle first, then try again.
  pause
  exit /b %status%
)

echo.
echo CareerSeeker Alpha evidence package exported to output\careerseeker-alpha-evidence.zip.
pause
