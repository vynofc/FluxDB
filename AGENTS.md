# AGENTS.md — FluxDB

## Project Overview

FluxDB consists of three components:

1. **WPF app** under `WPF/FluxDB/` — Windows desktop application for file indexing, search, tags, notes, and preview. Uses `WPF-UI` (wpfui) for Fluent design and SQLite via `System.Data.SQLite.Core`.
2. **Installer** under `Installer/` — Go TUI installer (Bubble Tea) for distribution.
3. **Log-Viewer** under `Log_Viewer/` — Go TUI viewer (Bubble Tea) for application and indexing logs.

**Important constraint:** The project is Windows-only and uses WPF, COM interop, and WebView2.

**Folder structure note:** The `FluxDB/` folder at repo root (not `WPF/FluxDB/`) contains only icon files and a `packages.config` — artifacts of the old structure. The actual WPF app lives under `WPF/FluxDB/`.

**Devcontainer:** `.devcontainer/devcontainer.json` configures a Universal image with PowerShell extensions and dotnet SDKs (8.0, 9.0, 10.0).

---

## Build & Run

### WPF app

```powershell
# Restore + Build (SDK-style project)
dotnet restore WPF/FluxDB/FluxDB.csproj
dotnet build WPF/FluxDB/FluxDB.csproj -c Release

# Publish
dotnet publish WPF/FluxDB/FluxDB.csproj -c Release -o bin\
```

Or via build script:

```powershell
build.bat          # Interactive menu
build.bat 2        # Build WPF only
build.bat 5        # Full release package (WPF + Installer + Log Viewer + ZIP)
build.bat 6        # Clean
```

Output: `bin/` with `FluxDB.exe`, `FluxDB-Installer.exe`, `components/Log_Viewer.exe`, `x64/SQLite.Interop.dll`, `x86/SQLite.Interop.dll`, and `FluxDB.zip` after the full build.

### Installer (Go)

```powershell
cd Installer
go build -ldflags="-s -w" -o ..\bin\FluxDB-Installer.exe .
```

### Log-Viewer (Go)

```powershell
cd Log_Viewer
go build -ldflags="-s -w" -o ..\bin\components\Log_Viewer.exe .
```

**There is currently no test suite in the repository.**

---

## CI / Workflows

| Workflow | Trigger | What happens |
|---|---|---|
| `build-wpf.yml` | Push/PR to `main` (changes in `WPF/`) | `dotnet publish` of the WPF app |
| `build-installer.yml` | Push/PR to `main` (changes in `Installer/`) | Builds the Go installer |
| `build-log-viewer.yml` | Push/PR to `main` (changes in `Log_Viewer/`) | Builds the Go log viewer |
| `release.yml` | Published release | Builds WPF app, installer, and log viewer; produces `FluxDB.zip` and `FluxDB-Installer.exe` as release assets |
| `issue-triage.yml` | Issue opened | Auto-labeling |
| `issue-cleanup.yml` | Issue closed | Deletes all branches of the issue |

---

## WPF Architecture

### Technology stack

| Component | Package |
|---|---|
| UI framework | `WPF-UI` (wpfui) 4.2.0 |
| JSON | `Newtonsoft.Json` 13.0.3 |
| SQLite | `System.Data.SQLite.Core` 1.0.119.0 |
| PDF preview | `Microsoft.Web.WebView2` 1.0.4129.50 |
| MVVM helpers | `CommunityToolkit.Mvvm` 8.4.0 (referenced but **not currently used in code**) |
| Target | `net10.0-windows7.0` (SDK-style `.csproj`) |
| Runtime | `win-x64`, `SelfContained=false` |

### Architecture note: No Dependency Injection

There is **no DI** in the project. The startup flow works entirely without DI:

1. `App.xaml` has `StartupUri="Views/SplashWindow.xaml"` — WPF instantiates `SplashWindow` directly
2. `SplashWindow` creates `new SettingsService()` directly in the constructor
3. `SplashWindow_Loaded` creates `new MainWindow()` directly
4. `MainWindow` constructor creates services itself via `InitializeServices()`

### Actual startup flow

