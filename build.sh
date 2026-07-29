#!/bin/bash
set -e
cd "$(dirname "$0")"

echo "=== FluxDB Build ==="
echo ""

# --- WPF App (benötigt MSBuild + NuGet) ---
echo "[1/5] Restore NuGet packages..."
nuget restore FluxDB.sln

echo "[2/5] Build WPF App..."
msbuild FluxDB.sln /p:Configuration=Release /p:Platform="Any CPU"

# --- Go: Installer (cross-compile) ---
echo "[3/5] Build Installer..."
cd Installer
GOOS=windows GOARCH=amd64 go build -ldflags="-s -w" -o ../FluxDB-Installer.exe .
cd ..

# --- Go: Log Viewer (cross-compile) ---
echo "[4/5] Build Log Viewer..."
mkdir -p bin/Release/components
cd Log_Viewer
GOOS=windows GOARCH=amd64 go build -ldflags="-s -w" -o ../bin/Release/components/Log_Viewer.exe .
cd ..

# --- Package ---
echo "[5/5] Package distribution..."
rm -rf dist
mkdir -p dist/components

cp bin/Release/FluxDB.exe          dist/
cp bin/Release/FluxDB.exe.config   dist/ 2>/dev/null || true
cp bin/Release/FluxDB.pdb          dist/ 2>/dev/null || true
cp bin/Release/Newtonsoft.Json.dll dist/ 2>/dev/null || true
cp bin/Release/System.Data.SQLite.dll dist/ 2>/dev/null || true
cp -r bin/Release/x64              dist/ 2>/dev/null || true
cp -r bin/Release/x86              dist/ 2>/dev/null || true
cp FluxDB-Installer.exe            dist/
cp bin/Release/components/Log_Viewer.exe dist/components/

zip -r FluxDB.zip dist/

echo "[6/6] Aufräumen..."
rm -rf dist

echo ""
echo "✓ Build abgeschlossen"
echo "  FluxDB.zip          (Release-Paket)"
echo "  FluxDB-Installer.exe"