# AGENTS.md — FluxDB

## Projektübersicht

FluxDB besteht aus drei Komponenten:

1. **WPF-App** unter `WPF/FluxDB/` — Windows-Desktopanwendung mit Dateisuche, Indizierung, Tags, Vorschau und Einstellungen. Nutzt MVVM mit `CommunityToolkit.Mvvm`, `WPF-UI` (wpfui) für Fluent-Design, und SQLite via `System.Data.SQLite.Core`.
2. **Installer** unter `Installer/` — Go-basierter TUI-Installer (Bubble Tea) für die Verteilung.
3. **Log-Viewer** unter `Log_Viewer/` — Go-basierter TUI-Viewer (Bubble Tea) für Anwendungs- und Indexierungs-Logs.

**Wichtige Einschränkung:** Das Projekt ist Windows-only und verwendet WPF, COM-Interop sowie feste Pfade wie `C:\NSCE\FluxDB`.

**Anmerkung zur Ordnerstruktur:** Der `FluxDB/` Ordner auf Repo-Root-Ebene (nicht `WPF/FluxDB/`) enthält nur Icon-Dateien und eine `packages.config` — das sind Artefakte der alten Struktur. Die eigentliche WPF-App liegt unter `WPF/FluxDB/`.

**Devcontainer:** `.devcontainer/devcontainer.json` konfiguriert ein Universal-Image mit PowerShell-Extensions und dotnet SDKs (8.0, 9.0, 10.0). Kann für VS Code Remote Development verwendet werden.

---

## Build & Run

### WPF-App

```powershell
# Restore + Build (SDK-style project)
dotnet restore WPF/FluxDB/FluxDB.csproj
dotnet build WPF/FluxDB/FluxDB.csproj -c Release

# Publish (self-contained output)
dotnet publish WPF/FluxDB/FluxDB.csproj -c Release -o bin\
```

Oder via Build-Skript:

```powershell
build.bat          # Interactive menu
build.bat 2        # Build WPF only
build.bat 5        # Full release package (WPF + Installer + Log Viewer + ZIP)
```

Ausgabe: `bin/` mit `FluxDB.exe`, `FluxDB-Installer.exe`, `components/Log_Viewer.exe`, `x64/SQLite.Interop.dll`, `x86/SQLite.Interop.dll` und `FluxDB.zip` nach dem Full-Build.

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

**Es gibt derzeit keine Test-Suite im Repository.**

---

## CI / Workflows

| Workflow | Trigger | Was passiert |
|---|---|---|
| `build-wpf.yml` | Push/PR auf `main` (Änderungen in `WPF/`) | `dotnet publish` der WPF-App |
| `build-installer.yml` | Push/PR auf `main` (Änderungen in `Installer/`) | Baut den Go-Installer |
| `build-log-viewer.yml` | Push/PR auf `main` (Änderungen in `Log_Viewer/`) | Baut den Go Log-Viewer |
| `release.yml` | Veröffentlichtes Release | Baut WPF-App, Installer und Log-Viewer, erzeugt `FluxDB.zip` und `FluxDB-Installer.exe` als Release-Assets |
| `issue-triage.yml` | Issue-Opening | Auto-labeling of issues |
| `issue-cleanup.yml` | Issue-Closing | Löscht alle Branches des Issues |

---

## WPF Architektur

### Technologie-Stack

| Komponente | Paket |
|---|---|
| MVVM-Toolkit | `CommunityToolkit.Mvvm` 8.4.0 |
| DI/Hosting | `Microsoft.Extensions.DependencyInjection` + `Microsoft.Extensions.Hosting` 10.0.0 |
| UI-Framework | `WPF-UI` (wpfui) 4.2.0 |
| JSON | `Newtonsoft.Json` 13.0.3 |
| SQLite | `System.Data.SQLite.Core` 1.0.119.0 |
| Target | `net10.0-windows7.0` (SDK-style `.csproj`) |
| Runtime | `win-x64`, `SelfContained=false` |

### Wichtiger Architektur-Hinweis: DI ist definiert, aber nicht verwendet

