@echo off
cd /d "%~dp0"

echo === FluxDB Requirements Setup ===
echo.

echo [1/4] Checking for NuGet...
if exist "nuget.exe" (
    echo NuGet found locally.
) else (
    echo NuGet not found locally. Downloading from nuget.org...
    powershell -Command "Invoke-WebRequest -Uri https://dist.nuget.org/win-x86-commandline/latest/nuget.exe -OutFile nuget.exe"
)

echo [2/4] Restoring WPF dependencies...
nuget.exe restore WPF\FluxDB\FluxDB.csproj -PackagesDirectory WPF\FluxDB\packages

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
echo   WPF packages restored under WPF\FluxDB\packages
echo   Go modules downloaded for Installer and Log_Viewer
