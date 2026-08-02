@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy.ps1"
set "PUBLISH_EXIT=%ERRORLEVEL%"
echo.
if not "%PUBLISH_EXIT%"=="0" echo Publishing failed. See publisher\state\reports\latest-deploy.json
if "%PUBLISH_EXIT%"=="0" echo Publishing completed successfully.
pause
exit /b %PUBLISH_EXIT%