`ServiceExtensions.AddFluxDB()` registriert alle Services, ViewModels, und Pages als Singletons. **Diese Registrierung wird jedoch aktuell nicht von `App.OnStartup` aufgerufen.** Das `App.xaml.cs` überschreibt `OnStartup` nur für Debug-Mode-Erkennung — es erstellt keinen `IHost`.

Der tatsächliche Startup-Flow arbeitet komplett ohne DI:

1. `App.xaml` hat `StartupUri="Views/SplashWindow.xaml"` — WPF instanziiert `SplashWindow` direkt
2. `SplashWindow` erstellt `new SettingsService()` direkt im Konstruktor
3. `SplashWindow_Loaded` erstellt `new MainWindow()` direkt (nicht via DI)
4. `MainWindow`-Konstruktor erstellt `new SettingsService()`, `new DatabaseService()`, etc. selbst

**Das bedeutet:** `ServiceExtensions`, `MainViewModel`, `NavigationViewModel`, `DashboardViewModel`, `SettingsViewModel` sind im Code vorhanden und korrekt strukturiert, werden aber aktuell nicht vom tatsächlichen Startup-Pfad verwendet. `MainWindow` hat seine eigene Navigation, Clipboard- und Indexing-Logik im Code-Behind.

Falls in Zukunft auf DI umgestellt wird, muss `App.OnStartup` einen `IHost` erstellen:
```csharp
Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
    .ConfigureServices((ctx, services) => services.AddFluxDB())
    .Build();
```
Und `SplashWindow` muss `MainWindow` via `Host.Services.GetRequiredService<MainWindow>()` auflösen.

### Tatsächlicher Startup-Flow

1. `App.OnStartup` (App.xaml.cs) — erkennt Debug-Mode (`-debug` suffix in version.txt), setzt `LoggingService.SetDebugMode()`
2. `SplashWindow` — lädt Icon, prüft auf Updates (GitHub Releases API), erstellt `new MainWindow()` und zeigt es
3. `MainWindow`-Konstruktor — erstellt Services selbst, lädt Icons, ruft `InitializeServices()` und `LoadInitialData()` auf
4. `LoadInitialData()` — liest `LastRootFolder` aus `SettingsService`, öffnet den zuletzt verwendeten Ordner mit existierendem Index oder zeigt "Ready"

### MainWindow Struktur

`MainWindow` ist ein `FluentWindow` mit folgendem Layout (alles im Code-Behind, nicht via MVVM-DataBinding):

- **Header** — "FluxDB" Titel, aktueller Ordner-Pfad, Select Folder / Refresh / Settings Buttons
- **Navigation Bar** — Back/Forward/Up Buttons (mit `IsEnabled`-Steuerung), Breadcrumbs (dynamisch generiertes `StackPanel`), Filter-ComboBox
- **Search Bar** — TextBox + Search/Clear Buttons
- **Main Content** — links `DataGrid` für Dateien (mit `SelectionMode="Extended"`), rechts Detail-Panel (Tags, Notes, Preview)
- **Status Bar** — Status-Text, File-Count, Indexing-Progress

### Navigation (Code-Behind)

`MainWindow` verwaltet Navigation **selbst** (nicht über `NavigationViewModel`):

- `_backHistory` / `_forwardHistory` als `Stack<string>` (max 50 Einträge)
- `_currentRootFolder` / `_currentViewFolder` für den aktuellen Zustand
- `NavigateToFolder()` aktualisiert History, Breadcrumbs, und lädt die Dateiliste neu
- Breadcrumbs werden als `TextBlock`-Elemente mit `Click`-Handler dynamisch ins `pnlBreadcrumbs`-StackPanel eingefügt
- Keyboard-Shortcuts: Alt+Left/Right/Up, Backspace, Enter, F5, Ctrl+F, F8 (Log-Viewer)

### Clipboard & File Operations (Code-Behind)

Alle Datei-Operationen sind im `MainWindow.xaml.cs` implementiert:

