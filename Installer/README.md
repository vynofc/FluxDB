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
# Einfache Installation (nimmt automatisch neueste Version, minimale Anzeige)
FluxDB-Installer.exe

# Detail-Modus (Versionsauswahl + ausführliches Log)
FluxDB-Installer.exe --detail

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
| `--detail` | Detailmodus: interaktive Versionsauswahl + ausführliches Log

## Ablauf

### Standard-Modus (ohne --detail)

```
Version ermitteln → Download → Entpacken → Shortcut? → Done
     │                 │            │            │
     ▼                 ▼            ▼            ▼
GitHub API         Fortschritt    ZIP nach    Desktop-.lnk
Latest Release     + Download    %LOCALAPPDATA%  (optional)
                   in %TEMP%     \FluxDB
```

1. **Version ermitteln**: Holt den neuesten Release-Tag via GitHub API
2. **Download**: Lädt `FluxDB.zip` nach `%TEMP%` mit Fortschrittsbalken
3. **Entpacken**: Extrahiert nach `%LOCALAPPDATA%\FluxDB`, schreibt `version.txt`
4. **Shortcut**: `huh`-Confirm fragt nach Desktop- und Startmenü-Verknüpfung
5. **Done**: Erfolgsmeldung mit Installationspfad

### Detail-Modus (mit --detail)

```
Loading → Version wählen → Download → Entpacken → Shortcut? → Done
   │           │               │            │            │
   ▼           ▼               ▼            ▼            ▼
GitHub API  huh-Select    Fortschritt    ZIP nach    Desktop-.lnk
Releases    (alle Tags)   + Download    %LOCALAPPDATA%  (optional)
abrufen                   in %TEMP%     \FluxDB
```

Zusätzlich: ausführlicher Log-Viewport mit allen API-Abfragen, Dateipfaden und Statusmeldungen.

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
- Auto-Update (das macht FluxDB selbst via `SplashWindow`)
- Deinstallation (`%LOCALAPPDATA%\FluxDB` löschen reicht)
- Admin-Rechte (Installation nach `%LOCALAPPDATA%` benötigt keine Elevation)

## Verknüpfungen

Auf Wunsch erstellt der Installer:
- **Desktop**: `FluxDB.lnk` auf dem Desktop
- **Startmenü**: `FluxDB.lnk` unter `Start Menu\Programs\FluxDB\`

Beide werden via VBScript (`cscript`) erstellt und zeigen auf die `FluxDB.exe` im Installationsverzeichnis.