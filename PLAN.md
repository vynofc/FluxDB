# PLAN.md — FluxDB WPF Application

## Bugs

### 1. Race Condition in `GetOrCreateTag`
**`Services/DatabaseService.cs:172-186`** — Zwei separate Queries (SELECT → INSERT) ohne Transaktion. Rufen zwei Threads gleichzeitig denselben Tag auf, crasht der zweite mit `UNIQUE constraint violation`.

**Fix**: `INSERT OR IGNORE INTO tags (name) VALUES (@n); SELECT id FROM tags WHERE name=@n;` in einer Transaktion.

### 2. `SetTagsForFile` nicht atomar
**`Services/DatabaseService.cs:188-206`** — DELETE + mehrere INSERTs ohne Transaktion. Bei einem Crash mittendrin hat die Datei nur einen Teil der Tags.

**Fix**: In Transaktion wrappen.

### 3. `RefreshSpecificFolderAsync` überschreibt `_indexCancellation` ohne Dispose
**`MainWindow.xaml.cs:1711`** — Erzeugt neuen `CancellationTokenSource`, ohne den alten zu disposen. Bei mehrfachem Aufruf → Leak.

**Fix**: `_indexCancellation?.Dispose()` vor Neuzuweisung.

### 4. `RefreshSpecificFolderAsync` setzt `_isIndexing` nicht
**`MainWindow.xaml.cs:1700-1732` vs `1594-1632`** — Anders als `StartIndexing` deaktiviert diese Methode keine Buttons und setzt `_isIndexing` nicht. Nutzer kann mehrere Indexierungen parallel starten.

**Fix**: Gleiche Guard-Logik wie `StartIndexing` einbauen.

### 5. `GetAllFiles()` in `RefreshCurrentFolderView` lädt ALLE Dateien
**`MainWindow.xaml.cs:1279`** — `GetAllFiles()` holt jede Datei aus der DB und filtert dann in-memory mit LINQ. Bei 100k+ Dateien wird das extrem langsam.

**Fix**: `GetFilesInFolder(folderPath)` mit `WHERE path LIKE @prefix%` in SQL statt LINQ-Filter.

### 6. `SearchFiles(string query)` (ohne folderPath) ist toter Code
**`Services/DatabaseService.cs:118-144`** — Diese Überladung wird nirgends aufgerufen. Die mit `folderPath` (Zeile 341) wird immer verwendet.

**Fix**: Entweder entfernen oder in der UI für Root-weite Suche verwenden.

### 7. `LoggingService` Queue-Drain-Race
**`Services/LoggingService.cs:59-96`** — Zwischen `_writeQueue.ToArray()` + `_writeQueue.Clear()` (Zeile 73-74) und dem Setzen von `_isProcessingQueue = false` (Zeile 70) kann ein `Log()`-Aufruf die Queue füllen, ohne dass ein neuer Worker gestartet wird. Logs gehen verloren.

**Fix**: `_isProcessingQueue` direkt nach `_writeQueue.Clear()` auf `false` setzen und danach prüfen, ob während des Drains neue Einträge hinzugekommen sind.

---

## Verbesserungen

| Bereich | Vorschlag |
|---|---|
| **DB-Performance** | `GetFilesInFolder(folderPath)` mit `WHERE path LIKE @prefix%` in SQL. Spart bei großen DBs massiv RAM und CPU. |
| **DB-Performance** | Periodisches `VACUUM` nach `MarkDeletedFiles` — SQLite gibt gelöschten Speicher nicht automatisch frei. |
| **UI-Responsiveness** | `AddFolderToIndex` (Zeile 1015) ruft `Directory.GetFiles` via `Dispatcher.BeginInvoke` auf dem UI-Thread auf. Bei großen Ordnern friert die UI ein. In `Task.Run` auslagern. |
| **File Watching** | `FileSystemWatcher` auf den Root-Ordner, um Änderungen in Echtzeit zu erkennen — statt manuelles Refresh. |
| **Thumbnails** | `GetShellThumbnail` (Zeile 1808) nutzt `SIIGBF_RESIZETOFIT` ohne `SIIGBF_THUMBNAILONLY` — liefert für manche Dateitypen Icons statt Vorschaubilder. |
| **Export** | `IndexExportItem` enthält kein `Extension`-Feld — Reimport müsste Extension aus Pfad parsen. |
| **Settings** | `Theme`-Property in `AppSettings` wird nie ausgewertet — nur Dark Theme existiert. |
| **Code-Struktur** | `MainWindow.xaml.cs` ist 1829 Zeilen lang. Services wie Clipboard, Drag&Drop, Preview könnten in eigene Partial-Klassen oder Services ausgelagert werden. |
| **Fehlerbehandlung** | Viele `catch { }` ohne Logging (z.B. Zeile 71, 100). `Debug.WriteLine` wird im Release-Build nicht ausgeführt. `LoggingService.Log` nutzen. |
| **`VersionHelper`** | `NormalizeVersion` und `CompareVersions` werden in `SplashWindow` (Zeile 121-129) als private Wrapper dupliziert — direkt `VersionHelper` aufrufen. |
| **`using System.Windows.Forms`** | In `RefreshDialog.xaml.cs:2` für `FolderBrowserDialog` — WPF hat kein natives Äquivalent, aber der Namespace-Import kollidiert konzeptionell. `Microsoft.Win32.OpenFileDialog` mit `ValidateNames = false` wäre eine Alternative. |