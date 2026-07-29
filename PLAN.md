# PLAN — Log Viewer als externes Go-Programm

## Ziel

Den WPF-internen Log Viewer (`LogViewer.xaml` / `LogViewer.xaml.cs`) durch ein eigenständiges Go-TUI-Programm ersetzen.  
FluxDB öffnet per F8 das externe Programm `components/Log_Viewer.exe --log <pfad>`, das die Logdatei live anzeigt.

---

## 1. Go-Programm: `Log Viewer/`

### 1.1 Projektstruktur

```
Log Viewer/
├── main.go          # Entrypoint, CLI-Flags, Bubble-Tea-Programm
├── model.go         # Bubble-Tea-Model, Messages, States
├── update.go        # State-Machine, Tastenkürzel
├── styles.go        # Lipgloss-Styles (Dark-Theme wie Installer)
├── view.go          # Rendering
├── go.mod
├── go.sum
├── build.bat        # Windows-Build (GOOS=windows)
└── build.sh         # Cross-Compile von Linux/macOS
```

### 1.2 Abhängigkeiten

| Modul | Zweck |
|---|---|
| `github.com/charmbracelet/bubbletea` | TUI-Framework |
| `github.com/charmbracelet/lipgloss` | Terminal-Styling |
| `github.com/charmbracelet/log` | Strukturiertes Logging (interne Fehler) |

### 1.3 CLI-Flags

| Flag | Beschreibung |
|---|---|
| `--log <pfad>` | Pfad zur Logdatei (erforderlich) |

### 1.4 Funktionsumfang

