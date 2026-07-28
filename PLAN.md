# FluxDB — Fehleranalyse & Korrekturplan

---

## 🔴 Kritische Fehler

### 1. IndexerService: Batch-Commit kann Datenverlust verursachen
**Datei:** `Services/IndexerService.cs:95-109`

```csharp
if (processed % BatchSize == 0)
{
    try
    {
        currentTransaction.Commit();
    }
    catch
    {
        currentTransaction.Rollback();
    }
    finally
    {
        currentTransaction.Dispose();
    }
    currentTransaction = _database.BeginTransaction();
}
```

Wenn `Commit()` fehlschlägt (z.B. DB-Lock), wird `Rollback()` aufgerufen und der gesamte Batch von 1000 Dateien unwiderruflich verworfen — ohne Retry. Alle bereits committeten Batches bleiben, der aktuelle Batch ist verloren. Die Datenbank ist danach inkonsistent (einige Dateien fehlen).

**Fix:** Retry-Logik für `Commit()` mit Exponential Backoff einbauen; nach mehrfachem Scheitern abbrechen und Fehler melden.

---

### 2. IndexerService: Bei Abbruch wird letzte Transaktion nicht explizit zurückgerollt
**Datei:** `Services/IndexerService.cs:125-135`

```csharp
if (!result.Cancelled)
{
    try
    {
        currentTransaction.Commit();
    }
    catch
    {
        currentTransaction.Rollback();
    }
}
```

Wenn `result.Cancelled == true`, wird `currentTransaction` weder committet noch gerollbackt — nur im `finally` disposed. Das Verlassen auf implizites Rollback via `Dispose()` ist fragil und implementationsabhängig.

**Fix:** Bei `Cancelled`-State explizit `Rollback()` aufrufen, bevor die Transaktion disposed wird.

---

### 3. MainWindow: Ordner löschen entfernt keine DB-Einträge
**Datei:** `MainWindow.xaml.cs:327-329`

```csharp
if (item.IsFolder && Directory.Exists(item.Path))
{
    Directory.Delete(item.Path, true);
    deletedCount++;
}
```

Dateien im gelöschten Ordner verbleiben mit `deleted=0` in der Datenbank. Sie erscheinen weiterhin in Suchergebnissen und Exporten, obwohl sie physisch nicht mehr existieren.

**Fix:** Vor dem Löschen rekursiv alle Dateien unterhalb des Ordnerpfads in der DB auf `deleted=1` setzen, oder eine `MarkPathAsDeleted`-Methode in `DatabaseService` ergänzen.

---

### 4. MainWindow: Ordner umbenennen aktualisiert die DB nicht
**Datei:** `MainWindow.xaml.cs:373-376`

```csharp
if (selected.IsFolder)
{
    Directory.Move(selected.Path, newPath);
}
```

Beim Umbenennen eines Ordners via `Directory.Move` werden Pfade aller enthaltenen Dateien in der DB nicht aktualisiert — sie zeigen danach ins Leere.

**Fix:** Nach `Directory.Move` alle DB-Einträge mit Pfad-Präfix `selected.Path` auf das neue Präfix `newPath` umschreiben (`UpdateFolderPath`-Methode in `DatabaseService`).

---

## 🟠 Logikfehler

### 5. MainWindow: Suche ignoriert aktuellen Ordner
**Datei:** `MainWindow.xaml.cs:1526`

```csharp
List<FileEntry> results = _databaseService.SearchFiles(query);
```

`SearchFiles` durchsucht die gesamte DB, nicht nur den aktuell angezeigten Ordner. Nach einer Suche sieht der Nutzer Dateien aus dem gesamten Index statt aus dem aktuellen Verzeichnis — die Navigation wird ad absurdum geführt.

**Fix:** `SearchFiles` um einen optionalen `folderPath`-Parameter erweitern, der die Ergebnisse auf Dateien unterhalb dieses Pfads einschränkt.