1. `App.OnStartup` (App.xaml.cs) — registers global exception handlers, detects debug mode (`-debug` suffix in version.txt or `#if DEBUG`), calls `LoggingService.SetDebugMode()`
2. `SplashWindow` — loads icon, checks for updates (GitHub Releases API via `UpdateService`), creates `new MainWindow()` and shows it
3. `MainWindow` constructor — calls `InitializeServices()` (creates `SettingsService`) and `LoadInitialData()`
4. `LoadInitialData()` — reads `LastRootFolder` from `SettingsService`, opens the last used folder with existing index or shows "Ready"

### MainWindow structure

`MainWindow` is a `FluentWindow` with the following layout (all in code-behind, not via MVVM data binding):

- **TitleBar** — WPF-UI `TitleBar` control
- **Header** — "FluxDB" title, current folder path, Select Folder / Refresh / Settings buttons
- **Navigation bar** — Back/Forward/Up buttons (with `IsEnabled` control), breadcrumbs (dynamically generated `StackPanel`), filter ComboBox
- **Search bar** — TextBox with debounced search + Search/Clear buttons
- **Main content** — left `DataGrid` for files (with `SelectionMode="Extended"`), right detail panel (tags, notes, preview)
- **Status bar** — status text, file count, indexing progress
- **Overlays** — drop overlay for drag & drop, cheat sheet overlay (Ctrl+K)

### Navigation (code-behind)

`MainWindow` manages navigation **itself**:

- `_backHistory` / `_forwardHistory` as `Stack<string>` (max size from `DevSettingsRegistry.NavigationHistorySizeKey`, default 50)
- `_currentRootFolder` / `_currentViewFolder` for current state
- `NavigateToFolder()` updates history, breadcrumbs, and reloads the file list
- Breadcrumbs are dynamically inserted as `TextBlock` elements with `Click` handlers into the `pnlBreadcrumbs` StackPanel
- Keyboard shortcuts: Alt+Left/Right/Up, Backspace, Enter, F5, Ctrl+F, F8 (log viewer), F9 (dev settings), Ctrl+K (cheat sheet)

### Clipboard & file operations (code-behind)

All file operations are implemented in `MainWindow.xaml.cs`:

- **Ctrl+C/X/V** — Copy/Cut/Paste with Windows `Clipboard` API and `StringCollection`/`FileDropList`
- **Delete** — Deletes files (with confirmation dialog) and marks them in the DB as `deleted=1`
- **F2** — Rename via `RenameDialog`
- **Drag & Drop** — `Window_Drop` handler opens folders per drag & drop; SHIFT = move instead of copy
- **New Folder** — Creates folder in current directory

### Preview

Preview is handled in `MainWindow.xaml.cs`:

- **Images** — `BitmapImage` with zoom support (max zoom from `DevSettingsRegistry.ImageZoomMaxKey`)
- **Text files** — `StreamReader` with encoding detection, max chars from `DevSettingsRegistry.PreviewMaxCharsKey` (default 5000)
- **PDF** — `WebView2` control (`Microsoft.Web.WebView2`), navigates to file URI; shows "WebView2 Runtime not installed" if missing
- Preview is debounced via `_previewCts` / `_previewVersion` to avoid race conditions

### Views

| File | Description |
|---|---|
| `SplashWindow` | Splash screen, update check, launches `MainWindow` |
| `MainWindow` | Main window with file list, navigation, search, preview, tags/notes |
| `SettingsWindow` | Settings UI (theme, auto-update, search-in-path, folder tags, persistence options) |
| `DevSettingsWindow` | Developer settings editor (F9), edits dotted-key dev settings |
| `RefreshDialog` | Dialog to confirm index refresh |
| `RenameDialog` | Dialog to rename files |
| `TagChip` (Controls/) | UserControl for displaying a single tag |

### WPF Services

| File | Responsibility |
|---|---|
| `DatabaseService.cs` | SQLite CRUD: file index, tags, notes, folder path updates, search, batch delete marking, aggregated folder tags. Uses WAL mode, `synchronous=NORMAL`, 8MB cache. |
| `IndexerService.cs` | File system scanning, batched indexing (batch size from `DevSettingsRegistry.IndexerBatchSizeKey`, default 1000), progress reporting, deleted file detection. Uses iterative `Stack<string>` enumeration instead of recursion. |
| `LoggingService.cs` | **Static** logger with in-memory buffer (size from `DevSettingsRegistry.LogBufferLinesKey`, default 2000), background file writes via `ThreadPool` and dedicated `_writeQueue` with `ProcessWriteQueue` loop. Logs to `%LOCALAPPDATA%\FluxDB\logs.txt`. |
| `SettingsService.cs` | JSON settings (`%LOCALAPPDATA%\FluxDB\settings.json`): recent folders, theme, auto-update, per-folder filter/sort/last-view state, column visibility, persistence options, dev settings. Caches settings in memory with dirty-flag debounced saves. |
| `UpdateService.cs` | **Static** update service: fetches latest release tag from GitHub API, downloads installer. |
| `ExportService.cs` | Export index to JSON or GZIP via `Newtonsoft.Json` |
| `ImportService.cs` | Import index from JSON or GZIP, upserts files/tags/notes in a transaction |

