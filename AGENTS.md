# AGENTS.md — FluxDB

## Projektübersicht

FluxDB besteht aus drei Komponenten:

1. **WPF-App** unter [WPF/FluxDB](WPF/FluxDB/) – Windows-Desktopanwendung mit Dateisuche, Indizierung, Tags, Vorschau und Einstellungen.
2. **Installer** unter [Installer](Installer/) – Go-basierter TUI-Installer für die Verteilung.
3. **Log-Viewer** unter [Log_Viewer](Log_Viewer/) – separater Viewer für Anwendungs- und Indexierungs-Logs.

**Wichtige Einschränkung:** Das Projekt ist Windows-only und verwendet WPF, COM-Interop sowie feste Pfade wie `C:\NSCE\FluxDB`.

**Anmerkung zur Ordnerstruktur:** Der `FluxDB/` Ordner auf Repo-Root-Ebene (nicht `WPF/FluxDB/`) enthält nur Icon-Dateien und eine `packages.config` — das sind Artefakte der alten Struktur. Die eigentliche WPF-App liegt unter `WPF/FluxDB/`.

**Devcontainer:** Es gibt eine `.devcontainer/devcontainer.json` Konfiguration mit einem Universal-Image, PowerShell-Extensions und dotnet SDKs (8.0, 9.0, 10.0). Diese kann für VS Code Remote Development verwendet werden.

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
| `build-wpf.yml` | Push/PR auf `main` (Änderungen in `WPF/`) | Baut die WPF-App aus [WPF/FluxDB](WPF/FluxDB/) |
| `build-installer.yml` | Push/PR auf `main` (Änderungen in `Installer/`) | Baut den Go-Installer |
| `build-log-viewer.yml` | Push/PR auf `main` (Änderungen in `Log_Viewer/`) | Baut den Go Log-Viewer |
| `release.yml` | Veröffentlichtes Release | Baut WPF-App, Installer und Log-Viewer, erzeugt `FluxDB.zip` |
| `issue-triage.yml` | Issue-Opening | Auto-labeling of issues |
| `issue-cleanup.yml` | Issue-Closing | Löscht alle Branches des Issues |

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
- `RenameDialog` — modal for renaming files/folders.

#### WPF Services

| File | Responsibility |
|---|---|
| `DatabaseService.cs` | SQLite CRUD: file index, tags, notes, folder path updates, search, batch delete marking |
| `IndexerService.cs` | File system scanning, batched indexing (1000 files/batch), progress reporting, deleted file detection |
| `LoggingService.cs` | Static logger with in-memory buffer (max 2000 lines), background file writes via `ThreadPool`, debug mode toggle |
| `SettingsService.cs` | JSON settings (`%LOCALAPPDATA%\FluxDB\settings.json`): recent folders, theme, auto-update, device ID |
| `ExportService.cs` | Export index to JSON or GZIP via `Newtonsoft.Json` |

#### Version handling

- `version.txt` at repo root (currently `1.0.1`) drives the assembly version via an MSBuild `BeforeBuild` target that generates `Properties/AssemblyVersion.cs`
- `App.GetLocalVersion()` reads `version.txt` from the app directory, falls back to `AssemblyInformationalVersionAttribute`
- Debug mode is auto-detected: if the local version ends with `-debug`, `LoggingService.SetDebugMode(true)` is called
- Version strings use `VersionHelper.CompareVersions` for comparison — strips `v` prefix, `!beta` suffix, handles `x.y.z-suffix` semver

### FluxDB Installer (Go)

#### Startup flow

1. `main.go` parses CLI flags (`--tag`, `--path`, `--silent`, `--silent-start`, `--detail`)
2. If `--silent` or `--silent-start`: runs `runSilent()` — plain text output, no TUI; `--silent-start` additionally calls `launchFluxDB()` after extraction
3. Otherwise: starts Bubble Tea TUI with `initialModel()`
   - If `--tag` is provided: skips to downloading directly
   - If `--detail`: fetches all releases, shows version selection form, verbose logging
   - Default: fetches latest release tag only

#### State machine

Without `--detail` (default):
```
stateLoading → stateDownloading → stateExtracting → stateAskShortcut → stateDone
     ↓              ↓                  ↓                  ↓                ↓
  stateError    stateError         stateError         stateError     (Enter→Quit)
```

