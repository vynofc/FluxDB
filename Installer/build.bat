@echo off
cd /d "%~dp0"
go build -ldflags="-s -w" -o .\bin\FluxDB-Installer.exe .
echo ✓ FluxDB-Installer.exe erstellt (bin\)