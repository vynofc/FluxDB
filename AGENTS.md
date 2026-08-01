# AGENTS.md — FluxDB

## Projektübersicht

FluxDB besteht aus drei Komponenten:

1. **WPF-App** unter [WPF/FluxDB](WPF/FluxDB/) – Windows-Desktopanwendung mit Dateisuche, Indizierung, Tags, Vorschau und Einstellungen.
2. **Installer** unter [Installer](Installer/) – Go-basierter TUI-Installer für die Verteilung.
3. **Log-Viewer** unter [Log_Viewer](Log_Viewer/) – separater Viewer für Anwendungs- und Indexierungs-Logs.

**Wichtige Einschränkung:** Das Projekt ist Windows-only und verwendet WPF, COM-Interop sowie feste Pfade wie `C:\NSCE\FluxDB`.

---

## Build & Run

### WPF-App

```powershell
# Restore NuGet packages
nuget restore WPF/FluxDB/FluxDB.csproj

# Build (Release)
msbuild WPF/FluxDB/FluxDB.csproj /p:Configuration=Release /p:Platform=AnyCPU /p:OutDir=bin\

# Build (Debug)
msbuild WPF/FluxDB/FluxDB.csproj /p:Configuration=Debug /p:Platform=AnyCPU /p:OutDir=bin\
```

Ausgabe: [bin](bin/) mit `FluxDB.exe`, `FluxDB-Installer.exe`, `components/Log_Viewer.exe`, `x64/SQLite.Interop.dll`, `x86/SQLite.Interop.dll` und `FluxDB.zip` nach dem Full-Build.

### Installer (Go)

```powershell
cd Installer
build.bat
```

Ausgabe: [bin](bin/).

### Log-Viewer (Go)

```powershell
cd Log_Viewer
go build -ldflags="-s -w" -o ..\bin\Log_Viewer.exe .
```

**Es gibt derzeit keine Test-Suite im Repository.**

---

## CI / Workflows

| Workflow | Trigger | Was passiert |
|---|---|---|
| `build.yml` | Push/PR auf `main` | Baut die WPF-App aus [WPF/FluxDB](WPF/FluxDB/) und den Log-Viewer |
| `release.yml` | Veröffentlichtes Release | Baut WPF-App, Installer und Log-Viewer, erzeugt `FluxDB.zip` |

---

## Architekturhinweise

Die WPF-App liegt jetzt unter [WPF/FluxDB](WPF/FluxDB/) und enthält die typischen Bausteine:

- UI-Dateien wie `App.xaml`, `MainWindow.xaml`, `SettingsWindow.xaml` und die Dialogfenster
- Modelle unter `Models/`
- Services unter `Services/`
- Hilfsfunktionen wie `VersionHelper.cs`

Die Build-Skripte und GitHub-Workflows verwenden diese neue Struktur, damit die App sauber von den anderen Komponenten getrennt ist.
#### Database schema

SQLite database file named `.fluxdb` lives in the root of the indexed folder:

- `files` — `id`, `path` (UNIQUE), `name`, `extension`, `size`, `created_at`, `modified_at`, `deleted` (0/1), `last_indexed_at`
- `tags` — `id`, `name` (UNIQUE, lowercase-trimmed)
- `file_tags` — `file_id`, `tag_id` (composite PK)
- `notes` — `file_id` (PK), `note`

#### UI layer

- `MainWindow` — primary file browser/search/tagging UI. Dark theme defined in XAML resources.
- `SettingsWindow` — update check, auto-update toggle, export button.
- `LogViewer` — reads `LoggingService.GetLogs()`.
- `RefreshDialog` — modal with three options: rescan entire root, current view, or specific folder.
- `SplashWindow` — transient startup window with update check.

### FluxDB Installer (Go)

#### Startup flow

1. `main.go` parses CLI flags (`--tag`, `--path`, `--silent`)
2. If `--silent`: runs `runSilent()` — plain text output, no TUI
3. Otherwise: starts Bubble Tea TUI with `initialModel()`

#### State machine

```
stateFetchingTag → stateDownloading → stateExtracting → stateDone
     ↓                  ↓                  ↓              ↓
  stateError        stateError         stateError     (Enter→Quit)
```

#### Source files

| File | Responsibility |
|---|---|
| `main.go` | Entrypoint, CLI flag parsing, TUI/Silent dispatch |
| `model.go` | Bubble Tea model, states, message types |
| `update.go` | State machine (Update function) |
| `view.go` | Rendering (TUI + silent view) |
| `github.go` | GitHub API: fetch latest release tag, build download URL |
| `download.go` | ZIP download from GitHub Releases to `%TEMP%` |
| `extract.go` | ZIP extraction to `%LOCALAPPDATA%\FluxDB`, writes `version.txt`, cleans up temp file |
| `silent.go` | Silent-mode logic (no Bubble Tea), `fetchTag`/`downloadSilent`/`extractSilent` helpers |
| `styles.go` | Lipgloss styles (dark theme matching FluxDB) |

#### CLI flags

| Flag | Description |
|---|---|
| `--tag <version>` | Install specific version (skips GitHub API call) |
| `--path <dir>` | Alternative install directory (default: `%LOCALAPPDATA%\FluxDB`) |
| `--silent` | No TUI, text-only output (for CI/scripting) |