---

### 6. MainWindow: Dispatcher.Invoke blockiert Background-Thread
**Datei:** `MainWindow.xaml.cs:961, 980`

```csharp
await Task.Run(() =>
{
    // ...
    Dispatcher.Invoke(() => AddFileToIndex(targetPath));  // synchron!
});
```

Innerhalb von `Task.Run` wird `Dispatcher.Invoke` (synchron) verwendet — der Background-Thread wartet auf den UI-Thread, was den Zweck der Hintergrundarbeit zunichtemacht und Deadlocks riskiert.

**Fix:** `Dispatcher.Invoke` durch `Dispatcher.BeginInvoke` ersetzen, oder die Indexierung nach `Task.Run` im UI-Kontext ausführen.

---

### 7. MainWindow: RefreshSpecificFolderAsync ändert Root-Ordner implizit
**Datei:** `MainWindow.xaml.cs:1726-1731`

```csharp
private async Task RefreshSpecificFolderAsync(string folder)
{
    if (_indexerService == null || _databaseService == null)
    {
        InitializeDatabaseForFolder(folder);
    }
    // ...
}
```

Wenn `_indexerService == null` (z.B. nach Programmstart ohne initialen Root-Ordner), wird `InitializeDatabaseForFolder(folder)` aufgerufen, was `_currentRootFolder` und die DB-Verbindung für einen möglicherweise anderen Ordner neu initialisiert. Der ursprüngliche Root-Ordner geht verloren.

**Fix:** `_currentRootFolder` nur setzen, wenn er noch nicht gesetzt ist. `InitializeDatabaseForFolder` von der Root-Ordner-Logik entkoppeln.

---

### 8. DatabaseService: TagsText-Split trennt fälschlich an Komma
**Datei:** `Services/DatabaseService.cs:96-99`

```csharp
f.Tags = new List<string>(f.TagsText.Split(new[] { ", " }, StringSplitOptions.None));
```

`GROUP_CONCAT(t.name, ', ')` in SQL und anschließendes Split an `", "` ist eine verlustbehaftete Roundtrip-Konvertierung. Tag-Namen mit Komma (z.B. `"Last, First"`) werden fälschlich aufgesplittet.

**Fix:** Einen eindeutigeren Separator verwenden (z.B. `\0` oder `|`), oder Tags als separate Query laden statt über `GROUP_CONCAT`.

---

### 9. LoggingService: Exception in Write-Thread tötet Log-Persistenz
**Datei:** `Services/LoggingService.cs:59-86`

```csharp
private static void ProcessWriteQueue(object state)
{
    while (true)
    {
        // ...
        try
        {
            File.AppendAllLines(_logFilePath, linesToWrite, Encoding.UTF8);
        }
        catch
        {
            // swallow file write errors
        }
        Thread.Sleep(100);
    }
}
```

Die `while(true)`-Schleife hat keinen äußeren try-catch. Eine unerwartete Exception (z.B. `NullReferenceException` durch Race-Condition beim Zugriff auf `_writeQueue`) beendet den Thread — danach werden keine Logs mehr auf die Platte geschrieben.

**Fix:** Äußeren try-catch um die gesamte `while`-Schleife legen; bei unerwarteter Exception neu starten.

---

## 🟡 Kleinere Probleme

### 10. SQL-String-Verkettung
**Datei:** `Services/DatabaseService.cs:66`

```csharp
WHERE " + (includeDeleted ? "1=1" : "f.deleted=0") + @"
```

Boolean direkt in SQL konkateniert statt parametrisiert. Aktuell nicht exploitable (da `includeDeleted` ein hardcodierter Boolean ist), aber unsauber und fehleranfällig bei zukünftigen Änderungen.

**Fix:** Parameterisieren oder als zwei separate Queries aufteilen.

---

### 11. Hartcodierte Windows-Pfade
**Datei:** `App.xaml.cs:47, 62`

