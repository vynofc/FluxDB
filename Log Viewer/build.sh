#!/bin/bash
set -e
cd "$(dirname "$0")"
GOOS=windows GOARCH=amd64 go build -ldflags="-s -w" -o ./bin/Log_Viewer.exe .
echo "✓ Log_Viewer.exe erstellt (bin/)"