- **Ctrl+C/X/V** — Copy/Cut/Paste mit Windows `Clipboard` API und `StringCollection`/`FileDropList`
- **Delete** — Löscht Dateien (mit Bestätigungsdialog) und markiert sie in der DB als `deleted=1`
- **F2** — Rename über `RenameDialog`
- **Drag & Drop** — `Window_Drop` Handler öffnet Ordner per Drag & Drop
- **New Folder** — Erstellt Ordner im aktuellen Verzeichnis

### ViewModels (derzeit nicht vom UI verwendet)

Diese ViewModels sind vollständig implementiert, aber nicht mit dem aktuellen `MainWindow` verdrahtet:

| ViewModel | Responsibility |
|---|---|
| `MainViewModel` | Dateiliste, Suche, Filter, Preview, Tags/Notes, Indexing, Clipboard-Operationen. Enthält `NavigationViewModel` als Property. |
| `NavigationViewModel` | Back/Forward/Up-Navigation (Stacks), Breadcrumbs, `Navigated` Event |
| `DashboardViewModel` | Recent Folders, "Open Folder"-Button, sendet `FolderOpenedMessage` via `WeakReferenceMessenger` |
| `SettingsViewModel` | Update-Check, Auto-Update-Toggle, Export/Import |

### Pages und Controls

| Component | Description |
|---|---|
| `DashboardPage` | Startseite mit "Open Folder"-Button und Recent-Folders-Liste |
| `FileBrowserPage` | Dateiliste mit Filter-ComboBox, Toolbar, Back/Forward/Up |
| `PreviewPanel` | UserControl für Image/Text/No-Preview Anzeige |
| `RefreshDialog` | Dialog zum Bestätigen der Index-Aktualisierung |
| `RenameDialog` | Dialog zum Umbenennen von Dateien |

### WPF Services

| File | Responsibility |
|---|---|
| `DatabaseService.cs` | SQLite CRUD: file index, tags, notes, folder path updates, search, batch delete marking. Nutzt WAL-Mode, `synchronous=NORMAL`, 8MB cache. |
| `IndexerService.cs` | File system scanning, batched indexing (1000 files/batch), progress reporting, deleted file detection. Verwendet iterative `Stack<string>`-Enumeration statt Rekursion. |
| `LoggingService.cs` | **Static** logger mit in-memory buffer (max 2000 lines), background file writes via `ThreadPool` und dedizierter `_writeQueue` mit `ProcessWriteQueue`-Loop. Logs nach `%LOCALAPPDATA%\FluxDB\logs.txt`. |
| `SettingsService.cs` | JSON settings (`%LOCALAPPDATA%\FluxDB\settings.json`): recent folders (max 10), theme, auto-update, device ID, per-folder filter state. |
| `ExportService.cs` | Export index to JSON or GZIP via `Newtonsoft.Json` |
| `ImportService.cs` | Import index from JSON or GZIP, upserts files/tags/notes in a transaction |

### Models

| Model | Description |
|---|---|
| `FileEntry` | `ObservableObject` mit `IconSymbol` (WPF-UI `SymbolRegular`), `SizeDisplay` (human-readable), `TypeDisplay`, `IconColorBrush`. Cached icon/size mit `_cacheValid` flag. `Tags` als `List<string>`, `TagsText` als nullbyte-separierter String. |
| `AppSettings` | `LastRootFolder`, `AutoUpdateCheck` (default `false`), `Theme` (Dark/Light), `RecentFolders`, `FolderFilters` (per-folder filter state) |
| `GitHubRelease` | JSON deserialization target for GitHub API (`TagName`) |

### Converters

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

- `version.txt` at repo root (currently `1.0.5`) drives the assembly version via an MSBuild `BeforeBuild` target that generates `Properties/AssemblyVersion.cs`
- `App.GetLocalVersion()` reads `version.txt` from the app directory, falls back to `AssemblyInformationalVersionAttribute`
- Debug mode is auto-detected: if the local version ends with `-debug`, `LoggingService.SetDebugMode(true)` is called
- Version strings use `VersionHelper.CompareVersions` for comparison — strips `v` prefix, `!beta` suffix, handles `x.y.z-suffix` semver

### Theme handling