- **Logdatei laden**: Datei beim Start komplett einlesen
- **Live-Tail**: Datei wird mit `fsnotify`-ähnlichem Polling (500ms) auf Änderungen überwacht und neue Zeilen werden angehängt
- **Scrollen**: PageUp/PageDown, Home/End
- **Refresh**: `R`-Taste lädt die Datei komplett neu
- **Clear**: `C`-Taste leert die Datei (schreibt leeren String) und refresht
- **Suchen**: `/`-Taste öffnet Suchleiste, `Enter` springt zum nächsten Treffer, `Esc` bricht ab
- **Beenden**: `Esc` oder `Q` oder `Ctrl+C`
- **Dark-Theme**: Gleiche Farbpalette wie FluxDB Installer (Hintergrund #1a1a2e, Text #e0e0e0, etc.)

### 1.5 Build

- Windows: `GOOS=windows GOARCH=amd64 go build -ldflags="-s -w" -o bin/Log_Viewer.exe .`
- Output: `Log Viewer/bin/Log_Viewer.exe`

---

## 2. FluxDB WPF-App: Änderungen

### 2.1 `ShowLogViewer()` in `MainWindow.xaml.cs` umbauen

Statt `new LogViewer().ShowDialog()`:

```csharp
private void ShowLogViewer()
{
    try
    {
        var exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        var viewerExe = Path.Combine(exeDir, "components", "Log_Viewer.exe");
        var logPath = LoggingService.LogFilePath;

        if (!File.Exists(viewerExe))
        {
            MessageBox.Show("Log Viewer nicht gefunden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = viewerExe,
            Arguments = $"--log \"{logPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        System.Diagnostics.Process.Start(startInfo);
    }
    catch
    {
        MessageBox.Show("Log Viewer konnte nicht gestartet werden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
```

**Kein Fallback** auf den alten WPF-Viewer — der wird komplett entfernt.

### 2.2 `LogViewer.xaml` + `LogViewer.xaml.cs` entfernen

- Beide Dateien löschen
- Aus `FluxDB.csproj` entfernen (`<Page Include="LogViewer.xaml">` und `<Compile Include="LogViewer.xaml.cs">`)

### 2.3 Keine Änderung an `LoggingService`

- `LoggingService` bleibt unverändert – sie schreibt weiterhin nach `%LocalAppData%\FluxDB\logs.txt`
- Der externe Viewer liest nur die Datei, interagiert nicht mit dem WPF-Prozess

---

## 3. Build & Deployment

### 3.1 Build-Skripte für FluxDB (Gesamtprojekt)

**`build.bat`** (Windows) — baut WPF-App + Log Viewer, legt alles wie im Release-ZIP ab:

```bat
@echo off
cd /d "%~dp0"

echo [1/3] FluxDB WPF-App bauen...
nuget restore FluxDB.sln
msbuild FluxDB.sln /p:Configuration=Release /p:Platform="Any CPU"

echo [2/3] Log Viewer bauen...
cd "Log Viewer"
go build -ldflags="-s -w" -o ..\bin\Release\components\Log_Viewer.exe .
cd ..

echo [3/3] Fertig!
echo FluxDB.exe      -> bin\Release\
echo Log_Viewer.exe  -> bin\Release\components\
```

**`build.sh`** (Linux/macOS Cross-Compile) — baut WPF-App + Log Viewer:

```bash
#!/bin/bash
set -e
cd "$(dirname "$0")"

echo "[1/3] FluxDB WPF-App bauen (Cross-Compile via MSBuild)..."
nuget restore FluxDB.sln
msbuild FluxDB.sln /p:Configuration=Release /p:Platform="Any CPU"

echo "[2/3] Log Viewer bauen (Cross-Compile)..."
cd "Log Viewer"
GOOS=windows GOARCH=amd64 go build -ldflags="-s -w" -o ../bin/Release/components/Log_Viewer.exe .
cd ..

echo "[3/3] Fertig!"
echo "FluxDB.exe      -> bin/Release/"
echo "Log_Viewer.exe  -> bin/Release/components/"
```

### 3.2 CI-Workflows

#### `build.yml` (Push/PR auf main)

Zusätzlich zum bestehenden MSBuild-Schritt:

```yaml
      - name: Setup Go
        uses: actions/setup-go@v5
        with:
          go-version: "1.22"

      - name: Build Log Viewer
        run: |
          cd "Log Viewer"
          go build -ldflags="-s -w" -o ..\bin\Release\components\Log_Viewer.exe .
```

#### `release.yml` (Release published)

Bestehender "Create zip"-Schritt wird erweitert — `components/` Ordner ins ZIP:

```yaml
      - name: Create zip
        run: |
          mkdir dist
          mkdir dist\components
          copy bin\Release\FluxDB.exe dist\
          copy bin\Release\FluxDB.exe.config dist\
          copy bin\Release\FluxDB.pdb dist\
          copy bin\Release\Newtonsoft.Json.dll dist\
          copy bin\Release\System.Data.SQLite.dll dist\
          xcopy bin\Release\x64 dist\x64\ /E /I
          xcopy bin\Release\x86 dist\x86\ /E /I
          copy FluxDB-Installer.exe dist\
          copy bin\Release\components\Log_Viewer.exe dist\components\
          Compress-Archive -Path dist\* -DestinationPath FluxDB.zip
```

### 3.3 Release-Assets (unverändert)

- `FluxDB.zip` enthält: FluxDB.exe + DLLs + `components/Log_Viewer.exe` + `FluxDB-Installer.exe`
- `FluxDB-Installer.exe` wird **zusätzlich** als separates Asset hochgeladen (für direkten Download ohne ZIP)

### 3.4 Installer

- Installer extrahiert `components/Log_Viewer.exe` mit nach `%LOCALAPPDATA%\FluxDB\components\`
- Keine zusätzliche Logik nötig, da die ZIP-Struktur bereits `components/` enthält

---

## 4. Update-Check auf GitHub API umstellen

### 4.1 Problem

Aktuell nutzt FluxDB (`SplashWindow.xaml.cs`) eine eigene CDN-API:
- Version: `https://nsce-cdn.fun/FluxDB/version.txt` (komma-separierte Versionen)
- Installer: `https://nsce-cdn.fun/FluxDB/FluxDB-Installer.exe`
- ZIPs: `C:\NSCE\FluxDB\{version}.zip`
- Central Dir: `C:\NSCE\FluxDB\`

Der **Installer** nutzt dagegen bereits die GitHub API:
- Releases: `https://api.github.com/repos/vynofc/FluxDB/releases`
- Latest: `https://api.github.com/repos/vynofc/FluxDB/releases/latest`
- Download: `https://github.com/vynofc/FluxDB/releases/download/{tag}/FluxDB.zip`

### 4.2 Änderungen in `SplashWindow.xaml.cs`

`CheckForUpdatesAsync()` wird komplett umgebaut:

```csharp
private async Task<bool> CheckForUpdatesAsync()
{
    var args = Environment.GetCommandLineArgs();
    bool skipUpdate = args.Any(a => a.Trim().Equals("--noupdate", StringComparison.OrdinalIgnoreCase));
    App.IsUpdateSkipped = skipUpdate;

    var localVersionStr = App.GetLocalVersion();
    var assembly = Assembly.GetExecutingAssembly();
    var exeDir = Path.GetDirectoryName(assembly.Location) ?? ".";

    // GitHub API: latest release
    var latestTag = await FetchLatestReleaseTagAsync();
    if (latestTag == null)
        return true; // API nicht erreichbar → weitermachen

    var remoteVersion = VersionHelper.NormalizeVersion(latestTag);
    var localVersion = VersionHelper.NormalizeVersion(localVersionStr);

    if (VersionHelper.CompareVersions(remoteVersion, localVersion) <= 0)
        return true; // up to date

    // Update verfügbar
    App.IsUpdateAvailable = true;
    App.AvailableVersion = remoteVersion;

    if (skipUpdate)
    {
        LoggingService.Log("Update available but --noupdate flag is set. Skipping.");
        return true;
    }

    // Installer im App-Verzeichnis suchen
    var installerPath = Path.Combine(exeDir, "FluxDB-Installer.exe");
    if (!File.Exists(installerPath))
    {
        // Download installer from GitHub
        var ok = await DownloadInstallerAsync(exeDir, latestTag);
        if (!ok) return true;
    }

    var startInfo = new ProcessStartInfo(installerPath)
    {
        WorkingDirectory = exeDir,
        UseShellExecute = true
    };
    Process.Start(startInfo);
    return false; // Installer gestartet → App beenden
}

private async Task<string> FetchLatestReleaseTagAsync()
{
    try
    {
        using (var http = new HttpClient())
        {
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FluxDB");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var json = await http.GetStringAsync(
                "https://api.github.com/repos/vynofc/FluxDB/releases/latest");
            var release = JsonConvert.DeserializeObject<GitHubRelease>(json);
            return release?.TagName;
        }
    }
    catch (Exception ex)
    {
        LoggingService.Log($"GitHub API error: {ex.Message}");
        return null;
    }
}

private async Task<bool> DownloadInstallerAsync(string exeDir, string tag)
{
    try
    {
        var url = $"https://github.com/vynofc/FluxDB/releases/download/{tag}/FluxDB-Installer.exe";
        var path = Path.Combine(exeDir, "FluxDB-Installer.exe");

        using (var http = new HttpClient())
        using (var resp = await http.GetAsync(url))
        {
            if (!resp.IsSuccessStatusCode) return false;
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                await resp.Content.CopyToAsync(fs);
        }
        return true;
    }
    catch
    {
        return false;
    }
}
```

### 4.3 Neues Model: `Models/GitHubRelease.cs`

```csharp
namespace FluxDB.Models
{
    public class GitHubRelease
    {
        public string TagName { get; set; }
    }
}
```

### 4.4 Änderungen in `App.xaml.cs`

`GetLocalVersion()` wird vereinfacht — **entfernt**: `C:\NSCE\FluxDB`-Pfade, ZIP-Scanning, `FLUXDB_CENTRAL_DIR`. Nur noch:

```csharp
public static string GetLocalVersion()
{
    // 1. version.txt im App-Verzeichnis
    // 2. Assembly-Version als Fallback
}
```

### 4.5 Entfallende Sachen

- `https://nsce-cdn.fun/FluxDB/version.txt` — ersetzt durch GitHub API
- `https://nsce-cdn.fun/FluxDB/FluxDB-Installer.exe` — ersetzt durch GitHub Releases Download
- `C:\NSCE\FluxDB\` — kein Hardcoded-Pfad mehr
- `FLUXDB_CENTRAL_DIR` Env-Variable — entfällt
- Beta-Logik (`!beta`-Suffix, `IsBetaUpdateAvailable`) — entfällt (GitHub Releases haben keine Beta-Markierung in dem Format)

---

## 5. Migrationspfad (Reihenfolge)

1. **Go Log Viewer bauen** → `Log Viewer/` Projekt erstellen und testen
2. **Build-Skripte erstellen** → `build.bat` + `build.sh` für Gesamtprojekt
3. **F8-Integration** → `MainWindow.xaml.cs` umbauen (kein Fallback, startet direkt `Log_Viewer.exe`)
4. **Alten Viewer entfernen** → `LogViewer.xaml`, `LogViewer.xaml.cs`, `.csproj`-Einträge löschen
5. **CI anpassen** → `build.yml` + `release.yml` bauen `Log_Viewer.exe` mit, `release.yml` packt `components/` ins ZIP
6. **Update-Check umstellen** → GitHub API statt CDN, `C:\NSCE`-Pfade entfernen, Beta-Logik entfernen