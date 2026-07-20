@echo off
setlocal

cd /d "%~dp0"

if not exist "%~dp0scripts\Import-AlphaPackage.ps1" (
  echo CareerSeeker package import could not find scripts\Import-AlphaPackage.ps1.
  echo Make sure this file is still in the extracted release folder.
  pause
  exit /b 1
)

echo Import a CareerSeeker Alpha evidence package.
echo Existing files are preserved unless you explicitly type OVERWRITE.
echo.
set /p CAREERSEEKER_IMPORT_PACKAGE=Package path [output\careerseeker-alpha-evidence.zip]:
if "%CAREERSEEKER_IMPORT_PACKAGE%"=="" set "CAREERSEEKER_IMPORT_PACKAGE=output\careerseeker-alpha-evidence.zip"

set /p CAREERSEEKER_IMPORT_TARGET=Import folder [.appdata\imported]:
if "%CAREERSEEKER_IMPORT_TARGET%"=="" set "CAREERSEEKER_IMPORT_TARGET=.appdata\imported"

echo.
echo Press Enter to preserve existing files.
echo Type OVERWRITE only if you want imported files to replace existing files in the target folder.
set /p CAREERSEEKER_IMPORT_MODE=Mode:
echo.

if /I "%CAREERSEEKER_IMPORT_MODE%"=="OVERWRITE" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Import-AlphaPackage.ps1" -Published -PackagePath "%CAREERSEEKER_IMPORT_PACKAGE%" -TargetRoot "%CAREERSEEKER_IMPORT_TARGET%" -Overwrite
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Import-AlphaPackage.ps1" -Published -PackagePath "%CAREERSEEKER_IMPORT_PACKAGE%" -TargetRoot "%CAREERSEEKER_IMPORT_TARGET%"
)
set "status=%ERRORLEVEL%"

if not "%status%"=="0" (
  echo.
  echo Package import stopped with exit code %status%.
  echo Check the package path and target folder, then try again.
  pause
  exit /b %status%
)

echo.
echo CareerSeeker package import complete.
pause
