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
  echo 6^) Exit
  echo.
  set /p choice=Choose an option [1-6]: 
)

if /i "%choice%"=="1" goto run_install_requirements
if /i "%choice%"=="2" goto run_build_wpf
if /i "%choice%"=="3" goto run_build_installer
if /i "%choice%"=="4" goto run_build_logviewer
if /i "%choice%"=="5" goto run_build_full
if /i "%choice%"=="6" goto end

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

:install_requirements
call install-requirements.bat
if errorlevel 1 exit /b 1
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

:end
echo.
echo Finished.