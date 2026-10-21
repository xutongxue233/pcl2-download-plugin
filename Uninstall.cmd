@echo off
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0scripts\Uninstall.ps1" %*
set "PCL_PLUGIN_EXIT=%ERRORLEVEL%"
echo.
if not "%PCL_PLUGIN_EXIT%"=="0" echo Uninstallation failed. Read the error above; keep your backup and installation state.
pause
exit /b %PCL_PLUGIN_EXIT%
