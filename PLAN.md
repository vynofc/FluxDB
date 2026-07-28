# FluxDB Installer — Plan

## Ziel

Ein Go-basierter CLI-Installer (TUI) für FluxDB, der über GitHub Releases die neueste Version herunterlädt und nach `%LOCALAPPDATA%\FluxDB` entpackt. Der Installer wird im Release-Workflow mitgebaut und als eigenes Release-Asset bereitgestellt.

---

## 1. Ordnerstruktur

```
Installer/
├── main.go              # Entrypoint, Bubble Tea App
├── go.mod
├── go.sum
├── model.go             # Bubble Tea Model (States, Messages)
├── update.go            # Update-Funktion (State Machine)
├── view.go              # View-Funktion (Rendering)
├── github.go            # GitHub API: Latest Release Tag ermitteln
├── download.go          # ZIP-Download mit Fortschritt
├── extract.go           # ZIP entpacken nach %LOCALAPPDATA%\FluxDB
├── styles.go            # Lipgloss Styles
└── README.md
```

---

## 2. Abhängigkeiten (charmbracelet)

| Modul | Zweck |
|---|---|
| `github.com/charmbracelet/bubbletea` | TUI-Framework (Model/Update/View) |
| `github.com/charmbracelet/huh` | Formular-Komponenten (optional, für Benutzereingaben) |
| `github.com/charmbracelet/lipgloss` | Terminal-Styling |
| `github.com/charmbracelet/bubbles` | Fortschrittsbalken (`progress`), Spinner |
| `github.com/charmbracelet/log` | Strukturiertes Logging |

---

## 3. Ablauf (State Machine)

```
┌──────────────┐
│  Fetch Tag   │  GET https://api.github.com/repos/vynofc/FluxDB/releases/latest
│              │  → Extrahiere "tag_name" (z.B. "v1.2.3")
└──────┬───────┘
       │ tag
       ▼
┌──────────────┐
│  Download    │  GET https://github.com/vynofc/FluxDB/releases/download/{tag}/FluxDB.zip
│              │  → Speichere in %TEMP%\FluxDB-{tag}.zip
│              │  → Zeige Fortschrittsbalken (bubbles/progress)
└──────┬───────┘
       │ zip path
       ▼
┌──────────────┐
│  Extract     │  Entpacke ZIP nach %LOCALAPPDATA%\FluxDB\
│              │  → Überschreibe existierende Dateien
│              │  → Zeige Spinner während des Entpackens
└──────┬───────┘
       │
       ▼
┌──────────────┐
│  Done        │  Erfolgsmeldung: "FluxDB {tag} installiert!"
│              │  Optional: "Verknüpfung erstellen?" (huh-Form)
└──────────────┘
```

---

## 4. GitHub API

- **Endpoint**: `https://api.github.com/repos/vynofc/FluxDB/releases/latest`
- **Feld**: `tag_name` (z.B. `"v1.0.0"`)
- Kein Token nötig (öffentliches Repo), aber `User-Agent` Header erforderlich
- Falls API-Limit erreicht (60 req/h ohne Token): Fallback auf eine hartcodierte URL? Oder Fehlermeldung.

---

## 5. Download & Extract

- **Download-URL**: `https://github.com/vynofc/FluxDB/releases/download/{tag}/FluxDB.zip`
- **Ziel-Temp**: `os.TempDir() + "/FluxDB-{tag}.zip"` (wird nach dem Entpacken gelöscht)
- **Ziel-Install**: `os.Getenv("LOCALAPPDATA") + "/FluxDB"` (bzw. `%LOCALAPPDATA%\FluxDB`)
- **Entpacken**: `archive/zip` aus der Standardbibliothek
- **Aufräumen**: Temp-ZIP nach erfolgreichem Entpacken löschen

---

## 6. CLI-Flags

| Flag | Beschreibung |
|---|---|
| `--tag <version>` | Bestimmte Version installieren (überspringt API-Call) |
| `--path <dir>` | Alternatives Installationsverzeichnis |
| `--silent` | Keine TUI, nur Text-Output (für Scripting/CI) |

---

## 7. Build & Workflow-Integration

### Lokaler Build

```bash
cd Installer
go mod tidy
go build -ldflags="-s -w" -o ../bin/FluxDB-Installer.exe .
```

### Workflow

Der Installer muss nicht im `build.yml` gebaut werden (nur WPF-App). Stattdessen **im `release.yml`**:

1. `actions/setup-go@v5` mit Go 1.22+
2. `cd Installer && go build -ldflags="-s -w" -o ../FluxDB-Installer.exe .`
3. `FluxDB-Installer.exe` als **zusätzliches Release-Asset** hochladen (neben `FluxDB.zip`)

### Matrix-Strategie (optional)

Go cross-compiled einfach: `GOOS=windows GOARCH=amd64`. Da das Release nur auf `windows-latest` läuft, reicht ein amd64-Build. Architektur-Erweiterung (arm64) später möglich.

---

## 8. Was der Installer NICHT macht (Scope-Abgrenzung)

- **Kein Autostart / Startmenü-Einträge** — nur entpacken. (Kann später mit `huh`-Formular ergänzt werden)
- **Kein Auto-Update-Mechanismus** — das macht die App selbst (`SplashWindow` checkt `version.txt`)
- **Kein Admin-Rechte** — Installation nach `%LOCALAPPDATA%` benötigt keine Elevation
- **Keine Deinstallation** — `%LOCALAPPDATA%\FluxDB` löschen reicht

---

## 9. Offene Fragen

1. **huh-Einsatz**: Soll der Installer nach dem Entpacken fragen, ob eine Desktop-Verknüpfung erstellt werden soll? (huh-Form wäre ideal dafür)
2. **Silent-Mode**: Für CI/CD-Scripting? Wäre `--silent` ohne Bubble Tea TUI.
3. **Fehlerbehandlung**: Was passiert wenn `%LOCALAPPDATA%\FluxDB` bereits existiert? → Überschreiben (mit Warnung im Log).
4. **Version-Check**: Soll der Installer vor dem Download prüfen, ob bereits die neueste Version installiert ist? (Z.B. durch Lesen einer `version.txt` im Installationsverzeichnis)

---

## 10. Nächste Schritte (Implementierung)

1. `Installer/` Ordner anlegen, `go mod init`
2. `main.go` mit Bubble Tea Grundgerüst
3. `github.go` — API-Client für Tag-Fetch
4. `download.go` — ZIP-Download mit Fortschritt
5. `extract.go` — ZIP-Extraktion
6. `model.go` / `update.go` / `view.go` — State Machine & UI
7. `styles.go` — Lipgloss-Theme (passend zum FluxDB-Dark-Theme)
8. `release.yml` erweitern um Go-Build-Schritt + Asset-Upload
9. Lokal testen: `go run .`
10. Release testen: Tag pushen → Workflow prüfen