### Models

| Model | Description |
|---|---|
| `FileEntry` | Plain POCO (no `ObservableObject`). Properties with manual cache invalidation (`InvalidateCache()` on `Extension`/`IsFolder` change, `_cachedSizeDisplay = null` on `Size` change). `Tags` as `List<string>`, `TagsText` as null-byte-separated string. Static `IconLookup` dictionary maps extensions to `SymbolRegular` + color. |
| `AppSettings` | `LastRootFolder`, `AutoUpdateCheck` (default `false`), `SearchInPathEnabled` (default `false`), `FolderTagsEnabled` (default `true`), `FolderTagsDepth` (0 = unlimited), `Theme` (Dark/Light), `RecentFolders`, `ColumnVisibility`, `FolderFilters`, `FolderLastView`, `FolderSortColumn`/`FolderSortDirection`, `Persistence` (`PersistenceOptions`), `DevSettings` (dotted key → value) |
| `PersistenceOptions` | Flags controlling which UI state gets persisted: `LastRootFolder`, `LastViewFolder`, `Filter`, `Sort`, `ColumnVisibility`, `RecentFolders` (all default `true`) |
| `DevSettingDefinition` | Single dev setting: dotted `Key`, `Description`, `DefaultValue` |
| `GitHubRelease` | JSON deserialization target for GitHub API (`TagName`) |

### DevSettings system

`DevSettingsRegistry` (in `SettingsService.cs`) defines developer settings with dotted keys:

| Key | Default | Description |
|---|---|---|
| `input.search.time` | 250 | Search debounce in ms |
| `preview.text.maxchars` | 5000 | Max chars in text preview |
| `navigation.history.size` | 50 | Max navigation history entries |
| `folders.recent.max` | 10 | Max recent folders |
| `log.buffer.lines` | 2000 | Max lines in log memory buffer |
| `preview.image.zoommax` | 10 | Max image zoom factor |
| `indexer.batch.size` | 1000 | Files per DB transaction during indexing |

Access via `SettingsService.GetDevSetting(key)` / `GetDevSettingInt(key)`. Editable at runtime via F9 (`DevSettingsWindow`).

### Converters

All converters live in `Converters/Converters.cs`:

| Converter | Description |
|---|---|
| `BoolToVisibilityConverter` | `true` → `Visible`, `false` → `Collapsed` |
| `InverseBoolToVisibilityConverter` | `false` → `Visible`, `true` → `Collapsed` |
| `FileSizeConverter` | `long` bytes → human-readable string |
| `DateTimeToRelativeConverter` | `DateTime` → "Just now" / "5 min ago" / "2h ago" / "3d ago" / "yyyy-MM-dd" |

### Database schema

SQLite database file named `.fluxdb` lives in the root of the indexed folder:

- `files` — `id`, `path` (UNIQUE), `name`, `extension`, `size`, `created_at`, `modified_at`, `deleted` (0/1), `last_indexed_at`
- `tags` — `id`, `name` (UNIQUE, lowercase-trimmed)
- `file_tags` — `file_id`, `tag_id` (composite PK)
- `notes` — `file_id` (PK), `note`

### Version handling

- `version.txt` at repo root (currently `1.1.0`) drives the assembly version via an MSBuild `BeforeBuild` target that generates `Properties/AssemblyVersion.cs`
- `App.GetLocalVersion()` reads `version.txt` from the app directory, falls back to `AssemblyInformationalVersionAttribute`
- Debug mode is auto-detected: if the local version ends with `-debug` or the build is `#if DEBUG`, `LoggingService.SetDebugMode(true)` is called
- Version strings use `VersionHelper.CompareVersions` for comparison — strips `v` prefix, `!beta` suffix, handles `x.y.z-suffix` semver

### Theme handling

