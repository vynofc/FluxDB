# FluxDB WPF Improvement-Report

**Datum:** 2026-08-14
**Scope:** Komplette WPF-App unter `WPF/FluxDB/` (Code-Review, statisch)
**Methode:** Vollständiges Durchlesen aller `.cs` und `.xaml` Dateien (Services, Views, ViewModels, Models, Converters, Dialoge)
**Begleitdokument:** `report_0198d6a2-7f3b-7c1e-9a45-a45a4adb4a48.md` (Bug-Report)

---

## Architektur & Struktur

### A1 — MVVM-Schicht entweder aktivieren oder entfernen
**Dateien:** `WPF/FluxDB/ViewModels/*`, `WPF/FluxDB/Views/Pages/*`, `WPF/FluxDB/Controls/PreviewPanel.*`

`MainViewModel`, `NavigationViewModel`, `DashboardViewModel`, `SettingsViewModel`, `DashboardPage`, `FileBrowserPage` und `PreviewPanel` sind vollständig implementiert, werden aber vom tatsächlichen Startup-Pfad nie instanziiert — `MainWindow` macht alles im Code-Behind (~2100 Zeilen). Das ist die größte strukturelle Schuld des Projekts: doppelte Implementierungen driften auseinander (z. B. hat `MainViewModel.Paste` eigene Bugs, der Filter "Tags" existiert nur im Code-Behind-`MatchesFilter`, nicht im ViewModel).

**Empfehlung:** Entscheidung treffen und konsequent umsetzen:
- **Option A (pragmatisch):** ViewModels/Pages/Controls löschen, Code-Behind als offizielles Muster dokumentieren. Weniger Code, keine Drift.
- **Option B (sauber):** `MainWindow` schrittweise auf `MainViewModel` umstellen (DataContext setzen, Event-Handler durch Commands ersetzen). Höherer Aufwand, aber testbar.

### A2 — Dependency Injection einführen (oder zumindest Service-Lifetimes klären)
Services (`SettingsService`, `DatabaseService`, `IndexerService`) werden per `new` verstreut erstellt — teils pro Aufruf (`new SettingsService()` in `IndexerService.ScanFolderAsync` und `LoggingService.GetMaxBufferLines`), teils als Feld. Ein leichtgewichtiger `ServiceProvider` (Microsoft.Extensions.DependencyInjection) oder zumindest eine zentrale `AppServices`-Klasse würde Instanz-Anzahl, Testbarkeit und den Ordnerwechsel-Lebenszyklus (dispose/re-create) sauber kapseln.

### A3 — `MainWindow.xaml.cs` aufteilen
2100 Zeilen in einer Datei: Navigation, Clipboard, Preview, Tags, Indexing, Drag&Drop, Spalten-Persistenz, Shell-COM. In Partial Classes oder Controller-Klassen auslagern:
- `MainWindow.Navigation.cs` (History, Breadcrumbs, Up/Back/Forward)
- `MainWindow.Clipboard.cs` (Copy/Cut/Paste/Delete/Rename)
- `MainWindow.Preview.cs` (Image/Text/PDF)
- `MainWindow.Indexing.cs` (Scan, Progress, Cancel)

### A4 — Harte UI-Farben zentralisieren
`RefreshDialog.xaml`/`RenameDialog.xaml` nutzen harte Hex-Farben (`#1e1e1e`, `#0078d4`), `MainWindow.xaml.cs` erzeugt Breadcrumb-Buttons mit `Color.FromRgb(0,120,212)` und Tag-Farben als String-Array im Code. In Light/High-Contrast bricht das. Empfehlung: `DynamicResource`-Brushes überall; Dialoge auf `ui:FluentWindow` umstellen; Tag-Farbpalette als ResourceDictionary.

### A5 — Tote Assets und Configs aufräumen
- `WPF/FluxDB/packages/` (alte NuPkg-Artefakte, falsche SQLite-Version 1.0.118) aus dem Repo entfernen und `.gitignore` ergänzen.
- `App.config`/`appsettings.json` werden nicht gelesen — entfernen oder nutzen.
- `GetShellThumbnail` + P/Invoke-Block in `MainWindow.xaml.cs` ist toter Code.
- `Properties/Settings.Designer.cs`/`Resources.Designer.cs` prüfen — wenn ungenutzt, entfernen.
- Root-`FluxDB/`-Ordner mit alten Icon-Artefakten dokumentiert lassen oder löschen.

