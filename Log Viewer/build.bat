@echo off
cd /d "%~dp0"
go build -ldflags="-s -w" -o .\bin\Log_Viewer.exe .
echo ✓ Log_Viewer.exe erstellt (bin\)