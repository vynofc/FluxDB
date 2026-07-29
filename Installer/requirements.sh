#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

echo "============================================"
echo "  FluxDB Installer - Requirements Check"
echo "============================================"
echo ""

# Check Go
GO_FOUND=0
if command -v go &>/dev/null; then
    GO_VERSION=$(go version | awk '{print $3}')
    echo "[OK] Go gefunden: $GO_VERSION"
    GO_FOUND=1
else
    echo "[FEHLT] Go ist nicht installiert oder nicht im PATH"
fi

if [ "$GO_FOUND" -eq 0 ]; then
    echo ""
    echo "Go 1.22+ wird benoetigt."
    echo ""
    echo "Installation:"
    echo "  Linux:   sudo apt install golang-go   (oder via https://go.dev/dl/)"
    echo "  macOS:   brew install go"
    echo "  Windows: winget install GoLang.Go"
    echo ""
    read -r -p "Trotzdem fortfahren? (j/n): " answer
    if [ "$answer" != "j" ] && [ "$answer" != "J" ]; then
        exit 1
    fi
fi

echo ""

# go mod tidy
echo "[INFO] Fuehre go mod tidy aus..."
go mod tidy
echo "[OK] Abhaengigkeiten aufgeloest"

echo ""

# Build (cross-compile for Windows by default)
echo "[INFO] Baue FluxDB-Installer..."
GOOS=windows GOARCH=amd64 go build -ldflags="-s -w" -o ./bin/FluxDB-Installer.exe .
echo ""
echo "============================================"
echo "[OK] FluxDB-Installer.exe erstellt (bin/)"
echo "============================================"