@echo off
setlocal
cd /d "%~dp0\.."
set /p "RELEASE_VERSION=Version (for example 0.1.0-beta.1): "
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-github-release.ps1" -Version "%RELEASE_VERSION%"
set "RELEASE_EXIT=%ERRORLEVEL%"
echo.
if not "%RELEASE_EXIT%"=="0" echo Release was not started.
pause
exit /b %RELEASE_EXIT%
