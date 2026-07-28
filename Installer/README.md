# FluxDB Installer

Go-basierter TUI-Installer für FluxDB. Lädt die neueste Version von GitHub Releases herunter und entpackt sie nach `%LOCALAPPDATA%\FluxDB`.

## Build

```bash
# Windows (build.bat)
cd Installer
build.bat

# Cross-Compile von Linux/macOS (build.sh)
cd Installer
bash build.sh
```

Ausgabe: `Installer/bin/FluxDB-Installer.exe`

**Voraussetzung**: Go 1.22+

## Verwendung

```bash
# Neueste Version installieren (TUI mit Fortschrittsbalken)
FluxDB-Installer.exe

# Bestimmte Version installieren
FluxDB-Installer.exe --tag v1.2.3

# Alternatives Installationsverzeichnis
FluxDB-Installer.exe --path "D:\MeineApps\FluxDB"

# Silent-Mode (kein TUI, reiner Text-Output — für CI/Scripting)
FluxDB-Installer.exe --silent --tag v1.2.3
```

## CLI-Flags

| Flag | Beschreibung |
|---|---|
| `--tag <version>` | Bestimmte Version installieren (überspringt GitHub-API-Call) |
| `--path <dir>` | Alternatives Installationsverzeichnis (Standard: `%LOCALAPPDATA%\FluxDB`) |
| `--silent` | Keine TUI, nur Text-Output |

## Ablauf

```
Fetch Tag  →  Download  →  Extract  →  Done
   │              │            │
   ▼              ▼            ▼
GitHub API    Fortschritt    ZIP nach
  Latest      + Download    %LOCALAPPDATA%
  Release     in %TEMP%     \FluxDB
```

1. **Fetch Tag**: Ermittelt den neuesten Release-Tag via `GET https://api.github.com/repos/vynofc/FluxDB/releases/latest`
2. **Download**: Lädt `FluxDB.zip` von GitHub Releases nach `%TEMP%` mit Fortschrittsanzeige
3. **Extract**: Entpackt das ZIP nach `%LOCALAPPDATA%\FluxDB` und schreibt `version.txt`
4. **Done**: Erfolgsmeldung mit Installationspfad

## Abhängigkeiten

- [Bubble Tea](https://github.com/charmbracelet/bubbletea) — TUI-Framework
- [Bubbles](https://github.com/charmbracelet/bubbles) — Spinner & Fortschrittsbalken
- [Lip Gloss](https://github.com/charmbracelet/lipgloss) — Terminal-Styling
- [Log](https://github.com/charmbracelet/log) — Strukturiertes Logging

## Architektur

| Datei | Zuständigkeit |
|---|---|
| `main.go` | Entrypoint, CLI-Flag-Parsing, TUI / Silent-Dispatch |
| `model.go` | Bubble Tea Model (States, Messages, Typen) |
| `update.go` | State Machine (Update-Funktion) |
| `view.go` | Rendering (TUI + Silent-View) |
| `github.go` | GitHub API: Latest Release Tag + Download-URL |
| `download.go` | ZIP-Download mit Fortschritt |
| `extract.go` | ZIP-Extraktion + Zip-Slip-Schutz |
| `silent.go` | Silent-Mode-Logik (ohne Bubble Tea) |
| `styles.go` | Lipgloss Styles (Dark-Theme) |

### State Machine

```
stateFetchingTag → stateDownloading → stateExtracting → stateDone
     ↓                  ↓                  ↓              ↓
  stateError        stateError         stateError     (Enter→Quit)
```

## Scope-Abgrenzung

Der Installer macht **nicht**:
- Autostart / Startmenü-Einträge
- Auto-Update (das macht FluxDB selbst via `SplashWindow`)
- Deinstallation (`%LOCALAPPDATA%\FluxDB` löschen reicht)
- Admin-Rechte (Installation nach `%LOCALAPPDATA%` benötigt keine Elevation)