```csharp
var centralFile = "C:\\NSCE\\FluxDB\\version.txt";
var centralDir = "C:\\NSCE\\FluxDB";
```

Schlägt auf Nicht-Windows-Systemen oder bei abweichender Installation fehl. Bietet keine `try-catch`-Absicherung für den Fall, dass `C:\NSCE` nicht existiert.

**Fix:** Pfade konfigurierbar machen oder aus einer Umgebungsvariable / AppConfig lesen.

---

### 12. Code-Duplizierung: Versionsvergleich
**Dateien:** `App.xaml.cs:101-138` und `SplashWindow.xaml.cs:151-190`

`NormalizeVersion`/`CompareVersions` sind in beiden Dateien identisch dupliziert. Änderungen müssen an zwei Stellen vorgenommen werden.

**Fix:** In eine gemeinsame Utility-Klasse auslagern (z.B. `VersionHelper`).

---

### 13. Upload-Funktionalität: Toter Code
**Datei:** `Services/LicenseService.cs:206, 289-308`

```csharp
if (IsUploadAllowed(features))
{
    _ = Task.Run(() => UploadAllIndexesIfNeededAsync(licenseKey));
}
```

`UploadAllIndexesIfNeededAsync` wird aufgerufen, tut aber nichts außer `"Upload disabled"` zu loggen. Die Aufrufstelle in `CheckLicenseAsync` (Zeile 206) und die gesamte Upload-Methodenkette wurden nicht bereinigt.

**Fix:** Entweder Upload-Logik entfernen oder vollständig implementieren. Aktueller Zustand: toter Code, der Ressourcen verschwendet und Verwirrung stiftet.

---

### 14. AddFolderToIndex: Keine Transaktion
**Datei:** `MainWindow.xaml.cs:1034-1038`

```csharp
foreach (var file in files)
{
    AddFileToIndex(file);
}
```

Jeder `UpsertFile`-Aufruf läuft ohne Transaktion einzeln — für große Ordner extrem langsam (pro Datei ein Commit).

**Fix:** `AddFolderToIndex` in eine Transaktion wrappen oder `BeginTransaction`/`Commit`-Zyklus verwenden.

---

### 15. DatabaseService.InitDb: Keine Transaktion
**Datei:** `Services/DatabaseService.cs:23-31`

```csharp
var sql = @"CREATE TABLE IF NOT EXISTS files (...);
CREATE TABLE IF NOT EXISTS tags (...);
CREATE TABLE IF NOT EXISTS file_tags (...);
CREATE TABLE IF NOT EXISTS notes (...);";
using (var cmd = new SQLiteCommand(sql, _connection)) { cmd.ExecuteNonQuery(); }
```

Mehrere `CREATE TABLE`-Statements ohne Transaktion. Wenn das dritte Statement fehlschlägt, bleiben die ersten beiden bestehen — partielle DB-Initialisierung.

**Fix:** `InitDb` in eine Transaktion wrappen.

---

## Zusammenfassung

| Schweregrad | Anzahl | Bereiche |
|---|---|---|
| 🔴 Kritisch | 4 | IndexerService, MainWindow |
| 🟠 Logikfehler | 5 | MainWindow, DatabaseService, LoggingService |
| 🟡 Klein | 6 | DatabaseService, App, LicenseService, MainWindow |

**Top-Priorität:** Fehler 1 (Datenverlust), 3 (verwaiste DB-Einträge), 4 (kaputte Pfade nach Umbenennung).

---

## 🧹 Refactoring: Lizenzsystem entfernen

Das gesamte Lizenzprüfungs- und Lizenzverwaltungssystem soll entfernt werden. Das Programm soll ohne Lizenzzwang, ohne Online-Prüfung und ohne Upload-Funktionalität funktionieren.

### Betroffene Komponenten

