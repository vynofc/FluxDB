# FluxDB Installer

Go-basierter TUI-Installer für FluxDB. Lädt Versionen von GitHub Releases herunter und entpackt sie nach `%LOCALAPPDATA%\FluxDB`. Bietet interaktive Versionsauswahl, Fortschrittsanzeige, detailliertes Log und optionale Desktop-Verknüpfung.

## Build

### requirements.bat / requirements.sh

Prüft Go-Installation, führt `go mod tidy` aus und baut den Installer:

```bash
# Windows
cd Installer
requirements.bat

# Linux/macOS
cd Installer
bash requirements.sh
```

### build.bat / build.sh

Nur Build (ohne Dependency-Check):

```bash
# Windows
build.bat

# Cross-Compile von Linux/macOS
bash build.sh
```

Ausgabe: `Installer/bin/FluxDB-Installer.exe`

**Voraussetzung**: Go 1.22+

## Verwendung

```bash
# Interaktive Installation (TUI mit Versionsauswahl, Fortschritt, Log)
FluxDB-Installer.exe

# Bestimmte Version installieren (überspringt Versionsauswahl)
FluxDB-Installer.exe --tag v1.2.3

# Alternatives Installationsverzeichnis
FluxDB-Installer.exe --path "D:\MeineApps\FluxDB"

# Silent-Mode (kein TUI, reiner Text-Output — für CI/Scripting)
FluxDB-Installer.exe --silent --tag v1.2.3
```

## CLI-Flags

| Flag | Beschreibung |
|---|---|
| `--tag <version>` | Bestimmte Version installieren (überspringt Versionsauswahl) |
| `--path <dir>` | Alternatives Installationsverzeichnis (Standard: `%LOCALAPPDATA%\FluxDB`) |
| `--silent` | Keine TUI, nur Text-Output |

## Ablauf

```
Loading → Version wählen → Download → Entpacken → Verknüpfung? → Done
   │           │               │            │            │
   ▼           ▼               ▼            ▼            ▼
GitHub API  huh-Select    Fortschritt    ZIP nach    Desktop-.lnk
Releases    (alle Tags)   + Download    %LOCALAPPDATA%  (optional)
abrufen                   in %TEMP%     \FluxDB
```

1. **Loading**: Ruft alle Releases via `GET https://api.github.com/repos/vynofc/FluxDB/releases` ab
2. **Version wählen**: Interaktives `huh`-Select-Menü mit allen verfügbaren Versionen (neueste zuerst)
3. **Download**: Lädt `FluxDB.zip` von GitHub Releases nach `%TEMP%` mit Echtzeit-Fortschrittsbalken
4. **Entpacken**: Extrahiert das ZIP nach `%LOCALAPPDATA%\FluxDB` und schreibt `version.txt`
5. **Verknüpfung**: `huh`-Confirm fragt, ob eine Desktop-Verknüpfung erstellt werden soll
6. **Done**: Erfolgsmeldung mit Installationspfad, Log-Viewport zeigt alle Details

## Abhängigkeiten

| Modul | Zweck |
|---|---|
| [Bubble Tea](https://github.com/charmbracelet/bubbletea) | TUI-Framework |
| [Bubbles](https://github.com/charmbracelet/bubbles) | Spinner, Fortschrittsbalken, Viewport |
| [Huh](https://github.com/charmbracelet/huh) | Interaktive Formulare (Versionsauswahl, Shortcut-Frage) |
| [Lip Gloss](https://github.com/charmbracelet/lipgloss) | Terminal-Styling |
| [Log](https://github.com/charmbracelet/log) | Strukturiertes Logging (Silent-Mode) |

## Architektur

| Datei | Zuständigkeit |
|---|---|
| `main.go` | Entrypoint, CLI-Flag-Parsing, TUI/Silent-Dispatch |
| `model.go` | Bubble Tea Model (States, Messages, Typen) |
| `update.go` | State Machine (Update-Funktion) mit huh-Integration |
| `view.go` | Rendering: Step-Header, Content, Log-Viewport, Footer |
| `forms.go` | huh-Formulare: Versionsauswahl + Shortcut-Abfrage |
| `github.go` | GitHub API: Releases-Liste, Latest Tag, Download-URL |
| `download.go` | ZIP-Download mit progressReader (Echtzeit-Fortschritt) |
| `extract.go` | ZIP-Extraktion + Zip-Slip-Schutz |
| `shortcut.go` | Desktop-Verknüpfung via VBScript (cscript) |
| `helpers.go` | Hilfsfunktionen: Log-Formatierung, Step-Header |
| `silent.go` | Silent-Mode-Logik (ohne Bubble Tea) |
| `styles.go` | Lipgloss Styles (Dark-Theme, Steps, Log-Viewport) |

### State Machine

```
stateLoading → stateSelectVersion → stateDownloading → stateExtracting → stateAskShortcut → stateCreatingShortcut → stateDone
     ↓                  ↓                   ↓                ↓                  ↓                    ↓               ↓
  stateError        stateError          stateError      stateError         stateError           stateError   (Enter/Q→Quit)
```

Mit `--tag` überspringt die Machine `stateSelectVersion` und beginnt direkt bei `stateDownloading`.

## Scope-Abgrenzung

Der Installer macht **nicht**:
- Autostart / Startmenü-Einträge (nur Desktop-Verknüpfung)
- Auto-Update (das macht FluxDB selbst via `SplashWindow`)
- Deinstallation (`%LOCALAPPDATA%\FluxDB` löschen reicht)
- Admin-Rechte (Installation nach `%LOCALAPPDATA%` benötigt keine Elevation)