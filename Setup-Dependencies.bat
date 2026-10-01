@echo off
setlocal
cd /d "%~dp0"

echo ========================================================
echo  O2Play Environment & Dependency Setup
echo ========================================================
echo.

:: Check for dotnet command
where dotnet >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] .NET CLI was not found in your PATH.
    echo Please install the .NET 10.0 SDK or Desktop Runtime from:
    echo https://dotnet.microsoft.com/download/dotnet/10.0
    echo.
    pause
    exit /b 1
)

echo [OK] .NET CLI found:
dotnet --version
echo.

:: Set local DOTNET_CLI_HOME fallback if needed
if exist "%~dp0.dotnet_cli" (
    set "DOTNET_CLI_HOME=%~dp0.dotnet_cli"
)

echo Restoring project dependencies (NuGet)...
dotnet restore O2Play.csproj
if %ERRORLEVEL% neq 0 (
    echo.
    echo [WARNING] Online restore had issues. Checking local package cache fallback...
    dotnet restore O2Play.csproj --source "%~dp0.dotnet_cli\.nuget\packages"
)

echo.
echo ========================================================
echo  Setup Completed Successfully!
echo  (Note: Build was NOT run, as configured.)
echo ========================================================
echo.
pause
exit /b 0