- `App.xaml` merges WPF-UI resource dictionaries: `<ui:ThemesDictionary Theme="Dark" />` and `<ui:ControlsDictionary />`
- `ApplicationThemeManager.Apply()` is the only way to change themes. Do not manipulate `ResourceDictionary` directly.
- `SettingsService` stores `Theme` ("Dark"/"Light") in `settings.json`

---

## FluxDB Installer (Go)

### Startup flow

1. `main.go` parses CLI flags (`--tag`, `--path`, `--silent`, `--silent-start`, `--detail`)
2. If `--silent` or `--silent-start`: runs `runSilent()` — plain text output, no TUI; `--silent-start` additionally calls `launchFluxDB()` after extraction
3. Otherwise: starts Bubble Tea TUI with `initialModel()`
   - If `--tag` is provided: skips to downloading directly
   - If `--detail`: fetches all releases, shows version selection form, verbose logging
   - Default: fetches latest release tag only

### State machine

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

### Source files

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

### CLI flags

| Flag | Description |
|---|---|
| `--tag <version>` | Install specific version (skips GitHub API call) |
| `--path <dir>` | Alternative install directory (default: `%LOCALAPPDATA%\FluxDB`) |
| `--silent` | No TUI, text-only output (for CI/scripting) |
| `--silent-start` | Like `--silent`, but auto-launches FluxDB after install |
| `--detail` | Detailed mode: version selection form + verbose log output |

### Dependencies

| Module | Purpose |
|---|---|
| `github.com/charmbracelet/bubbletea` | TUI framework |
| `github.com/charmbracelet/bubbles` | Progress bar, spinner, viewport |
| `github.com/charmbracelet/huh` | Form-based user input (version selection, shortcut confirmation) |
| `github.com/charmbracelet/lipgloss` | Terminal styling |
| `github.com/charmbracelet/log` | Structured logging |

### Go version

- Installer: `go 1.26.1` (module path: `github.com/vynofc/FluxDB/Installer`)
- Log-Viewer: `go 1.24.0` (module path: `github.com/vynofc/FluxDB/LogViewer`)

### What the installer does NOT do

- No auto-update (FluxDB handles this via `SplashWindow` + `UpdateService`)
- No uninstall (delete `%LOCALAPPDATA%\FluxDB` manually)
- No admin rights needed (installs to `%LOCALAPPDATA%`)

---

## Log-Viewer (Go)

| File | Responsibility |
|---|---|
| `main.go` | Entrypoint, CLI flag parsing (`--log <path>` required), Bubble Tea dispatch |
| `model.go` | Model struct, state, message types |
| `update.go` | Update function, key handling, log tailing |
| `view.go` | Rendering, log line parsing/styling |
| `styles.go` | Lipgloss styles |

### Tail behavior

- Polls the log file every 500ms via `tailTick`
- Seeks from last-known byte offset, appends new lines
- On file truncation (size < lastSize), resets to current size without re-reading

---

## Important Patterns & Gotchas

### SQL and tags

- Tags are stored via `GROUP_CONCAT(t.name, char(0))` with **null-byte separator** (was previously `, ` which broke on tag names containing commas). When parsing back, split on `'\0'` with `StringSplitOptions.RemoveEmptyEntries`.
- When adding new `GROUP_CONCAT` queries or modifying tag handling, always use `char(0)` as the separator. **Never** write `'\0'` in SQL: SQLite has no string escapes, so `'\0'` is the two-character literal backslash+zero, not a null byte.

### Folder tag aggregation

`DatabaseService.GetAggregatedTagsForFolders(childFolderPaths, depth)` returns tags aggregated from files within folders, up to a configurable depth (0 = unlimited). Used by `MainWindow` to show tags on folder entries when `FolderTagsEnabled` is true.

### Database file location

The database file `.fluxdb` is stored **inside the indexed folder**, not in `%LocalAppData%`. This means each root folder has its own independent index. `DatabaseService` gets the DB path directly from the folder path. The file is marked hidden after creation.

### Database lifecycle

When opening a new folder, `MainWindow.InitializeDatabaseForFolderAsync()` **disposes the old `DatabaseService`** and creates a new one. It also re-subscribes `IndexerService` events. Any code that holds a reference to `DatabaseService` or `IndexerService` will have stale references after a folder switch. The method waits for any running indexing to finish before switching.

### Batch commits in IndexerService

