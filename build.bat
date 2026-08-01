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
if not exist "nuget.exe" (
    echo nuget.exe not found. Please run install-requirements.bat first.
    exit /b 1
)

nuget.exe restore "%WPF_PROJECT%" -PackagesDirectory WPF\FluxDB\packages
if errorlevel 1 exit /b 1

echo [2/2] Build WPF app into bin\...
call :resolve_msbuild
if errorlevel 1 exit /b 1

if /i "%MSBUILD_EXE%"=="msbuild" (
    msbuild "%WPF_PROJECT%" /p:Configuration=Release /p:Platform=AnyCPU /p:OutDir=%BIN_DIR%\
) else (
    "%MSBUILD_EXE%" "%WPF_PROJECT%" /p:Configuration=Release /p:Platform=AnyCPU /p:OutDir=%BIN_DIR%\
)
if errorlevel 1 exit /b 1
exit /b 0

:resolve_msbuild
set "MSBUILD_EXE=msbuild"
where msbuild >nul 2>&1
if not errorlevel 1 exit /b 0

for /f "delims=" %%I in ('where /r "%ProgramFiles%\Microsoft Visual Studio" MSBuild.exe 2^>nul') do (
  set "MSBUILD_EXE=%%I"
  exit /b 0
)

for /f "delims=" %%I in ('where /r "%ProgramFiles(x86)%\Microsoft Visual Studio" MSBuild.exe 2^>nul') do (
  set "MSBUILD_EXE=%%I"
  exit /b 0
)

echo MSBuild was not found. Install Visual Studio Build Tools or add MSBuild to PATH.
exit /b 1

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