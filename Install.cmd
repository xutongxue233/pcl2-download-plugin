@echo off
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0scripts\Install.ps1" %*
set "PCL_PLUGIN_EXIT=%ERRORLEVEL%"
echo.
if not "%PCL_PLUGIN_EXIT%"=="0" echo Installation failed. Read the error above; no administrator access is required.
pause
exit /b %PCL_PLUGIN_EXIT%