`IndexerService.ScanFolderAsync` uses batched transactions (batch size from `DevSettingsRegistry.IndexerBatchSizeKey`, default 1000). `CommitBatchWithRetry` retries up to 3 times with exponential backoff (100ms → 200ms → 400ms). On final failure, it rolls back and rethrows. When cancelled, the current transaction is explicitly rolled back.

### Thread safety

- `LoggingService` is a **static class** with a `lock` and `ThreadPool.QueueUserWorkItem` for background file writes. It uses a `_writeQueue` with a dedicated `ProcessWriteQueue` processing loop.
- `DatabaseService` has a single `SQLiteConnection` — all DB access is serial (no connection pooling). SQLite writes are not thread-safe; ensure all DB operations happen on the same thread or serialize access.
- `SettingsService` caches settings in memory (`_cachedSettings`) with a dirty flag and debounced save (`_saveCts`). Always use `Load()` to get the current snapshot.

### SQLite interop DLLs

`System.Data.SQLite.Core` requires native `SQLite.Interop.dll` (x64 and x86) in the output directory. The SDK-style `.csproj` uses `PackageReference` which handles this automatically. When packaging, both architecture folders must be present alongside the managed DLLs.

### CommunityToolkit.Mvvm is referenced but unused

The `CommunityToolkit.Mvvm` package is still referenced in the `.csproj` and `GlobalUsings.cs` still has `global using CommunityToolkit.Mvvm.*`, but **no code currently uses `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`, or `WeakReferenceMessenger`**. `FileEntry` is a plain POCO with manual cache invalidation. Do not assume MVVM patterns are in use; the app is purely code-behind.

### WPF-UI (wpfui) specifics

- Windows extend `FluentWindow` (not `Window`). This provides Mica backdrop, rounded corners, and titlebar integration.
- `ApplicationThemeManager.Apply()` is the only way to change themes. Do not manipulate `ResourceDictionary` directly.
- XAML namespace: `xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"`
- `ISnackbarService` and `IContentDialogService` exist in WPF-UI, but are not currently used by the app.
- `SymbolRegular` enum values can change between wpfui versions. Verify the symbol exists in the version you're targeting (e.g., `FolderAdd24` doesn't exist in 4.2.0 — use `Folder24` instead).

### Code-behind pattern (current architecture)

The current `MainWindow` uses a **code-behind** pattern, not MVVM data-binding:
- Button clicks use `Click="BtnBack_Click"` handlers, not `Command` bindings
- Navigation state (`_backHistory`, `_forwardHistory`) is in the Window class, not a ViewModel
- `DataGrid` has `SelectionChanged`, `MouseDoubleClick`, `Sorting` event handlers in code-behind
- `DataContext` is not set to any ViewModel — the Window manages its own state
- Filter changes are handled via `CmbFilter_SelectionChanged` event, not a binding
- Search is debounced via `_tagDebounceCts` (using `DevSettingsRegistry.SearchDebounceKey`)

### FileEntry caching

`FileEntry` has manually managed caches (`_cachedIconSymbol`, `_cachedSizeDisplay`, `_cachedTypeDisplay`) that are invalidated in property setters (`Extension`, `Size`, `IsFolder` setters call `InvalidateCache()` or null out the relevant cache). The computed properties (`IconSymbol`, `SizeDisplay`, `TypeDisplay`, `IconColorBrush`) use these caches for performance. A static `IconLookup` dictionary maps file extensions to icon/color tuples.

### Folder rename / delete must update DB

When renaming or deleting folders, the DB entries for all files under that path must be updated. `DatabaseService` has `UpdateFolderPath()` and `MarkPathAsDeleted()` for this purpose. Always use these methods — do not do raw SQL for folder-level operations.

### Version comparison

`VersionHelper` is a **static utility** with `NormalizeVersion` and `CompareVersions`. It strips `v` prefix, `!beta` suffix, and handles `x.y.z-suffix` semver-like strings. The `Version` class is used for numeric comparison, then string comparison for pre-release suffixes. Do not duplicate version logic — always use `VersionHelper`.

### Auto-update

