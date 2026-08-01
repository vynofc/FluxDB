#!/bin/bash
set -e
cd "$(dirname "$0")"

echo "=== FluxDB Requirements Setup ==="
echo ""

if command -v nuget >/dev/null 2>&1; then
  echo "[1/4] Using system NuGet"
else
  echo "[1/4] NuGet not found in PATH; using local nuget.exe if present"
fi

if [ -f "nuget.exe" ]; then
  echo "[2/4] Restoring WPF dependencies..."
  mono nuget.exe restore WPF/FluxDB/FluxDB.csproj -PackagesDirectory WPF/FluxDB/packages
else
  echo "[2/4] Restoring WPF dependencies..."
  if command -v nuget >/dev/null 2>&1; then
    nuget restore WPF/FluxDB/FluxDB.csproj -PackagesDirectory WPF/FluxDB/packages
  else
    echo "NuGet is not available. Please install it first or place nuget.exe in the repository root."
    exit 1
  fi
fi

echo "[3/4] Checking Go toolchain..."
if ! command -v go >/dev/null 2>&1; then
  echo "Go is not installed or not available in PATH."
  exit 1
fi

echo "[4/4] Installing Go dependencies for Installer and Log Viewer..."
(cd Installer && go mod download)
(cd Log_Viewer && go mod download)

echo ""
echo "✓ Requirements setup completed."
echo "  WPF packages restored under WPF/FluxDB/packages"
echo "  Go modules downloaded for Installer and Log Viewer"
