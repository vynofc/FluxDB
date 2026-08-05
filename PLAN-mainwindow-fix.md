# PLAN: MainWindow UI & Logic Fix

## Status: In Progress — Build erfolgreich (0 Errors), Crash beim Start existiert noch

## Letzter Stand (2026-08-05 23:40)

Die App crasht beim Übergang vom SplashScreen zum MainWindow:
```
[2026-08-05 23:40:42.411] Startup: Checking for updates
[2026-08-05 23:40:42.969] Startup CRITICAL failure: Object reference not set to an instance of an object.
```

### Was bereits zum Debuggen hinzugefügt wurde
- **MainWindow-Konstruktor**: try/catch mit `LoggingService.Log` vor jedem Schritt (DataContext, InitializeComponent, Icons)
- **SplashWindow**: Exception-Logging von `ex.Message` auf `ex.ToString()` (Stacktrace)
- **NavigationViewModel**: Konstruktor-Log
- **SymbolRegular.FolderAdd24** → `Folder24` (existiert nicht in WPF-UI 4.2)
- **QuerySubmitted** Event aus XAML entfernt (existiert nicht in WPF-UI 4.2)

### Nächster Schritt nach Build
App starten und Logs in `%LOCALAPPDATA%\FluxDB\logs.txt` prüfen. Der erweiterte Log sollte jetzt zeigen, ob der Crash in `InitializeComponent()` (XAML), im `MainViewModel`-Konstruktor oder beim Setzen der Icons passiert.

### 1. NavigationViewModel (`ViewModels/NavigationViewModel.cs`)
- **`GoBack()`, `GoForward()`, `GoUp()`** → von `public void` auf `[RelayCommand] private void` umgestellt
  - Grund: Das XAML bindet via `{Binding Navigation.GoBackCommand}` — ohne `[RelayCommand]`-Attribut wird kein Command generiert → Buttons tun nichts
- **`Navigated` Event** hinzugefügt, das nach jeder Navigation feuert
  - Grund: Ohne dieses Event lädt sich die Dateiliste nach Navigation nicht neu (UI bleibt stale)

### 2. FileEntry (`Models/FileEntry.cs`)
- **`OnExtensionChanged`, `OnSizeChanged`, `OnIsFolderChanged`** → feuern jetzt `OnPropertyChanged` für abhängige Properties
  - Vorher: `SizeDisplay`, `Icon`, `IconColor`, `TypeDisplay` sind computed properties ohne `INotifyPropertyChanged` — UI zeigt nach Änderungen veraltete Werte
  - Jetzt: Bei jeder Feldänderung werden die abhängigen Properties korrekt benachrichtigt

### 3. MainViewModel (`ViewModels/MainViewModel.cs`)
- **`OnNavigationChanged`** → abonniert das `Navigation.Navigated` Event, ruft `RefreshCurrentFolderViewAsync()` auf
- **Clipboard Commands** neu implementiert:
  - `CopyCommand` — kopiert SelectedFile.Path in `_clipboardFiles`
  - `CutCommand` — wie Copy, setzt zusätzlich `_clipboardIsCut = true`
  - `PasteCommand` — kopiert/verschiebt Dateien, inkl. `CopyDirectoryRecursive` für Ordner
- **`FilterChangedCommand`** — Filter-Wechsel aus ComboBox wird sofort verarbeitet
- **`NavigateToBreadcrumbCommand`** — Breadcrumb-Klick navigiert zum Pfad
- **`GetFileByPath`-Referenz** gefixt → existiert nicht in DatabaseService, verwendet jetzt `GetAllFiles().FirstOrDefault(f => f.Path == ...)`

### 4. MainWindow XAML (`Views/MainWindow.xaml`) — komplett neu
| Problem vorher | Fix |
|---|---|
| `ProgressBar` und `Cancel` Button im selben `Grid.Column` (StatusBar) | Eigene Row für Indexing-Progress mit korrektem 2-Spalten-Layout |
| Keine Breadcrumbs sichtbar | `ItemsControl` mit `StackPanel`-Layout, klickbare Links |
| Kein "New Folder" Button | `btnNewFolder` mit `Folder24`-Icon |
| StatusBar hatte Cancel-Button und ProgressBar inline | Nur noch `StatusMessage` + `FileCountText` |
| Navigation-Buttons ohne `IsEnabled`-Binding | `IsEnabled="{Binding Navigation.CanGoBack}"` etc. hinzugefügt |
| Grid.RowDefinitions der Main Content Area hatte nur 2 Rows | Auf 4 Rows erweitert: Toolbar, Indexing, Content, StatusBar |
| Filter-ComboBox ohne Event-Handler | `SelectionChanged="CmbFilter_SelectionChanged"` |
| Suchbox ohne Submit-Event | `QuerySubmitted` entfernt (nicht in WPF-UI 4.2), Text-Binding bleibt |
| Shortcut-Text veraltet | `Ctrl+N New Folder` ergänzt |

