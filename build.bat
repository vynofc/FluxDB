@echo off
cd /d "%~dp0"

echo === FluxDB Build ===
echo.

REM --- WPF App ---
echo [1/5] Restore NuGet packages...
nuget restore FluxDB.sln

echo [2/5] Build WPF App...
msbuild FluxDB.sln /p:Configuration=Release /p:Platform="Any CPU"

REM --- Go: Installer ---
echo [3/5] Build Installer...
cd Installer
go build -ldflags="-s -w" -o ..\FluxDB-Installer.exe .
cd ..

REM --- Go: Log Viewer ---
echo [4/5] Build Log Viewer...
if not exist "bin\Release\components" mkdir bin\Release\components
cd "Log_Viewer"
go build -ldflags="-s -w" -o ..\bin\Release\components\Log_Viewer.exe .
cd ..

REM --- Package ---
echo [5/5] Package distribution...
if exist dist rmdir /s /q dist
mkdir dist
mkdir dist\components

copy bin\Release\FluxDB.exe          dist\
copy bin\Release\FluxDB.exe.config   dist\ 2>nul
copy bin\Release\FluxDB.pdb          dist\ 2>nul
copy bin\Release\Newtonsoft.Json.dll dist\ 2>nul
copy bin\Release\System.Data.SQLite.dll dist\ 2>nul
xcopy bin\Release\x64 dist\x64\ /E /I /Q 2>nul
xcopy bin\Release\x86 dist\x86\ /E /I /Q 2>nul
copy FluxDB-Installer.exe            dist\
copy bin\Release\components\Log_Viewer.exe dist\components\

powershell -Command "Compress-Archive -Path dist\* -DestinationPath FluxDB.zip -Force"

echo [6/6] Aufräumen...
rmdir /s /q dist

echo.
echo ✓ Build abgeschlossen
echo   FluxDB.zip          (Release-Paket)
echo   FluxDB-Installer.exe