---

## Performance

### P1 — `GetFilesInFolder` SQL-seitig auf direkte Kinder filtern
**Datei:** `WPF/FluxDB/Services/DatabaseService.cs:135-161`, Aufrufer `MainWindow.xaml.cs:1804`

Aktuell lädt die Query **alle** Dateien rekursiv unter dem Ordner und das UI filtert mit `Path.GetDirectoryName(f.Path) == currentFolder`. Bei 100k+ Dateien im Baum: voller Speicher-Transfer pro Ordnerwechsel. Besser in SQL:
```sql
WHERE f.deleted = 0 AND f.path LIKE @prefix
  AND INSTR(SUBSTR(f.path, @prefixLen), '\') = 0
```
(oder Depth-Spalte/relative Pfad-Spalte). Ergibt 10–100x schnellere Ordnerwechsel bei großen Roots.

### P2 — Dev-Settings cachen statt pro Aufruf `settings.json` lesen
**Dateien:** `WPF/FluxDB/Services/SettingsService.cs:116-143`, `WPF/FluxDB/Services/LoggingService.cs:49-60`

`GetDevSettingInt` lädt bei **jedem** Aufruf die komplette JSON-Datei — und `LoggingService.Log()` ruft es bei jeder Log-Zeile auf (Hot Path!). Zusätzlich wird dabei jedes Mal `new SettingsService()` erstellt (Verzeichnis-Check inklusive). Empfehlung: `SettingsService` cached die Settings in-memory mit `FileSystemWatcher`- oder Zeitstempel-Invalidierung, `LoggingService` liest `log.buffer.lines` einmalig lazy.

### P3 — Doppelte Enumeration beim Indexing eliminieren
**Datei:** `WPF/FluxDB/Services/IndexerService.cs:47-59`

Phase 1 zählt alle Dateien (kompletter FS-Walk), Phase 2 walkt nochmal zum Indizieren. Für Fortschrittsanzeige stattdessen: Totalschätzung progressiv erhöhen (`TotalFiles = Math.Max(TotalFiles, processed)`), oder Zählen optional machen. Halbiert die I/O-Zeit bei großen Bäumen.

### P4 — DB-Schema: Foreign Keys + Indizes für Suche
**Datei:** `WPF/FluxDB/Services/DatabaseService.cs:62-81`

- `file_tags`/`notes` haben keine FK-Constraints auf `files(id)` — verwaiste Zeilen möglich (`PRAGMA foreign_keys=ON`).
- Suche nutzt `LIKE '%...%'` — für große DBs FTS5-Tabelle (`files_fts` auf `name`) in Betracht ziehen; SQLite bringt FTS5 mit.
- `files(path)` hat UNIQUE → impliziter Index, gut. Zusätzlich sinnvoll: Index auf `(deleted, path)` für die häufige `GetFilesInFolder`-Query.
- `idx_files_extension` wird nie genutzt (Filter passiert im UI) — entweder SQL-Filterung oder Index droppen.

### P5 — Preview: Thumbnails cachen + Shell-Thumbnails nutzen
**Datei:** `WPF/FluxDB/Views/MainWindow.xaml.cs:642-775`

Jedes Selektieren dekodiert das Bild neu (400px Decode) und Textdateien werden komplett gelesen (erst danach auf 5000 Zeichen gekürzt). Empfehlung:
- `LruCache<string, BitmapSource>` (z. B. 50 Einträge) für Bilder.
- Text: nur die ersten N Zeichen lesen (`reader.Read(buffer, 0, maxChars)`) statt `ReadToEnd` + Substring — bei 100 MB Logs ein großer Unterschied.
- Den vorhandenen (toten) `GetShellThumbnail`-Code aktivieren für Video/PDF/EXE-Thumbnails.

### P6 — DataGrid: CollectionView statt ItemsSource-Ersetzung
`RefreshCurrentFolderViewAsync` ersetzt `dgFiles.ItemsSource` komplett bei jeder Änderung → voller Re-Render, Scroll-Position und Selektion gehen verloren. `ObservableCollection<FileEntry>` + gezielte Add/Remove-Operationen (oder zumindest `view.Refresh()`) erhält den UI-Zustand. Sortierung aktuell über `CollectionViewSource` — mit stabiler Collection würde Sort/Filter direkt auf der View laufen.

