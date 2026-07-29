@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo ============================================
echo   FluxDB Installer - Requirements Check
echo ============================================
echo.

:: Check Go
set GO_FOUND=0
where go >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    for /f "tokens=3" %%v in ('go version 2^>nul') do (
        echo [OK] Go gefunden: %%v
        set GO_FOUND=1
    )
) else (
    echo [FEHLT] Go ist nicht installiert oder nicht im PATH
)

if !GO_FOUND! EQU 0 (
    echo.
    echo Go 1.22+ wird benoetigt. Lade es herunter:
    echo   https://go.dev/dl/
    echo.
    echo Oder via winget:
    echo   winget install GoLang.Go
    echo.
    choice /c YN /m "Trotzdem fortfahren (wird fehlschlagen)"
    if errorlevel 2 exit /b 1
)

echo.

:: go mod tidy
echo [INFO] Fuehre go mod tidy aus...
go mod tidy
if %ERRORLEVEL% NEQ 0 (
    echo [FEHLER] go mod tidy fehlgeschlagen
    exit /b 1
)
echo [OK] Abhaengigkeiten aufgeloest

echo.

:: Build
echo [INFO] Baue FluxDB-Installer...
go build -ldflags="-s -w" -o .\bin\FluxDB-Installer.exe .
if %ERRORLEVEL% NEQ 0 (
    echo [FEHLER] Build fehlgeschlagen
    exit /b 1
)

echo.
echo ============================================
echo [OK] FluxDB-Installer.exe erstellt ^(bin\^)
echo ============================================
exit /b 0