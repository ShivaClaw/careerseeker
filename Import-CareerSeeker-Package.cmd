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
set "CAREERSEEKER_IMPORT_PACKAGE="
set /p CAREERSEEKER_IMPORT_PACKAGE=Package path [output\careerseeker-alpha-evidence.zip]:
if not defined CAREERSEEKER_IMPORT_PACKAGE set "CAREERSEEKER_IMPORT_PACKAGE=output\careerseeker-alpha-evidence.zip"

set "CAREERSEEKER_IMPORT_TARGET="
set /p CAREERSEEKER_IMPORT_TARGET=Import folder [.appdata\imported]:
if not defined CAREERSEEKER_IMPORT_TARGET set "CAREERSEEKER_IMPORT_TARGET=.appdata\imported"

echo.
echo Press Enter to preserve existing files.
echo Type OVERWRITE only if you want imported files to replace existing files in the target folder.
set "CAREERSEEKER_IMPORT_MODE="
set /p CAREERSEEKER_IMPORT_MODE=Mode:
echo.

set "CAREERSEEKER_IMPORT_SCRIPT=%~dp0scripts\Import-AlphaPackage.ps1"
powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $packagePath = if ([string]::IsNullOrWhiteSpace($env:CAREERSEEKER_IMPORT_PACKAGE)) { 'output\careerseeker-alpha-evidence.zip' } else { $env:CAREERSEEKER_IMPORT_PACKAGE.Trim() }; $targetRoot = if ([string]::IsNullOrWhiteSpace($env:CAREERSEEKER_IMPORT_TARGET)) { '.appdata\imported' } else { $env:CAREERSEEKER_IMPORT_TARGET.Trim() }; $importArgs = @('-Published', '-PackagePath', $packagePath, '-TargetRoot', $targetRoot); if ($env:CAREERSEEKER_IMPORT_MODE -ieq 'OVERWRITE') { $importArgs += '-Overwrite' }; & $env:CAREERSEEKER_IMPORT_SCRIPT @importArgs }"
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
