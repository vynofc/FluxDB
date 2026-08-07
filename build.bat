@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "ROOT_DIR=%CD%"
set "BIN_DIR=%ROOT_DIR%\bin"
set "WPF_PROJECT=%ROOT_DIR%\WPF\FluxDB\FluxDB.csproj"
set "INSTALLER_DIR=%ROOT_DIR%\Installer"
set "LOG_VIEWER_DIR=%ROOT_DIR%\Log_Viewer"

set "choice=%~1"
if not defined choice (
  echo === FluxDB Build Menu ===
  echo.
  echo 1^) Install requirements
  echo 2^) Build WPF app
  echo 3^) Build Installer
  echo 4^) Build Log Viewer
  echo 5^) Build full release package
  echo 6^) Clean
  echo 7^) Exit
  echo.
  set /p choice=Choose an option [1-7]: 
)

if /i "%choice%"=="1" goto run_install_requirements
if /i "%choice%"=="2" goto run_build_wpf
if /i "%choice%"=="3" goto run_build_installer
if /i "%choice%"=="4" goto run_build_logviewer
if /i "%choice%"=="5" goto run_build_full
if /i "%choice%"=="6" goto run_clean
if /i "%choice%"=="7" goto end

echo Invalid selection.
goto end

:run_install_requirements
call :install_requirements
if errorlevel 1 exit /b 1
goto end

:run_build_wpf
call :build_wpf
if errorlevel 1 exit /b 1
goto end

:run_build_installer
call :build_installer
if errorlevel 1 exit /b 1
goto end

:run_build_logviewer
call :build_logviewer
if errorlevel 1 exit /b 1
goto end

:run_build_full
call :build_full
if errorlevel 1 exit /b 1
goto end

:run_clean
call :clean
if errorlevel 1 exit /b 1
goto end

:install_requirements
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
pushd Installer
go mod download
popd
pushd Log_Viewer
go mod download
popd

echo.
echo ✓ Requirements setup completed.
echo   WPF packages restored via dotnet restore
echo   Go modules downloaded for Installer and Log_Viewer
exit /b 0

:ensure_bin_dir
if not exist "%BIN_DIR%" mkdir "%BIN_DIR%"
if errorlevel 1 exit /b 1
exit /b 0

:build_wpf
echo.
echo [1/2] Restore NuGet packages...
dotnet restore "%WPF_PROJECT%"
if errorlevel 1 exit /b 1

echo [2/2] Publish WPF app into bin\...
dotnet publish "%WPF_PROJECT%" -c Release -o "%BIN_DIR%"
if errorlevel 1 exit /b 1
exit /b 0

:build_installer
echo.
echo Building Installer into bin\...
call :ensure_bin_dir
if errorlevel 1 exit /b 1

pushd "%INSTALLER_DIR%"
go build -ldflags="-s -w" -o "%BIN_DIR%\FluxDB-Installer.exe" .
if errorlevel 1 (
    popd
    exit /b 1
)
popd
exit /b 0

:build_logviewer
echo.
echo Building Log Viewer into bin\components\...
call :ensure_bin_dir
if errorlevel 1 exit /b 1
if not exist "%BIN_DIR%\components" mkdir "%BIN_DIR%\components"
if errorlevel 1 exit /b 1

pushd "%LOG_VIEWER_DIR%"
go build -ldflags="-s -w" -o "%BIN_DIR%\components\Log_Viewer.exe" .
if errorlevel 1 (
    popd
    exit /b 1
)
popd
exit /b 0

:build_full
echo.
echo Building full release package...
call :build_wpf
if errorlevel 1 exit /b 1
call :build_installer
if errorlevel 1 exit /b 1
call :build_logviewer
if errorlevel 1 exit /b 1

powershell -NoProfile -Command "Compress-Archive -Path '%BIN_DIR%\*' -DestinationPath '%ROOT_DIR%\FluxDB.zip' -Force"
if errorlevel 1 exit /b 1

echo.
echo ✓ Full release package created
echo   bin\FluxDB.exe
echo   bin\FluxDB-Installer.exe
echo   bin\components\Log_Viewer.exe
echo   FluxDB.zip
exit /b 0

:clean
echo.
echo Cleaning build artifacts...
if exist "%BIN_DIR%" rmdir /s /q "%BIN_DIR%"
if exist "%ROOT_DIR%\FluxDB.zip" del "%ROOT_DIR%\FluxDB.zip"
echo ✓ Clean completed.
exit /b 0

:end
echo.
echo Finished.