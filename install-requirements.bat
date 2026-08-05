@echo off
cd /d "%~dp0"

echo === FluxDB Requirements Setup ===
echo.

echo [1/4] Checking for .NET 10 SDK...
dotnet --list-sdks | findstr "10.0" >nul 2>&1
if errorlevel 1 (
    echo .NET 10 SDK is not installed or not available in PATH.
    echo Install from https://dotnet.microsoft.com/download
    exit /b 1
)

echo [2/4] Restoring WPF dependencies...
dotnet restore WPF\FluxDB\FluxDB.csproj

echo [3/4] Checking Go toolchain...
go version >nul 2>&1
if errorlevel 1 (
    echo Go is not installed or not available in PATH.
    exit /b 1
)

echo [4/4] Installing Go dependencies for Installer and Log Viewer...
cd Installer
go mod download
cd ..\Log_Viewer
go mod download

echo.
echo ✓ Requirements setup completed.
echo   WPF packages restored via dotnet restore
echo   Go modules downloaded for Installer and Log_Viewer
