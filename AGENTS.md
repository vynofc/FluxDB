# AGENTS.md — FluxDB

## Project Overview

FluxDB is a **WPF desktop application** (C# 7.3, .NET Framework 4.7.2) for Windows. It scans local folders, indexes file metadata into an embedded SQLite database, and provides a file manager with tagging, search, preview, and export capabilities.

**Key constraint**: Windows-only. Uses WPF, shell32.dll COM interop, and hardcoded `C:\NSCE\FluxDB` paths.

---

## Build & Run

```bash
# Restore NuGet packages
nuget restore FluxDB.sln

# Build (Release)
msbuild FluxDB.sln /p:Configuration=Release /p:Platform="Any CPU"

# Build (Debug)
msbuild FluxDB.sln /p:Configuration=Debug /p:Platform="Any CPU"
```

Executable output: `bin/Release/FluxDB.exe` or `bin/Debug/FluxDB.exe`.

CI runs on `windows-latest` via GitHub Actions (`.github/workflows/build.yml`). The release workflow (`release.yml`) builds Release and zips `bin\Release\*` plus `x64`/`x86` SQLite interop folders.

**No test suite exists** in this project.

---

## Architecture

### Startup flow

1. `App.xaml` sets `StartupUri="SplashWindow.xaml"`
2. `SplashWindow` checks for updates (HTTP GET `https://nsce-cdn.fun/FluxDB/version.txt`), then creates and shows `MainWindow`
3. `MainWindow` constructor calls `InitializeServices()` → `LoadInitialData()`

### Service layer

| Service | Type | Responsibilities |
|---|---|---|
| `SettingsService` | instance | Loads/saves `AppSettings` as JSON from `%LocalAppData%\FluxDB\settings.json`. Manages recent folders (max 10). |
| `DatabaseService` | instance, `IDisposable` | Opens SQLite connection to `.fluxdb` file in the indexed folder. All CRUD for files, tags, notes. |
| `IndexerService` | instance | Recursively scans a folder, builds `FileEntry` objects, upserts into DB via `DatabaseService`. Raises `ProgressChanged` and `StatusChanged` events. |
| `ExportService` | instance | Converts DB contents to JSON (`IndexExport` model), writes to file or GZip stream. |
| `LoggingService` | **static** | Thread-safe in-memory log buffer (2000 lines) + background file writer to `%LocalAppData%\FluxDB\logs.txt`. |

### Models

| Model | Notes |
|---|---|
| `FileEntry` | `INotifyPropertyChanged` for WPF binding. Has computed `SizeDisplay`, `Icon`, `IconColor` properties. Tags are stored as `List<string>` and serialized as `TagsText` (null-byte `\0` separated). |
| `Tag` | Simple `Id` + `Name`. |
| `AppSettings` | JSON-serialized via Newtonsoft.Json. Contains `DeviceId`, `LastRootFolder`, `Theme`, `PreviewScale`, `AutoUpdateCheck`, `RecentFolders`, `FolderFilters`. |
| `IndexExport` / `IndexExportItem` | Export format for JSON/GZip output. |

### Database schema

SQLite database file named `.fluxdb` lives in the root of the indexed folder:

- `files` — `id`, `path` (UNIQUE), `name`, `extension`, `size`, `created_at`, `modified_at`, `deleted` (0/1), `last_indexed_at`
- `tags` — `id`, `name` (UNIQUE, lowercase-trimmed)
- `file_tags` — `file_id`, `tag_id` (composite PK)
- `notes` — `file_id` (PK), `note`

### UI layer

- `MainWindow` — primary file browser/search/tagging UI. Dark theme defined in XAML resources.
- `SettingsWindow` — update check, auto-update toggle, export button.
- `LogViewer` — reads `LoggingService.GetLogs()`.
- `RefreshDialog` — modal with three options: rescan entire root, current view, or specific folder.
- `SplashWindow` — transient startup window with update check.

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

### Keyboard shortcuts (MainWindow)

| Key | Action |
|---|---|
| `Ctrl+C` | Copy selected files |
| `Ctrl+X` | Cut selected files |
| `Ctrl+V` | Paste from clipboard |
| `Delete` | Delete selected files (with confirmation) |
| `F2` | Rename selected file/folder |
| `F5` | Refresh current folder view |
| `F8` | Open log viewer |
| `Ctrl+F` | Focus search box |
| `Alt+Left` | Navigate back |
| `Alt+Right` | Navigate forward |
| `Alt+Up` / `Backspace` | Go to parent folder |
| `Enter` | Open selected item |
| `Escape` | Focus away from TextBox to DataGrid |

Shortcuts are handled in `Window_PreviewKeyDown` and suppressed when focus is in a TextBox (except Escape).

### Navigation history

`MainWindow` maintains `_backHistory` and `_forwardHistory` stacks (`Stack<string>`) for folder navigation. Navigating to a new folder pushes the current folder onto the back stack and clears the forward stack. The `Alt+Left`/`Alt+Right` shortcuts pop from these stacks.

### Drag & drop

- Dropping a folder on the window when no root folder is open → indexes that folder as new root.
- Dropping files/folders into the current view → copies them (or moves them if **Shift** is held). Uses `CopyOrMoveFilesAsync` which runs on a background thread and calls `Dispatcher.BeginInvoke` for UI updates.
- `GetUniqueFilePath` and `GetUniqueFolderPath` handle naming conflicts (appends ` (2)`, ` (3)`, etc.) before the copy/move.

### Sorting

DataGrid sorting (`DgFiles_Sorting`) is handled manually rather than via `CollectionViewSource`. **Folders always appear first** regardless of sort direction. Sorting is done in-memory by splitting the list into folders and files, sorting each, then concatenating.

### Preview

- **Images**: Loaded via `BitmapImage` with `DecodePixelWidth=400` and `Freeze()` to prevent memory leaks.
- **PDFs**: Uses `GetShellThumbnail` (shell32.dll COM interop via `IShellItemImageFactory`) to extract thumbnails. If no thumbnail handler is available, shows a fallback message.
- **Text files**: Read with UTF-8 detection. If the file contains replacement characters (`\ufffd`) and no BOM was found, it falls back to `Encoding.Default` (ANSI). Content is truncated at 5000 characters.
- **Image zoom**: Vertical-only zoom via mouse wheel in the preview ScrollViewer. Ctrl+wheel accelerates zoom (1.25x vs 1.1x). Scale is clamped to 0.1–10.0.

### Shell thumbnail COM interop

`GetShellThumbnail` in `MainWindow.xaml.cs` uses `IShellItemImageFactory` from shell32.dll via COM interop. This is a **Windows-only** pattern and requires the `WindowsAPICodePack` or manual P/Invoke. The method is fragile — any changes to COM interop or the shell API surface must be tested on the target Windows version.

### SQLite interop DLLs

The `System.Data.SQLite` NuGet package requires platform-specific native interop DLLs (`SQLite.Interop.dll`) in `x64/` and `x86/` subdirectories relative to the executable. These are included in the release zip. The csproj explicitly references them as content items.

### App-level version state

Static properties on `App` (`App.xaml.cs`):
- `IsUpdateAvailable`, `AvailableVersion` — set by `SplashWindow` after checking remote version
- `IsBetaUpdateAvailable`, `AvailableBetaVersion` — same for beta releases
- `IsUpdateSkipped` — set when `--noupdate` CLI flag is present

`App.GetLocalVersion()` resolves the installed version in this priority order:
1. `version.txt` in the app directory
2. `version.txt` in `C:\NSCE\FluxDB` (or `FLUXDB_CENTRAL_DIR`)
3. Highest version number from `.zip` filenames in the central directory
4. Assembly informational version as fallback

### `SearchFiles` overloads

`DatabaseService` has two overloads:
- `SearchFiles(string query)` — searches all files globally
- `SearchFiles(string query, string folderPath)` — restricts to files whose path starts with the given folder

The folder-scoped overload uses `LIKE @folderPrefix` in SQL and filters the no-query fallback with `StartsWith` on the client side. Always use the folder-scoped overload when searching within the current view.

### `FolderFilters` persistence

`AppSettings` has a `FolderFilters` dictionary (`Dictionary<string, string>`) that stores the last selected filter type per root folder. This is loaded in `LoadFilterForFolder` and saved in `SaveFilterForFolder`. The `MatchesFilter` method in `MainWindow` does client-side filtering (folders always pass through).

### InitializeDatabaseForFolder

Called whenever a new root folder is opened. It **disposes the old `DatabaseService`** (and implicitly the old connection), creates a new one pointing to the `.fluxdb` file in the new folder, then wires up `IndexerService` and `ExportService`. The `_settingsService` persists, but `_databaseService`, `_indexerService`, and `_exportService` are replaced.

### PLAN.md

The `PLAN.md` file at the repo root documents known bugs and a planned refactoring. It is not a spec for new features — it's a bug tracker/audit. Many of the issues listed there have been partially addressed (e.g., `InitDb` now uses transactions, `GROUP_CONCAT` uses `\0` separator, `SearchFiles` accepts a `folderPath` parameter, `MarkPathAsDeleted` and `UpdateFolderPath` exist, `CommitBatchWithRetry` was added) but the file was not updated to reflect fixes.

---

## Conventions

- **Namespace**: `FluxDB` for UI/root, `FluxDB.Models` for models, `FluxDB.Services` for services.
- **Naming**: PascalCase for public, `_camelCase` for private fields. Controls use Hungarian-like prefixes (`txtSearch`, `btnRefresh`, `dgFiles`, `pnlProgress`). Event handlers follow the `ControlName_EventName` pattern (e.g., `BtnRefresh_Click`, `DgFiles_Sorting`).
- **Error handling**: Broad try-catch with silent swallowing is common. `LoggingService.Log()` is used to record errors.
- **German UI**: Some UI strings and comments are in German (the project is German-authored).
- **No async/await in constructors**: Services are initialized synchronously; async work is fire-and-forget or triggered by UI events.
- **XAML**: Dark theme with hardcoded color brushes. No resource dictionaries or theming abstraction.
- **Code organization**: `MainWindow.xaml.cs` is ~1800+ lines and uses `#region` blocks for logical grouping (`Keyboard Shortcuts`, `Clipboard Operations`, `Context Menu`, `Filter`, `Preview`, `Sorting`, `Drag & Drop`, etc.). New features should follow this pattern.