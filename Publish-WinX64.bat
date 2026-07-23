@echo off
setlocal
pushd "%~dp0"
echo.
echo GitKeyRouter Windows x64 publish
echo Repository: %CD%
echo Output: artifacts\publish and artifacts\release
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Publish-WinX64.ps1" -Variant All -OpenOutput %*
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
    echo.
    echo GitKeyRouter publish failed with exit code %RC%.
    echo Review the error above. Expected output root: %CD%\artifacts
    if not "%GITKEYROUTER_NO_PAUSE%"=="1" pause
)
popd
exit /b %RC%
