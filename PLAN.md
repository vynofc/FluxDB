# PLAN.md — FluxDB Electron + TypeScript Refactor

## Ziel

Rewrite der WPF-App (C# / .NET Framework 4.7.2) als **Electron + TypeScript + React** Desktop-Applikation nach dem Vorbild von VS Code. Der Go-Installer bleibt erhalten und wird nur um Electron-kompatible Pfade erweitert.

---

## 1. Tech Stack

| Layer | Technology | Begründung |
|---|---|---|
| **Runtime** | Electron 33+ | Stable, Cross-Platform, VS Code bewährt |
| **Renderer UI** | React 18 + TypeScript 5 | Komponenten-Modell, riesiges Ecosystem |
| **State** | Zustand | Minimal, kein Boilerplate, perfekt für Electron |
| **Build** | Vite + electron-vite | Schnell, HMR, optimierte Bundles |
| **SQLite** | better-sqlite3 (main process) | Synchron, kein IPC-Overhead pro Query |
| **Styling** | Tailwind CSS + CSS Variables | Dark Theme trivial, Utility-First |
| **Virtual Scrolling** | @tanstack/react-virtual | Für 100k+ Dateien im DataGrid |
| **Icons** | Lucide React | Konsistent, Tree-Shakeable |
| **Packaging** | electron-builder | NSIS-Installer, Auto-Update via GitHub Releases |
| **Testing** | Vitest + Playwright | Unit + E2E |
| **Linting** | ESLint + Prettier | Standard |

---

## 2. Projektstruktur

```
FluxDB/
├── electron/                    # Electron Main Process
│   ├── main.ts                  # Entrypoint, Window-Management, App-Lifecycle
│   ├── preload.ts               # Context-Bridge API (sichere IPC)
│   ├── ipc/
│   │   ├── index.ts             # Handler-Registrierung
│   │   ├── database.ipc.ts      # SQLite CRUD, Search, Tags, Notes
│   │   ├── filesystem.ipc.ts    # Scan, Copy, Move, Delete, Rename, Mkdir
│   │   ├── export.ipc.ts        # JSON / GZip Export
│   │   ├── settings.ipc.ts      # settings.json lesen/schreiben
│   │   ├── preview.ipc.ts       # Thumbnails, Text-Preview
│   │   ├── update.ipc.ts        # GitHub Releases Auto-Update
│   │   └── shell.ipc.ts         # Open File, Open Folder, Show in Explorer
│   ├── services/
│   │   ├── database.service.ts  # better-sqlite3 Wrapper (Schema, Queries, Transactions)
│   │   ├── indexer.service.ts   # Rekursiver Folder-Scan, Batch-Insert, Progress-Events
│   │   ├── export.service.ts    # JSON/GZip Serialisierung
│   │   ├── settings.service.ts  # JSON Config File (AppData)
│   │   ├── logging.service.ts   # In-Memory Ring Buffer + File Writer
│   │   ├── thumbnail.service.ts # Native Image/PDF Thumbnails (sharp, pdf-thumbnail)
│   │   └── updater.service.ts   # electron-updater Integration
│   └── utils/
│       ├── version.ts           # Version Normalisierung & Vergleich
│       ├── file-icon.ts         # Extension → Icon Mapping
│       └── paths.ts             # AppData, Temp, Platform-spezifische Pfade
│
├── src/                         # React Renderer
│   ├── main.tsx                 # React Root
│   ├── App.tsx                  # Layout, Routing, Global State
│   ├── components/
│   │   ├── layout/
│   │   │   ├── TitleBar.tsx          # Custom Title Bar (optional, OS-native als Fallback)
│   │   │   ├── Header.tsx            # Logo, Folder-Select, Buttons
│   │   │   ├── NavigationBar.tsx     # Back/Forward/Up, Breadcrumbs, Filter-Dropdown
│   │   │   ├── SearchBar.tsx         # Suchfeld + Search/Clear Buttons
│   │   │   ├── StatusBar.tsx         # Status-Meldung + File-Count
│   │   │   └── ProgressBar.tsx       # Indexing-Fortschritt + Cancel
│   │   ├── filelist/
│   │   │   ├── FileTable.tsx         # Virtual Table (Name, Type, Size, Tags, Modified)
│   │   │   ├── FileRow.tsx           # Einzelne Zeile (Icon, Name, Context Menu)
│   │   │   ├── FileIcon.tsx          # Icon + Farbe basierend auf Extension
│   │   │   ├── ContextMenu.tsx       # Rechtsklick: Open, Copy, Cut, Paste, Delete, Rename
│   │   │   └── SortHeader.tsx        # Sortierbare Spalten-Header
│   │   ├── preview/
│   │   │   ├── PreviewPanel.tsx      # Container für Vorschau
│   │   │   ├── ImagePreview.tsx      # Bild-Vorschau + Zoom (Mousewheel)
│   │   │   ├── TextPreview.tsx       # Text/Code-Vorschau (max 5000 Zeichen)
│   │   │   └── NoPreview.tsx         # "No Preview Available" Fallback
│   │   ├── details/
│   │   │   ├── DetailsPanel.tsx      # Datei-Details (Name, Path, Size, Dates)
│   │   │   ├── TagEditor.tsx         # Tags bearbeiten (Input + Save)
│   │   │   └── NoteEditor.tsx        # Notizen (Textarea + Save)
│   │   ├── dialogs/
│   │   │   ├── SettingsDialog.tsx    # Settings-Fenster
│   │   │   ├── RefreshDialog.tsx     # Refresh-Optionen (Entire/Current/Specific)
│   │   │   ├── RenameDialog.tsx      # Umbenennen-Dialog
│   │   │   ├── SplashScreen.tsx      # Startup-Screen mit Update-Check
│   │   │   └── ConfirmDialog.tsx     # Generischer Bestätigungsdialog
│   │   └── common/
│   │       ├── Button.tsx            # Primary / Secondary Button
│   │       ├── Dropdown.tsx          # Filter-Combobox
│   │       ├── DropOverlay.tsx       # Drag & Drop Overlay
│   │       └── Tooltip.tsx           # Tooltip
│   ├── hooks/
│   │   ├── useDatabase.ts            # DB Queries via IPC
│   │   ├── useIndexer.ts             # Indexing Progress + Cancel
│   │   ├── useFileOperations.ts      # Copy, Cut, Paste, Delete, Rename
│   │   ├── useDragDrop.ts            # External Drop + Internal Drag
│   │   ├── useKeyboardShortcuts.ts   # Globale Shortcuts
│   │   ├── useNavigation.ts          # Back/Forward/Up + Breadcrumbs
│   │   ├── usePreview.ts             # Preview laden
│   │   ├── useSettings.ts            # Settings laden/speichern
│   │   └── useUpdate.ts              # Auto-Update Logik
│   ├── stores/
│   │   ├── app.store.ts              # Global: RootFolder, ViewFolder, DB-Path
│   │   ├── files.store.ts            # File-Liste, Selection, Sort, Filter
│   │   ├── search.store.ts           # Search-Query, Results, IsSearchMode
│   │   ├── clipboard.store.ts        # Clipboard (Copy/Cut), Drag-Drop Source
│   │   ├── navigation.store.ts       # Back/Forward History, Breadcrumbs
│   │   └── settings.store.ts         # AppSettings State
│   ├── types/
│   │   ├── file-entry.ts             # FileEntry Interface
│   │   ├── app-settings.ts           # AppSettings Interface
│   │   ├── index-export.ts           # IndexExport Interfaces
│   │   ├── ipc.ts                    # IPC Channel Names + Payload Types
│   │   └── events.ts                 # Event-Typen (Progress, Status)
│   └── styles/
│       ├── globals.css               # CSS Variables + Tailwind Base
│       ├── theme.css                 # Dark Theme Colors (1:1 aus WPF XAML)
│       └── components.css            # Komponenten-spezifische Styles
│
├── tests/
│   ├── unit/
│   │   ├── database.service.test.ts
│   │   ├── indexer.service.test.ts
│   │   ├── version.test.ts
│   │   └── stores/
│   └── e2e/
│       ├── indexing.spec.ts
│       ├── file-operations.spec.ts
│       └── search.spec.ts
│
├── resources/                    # App-Icons, Installer-Assets
│   ├── icon.ico
│   ├── icon.png
│   └── icon.icns
│
├── electron-builder.yml          # Build-Konfiguration
├── vite.config.ts
├── tsconfig.json
├── package.json
└── README.md
```

---

## 3. IPC-Architektur

Electron trennt **Main Process** (Node.js, Dateisystem, SQLite) vom **Renderer Process** (Chromium, React). Kommunikation ausschließlich über `contextBridge` + `ipcRenderer.invoke` / `ipcMain.handle`.

### 3.1 Preload API (Renderer → Main)

```typescript
// electron/preload.ts — Exposed via contextBridge
interface FluxDBAPI {
  // Database
  db: {
    getFilesInFolder(folderPath: string): Promise<FileEntry[]>;
    searchFiles(query: string, folderPath: string): Promise<FileEntry[]>;
    searchByTag(tagName: string): Promise<FileEntry[]>;
    getFileCount(): Promise<number>;
    upsertFile(entry: FileEntry): Promise<number>;
    setTagsForFile(fileId: number, tags: string[]): Promise<void>;
    setNoteForFile(fileId: number, note: string): Promise<void>;
    getTagsForFile(fileId: number): Promise<string[]>;
    getAllTags(): Promise<Tag[]>;
    markFileAsDeleted(fileId: number): Promise<void>;
    markPathAsDeleted(path: string): Promise<void>;
    updateFilePathAndName(fileId: number, newPath: string, newName: string): Promise<void>;
    updateFolderPath(oldPath: string, newPath: string): Promise<void>;
    markDeletedFiles(existingPaths: string[]): Promise<void>;
  };

  // File System
  fs: {
    listDirectories(path: string): Promise<string[]>;
    scanFolder(rootPath: string): Promise<ScanResult>;
    getFileInfo(path: string): Promise<FileInfo>;
    copyFiles(sources: string[], target: string): Promise<CopyResult>;
    moveFiles(sources: string[], target: string): Promise<CopyResult>;
    deleteFiles(paths: string[]): Promise<DeleteResult>;
    renameFile(oldPath: string, newPath: string): Promise<void>;
    createFolder(path: string): Promise<void>;
    openFile(path: string): Promise<void>;
    openFileLocation(path: string): Promise<void>;
    selectFolder(): Promise<string | null>;
    saveFileDialog(filters: FileFilter[]): Promise<string | null>;
  };

  // Export
  export: {
    toJson(filePath: string, rootFolder: string): Promise<void>;
    toGzip(filePath: string, rootFolder: string): Promise<void>;
  };

  // Preview
  preview: {
    getImageThumbnail(path: string, size: number): Promise<string>; // base64 data URL
    readTextContent(path: string, maxLength: number): Promise<string>;
  };

  // Settings
  settings: {
    load(): Promise<AppSettings>;
    save(settings: AppSettings): Promise<void>;
    getAppDataDir(): Promise<string>;
  };

  // Update
  update: {
    checkForUpdates(): Promise<UpdateInfo | null>;
    downloadUpdate(): Promise<void>;
    installUpdate(): Promise<void>;
  };

  // Events (Main → Renderer)
  onIndexingProgress(callback: (e: IndexProgressEvent) => void): () => void;
  onIndexingStatus(callback: (status: string) => void): () => void;
  onUpdateStatus(callback: (status: string) => void): () => void;
}
```

### 3.2 Security

- `contextIsolation: true`, `nodeIntegration: false`, `sandbox: true`
- Kein `remote`-Modul, keine `shell.openExternal` ohne Whitelist
- Alle Pfade werden im Main Process validiert (keine Path-Traversal)
- File-Operationen nur innerhalb des Root-Folders + Temp

---

## 4. Komponenten-Baum

```
<App>
  <SplashScreen />                           # Startup: Update Check, Loading
  <MainLayout>
    <TitleBar />                             # Optional: Custom Title Bar
    <Header>
      <Logo />
      <Button onClick={selectFolder}>Select Folder</Button>
      <Button onClick={showRefresh}>Refresh</Button>
      <Button onClick={handleExport}>Export</Button>
      <Button onClick={openSettings}>Settings</Button>
    </Header>
    <NavigationBar>
      <NavButton icon={ArrowLeft} onClick={goBack} />
      <NavButton icon={ArrowRight} onClick={goForward} />
      <NavButton icon={ArrowUp} onClick={goUp} />
      <Breadcrumbs path={viewFolder} onClick={navigate} />
      <FilterDropdown value={filter} onChange={setFilter} />
    </NavigationBar>
    <SearchBar>
      <SearchInput value={query} onChange={setQuery} onEnter={search} />
      <Button onClick={search}>Search</Button>
      <Button onClick={clearSearch}>Clear</Button>
    </SearchBar>
    <SplitPane>
      <FileTable
        files={filteredFiles}
        selected={selectedFiles}
        onSelect={handleSelect}
        onDoubleClick={handleOpen}
        onSort={handleSort}
        onContextMenu={showContextMenu}
        onDragStart={handleDragStart}
      />
      <DetailsPanel>
        <PreviewPanel>
          <ImagePreview src={previewSrc} scale={zoom} onWheel={handleZoom} />
          <TextPreview content={textContent} />
          <NoPreview />
        </PreviewPanel>
        <FileDetails>
          <FileName />
          <FilePath />
          <FileSize />
          <FileDates />
          <TagEditor tags={tags} onSave={saveTags} />
          <NoteEditor note={note} onSave={saveNote} />
        </FileDetails>
        <ShortcutsInfo />
      </DetailsPanel>
    </SplitPane>
    <ProgressBar
      visible={isIndexing}
      progress={percentage}
      status={currentFile}
      onCancel={cancelIndexing}
    />
    <StatusBar
      message={statusMessage}
      fileCount={totalFiles}
    />
  </MainLayout>

  {/* Dialogs (Conditional) */}
  <SettingsDialog />
  <RefreshDialog />
  <RenameDialog />
  <ConfirmDialog />
  <DropOverlay visible={isDragging} />
</App>
```

---

## 5. State Management (Zustand Stores)

### 5.1 `app.store.ts`

```typescript
interface AppState {
  rootFolder: string | null;
  viewFolder: string | null;
  dbPath: string | null;
  isInitialized: boolean;
  init(rootFolder: string): Promise<void>;
  setViewFolder(path: string): void;
}
```

### 5.2 `files.store.ts`

```typescript
interface FilesState {
  files: FileEntry[];
  selectedIds: Set<number>;
  sortColumn: 'name' | 'type' | 'size' | 'modified';
  sortDirection: 'asc' | 'desc';
  filter: FilterType;
  refreshView(): Promise<void>;
  setSort(column: string): void;
  setFilter(filter: FilterType): void;
  selectFile(id: number, multi: boolean): void;
}
```

### 5.3 `navigation.store.ts`

```typescript
interface NavigationState {
  backHistory: string[];
  forwardHistory: string[];
  canGoBack: boolean;
  canGoForward: boolean;
  canGoUp: boolean;
  navigateTo(path: string): void;
  goBack(): void;
  goForward(): void;
  goUp(): void;
}
```

### 5.4 `search.store.ts`

```typescript
interface SearchState {
  query: string;
  isSearchMode: boolean;
  results: FileEntry[];
  search(query: string, folderPath: string): Promise<void>;
  clear(): void;
}
```

### 5.5 `clipboard.store.ts`

```typescript
interface ClipboardState {
  files: string[];
  isCut: boolean;
  copy(paths: string[]): void;
  cut(paths: string[]): void;
  paste(targetFolder: string): Promise<void>;
  clear(): void;
}
```

---

## 6. Datenfluss: Indexing (Beispiel)

```
User klickt "Select Folder"
  → Main Process: dialog.showOpenDialog()
  → Renderer: app.init(folderPath)
    → Main: database.service.open(folderPath/.fluxdb)
    → Main: settings.service.save({ lastRootFolder: folderPath })
    → Renderer: files.refreshView()
    → Renderer: navigation.navigateTo(folderPath)

User klickt "Refresh"
  → RefreshDialog: Choice = "Entire"
  → Main: indexer.service.scanFolder(rootFolder)
    → Sammle alle Dateien via fs.readdir (rekursiv)
    → BATCH (1000 files):
      → database.service.upsertFile() in Transaktion
      → Commit mit Retry
    → database.service.markDeletedFiles(existingPaths)
    → Events: 'indexing-progress' (current, total, percentage)
    → Renderer: ProgressBar aktualisiert
    → Events: 'indexing-status' (text)
    → Result: { filesIndexed, duration, errors }
  → Renderer: files.refreshView()
  → Renderer: StatusBar = "Indexed X files in Ys"
```

---

## 7. Migration: WPF → Electron Feature Mapping

| WPF Feature | Electron Implementation |
|---|---|
| `MainWindow` (1840 Zeilen) | `App.tsx` + Stores + Hooks (aufgeteilt) |
| `DataGrid` mit Virtualisierung | `@tanstack/react-virtual` + Custom Table |
| `INotifyPropertyChanged` Bindings | React State + Zustand (reaktiv) |
| `Dispatcher.Invoke` | IPC Events (main → renderer) |
| `Task.Run` | IPC async handlers (immer im Main Process) |
| `CancellationToken` | `AbortController` + `cancellationToken` im IPC |
| `FolderBrowserDialog` | `dialog.showOpenDialog({ properties: ['openDirectory'] })` |
| `SaveFileDialog` | `dialog.showSaveDialog()` |
| `MessageBox` | Custom `ConfirmDialog` Komponente |
| `Clipboard.SetFileDropList` | `clipboard.writeBuffer('FileDrop', ...)` |
| `DragDrop.DoDragDrop` | HTML5 Drag & Drop API + `startDrag()` |
| `Process.Start` | `shell.openPath()` / `shell.showItemInFolder()` |
| `shell32.dll` COM Thumbnails | `sharp` (Bilder) + `pdf-thumbnail` (PDF) |
| `System.Windows.Forms.FolderBrowserDialog` | Electron `dialog` (erster Klasse) |
| `BitmapImage` / `BitmapSource` | `<img src={base64DataUrl}>` + `sharp` |
| `WebBrowser` (PDF) | `<iframe src={filePath}>` oder `pdf-thumbnail` |
| `StreamReader` (Text) | `fs.readFile(path, 'utf-8')` |
| `System.Data.SQLite` | `better-sqlite3` |
| `Newtonsoft.Json` | `JSON.stringify/parse` (nativ) |
| `GZipStream` | `zlib.gzipSync` (nativ) |
| `ThreadPool.QueueUserWorkItem` | `setImmediate` / Worker Threads |
| `System.Windows.Interop` | N/A — Electron handled via `shell` |
| Shell Icon Overlay | `app.getFileIcon(path, { size: 'small' })` |

---

## 8. Feature Parity Checklist

### Phase 1: Core (MVP)

- [x] Fenster mit Dark Theme starten
- [x] Folder auswählen (Dialog)
- [x] SQLite DB erstellen/öffnen im ausgewählten Ordner (`.fluxdb`)
- [x] Rekursives Scannen + Indexing mit Progress
- [x] File-Liste anzeigen (Name, Type, Size, Modified, Tags)
- [x] Virtual Scrolling (Performance bei 100k+ Einträgen)
- [x] Sortierung (Name, Size, Type, Modified — Folders First)
- [x] Navigation: Back, Forward, Up, Breadcrumbs
- [x] Doppelklick: Ordner öffnen, Datei mit System-App öffnen
- [ ] Filter (All, Images, Audio, Video, Documents, Archives, Code)
- [x] Suche (Name, Extension, Tags)
- [x] Details Panel (Name, Path, Size, Created, Modified)
- [x] Tags bearbeiten + speichern
- [x] Notizen bearbeiten + speichern
- [x] Preview: Bilder, Text-Dateien
- [x] Export (JSON, GZip)

### Phase 2: File Operations

- [x] Copy / Cut / Paste (intern + System Clipboard)
- [x] Delete (mit Bestätigung, DB-Update)
- [x] Rename (F2, Dialog)
- [x] New Folder
- [x] Context Menu (Rechtsklick)
- [x] Keyboard Shortcuts (Ctrl+C/X/V, Del, F2, F5, Ctrl+F, Alt+Arrows, Enter, Backspace)

### Phase 3: Drag & Drop

- [x] Externer Drop: Folder zum Indexen, Files zum Kopieren/Verschieben
- [x] Shift+Drag = Move, Drag = Copy
- [x] Interner Drag: Dateien aus DataGrid rausziehen
- [x] Drop Overlay (VS Code-style)

### Phase 4: Settings & Auto-Update

- [x] Settings Dialog (Theme, Auto-Update, Recent Folders, Folder Filters)
- [x] `settings.json` in `%APPDATA%/FluxDB/`
- [x] Auto-Update via `electron-updater` (GitHub Releases)
- [x] Version-Check beim Start
- [x] Recent Folders (max 10, persistiert)
- [x] Folder-spezifische Filter speichern
- [x] Splash Screen mit Update-Status

### Phase 5: Polish

- [x] Image Preview Zoom (Mousewheel, Ctrl für schneller)
- [x] PDF Thumbnail Preview
- [x] Datei-Icons mit Farben (1:1 aus WPF `FileEntry.Icon`/`IconColor`)
- [x] Logging (In-Memory Ring Buffer + File)
- [x] Log Viewer (integriert in Settings oder separater Tab)
- [x] Refresh-Dialog (Entire / Current / Specific Folder)
- [x] Indexing Cancel + Retry
- [x] Batch-Commits (alle 1000 Files + Retry 3x)
- [x] MarkDeletedFiles nach Indexing

---

## 9. Meilensteine

| Milestone | Dauer | Tasks |
|---|---|---|
| **M1: Projekt-Setup** | 2 Tage | electron-vite Scaffold, TypeScript Config, ESLint, Tailwind, electron-builder |
| **M2: Core Services** | 5 Tage | DatabaseService, SettingsService, LoggingService, IPC-Handler, Preload API |
| **M3: Indexer** | 3 Tage | IndexerService (Scan + Batch-Insert + Progress), Cancel/Retry |
| **M4: Main UI** | 5 Tage | Layout, Header, Navigation, DataGrid, Virtual Scrolling, Sort, Filter |
| **M5: File Operations** | 4 Tage | Copy/Cut/Paste/Delete/Rename, Context Menu, Keyboard Shortcuts, Drag & Drop |
| **M6: Preview & Details** | 3 Tage | Image/Text/PDF Preview, Details Panel, Tag Editor, Note Editor |
| **M7: Search & Export** | 2 Tage | Search, Export JSON/GZip |
| **M8: Settings & Update** | 3 Tage | Settings Dialog, Splash Screen, Auto-Update, Recent Folders |
| **M9: Testing** | 4 Tage | Unit Tests (Services, Stores), E2E Tests (Playwright) |
| **M10: Packaging & Release** | 2 Tage | electron-builder Config, NSIS Installer, CI/CD, Release Pipeline |
| **Gesamt** | **~33 Tage** | 1 Person Vollzeit, oder ~6 Wochen realistisch mit Puffer |

---

## 10. Risiken & Mitigationen

| Risiko | Impact | Mitigation |
|---|---|---|
| **SQLite Sync blockiert Main Process** | `better-sqlite3` ist synchron. Bei großen Queries friert die UI (Events) kurz ein. | DB-Queries in `Worker Threads` auslagern, oder `setImmediate` chunking. Alternativ: `sql.js` mit Async-API evaluieren. |
| **Virtual Scrolling Buggy** | 100k+ Zeilen ruckeln oder rendern falsch. | `@tanstack/react-virtual` ist VS Code-erprobt. Row-Caching, `overscan`, konstante Row-Höhe. |
| **Electron RAM-Verbrauch** | 150-300 MB idle. Nutzer mit schwachen Rechnern beschweren sich. | `--js-flags="--max-old-space-size=128"`, Shared WebSQL statt eigenem Chromium-Cache, lazy load von `sharp` und `pdf-thumbnail`. |
| **Cross-Platform Pfade** | `\` vs `/`, `%LOCALAPPDATA%` vs `~/Library/Application Support` vs `~/.config`. | `path.join`, `path.normalize`, `app.getPath('userData')` konsequent verwenden. |
| **Shell Thumbnails** | WPF nutzt `shell32.dll` COM-Interop für native Icons/Vorschaubilder. | `app.getFileIcon()` für Icons. `sharp` für Bild-Vorschaubilder. PDF-Thumbnails: `pdf-thumbnail` npm package. Nicht alle Formate abdeckbar — Issue dokumentieren. |
| **Auto-Update auf macOS** | `electron-updater` braucht Code-Signing auf macOS. | Zunächst Windows-only releasen (wie WPF). macOS Support später mit Signing. |
| **Bundle-Größe** | 100-150 MB mit Electron + Chromium. | `electron-builder` compression, `nsis-web` für incremental updates. ASAR-Archivierung. |
| **Performance bei Drag & Drop** | Große Dateien (10+ GB) kopieren blockiert. | `fs.copyFile` mit Progress-Callback, Stream-Piping, Abbruch via AbortController. |

---

## 11. Design-Entscheidungen

### 11.1 Warum better-sqlite3 statt sql.js?

| Kriterium | better-sqlite3 | sql.js |
|---|---|---|
| Performance | Native C, 10x schneller | Wasm, langsamer |
| Async | Nein (synchron) | Ja (async/await) |
| Blockiert Main | Ja (Workaround: Worker Threads) | Nein |
| Build | Benötigt native Addon (node-gyp) | Pure Wasm, kein Build |
| Empfehlung | **Ja** (Performance für 100k+ Rows) | Nur wenn native Addon nicht möglich |

### 11.2 Warum Zustand statt Redux?

- FluxDB hat ~6 kleine Stores, keine komplexe Cross-Store-Logik
- Zustand: 2 KB, keine Boilerplate, TypeScript-first
- Redux: Overkill für diese App-Größe

### 11.3 Warum Custom DataGrid statt Bibliothek?

- `ag-Grid` / `MUI DataGrid`: 200+ KB, Lizenzkosten, weniger flexibel
- Custom Table mit `@tanstack/react-virtual`: Vollständige Kontrolle, 0 KB extra, passt exakt zum Dark Theme
- Sortierung, Multi-Select, Context Menu sind trivial selbst implementiert

### 11.4 Warum kein React Router?

- FluxDB ist eine Single-View-App (kein Routing)
- Dialoge (Settings, Refresh, Rename) sind modale Overlays
- `react-router` würde nur Komplexität hinzufügen

---

## 12. Theme Mapping (WPF XAML → CSS Variables)

```css
/* electron/src/styles/theme.css — 1:1 aus WPF MainWindow.xaml Resources */
:root {
  --bg-primary: #1e1e1e;           /* BackgroundBrush */
  --bg-secondary: #252526;          /* SecondaryBackgroundBrush */
  --bg-tertiary: #2d2d30;           /* ContextMenu Background */
  --border: #3c3c3c;                /* BorderBrush */
  --text-primary: #cccccc;          /* ForegroundBrush */
  --text-secondary: #8a8a8a;        /* Opacity 0.7 */
  --accent: #0078d4;                /* AccentBrush */
  --accent-hover: #1e88e5;          /* AccentHoverBrush */
  --success: #4caf50;               /* SuccessBrush */
  --warning: #ff9800;               /* WarningBrush */
  --error: #f44336;                 /* ErrorBrush */
  --folder: #dcb67a;                /* FolderBrush */
  --drop-highlight: #3078d4;        /* DropHighlightBrush */

  /* DataGrid */
  --grid-row: #252526;
  --grid-row-alt: #2d2d30;
  --grid-hover: #3c3c3c;
  --grid-selected: var(--accent);
  --grid-header: #1e1e1e;

  /* File Icon Colors (1:1 aus FileEntry.IconColor) */
  --icon-folder: #DCB67A;
  --icon-image: #9B59B6;
  --icon-audio: #2ECC71;
  --icon-video: #E74C3C;
  --icon-pdf: #E74C3C;
  --icon-document: #3498DB;
  --icon-spreadsheet: #27AE60;
  --icon-archive: #E67E22;
  --icon-executable: #95A5A6;
  --icon-code: #F1C40F;
  --icon-default: #95A5A6;
}
```

---

## 13. Known Gaps (nach Phase 5)

Diese Features der WPF-App werden im Electron-Rewrite nicht / anders implementiert:

| Feature | Status |
|---|---|
| `shell32.dll` COM Thumbnails (IShellItemImageFactory) | Ersetzt durch `sharp` + `pdf-thumbnail`. Weniger Formate, aber cross-platform. |
| `System.Windows.Forms.FolderBrowserDialog` | Ersetzt durch Electron `dialog.showOpenDialog`. Moderner, aber kein Tree-View. |
| `WebBrowser` Control (embedded IE) | Ersetzt durch `<iframe>` oder PDF-Thumbnail. Kein ActiveX/IE. |
| Windows-spezifische Pfade (`C:\NSCE\FluxDB`) | Ersetzt durch `app.getPath('userData')`. Cross-Platform. |
| Go Installer (TUI) | Bleibt erhalten für Windows-NSIS. electron-builder für macOS `.dmg`, Linux `.AppImage`. |
| Go Log Viewer (TUI) | Wird durch integrierten React-Log-Viewer ersetzt (Settings-Dialog-Tab). |
| `Segoe MDL2 Assets` Icons | Ersetzt durch Lucide React Icons. Unicode-Entities entfallen. |
| Dual Go-Installer + WPF Release Pipeline | Vereinfacht: Single `electron-builder` Pipeline für alle Plattformen. |

---

## 14. Was der Rewrite löst (PLAN.md-Bugs)

| Bug (WPF) | Gelöst durch |
|---|---|
| `GetOrCreateTag` Race Condition | `INSERT OR IGNORE` in Transaktion (bereits gefixt in WPF, übernommen) |
| `SetTagsForFile` nicht atomar | Transaktion (bereits gefixt in WPF) |
| `RefreshSpecificFolderAsync` `_indexCancellation` Leak | `AbortController` wird automatisch via GC + `finally` disposed |
| `RefreshSpecificFolderAsync` setzt `_isIndexing` nicht | Zustand Store: `isIndexing` Guard in `useIndexer` Hook |
| `GetAllFiles()` in `RefreshCurrentFolderView` (Performance) | `GetFilesInFolder` mit `WHERE path LIKE` (bereits gefixt in WPF) |
| `SearchFiles` ohne `folderPath` (toter Code) | Nur eine Search-Methode mit `folderPath` |
| `LoggingService` Queue-Drain-Race | `Atomics` + `setImmediate` im Main Process |
| `Dispatcher.Invoke` Blockaden | IPC Events sind non-blocking (main → renderer) |
| `MainWindow.xaml.cs` 1840 Zeilen | Aufgeteilt in 8 Stores + 12 Hooks + 30 Komponenten |

---

## 15. MVP-Abgrenzung

**In Phase 1 (MVP) enthalten:**
- Folder auswählen, Indexing, File-Liste mit Virtual Scrolling
- Sortierung, Navigation, Suche
- Details Panel, Tags, Notizen
- Preview (Bilder, Text)
- Export (JSON, GZip)
- Dark Theme (1:1 WPF)

**Nicht im MVP (Phase 2+):**
- Copy/Cut/Paste/Delete/Rename
- Drag & Drop
- Context Menu
- Keyboard Shortcuts
- Settings, Auto-Update
- PDF Preview
- Image Zoom

**MVP-Ziel**: 2 Wochen → funktionierender File-Indexer mit UI, Release-fähig als Alpha.