With `--detail`:
```
stateLoading → stateSelectVersion → stateDownloading → stateExtracting → stateAskShortcut → stateCreatingShortcut → stateDone
     ↓                 ↓                  ↓                  ↓                ↓                     ↓                  ↓
  stateError       stateError         stateError         stateError      stateError            stateError       (Enter→Quit)
```

With `--tag` (custom tag):
```
stateDownloading → stateExtracting → stateAskShortcut → stateDone
     ↓                 ↓                  ↓                ↓
  stateError        stateError         stateError     (Enter→Quit)
```

#### Source files

| File | Responsibility |
|---|---|
| `main.go` | Entrypoint, CLI flag parsing, TUI/Silent dispatch, `launchFluxDB` helper |
| `model.go` | Bubble Tea model, states, message types, `initialModel` |
| `update.go` | State machine (Update function), form handlers |
| `view.go` | Rendering (TUI + silent view) |
| `github.go` | GitHub API: fetch latest release tag (`/releases/latest`), fetch all releases (`/releases?per_page=20`), build download URL |
| `download.go` | ZIP download from GitHub Releases to `%TEMP%`, progress reporting via channel |
| `extract.go` | ZIP extraction to `%LOCALAPPDATA%\FluxDB` (or `--path`), writes `version.txt`, handles locked files by renaming to `.old`, cleans up temp file |
| `silent.go` | Silent-mode logic (no Bubble Tea), `runSilent` orchestrates full flow |
| `shortcut.go` | Creates Desktop and Start Menu shortcuts via PowerShell COM script |
| `forms.go` | `huh` form builders for version selection and shortcut confirmation |
| `helpers.go` | Log formatting, step header rendering |
| `styles.go` | Lipgloss styles (dark theme matching FluxDB) |

#### CLI flags

| Flag | Description |
|---|---|
| `--tag <version>` | Install specific version (skips GitHub API call) |
| `--path <dir>` | Alternative install directory (default: `%LOCALAPPDATA%\FluxDB`) |
| `--silent` | No TUI, text-only output (for CI/scripting) |
| `--silent-start` | Like `--silent`, but auto-launches FluxDB after install |
| `--detail` | Detailed mode: version selection form + verbose log output |

#### Dependencies

| Module | Purpose |
|---|---|
| `github.com/charmbracelet/bubbletea` | TUI framework |
| `github.com/charmbracelet/bubbles` | Progress bar, spinner, viewport |
| `github.com/charmbracelet/huh` | Form-based user input (version selection, shortcut confirmation) |
| `github.com/charmbracelet/lipgloss` | Terminal styling |
| `github.com/charmbracelet/log` | Structured logging |

#### What the installer does NOT do

- No auto-update (FluxDB handles this via `SplashWindow`)
- No uninstall (delete `%LOCALAPPDATA%\FluxDB` manually)
- No admin rights needed (installs to `%LOCALAPPDATA%`)

### Log-Viewer (Go)

| File | Responsibility |
|---|---|
| `main.go` | Entrypoint, CLI flag parsing (`--log <path>` required), Bubble Tea dispatch |
| `model.go` | Model struct, state, message types |
| `update.go` | Update function, key handling, log tailing |
| `view.go` | Rendering, log line parsing/styling |

#### Tail behavior

- Polls the log file every 500ms via `tailTick`
- Seeks from last-known byte offset, appends new lines
- On file truncation (size < lastSize), resets to current size without re-reading

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

### SQLite interop DLLs

`System.Data.SQLite.Core` requires native `SQLite.Interop.dll` (x64 and x86) in the output directory. The `.csproj` imports `Stub.System.Data.SQLite.Core.NetFramework.targets` which copies these to `bin\x64\` and `bin\x86\` during build. When packaging, both architecture folders must be present alongside the managed DLLs.

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

### PLAN-csproj-fix.md

The `PLAN-csproj-fix.md` file at the repo root documents a planned migration of the WPF `.csproj` from the legacy format to SDK-style. Key points:
- Replace `FluxDB.csproj` with `<Project Sdk="Microsoft.NET.Sdk">` + `<UseWPF>true</UseWPF>`
- Replace `packages.config` with `PackageReference` entries
- Drop `Stub.System.Data.SQLite.Core.NetFramework` (only needed for old-style csproj)
- Keep `GenerateAssemblyInfo=false` because `AssemblyInfo.cs` contains `[assembly: ThemeInfo(...)]`
- Build scripts and CI pass `/p:OutDir=bin\` which overrides SDK default, so no downstream changes needed

This has NOT yet been implemented. The current `.csproj` still uses the legacy format.

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