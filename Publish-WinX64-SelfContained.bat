@echo off
setlocal
pushd "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Publish-WinX64.ps1" -Variant SelfContained %*
set "RC=%ERRORLEVEL%"
popd
if not "%RC%"=="0" echo GitKeyRouter self-contained publish failed with exit code %RC%.
exit /b %RC%
