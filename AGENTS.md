# AGENTS.md — FluxDB

## Project Overview

FluxDB consists of two components:

1. **FluxDB** — a **WPF desktop application** (C# 7.3, .NET Framework 4.7.2) for Windows. It scans local folders, indexes file metadata into an embedded SQLite database, and provides a file manager with tagging, search, preview, and export capabilities.
2. **FluxDB Installer** — a **Go-based TUI installer** (Go 1.22+) that downloads the latest FluxDB release from GitHub and extracts it to `%LOCALAPPDATA%\FluxDB`.

**Key constraint**: Windows-only. Uses WPF, shell32.dll COM interop, and hardcoded `C:\NSCE\FluxDB` paths.

---

## Build & Run

### FluxDB (WPF App)

```bash
# Restore NuGet packages
nuget restore FluxDB.sln

# Build (Release)
msbuild FluxDB.sln /p:Configuration=Release /p:Platform="Any CPU"

# Build (Debug)
msbuild FluxDB.sln /p:Configuration=Debug /p:Platform="Any CPU"
```

Executable output: `bin/Release/FluxDB.exe` or `bin/Debug/FluxDB.exe`.

### FluxDB Installer (Go)

```bash
# Windows
cd Installer && build.bat

# Cross-compile from Linux/macOS
cd Installer && bash build.sh
```

Executable output: `Installer/bin/FluxDB-Installer.exe`.

**No test suite exists** in this project.

---

## CI / Workflows

| Workflow | Trigger | What it does |
|---|---|---|
| `build.yml` | Push/PR to `main` | Builds FluxDB.sln (MSBuild) on `windows-latest` |
| `release.yml` | Release published | Builds FluxDB.sln + Installer (Go), packages both into `FluxDB.zip`, uploads zip + `FluxDB-Installer.exe` as release assets |

---

## Architecture

### FluxDB (WPF App)

#### Startup flow

1. `App.xaml` sets `StartupUri="SplashWindow.xaml"`
2. `SplashWindow` checks for updates (HTTP GET `https://nsce-cdn.fun/FluxDB/version.txt`), then creates and shows `MainWindow`
3. `MainWindow` constructor calls `InitializeServices()` → `LoadInitialData()`

#### Service layer

| Service | Type | Responsibilities |
|---|---|---|
| `SettingsService` | instance | Loads/saves `AppSettings` as JSON from `%LocalAppData%\FluxDB\settings.json`. Manages recent folders (max 10). |
| `DatabaseService` | instance, `IDisposable` | Opens SQLite connection to `.fluxdb` file in the indexed folder. All CRUD for files, tags, notes. |
| `IndexerService` | instance | Recursively scans a folder, builds `FileEntry` objects, upserts into DB via `DatabaseService`. Raises `ProgressChanged` and `StatusChanged` events. |
| `ExportService` | instance | Converts DB contents to JSON (`IndexExport` model), writes to file or GZip stream. |
| `LoggingService` | **static** | Thread-safe in-memory log buffer (2000 lines) + background file writer to `%LocalAppData%\FluxDB\logs.txt`. |

#### Models

| Model | Notes |
|---|---|
| `FileEntry` | `INotifyPropertyChanged` for WPF binding. Has computed `SizeDisplay`, `Icon`, `IconColor` properties. Tags are stored as `List<string>` and serialized as `TagsText` (null-byte `\0` separated). |
| `Tag` | Simple `Id` + `Name`. |
| `AppSettings` | JSON-serialized via Newtonsoft.Json. Contains `DeviceId`, `LastRootFolder`, `Theme`, `PreviewScale`, `AutoUpdateCheck`, `RecentFolders`, `FolderFilters`. |
| `IndexExport` / `IndexExportItem` | Export format for JSON/GZip output. |

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