- Version check: `UpdateService.FetchLatestReleaseTagAsync()` → HTTP GET `https://api.github.com/repos/vynofc/FluxDB/releases/latest` (GitHub API).
- Installer download: `UpdateService.DownloadInstallerAsync(exeDir, tag)`.
- Installer: `FluxDB-Installer.exe` in the app directory, launched with `--silent-start`.
- Skip with `--noupdate` CLI flag.
- `App.IsUpdateAvailable`, `App.AvailableVersion`, `App.AvailableTag`, `App.AvailableBetaVersion`, `App.IsUpdateSkipped`, `App.IsBetaUpdate` are static properties set by `SplashWindow`.

### Logging

- `LoggingService` is a **static** class. Use `LoggingService.Log(message)` for general logging, `LoggingService.LogDebug(message)` for debug-only messages.
- Logs go to `%LOCALAPPDATA%\FluxDB\logs.txt`
- `LoggingService.SetDebugMode(true)` is called when the version string ends with `-debug` or in `#if DEBUG` builds
- `LoggingService.GetLogs()` returns the in-memory buffer
- `LoggingService.Shutdown()` flushes and disposes the writer — call on app exit
- Global exception handlers are registered in `App.OnStartup` for `DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException`, and `AppDomain.CurrentDomain.UnhandledException`

### Settings

- Settings file: `%LOCALAPPDATA%\FluxDB\settings.json`
- `AutoUpdateCheck` defaults to `false`
- `SearchInPathEnabled` defaults to `false` (when true, search matches against full path, not just filename)
- `FolderTagsEnabled` defaults to `true`, `FolderTagsDepth` defaults to `0` (unlimited)
- `Theme` defaults to `"Dark"`
- `RecentFolders` max from `DevSettingsRegistry.RecentFoldersMaxKey` (default 10), case-insensitive dedup
- `FolderFilters` is a `Dictionary<string, string>` for per-folder filter persistence
- `FolderLastView`, `FolderSortColumn`, `FolderSortDirection` are per-root-folder dictionaries
- `Persistence` (`PersistenceOptions`) controls which UI state gets saved
- `DevSettings` is a `Dictionary<string, string>` for developer settings (dotted keys)

---

## Conventions

### FluxDB (C# / WPF)

- **Namespace**: `FluxDB` for root (App, VersionHelper), `FluxDB.Models` for models, `FluxDB.Services` for services, `FluxDB.Views` for windows/dialogs, `FluxDB.Views.Controls` for controls, `FluxDB.Converters` for value converters.
- **Naming**: PascalCase for public, `_camelCase` for private fields. Controls use Hungarian-like prefixes (`txtSearch`, `btnRefresh`, `dgFiles`, `pnlBreadcrumbs`, `cmbFilter`, `chkSearchInPath`).
- **Error handling**: Broad try-catch with silent swallowing is common. `LoggingService.Log()` is used to record errors.
- **German UI**: Some UI strings and comments are in German (the project is German-authored).
- **No async/await in constructors**: Services are initialized synchronously; async work is fire-and-forget or triggered by UI events.
- **XAML**: Uses WPF-UI resource dictionaries and dynamic resource references (`{DynamicResource ...}`). Theme colors come from `ApplicationThemeManager`.
- **SDK-style `.csproj`**: Uses `<Project Sdk="Microsoft.NET.Sdk">` with `<UseWPF>true</UseWPF>`, `<UseWindowsForms>true</UseWindowsForms>`, `<Nullable>disable</Nullable>`, `<ImplicitUsings>disable</ImplicitUsings>`. `GenerateAssemblyInfo=false` because `AssemblyInfo.cs` contains `[assembly: ThemeInfo(...)]`. `NoWarn` suppresses `CA1416`, `MVVMTK0034`, `CS0169`.
- **GlobalUsings.cs** provides project-wide usings — new files can use types from `FluxDB.Models`, `FluxDB.Services`, `FluxDB.Views`, `FluxDB.Converters`, `Wpf.Ui`, etc. without explicit imports.

### Installer (Go)

- **Package**: Single `main` package (no sub-packages).
- **Naming**: Standard Go conventions (camelCase, PascalCase for exports).
- **Error handling**: Errors propagated as Bubble Tea messages (`errMsg`), surfaced in TUI or stderr.
- **German UI**: All user-facing strings are in German.
- **Build**: `-ldflags="-s -w"` for stripped release binaries. Cross-compiled with `GOOS=windows GOARCH=amd64`.

### Log-Viewer (Go)

- **Package**: Single `main` package (no sub-packages).
- **Naming**: Standard Go conventions.
- **Build**: `-ldflags="-s -w"` for stripped release binaries.
