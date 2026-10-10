@echo off
setlocal
cd /d "%~dp0"

REM "dotnet" can exist without the SDK (runtime only), so check for an SDK specifically.
set "HAS_SDK="
for /f "delims=" %%i in ('dotnet --list-sdks 2^>nul') do set "HAS_SDK=1"
if not defined HAS_SDK (
    echo The .NET SDK was not found. You may have the runtime only, which can run apps but not build them.
    echo.
    echo Install the SDK with this command, then run build.bat again:
    echo     winget install Microsoft.DotNet.SDK.8
    echo.
    pause
    exit /b 1
)

if exist "dist\SISRWatcher.exe" del /q "dist\SISRWatcher.exe"

echo Building SISR watcher...
dotnet publish SISRWatcher.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist

if not exist "dist\SISRWatcher.exe" (
    echo.
    echo The build failed, so dist\SISRWatcher.exe was not created.
    echo Copy the messages above and send them to Claude.
    echo.
    pause
    exit /b 1
)

echo.
echo Done. Your app is at: %~dp0dist\SISRWatcher.exe
pause