- `App.xaml` merges WPF-UI resource dictionaries: `<ui:ThemesDictionary Theme="Dark" />` and `<ui:ControlsDictionary />`
- `ApplicationThemeManager.Apply()` is the only way to change themes. Do not manipulate `ResourceDictionary` directly.
- `SettingsService` speichert `Theme` ("Dark"/"Light") in `settings.json`

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

- No auto-update (FluxDB handles this via `SplashWindow`)
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

- Tags are stored via `GROUP_CONCAT(t.name, '\0')` with **null-byte separator** `\0` (was previously `, ` which broke on tag names containing commas). When parsing back, split on `'\0'` with `StringSplitOptions.RemoveEmptyEntries`.
- When adding new `GROUP_CONCAT` queries or modifying tag handling, always use `\0` as the separator.

### Database file location

The database file `.fluxdb` is stored **inside the indexed folder**, not in `%LocalAppData%`. This means each root folder has its own independent index. `DatabaseService` gets the DB path directly from the folder path.

### Database lifecycle

When opening a new folder, `MainWindow.InitializeDatabaseForFolder()` **disposes the old `DatabaseService`** and creates a new one. It also re-subscribes `IndexerService` events. Any code that holds a reference to `DatabaseService` or `IndexerService` will have stale references after a folder switch.

### Batch commits in IndexerService

`IndexerService.ScanFolderAsync` uses batched transactions (every 1000 files). `CommitBatchWithRetry` retries up to 3 times with exponential backoff (100ms → 200ms → 400ms). On final failure, it rolls back and rethrows. When cancelled, the current transaction is explicitly rolled back.

### Thread safety

- `LoggingService` is a **static class** with a `lock` and `ThreadPool.QueueUserWorkItem` for background file writes. It uses a `_writeQueue` with a dedicated `ProcessWriteQueue` processing loop.
- `DatabaseService` has a single `SQLiteConnection` — all DB access is serial (no connection pooling). SQLite writes are not thread-safe; ensure all DB operations happen on the same thread or serialize access.

### SQLite interop DLLs

`System.Data.SQLite.Core` requires native `SQLite.Interop.dll` (x64 and x86) in the output directory. The SDK-style `.csproj` uses `PackageReference` which handles this automatically. When packaging, both architecture folders must be present alongside the managed DLLs.

### MVVM pattern with CommunityToolkit.Mvvm

- All ViewModels inherit from `ObservableObject`. Properties use `[ObservableProperty]` source generator (field `_foo` → property `Foo`).
- Commands use `[RelayCommand]` on methods. Async commands use `[RelayCommand]` on `async Task` methods.
- `WeakReferenceMessenger.Default.Send()` / `.Register()` is used for cross-ViewModel communication (e.g., `FolderOpenedMessage`).
- `GlobalUsings.cs` provides project-wide usings — any new file can use `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`, etc. without explicit imports.

### WPF-UI (wpfui) specifics

- Windows extend `FluentWindow` (not `Window`). This provides Mica backdrop, rounded corners, and titlebar integration.
- `ApplicationThemeManager.Apply()` is the only way to change themes. Do not manipulate `ResourceDictionary` directly.
- XAML namespace: `xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"`
- `ISnackbarService` and `IContentDialogService` are available via DI, but DI is not currently wired.
- `SymbolRegular` enum values can change between wpfui versions. Verify the symbol exists in the version you're targeting (e.g., `FolderAdd24` doesn't exist in 4.2.0 — use `Folder24` instead).

### Code-behind pattern (current architecture)

The current `MainWindow` uses a **code-behind** pattern, not pure MVVM data-binding:
- Button clicks use `Click="BtnBack_Click"` handlers, not `Command` bindings
- Navigation state (`_backHistory`, `_forwardHistory`) is in the Window class, not a ViewModel
- `DataGrid` has `SelectionChanged`, `MouseDoubleClick`, `Sorting` event handlers in code-behind
- `DataContext` is not set to any ViewModel — the Window manages its own state
- Filter changes are handled via `CmbFilter_SelectionChanged` event, not a binding

### FileEntry icon caching

