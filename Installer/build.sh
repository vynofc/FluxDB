#!/bin/bash
set -e
cd "$(dirname "$0")"
GOOS=windows GOARCH=amd64 go build -ldflags="-s -w" -o ../bin/FluxDB-Installer.exe .
echo "✓ FluxDB-Installer.exe erstellt (bin/)"