| Komponente | Datei | Aktion |
|---|---|---|
| `LicenseService` | `Services/LicenseService.cs` | Komplett löschen |
| `LicenseInfo` / `LicenseCheckRequest` / `LicenseCheckResponse` | `Models/LicenseInfo.cs` | Komplett löschen |
| `AppSettings`-Lizenzfelder | `Models/AppSettings.cs` | `LicenseKey`, `LastLicenseCheck`, `LicenseValid`, `LicenseExpiresAt`, `LicenseFeatures`, `IsAutoGeneratedFreeLicense`, `UploadedIndexHashes` entfernen |
| `MainWindow`-Lizenz-UI | `MainWindow.xaml` / `MainWindow.xaml.cs` | Lizenz-Indikator, Status-Text, `UpdateLicenseStatus()`, `UpdateLicenseUI()`, `_licenseService`-Feld und alle Referenzen entfernen |
| `SettingsWindow`-Lizenz-Tab | `SettingsWindow.xaml` / `SettingsWindow.xaml.cs` | Lizenz-Tab (Activate, Clear, Device ID, Status) komplett entfernen; `_licenseService`-Parameter aus Konstruktor entfernen |
| `SplashWindow`-Lizenz-Check | `SplashWindow.xaml.cs` | `EnsureFreeLicenseAsync()`-Aufruf und `_licenseService`-Feld entfernen |
| `ExportService`-DeviceId | `Services/ExportService.cs` | `GetDeviceId()` entfernen oder durch statische ID ersetzen |
| Upload-Funktionalität + Endpunkte | `Services/LicenseService.cs` | Entfällt mit Löschung des `LicenseService` — die Endpunkte `https://fluxdb.nsce.fr/api/index/upload` und `https://fluxdb.nsce.fr/api/license/verify` werden komplett entfernt, inklusive aller dahinterstehenden Systeme: `HttpClient`, `_uploadEndpoint`/`_licenseEndpoint`-Felder, `UploadIndexAsync()`, `UploadAllIndexesIfNeededAsync()`, `UploadAllIndexesNowAsync()`, `TriggerUploadIfAllowed()`, `IsUploadAllowed()`, `SafeEnumerateFiles()`, `UploadStatusChanged`-Event, `SetExportService()`-Methode — alles fliegt raus |
| `FluxDB.csproj` | Projektdatei | `System.Net.Http`-Referenz prüfen (wird ggf. nur noch für Update-Check benötigt) |

### Konkrete Schritte

1. **`Models/LicenseInfo.cs`** löschen
2. **`Services/LicenseService.cs`** löschen
3. **`Models/AppSettings.cs`** — alle Lizenz-bezogenen Properties entfernen:
   - `LicenseKey`
   - `LastLicenseCheck`
   - `LicenseValid`
   - `LicenseExpiresAt`
   - `LicenseFeatures`
   - `IsAutoGeneratedFreeLicense`
   - `UploadedIndexHashes`
4. **`Services/ExportService.cs`** — `GetDeviceId()` vereinfachen (feste ID oder Guid)
5. **`MainWindow.xaml.cs`** — `_licenseService`-Feld, `InitializeServices()`-Abschnitt, `InitializeDatabaseForFolder()`-Upload-Trigger, `UpdateLicenseStatus()`, `UpdateLicenseUI()` entfernen
6. **`MainWindow.xaml`** — Lizenz-Indikator (`licenseIndicator`, `txtLicenseStatus`) und ggf. Settings-Button-Anpassung
7. **`SettingsWindow.xaml.cs`** — `_licenseService`-Parameter aus Konstruktor, `LoadLicenseInfo()`, `UpdateLicenseStatusDisplay()`, `BtnActivate_Click`, `BtnClearLicense_Click`, `BtnCopyDeviceId_Click` entfernen
8. **`SettingsWindow.xaml`** — Lizenz-Tab / Lizenz-UI-Elemente entfernen
9. **`SplashWindow.xaml.cs`** — `_licenseService`-Feld, `EnsureFreeLicenseAsync()`-Aufruf, Upload-Trigger entfernen
10. **`App.xaml`** — `LicenseService`-Import ggf. bereinigen
11. **Kompilierung prüfen** und sicherstellen, dass keine verwaisten Referenzen übrig bleiben

