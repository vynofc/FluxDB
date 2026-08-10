# FluxDB Installer

Go-basierter TUI-Installer für FluxDB. Lädt Versionen von GitHub Releases herunter und entpackt sie nach `%LOCALAPPDATA%\FluxDB`. Bietet interaktive Versionsauswahl, Fortschrittsanzeige, detailliertes Log und optionale Desktop- und Startmenü-Verknüpfung.

## Build

### requirements.bat / requirements.sh

Prüft Go-Installation, führt `go mod tidy` aus und baut den Installer in das
Root-Verzeichnis `bin/`:

```bash
# Windows
cd Installer
requirements.bat

# Linux/macOS
cd Installer
bash requirements.sh
```

### Direkter Build

```bash
cd Installer
go build -ldflags="-s -w" -o ../bin/FluxDB-Installer.exe .
```

Alternativ über das zentrale Build-Skript im Repo-Root:

```powershell
build.bat 3
```

Ausgabe: `bin/FluxDB-Installer.exe` (Repo-Root)

**Voraussetzung**: Go 1.26+ (siehe `go.mod`)

## Verwendung

```bash
# Einfache Installation (nimmt automatisch neueste stabile Version, minimale Anzeige)
FluxDB-Installer.exe

# Beta-Releases bei der Versionsermittlung einschliessen
FluxDB-Installer.exe --beta

# Detail-Modus (Versionsauswahl + ausführliches Log)
FluxDB-Installer.exe --detail

# Bestimmte Version installieren (überspringt Versionsauswahl)
FluxDB-Installer.exe --tag v1.2.3

# Alternatives Installationsverzeichnis
FluxDB-Installer.exe --path "D:\MeineApps\FluxDB"

# Silent-Mode (kein TUI, reiner Text-Output — für CI/Scripting)
FluxDB-Installer.exe --silent --tag v1.2.3

# Silent-Mode mit automatischem Start von FluxDB nach der Installation
FluxDB-Installer.exe --silent-start
```

## CLI-Flags

| Flag | Beschreibung |
|---|---|
| `--tag <version>` | Bestimmte Version installieren (überspringt Versionsauswahl) |
| `--path <dir>` | Alternatives Installationsverzeichnis (Standard: `%LOCALAPPDATA%\FluxDB`) |
| `--silent` | Keine TUI, nur Text-Output |
| `--silent-start` | Wie `--silent`, startet FluxDB nach der Installation automatisch |
| `--detail` | Detailmodus: interaktive Versionsauswahl + ausführliches Log |
| `--beta` | Beta-Releases (Prereleases) bei der Versionsermittlung einschliessen |

## Ablauf

### Standard-Modus (ohne --detail)

```
Version ermitteln → Download → Entpacken → Shortcut? → Done
     │                 │            │            │
     ▼                 ▼            ▼            ▼
GitHub API         Fortschritt    ZIP nach    Desktop-.lnk
Latest Release     + Download    %LOCALAPPDATA%  + Startmenü
                   in %TEMP%     \FluxDB       (optional)
```

1. **Version ermitteln**: Holt den neuesten Release-Tag via GitHub API
   (mit `--beta` werden auch Prereleases berücksichtigt)
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
Releases    (alle Tags)   + Download    %LOCALAPPDATA%  + Startmenü
abrufen                   in %TEMP%     \FluxDB       (optional)
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
| `main.go` | Entrypoint, CLI-Flag-Parsing, TUI/Silent-Dispatch, `launchFluxDB` |
| `model.go` | Bubble Tea Model (States, Messages, Typen) |
| `update.go` | State Machine (Update-Funktion) mit huh-Integration |
| `view.go` | Rendering: Step-Header, Content, Log-Viewport, Footer |
| `forms.go` | huh-Formulare: Versionsauswahl + Shortcut-Abfrage |
| `github.go` | GitHub API: Releases-Liste (inkl. Beta-Filter), Latest Tag, Download-URL |
| `download.go` | ZIP-Download mit progressReader (Echtzeit-Fortschritt) |
| `extract.go` | ZIP-Extraktion + Zip-Slip-Schutz, Locked-File-Handling |
| `shortcut.go` | Desktop- und Startmenü-Verknüpfung via PowerShell COM (WScript.Shell) |
| `helpers.go` | Hilfsfunktionen: Log-Formatierung, Step-Header |
| `silent.go` | Silent-Mode-Logik (ohne Bubble Tea), LOCALAPPDATA-Fallback |
| `styles.go` | Lipgloss Styles (Dark-Theme, Steps, Log-Viewport) |

### State Machine

```
stateLoading → stateSelectVersion → stateDownloading → stateExtracting → stateAskShortcut → stateCreatingShortcut → stateDone
     ↓                  ↓                   ↓                ↓                  ↓                    ↓               ↓
  stateError        stateError          stateError      stateError         stateError           stateError   (Enter/Q→Quit)
```

Mit `--tag` überspringt die Machine `stateSelectVersion` und beginnt direkt bei `stateDownloading`.
Im Standard-Modus (ohne `--detail`) wird `stateSelectVersion` ebenfalls übersprungen.

## Scope-Abgrenzung

Der Installer macht **nicht**:
- Auto-Update (das macht FluxDB selbst via `SplashWindow`)
- Deinstallation (`%LOCALAPPDATA%\FluxDB` löschen reicht)
- Admin-Rechte (Installation nach `%LOCALAPPDATA%` benötigt keine Elevation)

## Verknüpfungen

Auf Wunsch erstellt der Installer:
- **Desktop**: `FluxDB.lnk` auf dem Desktop
- **Startmenü**: `FluxDB.lnk` unter `Start Menu\Programs\FluxDB\`

Beide werden via PowerShell COM-Script (`WScript.Shell`) erstellt und zeigen auf die
`FluxDB.exe` im Installationsverzeichnis.
