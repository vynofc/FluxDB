#!/bin/bash
set -e
cd "$(dirname "$0")"

ROOT_DIR="$(pwd)"
BIN_DIR="$ROOT_DIR/bin"
WPF_PROJECT="$ROOT_DIR/WPF/FluxDB/FluxDB.csproj"
INSTALLER_DIR="$ROOT_DIR/Installer"
LOG_VIEWER_DIR="$ROOT_DIR/Log_Viewer"

ensure_bin_dir() {
  mkdir -p "$BIN_DIR"
}

build_wpf() {
  echo ""
  echo "[1/2] Restore NuGet packages..."
  if [ -f "nuget.exe" ]; then
    mono nuget.exe restore "$WPF_PROJECT" -PackagesDirectory WPF/FluxDB/packages
  else
    echo "nuget.exe not found. Please run install-requirements.sh first."
    exit 1
  fi
  echo "[2/2] Build WPF App..."
  ensure_bin_dir
  msbuild "$WPF_PROJECT" /p:Configuration=Release /p:Platform=AnyCPU /p:OutDir="$BIN_DIR/"
}

build_installer() {
  echo ""
  echo "Building Installer into bin/..."
  ensure_bin_dir
  (cd "$INSTALLER_DIR" && GOOS=windows GOARCH=amd64 go build -ldflags="-s -w" -o "$BIN_DIR/FluxDB-Installer.exe" .)
}

build_logviewer() {
  echo ""
  echo "Building Log Viewer into bin/components/..."
  ensure_bin_dir
  mkdir -p "$BIN_DIR/components"
  (cd "$LOG_VIEWER_DIR" && GOOS=windows GOARCH=amd64 go build -ldflags="-s -w" -o "$BIN_DIR/components/Log_Viewer.exe" .)
}

build_full() {
  echo ""
  echo "Building full release package..."
  build_wpf
  build_installer
  build_logviewer

  if command -v zip >/dev/null 2>&1; then
    (cd "$BIN_DIR" && zip -r "$ROOT_DIR/FluxDB.zip" .)
  else
    echo "zip command not found; skipping archive creation."
  fi

  echo ""
  echo "✓ Full release package created"
  echo "  bin/FluxDB.exe"
  echo "  bin/FluxDB-Installer.exe"
  echo "  bin/components/Log_Viewer.exe"
  echo "  FluxDB.zip"
}

show_menu() {
  echo "=== FluxDB Build Menu ==="
  echo ""
  echo "1) Install requirements"
  echo "2) Build WPF app"
  echo "3) Build Installer"
  echo "4) Build Log Viewer"
  echo "5) Build full release package"
  echo "6) Exit"
  echo ""
  read -r -p "Choose an option [1-6]: " choice
}

choice="${1:-}"
if [ -z "$choice" ]; then
  while true; do
    show_menu

    case "$choice" in
      1)
        bash install-requirements.sh
        ;;
      2)
        build_wpf
        ;;
      3)
        build_installer
        ;;
      4)
        build_logviewer
        ;;
      5)
        build_full
        ;;
      6)
        exit 0
        ;;
      *)
        echo "Invalid selection."
        ;;
    esac

    echo ""
    read -r -p "Press Enter to return to the menu..." _
    clear
  done
else
  case "$choice" in
    1)
      bash install-requirements.sh
      ;;
    2)
      build_wpf
      ;;
    3)
      build_installer
      ;;
    4)
      build_logviewer
      ;;
    5)
      build_full
      ;;
    6)
      exit 0
      ;;
    *)
      echo "Invalid selection."
      ;;
  esac
fi