### Downstream-Abhängigkeiten — ebenfalls zu entfernen

| Ort | Was entfernt wird |
|---|---|
| `MainWindow.xaml.cs:82-92` | `_licenseService`-Initialisierung, `UploadStatusChanged`-Event-Handler |
| `MainWindow.xaml.cs:105` | `_licenseService?.SetExportService(_exportService)` |
| `MainWindow.xaml.cs:114-119` | `_licenseService?.TriggerUploadIfAllowed()` |
| `MainWindow.xaml.cs:1351-1382` | `UpdateLicenseStatus()` und `UpdateLicenseUI()` — komplett |
| `MainWindow.xaml.cs:1486` | `UpdateLicenseStatus()`-Aufruf in `BtnSettings_Click` |
| `MainWindow.xaml` | `licenseIndicator`-Ellipse, `txtLicenseStatus`-TextBlock, Lizenz-Statusleiste |
| `MainWindow.xaml.cs:158` | `UpdateLicenseStatus()`-Aufruf in `LoadInitialData()` |
| `SettingsWindow.xaml.cs:13-14` | `_licenseService`- und `_exportService`-Felder |
| `SettingsWindow.xaml.cs:17` | Konstruktor-Parameter `LicenseService licenseService` |
| `SettingsWindow.xaml.cs:26` | `LoadLicenseInfo()`-Aufruf |
| `SettingsWindow.xaml.cs:35-40` | `LoadLicenseInfo()`-Methode |
| `SettingsWindow.xaml.cs:79-111` | `UpdateLicenseStatusDisplay()`-Methode |
| `SettingsWindow.xaml.cs:131-167` | `BtnActivate_Click`-Handler |
| `SettingsWindow.xaml.cs:169-179` | `BtnClearLicense_Click`-Handler |
| `SettingsWindow.xaml.cs:125-128` | `BtnCopyDeviceId_Click`-Handler |
| `SettingsWindow.xaml.cs:37` | `txtDeviceId.Text`-Zuweisung |
| `SettingsWindow.xaml` | Ganzer Lizenz-Tab (TabItem mit License-Header), alle Lizenz-Controls (`txtDeviceId`, `txtLicenseKey`, `txtLicenseStatus`, `txtLicenseExpires`, `txtLastCheck`, `btnCopyDeviceId`, `btnActivate`, `btnClearLicense`) |
| `SplashWindow.xaml.cs:17` | `_licenseService`-Feld |
| `SplashWindow.xaml.cs:22-27` | `_licenseService`-Initialisierung und `UploadStatusChanged`-Handler |
| `SplashWindow.xaml.cs:102-112` | `EnsureFreeLicenseAsync()`-Aufruf (Schritt 2) |
| `SplashWindow.xaml.cs:114-133` | Upload-Trigger (Schritt 3) |
| `ExportService.cs:16` | `_settings`-Feld (wird nur für `GetDeviceId()` benötigt) |
| `ExportService.cs:21` | `SettingsService`-Parameter aus Konstruktor |
| `ExportService.cs:91-95` | `GetDeviceId()`-Methode |
| `ExportService.cs:36` | `DeviceId`-Zuweisung in `CreateExport()` |
| `Models/IndexExport.cs:28` | `DeviceId`-Property |
| `FluxDB.csproj` | `System.Net.Http`-NuGet-Referenz prüfen/entfernen |
| `FluxDB.csproj` | `Newtonsoft.Json`-NuGet-Referenz prüfen (wird nur noch für Settings/Export benötigt — bleibt) |
| `packages.config` | `System.Net.Http`-Eintrag prüfen |