#### Dependencies

| Module | Purpose |
|---|---|
| `github.com/charmbracelet/bubbletea` | TUI framework |
| `github.com/charmbracelet/bubbles` | Progress bar, spinner |
| `github.com/charmbracelet/lipgloss` | Terminal styling |
| `github.com/charmbracelet/log` | Structured logging |

#### What the installer does NOT do

- No autostart / Start Menu entries
- No auto-update (FluxDB handles this via `SplashWindow`)
- No uninstall (delete `%LOCALAPPDATA%\FluxDB` manually)
- No admin rights needed (installs to `%LOCALAPPDATA%`)

---

## Important Patterns & Gotchas

### SQL and tags

- Tags are stored via `GROUP_CONCAT(t.name, '\0')` with **null-byte separator** `\0` (was previously `, ` which broke on tag names containing commas). When parsing back, split on `'\0'` with `StringSplitOptions.RemoveEmptyEntries`.
- When adding new `GROUP_CONCAT` queries or modifying tag handling, always use `\0` as the separator.

### Database file location

The database file `.fluxdb` is stored **inside the indexed folder**, not in `%LocalAppData%`. This means each root folder has its own independent index. `SettingsService.GetDatabasePath()` returns `%LocalAppData%\FluxDB\fluxdb.db` but is **not used** — `MainWindow` constructs the db path as `Path.Combine(folderPath, ".fluxdb")`.

### Batch commits in IndexerService

`IndexerService.ScanFolderAsync` uses batched transactions (every 1000 files). `CommitBatchWithRetry` retries up to 3 times with exponential backoff (100ms → 200ms → 400ms). On final failure, it rolls back and rethrows. When cancelled, the current transaction is explicitly rolled back.

### Thread safety

- `LoggingService` uses a `lock` and `ThreadPool.QueueUserWorkItem` for background file writes.
- `DatabaseService` has a single `SQLiteConnection` — all DB access is serial (no connection pooling). SQLite writes are not thread-safe; ensure all DB operations happen on the same thread or serialize access.

### Dispatcher.Invoke in background tasks

`MainWindow` at several points calls `Dispatcher.Invoke` (synchronous) inside `Task.Run` — this blocks the background thread waiting for the UI thread. Prefer `Dispatcher.BeginInvoke` for fire-and-forget UI updates from background tasks.

### Folder rename / delete must update DB

When renaming or deleting folders, the DB entries for all files under that path must be updated. `DatabaseService` has `UpdateFolderPath()` and `MarkPathAsDeleted()` for this purpose. Always use these methods — do not do raw SQL for folder-level operations.

### Version comparison

`VersionHelper` is a **static utility** with `NormalizeVersion` and `CompareVersions`. It strips `v` prefix, `!beta` suffix, and handles `x.y.z-suffix` semver-like strings. The `Version` class is used for numeric comparison, then string comparison for pre-release suffixes. Do not duplicate version logic — always use `VersionHelper`.

### Auto-update

- Version check: HTTP GET `https://nsce-cdn.fun/FluxDB/version.txt` (comma-separated versions, `!beta` suffix for beta releases).
- Installer: `C:\NSCE\FluxDB\FluxDB-Installer.exe` or downloaded zip from `C:\NSCE\FluxDB\{version}.zip`.
- Skip with `--noupdate` CLI flag.
- Central version file: `C:\NSCE\FluxDB\version.txt` (overridable via `FLUXDB_CENTRAL_DIR` env var).

### PLAN.md

The `PLAN.md` file at the repo root documents the Installer design and known bugs/planned refactoring for the WPF app. It is not a spec for new features — it's a planning document. Many of the issues listed there have been partially addressed (e.g., `InitDb` now uses transactions, `GROUP_CONCAT` uses `\0` separator, `SearchFiles` accepts a `folderPath` parameter, `MarkPathAsDeleted` and `UpdateFolderPath` exist, `CommitBatchWithRetry` was added) but the file was not updated to reflect fixes.

---

## Conventions

### FluxDB (C# / WPF)

- **Namespace**: `FluxDB` for UI/root, `FluxDB.Models` for models, `FluxDB.Services` for services.
- **Naming**: PascalCase for public, `_camelCase` for private fields. Controls use Hungarian-like prefixes (`txtSearch`, `btnRefresh`, `dgFiles`, `pnlProgress`).
- **Error handling**: Broad try-catch with silent swallowing is common. `LoggingService.Log()` is used to record errors.
- **German UI**: Some UI strings and comments are in German (the project is German-authored).
- **No async/await in constructors**: Services are initialized synchronously; async work is fire-and-forget or triggered by UI events.
- **XAML**: Dark theme with hardcoded color brushes. No resource dictionaries or theming abstraction.

### Installer (Go)

- **Package**: Single `main` package (no sub-packages).
- **Naming**: Standard Go conventions (camelCase, PascalCase for exports).
- **Error handling**: Errors propagated as Bubble Tea messages (`errMsg`), surfaced in TUI or stderr.
- **German UI**: All user-facing strings are in German.
- **Build**: `-ldflags="-s -w"` for stripped release binaries. Cross-compiled with `GOOS=windows GOARCH=amd64`.