`FileEntry` has a manually managed cache (`_cacheValid`, `_cachedIconSymbol`, `_cachedSizeDisplay`) that is invalidated in `OnExtensionChanged`, `OnSizeChanged`, and `OnIsFolderChanged` partial methods. These partial methods are called by the CommunityToolkit source generator when the corresponding properties change. The computed properties (`IconSymbol`, `SizeDisplay`, `TypeDisplay`, `IconColorBrush`) use these caches for performance.

### MVVMTK0034 warnings

The computed properties in `FileEntry` reference the backing fields (`_isFolder`, `_extension`, `_size`) directly instead of the generated properties (`IsFolder`, `Extension`, `Size`). This generates compiler warnings but works correctly. The fields are used because the partial methods are triggered by the generated property setters, and the computed getters need the current value — using the generated properties would cause infinite recursion in some paths.

### Folder rename / delete must update DB

When renaming or deleting folders, the DB entries for all files under that path must be updated. `DatabaseService` has `UpdateFolderPath()` and `MarkPathAsDeleted()` for this purpose. Always use these methods — do not do raw SQL for folder-level operations.

### Version comparison

`VersionHelper` is a **static utility** with `NormalizeVersion` and `CompareVersions`. It strips `v` prefix, `!beta` suffix, and handles `x.y.z-suffix` semver-like strings. The `Version` class is used for numeric comparison, then string comparison for pre-release suffixes. Do not duplicate version logic — always use `VersionHelper`.

### Auto-update

- Version check: HTTP GET `https://api.github.com/repos/vynofc/FluxDB/releases/latest` (GitHub API).
- Installer: `FluxDB-Installer.exe` in the app directory, launched with `--silent-start`.
- Skip with `--noupdate` CLI flag.
- `App.IsUpdateAvailable` and `App.AvailableVersion` are static properties set by `SplashWindow`.

### Logging

- `LoggingService` is a **static** class. Use `LoggingService.Log(message)` for general logging, `LoggingService.LogDebug(message)` for debug-only messages.
- Logs go to `%LOCALAPPDATA%\FluxDB\logs.txt`
- `LoggingService.SetDebugMode(true)` is called when the version string ends with `-debug`
- `LoggingService.GetLogs()` returns the in-memory buffer
- `LoggingService.Shutdown()` flushes and disposes the writer — call on app exit

### Settings

- Settings file: `%LOCALAPPDATA%\FluxDB\settings.json`
- `AutoUpdateCheck` defaults to `false`
- `Theme` defaults to `"Dark"`
- `RecentFolders` max 10, case-insensitive dedup
- `FolderFilters` is a `Dictionary<string, string>` for per-folder filter persistence

---

## Conventions

### FluxDB (C# / WPF)

- **Namespace**: `FluxDB` for UI/root, `FluxDB.Models` for models, `FluxDB.Services` for services, `FluxDB.ViewModels` for ViewModels, `FluxDB.Views` for windows, `FluxDB.Views.Pages` for pages, `FluxDB.Views.Controls` for controls, `FluxDB.Converters` for value converters, `FluxDB.Helpers` for helpers.
- **Naming**: PascalCase for public, `_camelCase` for private fields. Controls use Hungarian-like prefixes (`txtSearch`, `btnRefresh`, `dgFiles`, `pnlBreadcrumbs`, `cmbFilter`).
- **Error handling**: Broad try-catch with silent swallowing is common. `LoggingService.Log()` is used to record errors.
- **German UI**: Some UI strings and comments are in German (the project is German-authored).
- **No async/await in constructors**: Services are initialized synchronously; async work is fire-and-forget or triggered by UI events.
- **XAML**: Uses WPF-UI resource dictionaries and dynamic resource references (`{DynamicResource ...}`). Theme colors come from `ApplicationThemeManager`.
- **SDK-style `.csproj`**: Uses `<Project Sdk="Microsoft.NET.Sdk">` with `<UseWPF>true</UseWPF>`, `<UseWindowsForms>true</UseWindowsForms>`, `<Nullable>disable</Nullable>`, `<ImplicitUsings>disable</ImplicitUsings>`. `GenerateAssemblyInfo=false` because `AssemblyInfo.cs` contains `[assembly: ThemeInfo(...)]`.

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