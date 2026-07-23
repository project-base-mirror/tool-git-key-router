@echo off
setlocal
pushd "%~dp0"
echo.
echo GitKeyRouter self-contained Windows x64 publish
echo Repository: %CD%
echo Output: artifacts\publish\win-x64
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Publish-WinX64.ps1" -Variant SelfContained -OpenOutput %*
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
    echo.
    echo GitKeyRouter self-contained publish failed with exit code %RC%.
    echo Review the error above. Expected output: %CD%\artifacts\publish\win-x64
    if not "%GITKEYROUTER_NO_PAUSE%"=="1" pause
)
popd
exit /b %RC%