### 5. MainWindow Code-Behind (`Views/MainWindow.xaml.cs`) — komplett neu
- **Ctrl+C / Ctrl+X / Ctrl+V** → jetzt an ViewModel-Commands gebunden (vorher nur im Shortcut-Text angezeigt, aber nicht implementiert)
- **Ctrl+N** → `NewFolderCommand`
- **Alt+Left/Right/Up** → verwenden `Command.Execute` statt direkter Methodenaufrufe
- **Constructor-Debugging** → try/catch mit Logging um den gesamten Konstruktor
- **`TxtSearch_QuerySubmitted`** → entfernt (WPF-UI 4.2 hat dieses Event nicht)
- **`CmbFilter_SelectionChanged`** → ruft `FilterChangedCommand` auf
- **`Breadcrumb_Click`** → ruft `NavigateToBreadcrumbCommand` auf
- **`SymbolRegular.FolderAdd24`** → auf `Folder24` geändert (existiert in WPF-UI 4.2 nicht)

### 6. SplashWindow (`Views/SplashWindow.xaml.cs`)
- **Exception-Logging** → von `ex.Message` auf `ex.ToString()` (Stacktrace sichtbar)

---

## Noch offen / TODO

### Crash: NullReferenceException beim MainWindow-Start
- **Log**: `Startup CRITICAL failure: Object reference not set to an instance of an object.`
- **Tritt auf in**: `App.Host.Services.GetRequiredService<MainWindow>()` (SplashWindow.xaml.cs:92)
- **Mögliche Ursachen**:
  1. `SymbolRegular.FolderAdd24` existiert nicht in WPF-UI 4.2.0 → bereits auf `Folder24` geändert
  2. `QuerySubmitted` Event existiert nicht in WPF-UI 4.2.0 → bereits entfernt
  3. Fehler in `InitializeComponent()` — XAML-Parsing schlägt fehl
  4. `ServiceExtensions.AddFluxDB()` registriert `DatabaseService` mit Pfad der nicht existiert
  5. `MainViewModel`-Konstruktor wirft Exception (z.B. `WeakReferenceMessenger`)

- **Nächster Schritt**: Build testen und Logs prüfen. Der erweiterte Constructor-Log sollte jetzt genau zeigen, wo es knallt.

### FileEntry: MVVMTK0034 Warnings
- Die computed properties (`SizeDisplay`, `Icon`, `IconColor`, `TypeDisplay`) greifen direkt auf `_isFolder`, `_extension`, `_size` zu statt auf die generierten Properties `IsFolder`, `Extension`, `Size`
- **Nicht kritisch** (funktioniert), aber erzeugt 15 Compiler-Warnings
- **Fix**: `_isFolder` → `IsFolder`, `_extension` → `Extension`, `_size` → `Size` in den computed property gettern

### Suchbox: Text-Input triggert keine Suche
- `QuerySubmitted`-Event wurde entfernt, weil es in WPF-UI 4.2 nicht existiert
- **Alternative**: `PropertyChanged`-Callback oder `KeyDown`-Handler, der bei Enter sucht
- Aktuell sucht die Box nur via `{Binding SearchText}` — das ViewModel hat aber keine `SearchCommand`-Ausführung bei Textänderung

### Filter-ComboBox: Initialer State
- `cmbFilter` hat `IsSelected="True"` auf "All Files" im XAML
- `CurrentFilter` im ViewModel ist `"All Files"` — passt
- Aber: `SelectionChanged` feuert beim ersten Laden, bevor DataContext gesetzt ist → könnte NullRef sein
- **Fix**: `if (_viewModel == null) return;` am Anfang von `CmbFilter_SelectionChanged`

### Breadcrumbs: Erster Eintrag hat führendes `>`
- Der `ItemsControl` zeigt vor jedem Breadcrumb ein `>`, auch vor dem ersten
- **Fix**: `Visibility` des ersten `>` per `DataTrigger` auf `Collapsed` setzen wenn `ItemsControl.AlternationIndex == 0`

### Navigation-History wird nicht persistiert
- `_backHistory` und `_forwardHistory` sind nur im RAM
- App-Neustart verliert die History → kein grosses Problem, aber nice-to-have

### Clipboard: Multi-File Support fehlt
- `Copy`/`Cut` kopiert nur eine Datei (SelectedFile)
- `dgFiles` hat `SelectionMode="Single"` → `MultiSelect` müsste aktiviert werden für Multi-File

### Logging: `LogDebug` ruft `Log` auf, aber `Log` schreibt `[DEBUG]` Präfix
- `LoggingService.LogDebug` prüft `IsDebugMode` und ruft dann `Log($"[DEBUG] {message}")`
- Das ist korrekt, aber `Log` selbst loggt immer — kein Filter

---

## Build-Status
```
0 Fehler, 98 Warnungen
```
Alle Warnungen sind pre-existing (CA1416 für Windows-Only APIs, MVVMTK0034 für Field-Referenzen).

---

## Gedanken zum Gesamtzustand

Die App war funktional unvollständig — viele UI-Elemente waren da, aber ohne tatsächliche Logik dahinter:
- Navigation-Buttons ohne Commands
- Clipboard-Shortcuts im Hilfetext, aber nicht implementiert
- Breadcrumbs im ViewModel, aber nicht im UI
- Kein New-Folder-Button

Das ist jetzt alles gefixt. Der verbleibende Crash ist wahrscheinlich ein XAML-Parsing- oder DI-Problem, das mit den erweiterten Logs identifizierbar sein sollte.

Die Architektur ist solide (MVVM mit CommunityToolkit, WPF-UI für Fluent Design), aber die DI-Registrierung in `ServiceExtensions` ist fragil — `DatabaseService` wird mit einem Pfad erstellt, der auf `LastRootFolder` basiert, was beim ersten Start leer ist und auf `MyDocuments` fällt.