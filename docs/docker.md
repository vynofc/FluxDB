# Docker-Builds für FluxDB

Diese Anleitung beschreibt die Dockerfiles und das Compose-Setup des Projekts.

## Übersicht

| Komponente | Dockerfile | Container-Typ | Zweck |
|---|---|---|---|
| WPF-App | [WPF/FluxDB/Dockerfile](../WPF/FluxDB/Dockerfile) | **Windows-Container** | `dotnet publish` der WPF-App |
| Installer | [Installer/Dockerfile](../Installer/Dockerfile) | Linux-Container (Cross-Compile) | `FluxDB-Installer.exe` |
| Log-Viewer | [Log_Viewer/Dockerfile](../Log_Viewer/Dockerfile) | Linux-Container (Cross-Compile) | `Log_Viewer.exe` |

**Wichtig:** Die WPF-App kann nicht in einem Linux-Container gebaut werden (WPF erfordert Windows).
Docker Desktop kann Windows- und Linux-Container nicht gleichzeitig ausführen — daher sind die
Services in [compose.yaml](../compose.yaml) über **Profile** getrennt.

Die Dockerfiles sind reine **Build-Images**: Sie bauen die Artefakte und kopieren sie beim
Container-Start in ein gemountetes Volume unter `./bin/docker/<service>/`. Die WPF-App ist eine
GUI-Anwendung und kann nicht *in* einem Container ausgeführt werden.

## Voraussetzungen

- Docker Desktop für Windows
- Für den WPF-Build: Wechsel in den **Windows-Container-Modus**
  (Rechtsklick auf das Docker-Tray-Icon → *Switch to Windows containers…*)
- Für die Go-Builds: **Linux-Container-Modus** (Standard)

## Verwendung

### Alles mit Compose (empfohlen)

```powershell
# 1. Windows-Container-Modus aktivieren, dann:
docker compose --profile windows up wpf
#    → Artefakte in ./bin/docker/wpf/

# 2. Zurück in den Linux-Container-Modus wechseln, dann:
docker compose --profile linux up installer log-viewer
#    → Artefakte in ./bin/docker/installer/ und ./bin/docker/log-viewer/
```

### Einzelne Builds ohne Compose

```powershell
# WPF (Windows-Container-Modus, Build-Kontext = Repo-Root!)
docker build -f WPF/FluxDB/Dockerfile -t fluxdb-wpf-build .
docker run --rm -v "${PWD}\bin\docker\wpf:C:\out" fluxdb-wpf-build

# Installer (Linux-Container-Modus)
docker build -t fluxdb-installer-build ./Installer
docker run --rm -v "${PWD}/bin/docker/installer:/out" fluxdb-installer-build

# Log-Viewer (Linux-Container-Modus)
docker build -t fluxdb-logviewer-build ./Log_Viewer
docker run --rm -v "${PWD}/bin/docker/log-viewer:/out" fluxdb-logviewer-build
```

## Technische Details

### WPF-Dockerfile

- Basis: `mcr.microsoft.com/dotnet/sdk:10.0` (Windows Server Core), passend zu `net10.0-windows7.0`.
- Der Build-Kontext **muss das Repo-Root sein**, weil [FluxDB.csproj](../WPF/FluxDB/FluxDB.csproj)
  die Assembly-Version per MSBuild-Target aus `version.txt` im Repo-Root generiert.
- Layer-Caching: `version.txt` + `.csproj` werden zuerst kopiert, dann `dotnet restore`, dann
  erst der restliche Quellcode.
- Export-Stage: `windows/servercore:ltsc2022`, kopiert den Publish-Output per `xcopy` nach `C:\out`.

### Go-Dockerfiles

- Basis: `golang:1.26-alpine` (Installer, `go 1.26.1`) bzw. `golang:1.24-alpine` (Log-Viewer, `go 1.24.0`).
- Cross-Compile: `CGO_ENABLED=0 GOOS=windows GOARCH=amd64 go build -ldflags="-s -w"`
  — entspricht exakt den bisherigen lokalen Build-Flags.
- Export-Stage: `alpine:3.22`, kopiert die `.exe` beim Start nach `/out`.

### .dockerignore

Die [.dockerignore](../.dockerignore) im Root hält den Build-Kontext klein
(`.git`, `bin/`, `obj/`, `packages/`, Markdown-Dokumente — ausgenommen `version.txt`).

## Einschränkungen

- **Kein Testen im Container:** Es gibt keine Test-Suite und die Artefakte sind Windows-Binaries;
  die Container dienen ausschließlich dem reproduzierbaren Bauen.
- **Kein gleichzeitiger Build aller Services:** Der Container-Modus von Docker Desktop muss
  zwischen Windows (WPF) und Linux (Go) gewechselt werden.
- Der bisherige `build.bat`-Workflow bleibt unverändert der Standard für lokale Release-Builds;
  Docker ist eine zusätzliche, reproduzierbare Alternative.