### P7 — `BitmapImage` erzeugen ist wiederholbarer Code
Vier Stellen (`SplashWindow`, `MainWindow` Icon, Preview x2) mit identischem BeginInit/CacheOption/Freeze-Muster. Ein `ImageHelper.LoadBitmap(path, decodeWidth)`-Helper zentralisiert das (inkl. konsistentem Fehlerverhalten).

### P8 — Settings-Speicherung entprellen
`SaveFilterForFolder`, `SaveSortForFolder`, `SaveColumnVisibility`, `SaveLastViewFolder` schreiben bei **jeder** Interaktion die komplette `settings.json` synchron auf Disk. Debounce (z. B. 500 ms Timer) oder Save-on-Exit + Save-on-Change-Threshold reduziert I/O drastisch. Auch `lock` für Thread-Safety (siehe Bug #28).

---

## Robustheit & Code-Qualität

### Q1 — Exceptions zentral protokollieren statt MessageBox-Streu
Über 20 `MessageBox.Show("...")` in Handlern, dazu viele leise `catch { }`. Ein zentraler `ErrorHandler.Report(ex, userMessage)` (loggt + Toast statt modalem Dialog) würde die UX verbessern und das Logging vervollständigen. Mindestens: leere `catch {}`-Blöcke mit `LoggingService.LogDebug` füllen (z. B. `IndexerService.IsHiddenOrSystem`, `DatabaseService` WAL-Cleanup).

### Q2 — `async void` Handler absichern
Alle Event-Handler sind `async void` (`PasteFiles`, `DeleteSelectedFiles`, `ShowRefreshDialog`, `BtnExport_Click`...) — Exceptions darin crashen die App. `App.xaml.cs` sollte `DispatcherUnhandledException` + `TaskScheduler.UnobservedTaskException` registrieren und loggen. Aktuell fehlt beides komplett.

### Q3 — Pfad-Vergleiche zentralisieren
An ~10 Stellen werden Pfade mit `StartsWith`/`==`/TrimEnd verglichen, mal case-sensitiv, mal nicht. Ein `PathHelper` (`PathsEqual`, `IsUnder`, `NormalizeTrailingSlash`) eliminiert eine ganze Bug-Klasse (siehe Bug-Report #23, #43).

### Q4 — SQL-Strings als Konstanten/Prepared Statements
SQL ist als Inline-Strings verstreut. Spaltennamen-Mapping in `MapFileEntry` per `GetOrdinal` statt harter Indizes (Bug #32). Erwägung: Mini-Query-Builder oder Dapper (würde das Mapping komplett übernehmen) — aktuell ~450 Zeilen handgeschriebenes ADO.NET.

### Q5 — `FileEntry`-Cache-Design vereinfachen
`FileEntry` hat manuellen Cache mit `_cacheValid`-Flag und MVVMTK0034-Warnungen (unterdrückt per NoWarn). Alternativ: computed Properties ohne Cache (Icon-Lookup ist ein Dictionary-Zugriff, Size-Formatierung trivial — der Cache spielt kaum eine Rolle, kostet aber Komplexität und Warnungen). Oder: Cache korrekt über die generierten Properties statt Backing Fields.

### Q6 — Versionslogik in Build verschieben
`version.txt` wird zur Laufzeit gelesen **und** per MSBuild-Target in `AssemblyVersion.cs` generiert (die dann bei jedem Build dirty wird, Bug #44). Sauber: Target schreibt nach `obj/`, `<Compile Include>` darauf, `version.txt` nur noch als Source-of-Truth im Repo; Laufzeit-Fallback über `AssemblyInformationalVersion` reicht.

### Q7 — Installer-Download-Logik deduplizieren
`SplashWindow.DownloadInstallerAsync` (mit SHA256) und `SettingsWindow.DownloadInstallerAsync` (ohne SHA256) sind fast identisch. In `UpdateService` auslagern (Fetch Releases, Download+Verify, Start Installer) — beide Fenster nutzen dann denselben getesteten Pfad.

### Q8 — Cancellation sauber durchziehen
- `DeleteSelectedFiles`/`CopyOrMoveFilesAsync` haben kein CancellationToken — bei langen Copy-Jobs kann das Fenster nicht sauber schließen.
- `webPdfPreview.EnsureCoreWebView2Async()` ohne Token.
- `OnClosed` sollte zusätzlich einen Timeout haben, bevor die App auf laufende Tasks wartet (oder sie hart verwirft).

---

## UX & Features

### U1 — Tag-Autocomplete fertigbauen
UI existiert (`tagAutocompletePopup`, `tagAutocompleteList`), aber es gibt keinen Befüll-Mechanismus und keine `GetAllTags()`-Query. Umsetzung: `SELECT name FROM tags ORDER BY name` + `TextChanged` auf `txtTagInput` mit Prefix-Filter. Zusätzlich Tag-Vorschläge beim Batch-Dialog.

### U2 — Spalten "Tags" sortierbar machen + Tags-Spaltenfilter
`Tags` ist die einzige nicht sortierbare Spalte (`CanUserSort="False"`). Da `TagsText` ein String ist, wäre `SortMemberPath="TagsText"` trivial. Ergänzend: Filter-ComboBox um konkrete Tags erweitern (Dropdown mit allen vorhandenen Tags).

### U3 — Suche: Ergebnis-Highlighting + Scope-Wahl
- Treffer-Begriff im Namen hervorheben (DataGrid CellTemplate mit Converter).
- Such-Scope umschaltbar: "aktueller Ordner" vs. "gesamter Index" (aktuell immer ab View-Ordner, nicht offensichtlich).
- Suche nach Tags mit Prefix-Syntax (`tag:rechnung`), nach Größe (`size:>10MB`) — die DB kann das alles schon.

### U4 — Paper-Cuts im Dateibetrieb
- **F5** aktualisiert nur die Ansicht (RefreshDialog), nicht das Dateisystem diff — ein "Quick Refresh" (nur geänderte Dateien per Timestamp-Vergleich) wäre deutlich schneller als Vollscan.
- **Undo** für Delete (in Papierkorb verschieben statt `File.Delete` — `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(..., RecycleOption.SendToRecycleBin)` oder `SHFileOperation`). Aktuell ist Delete endgültig — risikoreich.
- Copy/Paste-Fortschrittsdialog bei großen Ordnern (aktuell läuft es still, UI wirkt eingefroren trotz async).
- Doppelklick-Verhalten konfigurierbar (Öffnen vs. Vorschau).

### U5 — Statusleiste mit echten Informationen
Aktuell nur Text. Nützlich wären: Index-Alter ("Indexiert vor 3 Tagen — F5 zum Aktualisieren"), DB-Größe, Anzahl Tags/Notizen im Index, Datei-Summen der aktuellen Ansicht.

### U6 — Breadcrumbs: Adressleisten-Modus
Breadcrumb-Leiste per Klick in editierbares Textfeld verwandeln (Explorer-Style), mit Pfad-Validierung und Autocomplete. Aktuell ist der Pfad nur lesbar in `txtCurrentFolder` angezeigt — Copy des Pfads ist nur im Detail-Panel möglich.

### U7 — Tastatur-Navigation im DataGrid vervollständigen
- Type-ahead (Buchstaben springen zur Datei) — DataGrid kann das nicht out-of-the-box.
- `Strg+A` für Alle auswählen geht nur über DataGrid-Fokus; explizit abfangen.
- Pfeil-hoch auf erstem Eintrag → Fokus zurück auf Search.
- Kontextmenü-Taste (Menu-Key) öffnet das ContextMenu nicht (DataGrid-Standardverhalten prüfen).

### U8 — Mehrere Root-Ordner / Workspaces
Aktuell ein Root pro App-Instanz, DB liegt im Root. Für Vergleiche/Sammlungen wäre ein "Workspace"-Konzept (mehrere Roots in einer Sidebar, Schnellwechsel) sinnvoll. Die Service-Architektur (DB pro Root) unterstützt das bereits — es fehlt nur die UI.

### U9 — Einstellungen: Export/Import der App-Settings
Index kann exportiert werden, `settings.json` (Theme, Persistence, DevSettings, Recent Folders) nicht. Ein "Export/Import Settings"-Button im Settings-Fenster wäre konsistent.

### U10 — Cheat-Sheet automatisch beim ersten Start zeigen
`Ctrl+K` Overlay ist gut versteckt. Einmalig beim ersten Start (Flag in settings.json) anzeigen erhöht die Discoverability der Shortcuts massiv.

### U11 — Splash: Fortschritt anzeigen statt statischer ProgressBar
`pb` im Splash ist `IsIndeterminate="False"` mit Value 0 — wirkt kaputt. Entweder `IsIndeterminate="True"` oder echte Schritte (Settings laden → MainWindow bauen → Update-Check) als Prozent.

### U12 — Preview für mehr Dateitypen
- Audio/Video über `MediaElement` (play/pause im Preview-Panel).
- Markdown-Rendering (z. B. Markdig → HTML → WebView2, oder einfach Syntax-Highlighting im Text).
- Hex-View für Binärdateien (erste 512 Bytes).
- SVG aktuell in `ImageExtensions`, aber `BitmapImage` kann kein SVG → landet im "Cannot load image"-Zweig. Entweder aus der Liste nehmen oder SvgImage-Package nutzen.

---

## Sicherheit & Wartung

### S1 — Update-Signaturprüfung vereinheitlichen
SHA256-Check nur im Splash-Pfad, nicht im Settings-Pfad (Bug #19). Beim Ausbau zu `UpdateService` (Q7) die Verifikation verpflichtend machen (kein "proceed without verification" bei neuen Releases — nur Legacy-Ausnahme).

### S2 — `GROUP_CONCAT`+NUL-Trenner dokumentiert lassen, aber Tag-Namen normalisieren
Tags werden lowercase-getrimmt gespeichert (`GetOrCreateTagInTransaction`), aber die UI zeigt sie wie eingegeben — doppelte Anzeige-Varianten ("Rechnung" vs "rechnung") verwirren. Einheitlich beim Speichern/Anzeigen.

### S3 — Logging: Rotation + Level
`logs.txt` wächst unbegrenzt (nur der In-Memory-Buffer ist begrenzt). Tägliche Dateien oder Größen-Rotation (z. B. 5 MB, 3 Archive). Ebenso Log-Level (Info/Warn/Error) statt allem außer Debug.

### S4 — Telemetriefreie Crash-Berichte
Bei `DispatcherUnhandledException` (Q2) optional "Absturzbericht kopieren" (Stack + letzte 50 Log-Zeilen in Zwischenablage) — passt zum vorhandenen "Report a Bug"-Button.

### S5 — .editorconfig + Analyzer
Projekt hat keine Formatierungs-Policy. `.editorconfig` + `Microsoft.CodeAnalysis.NetAnalyzers` (schon teilweise adressiert via NoWarn) würden Konsistenz erzwingen. MVVMTK0034 aktuell global unterdrückt — besser gezielt fixen.

### S6 — Tests einführen (testbarer Kern zuerst)
Keine Test-Suite vorhanden. Gute erste Kandidaten ohne UI:
- `VersionHelper` (reine Funktionen, hohe Bug-Dichte-Historie)
- `DatabaseService` (SQLite in-memory, CRUD, Tag-Roundtrip mit NUL-Separator, UpdateFolderPath-Ecke)
- `ImportService`/`ExportService` Roundtrip
- `PathHelper` (nach Q3)

---

## Zusammenfassung nach Kategorie

| Kategorie | Anzahl | Top-Items |
|---|---|---|
| Architektur | 5 | A1 MVVM-Entscheidung, A3 MainWindow aufteilen |
| Performance | 8 | P1 SQL-Filterung, P2 Settings-Cache, P3 Single-Pass-Indexing |
| Robustheit | 8 | Q2 globale Exception-Handler, Q3 PathHelper, Q7 UpdateService |
| UX & Features | 12 | U1 Tag-Autocomplete, U4 Papierkorb-Delete, U12 Media-Preview |
| Sicherheit/Wartung | 6 | S3 Log-Rotation, S6 Tests für VersionHelper/DatabaseService |

## Empfohlene Reihenfolge (Quick Wins zuerst)

1. **P2** Settings/DevSettings cachen — 30 Minuten, sofort spürbar.
2. **Q2** Globale Exception-Handler — 20 Zeilen, verhindert stille Crashes.
3. **U1** Tag-Autocomplete fertigstellen — UI existiert schon.
4. **P1** `GetFilesInFolder` SQL-Filter — größter Performance-Hebel.
5. **Q7 + S1** `UpdateService` konsolidieren — behebt nebenbei Bug #19.
6. **A1** MVVM-Entscheidung treffen — strategisch, verhindert weitere Drift.
7. **U4** Delete → Papierkorb — eine Zeile mit `Microsoft.VisualBasic.FileIO`, großer Sicherheitsgewinn für Nutzer.
