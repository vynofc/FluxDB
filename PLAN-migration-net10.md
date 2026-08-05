# FluxDB .NET 4.7.2 → .NET 10 Migration Plan

## 1. Ziel & Motivation

FluxDB von .NET Framework 4.7.2 auf .NET 10 migrieren, um:
- **WPF-UI (lepoco/wpfui)** nutzen zu können (braucht .NET 8+)
- Modernere C# Features (nullability, records, pattern matching, spans)
- Bessere Performance (JIT, GC, NativeAOT-ready)
- Zukunftssicherheit (net472 ist end-of-life)
- Devcontainer hat bereits .NET 10 SDK installiert

## 2. Projektspezifische Änderungen

### 2.1 FluxDB.csproj

```xml
<!-- VORHER -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net472</TargetFramework>
    <UseWPF>true</UseWPF>
    ...
  </PropertyGroup>

<!-- NACHHER -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>false</SelfContained>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    ...
  </PropertyGroup>
```

**Änderungen im Detail:**

| Eintrag | Vorher | Nachher | Begründung |
|---------|--------|---------|------------|
| `TargetFramework` | `net472` | `net10.0-windows` | .NET 10 + Windows-spezifische APIs |
| `UseWindowsForms` | fehlt | `true` | `System.Windows.Forms` wird für FolderBrowserDialog, MessageBox etc. genutzt |
| `RuntimeIdentifier` | fehlt | `win-x64` | Explizit für Windows x64 (kein AnyCPU-Selbstbetrug) |
| `Nullable` | fehlt | `enable` | Optional, aber empfohlen |
| `ImplicitUsings` | fehlt | `enable` | Reduziert using-Deklarationen |

### 2.2 NuGet Package Updates

| Package | Vorher | Nachher | Begründung |
|---------|--------|---------|------------|
| `Newtonsoft.Json` | 13.0.3 | **entfernen** | `System.Text.Json` ist built-in in .NET 10 |
| `System.Data.SQLite.Core` | 1.0.118.0 | `1.0.119.0` | Neueste Stable mit net10.0 Support |
| `Stub.System.Data.SQLite.Core.NetFramework` | 1.0.118 | **entfernen** | Nur für net472 nötig |
| `WPF-UI` | — | `4.2.0` | Modernes Fluent UI |
| `CommunityToolkit.Mvvm` | — | `8.4.0` | MVVM Toolkit (optional) |

### 2.3 Framework-References entfernen

```xml
<!-- ENTFERNEN - sind in .NET 10 built-in -->
<Reference Include="System.Net.Http" />
<Reference Include="System.IO.Compression" />
<Reference Include="System.Windows.Forms" />
```

### 2.4 AssemblyInfo.cs anpassen

Die Auto-Generierung von `Properties/AssemblyVersion.cs` bleibt, aber das manuelle `AssemblyInfo.cs` muss geprüft werden:

```csharp
// AssemblyInfo.cs - ThemeInfo muss bleiben
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]
```

Das `GenerateAssemblyInfo=false` im csproj **muss bleiben**, sonst Konflikt.

### 2.5 Newtonsoft.Json → System.Text.Json Migration

**Betroffene Files:**
- `Services/SettingsService.cs` — `JsonConvert.SerializeObject` / `DeserializeObject`
- `Services/ExportService.cs` — `JObject`, `JsonConvert.SerializeObject`
- `Services/ImportService.cs` — `JObject`, `JsonConvert.DeserializeObject`

**Mapping:**
```csharp
// VORHER
var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
var obj = JsonConvert.DeserializeObject<AppSettings>(json);

// NACHHER
var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
var obj = JsonSerializer.Deserialize<AppSettings>(json);
```

`JObject` → `JsonDocument` oder `JsonNode`:
```csharp
// VORHER
var jObject = JObject.Parse(json);
var name = jObject["name"]?.Value<string>();

// NACHHER
using var doc = JsonDocument.Parse(json);
var name = doc.RootElement.GetProperty("name").GetString();
```

**Falls Newtonsoft.Json bleiben soll (weniger Aufwand):** Einfach behalten. Läuft auch auf .NET 10. Entscheidung: **Entfernen**, da nur 3 Dateien betroffen.

### 2.6 SQLite Interop

`System.Data.SQLite.Core` braucht native `SQLite.Interop.dll` (x64/x86). Auf .NET 10:
- Das NuGet-Paket bringt weiterhin die natives DLLs mit
- Im Build-Output landen sie in `runtimes/win-x64/native/` etc.
- **Wichtig:** `SQLite.Interop.dll` muss beim Packaging mitkopiert werden

Alternativ: `Microsoft.Data.Sqlite` (EF Core's SQLite Provider). Weniger Interop-Ärger, aber anderes API. **Nicht empfohlen** wegen des Refactoring-Aufwands in `DatabaseService.cs`.

### 2.7 COM-Interop

```csharp
// MainWindow.xaml.cs verwendet Shell-COM für Datei-Dialoge
private static readonly Guid ShellItemIID = new Guid("...");
```

Funktioniert auf .NET 10 unverändert. COM-Interop ist voll unterstützt.

## 3. Code-Änderungen

### 3.1 C# Language Updates

| Feature | Anwendung |
|---------|-----------|
| `record` types | `FileEntry`, `AppSettings` → records (optional) |
| Primary constructors | Services (optional) |
| `required` properties | Model-Klassen |
| File-scoped namespaces | Alle `.cs` Dateien |
| `global using` | `GlobalUsings.cs` für häufige Namespaces |
| `Span<T>` / `Memory<T>` | Performance-Optimierung in IndexerService |

### 3.2 GlobalUsings.cs (neu)

```csharp
// WPF/FluxDB/GlobalUsings.cs
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using System.Windows;
global using System.Windows.Controls;
global using System.Windows.Input;
global using System.Windows.Media;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using FluxDB.Models;
global using FluxDB.Services;
```

### 3.3 Nullable aktivieren

```xml
<Nullable>enable</Nullable>
```

Erwartete Warnungen: ~50-100. Die meisten harmlos, aber gut für Code-Qualität. Pragmatisch: `#nullable disable` in Dateien mit vielen Problemen, schrittweise aktivieren.

## 4. Build-System

### 4.1 build.bat

```diff
- dotnet restore WPF/FluxDB/FluxDB.csproj
- msbuild /p:Configuration=Release /p:Platform=AnyCPU /p:OutDir=bin\
+ dotnet publish WPF/FluxDB/FluxDB.csproj -c Release -o bin\ --self-contained false
```

**Warum `dotnet publish` statt `msbuild`?**
- Auf .NET 10 ist `dotnet build`/`publish` der Standard
- `msbuild` braucht VS Build Tools (nicht nötig für .NET SDK-Projekte)
- `dotnet publish` handled alle Dependencies, natives Interop, Output

### 4.2 install-requirements.bat

```diff
- echo [1/4] Checking for .NET SDK...
+ echo [1/4] Checking for .NET 10 SDK...
- dotnet --version >nul 2>&1
+ dotnet --list-sdks | findstr "10.0" >nul 2>&1
```

### 4.3 CI/CD (GitHub Actions)

**build-wpf.yml:**
```diff
- - name: Setup MSBuild
-   uses: microsoft/setup-msbuild@v2
- - name: Restore packages
-   run: dotnet restore WPF/FluxDB/FluxDB.csproj
- - name: Build
-   run: |
-     if (-not (Test-Path bin)) { New-Item ... }
-     msbuild WPF/FluxDB/FluxDB.csproj /p:Configuration=Release ...
+ - name: Setup .NET
+   uses: actions/setup-dotnet@v4
+   with:
+     dotnet-version: '10.0.x'
+ - name: Build
+   run: dotnet publish WPF/FluxDB/FluxDB.csproj -c Release -o bin\
```

**release.yml:** Gleiche Änderung wie build-wpf.yml.

### 4.4 devcontainer.json

Bereits .NET 10-kompatibel. Keine Änderungen nötig.

## 5. Packaging & Deployment

### 5.1 Output-Struktur (dotnet publish)

```
bin/
├── FluxDB.exe
├── FluxDB.dll
├── FluxDB.runtimeconfig.json
├── FluxDB.deps.json
├── WPF-UI.dll
├── CommunityToolkit.Mvvm.dll
├── System.Data.SQLite.dll
├── FluxDB-icon.ico
├── FluxDB-icon.png
├── FluxDB-Installer.exe        (Go)
├── version.txt
├── components/
│   └── Log_Viewer.exe          (Go)
└── runtimes/
    └── win-x64/
        └── native/
            └── SQLite.Interop.dll
```

### 5.2 Installer-Anpassung

Der Go-Installer extrahiert ins `%LOCALAPPDATA%\FluxDB`. Die `runtimes/` Ordnerstruktur muss im ZIP erhalten bleiben. `System.Data.SQLite.Core` lädt die native DLL automatisch aus `runtimes/win-x64/native/`.

### 5.3 Self-Contained vs Framework-Dependent

| Variante | Vorteil | Nachteil | Größe |
|----------|---------|----------|-------|
| Framework-dependent | Kleiner, Updates via Windows Update | .NET 10 Runtime muss installiert sein | ~15 MB |
| Self-contained | Läuft überall, keine Runtime nötig | Größer | ~80 MB |

**Empfehlung:** Framework-dependent + Installer prüft auf .NET 10 Runtime.

## 6. Risiken & Mitigation

| Risiko | Impact | Mitigation |
|--------|--------|------------|
| **SQLite Interop** — Native DLLs nicht gefunden | High | `runtimes/` Ordner prüfen, ggf. `SQLiteConnection.LoadExtension` anpassen |
| **COM-Interop** — Shell-APIs brechen | Medium | Auf .NET 10 getestet, sollte funktionieren |
| **WinForms-Interop** — FolderBrowserDialog | Low | `UseWindowsForms=true` im csproj |
| **Newtonsoft → System.Text.Json** — Verhalten abweichend | Medium | Case-sensitivity, null-handling, custom converters |
| **CI bricht** — msbuild → dotnet | Low | Einmalige Änderung, gut getestet |
| **WPF-UI Rendering** — Controls rendern anders | Medium | Nur nach Migration testen (Phase 2) |

## 7. Phasen

### Phase 1: csproj + Build (≈ 1h)
1. `FluxDB.csproj` auf net10.0-windows umstellen
2. NuGet-Packages updaten
3. Framework-References entfernen
4. `dotnet restore` + `dotnet build` testen
5. Fehler beheben (fehlende APIs, Assembly-Konflikte)

### Phase 2: Newtonsoft.Json → System.Text.Json (≈ 1.5h)
6. `SettingsService.cs` migrieren
7. `ExportService.cs` migrieren
8. `ImportService.cs` migrieren
9. Serialisierungs-Tests (manuell)

### Phase 3: C# Modernisierung (≈ 1h)
10. `GlobalUsings.cs` anlegen
11. File-scoped namespaces
12. Nullable-Warnungen fixen (oder `#nullable disable`)

### Phase 4: Build-System (≈ 0.5h)
13. `build.bat` anpassen
14. `install-requirements.bat` anpassen
15. CI-Workflows anpassen

### Phase 5: Test & Verify (≈ 1h)
16. Vollständiger Build (`dotnet publish`)
17. App starten, alle Funktionen testen
18. SQLite-Interop testen
19. Installer mit neuer Struktur testen

**Gesamtaufwand: ≈ 5h**

## 8. Checkliste

- [ ] `TargetFramework` auf `net10.0-windows`
- [ ] `UseWindowsForms` hinzugefügt
- [ ] `System.Data.SQLite.Core` auf 1.0.119 aktualisiert
- [ ] `Stub.System.Data.SQLite.Core.NetFramework` entfernt
- [ ] `Newtonsoft.Json` entfernt (oder behalten)
- [ ] `System.Net.Http` Reference entfernt
- [ ] `System.IO.Compression` Reference entfernt
- [ ] `System.Windows.Forms` Reference entfernt
- [ ] `SettingsService.cs` auf System.Text.Json
- [ ] `ExportService.cs` auf System.Text.Json
- [ ] `ImportService.cs` auf System.Text.Json
- [ ] `GlobalUsings.cs` erstellt
- [ ] `build.bat` auf `dotnet publish` umgestellt
- [ ] `install-requirements.bat` auf .NET 10 geprüft
- [ ] CI `build-wpf.yml` auf .NET 10
- [ ] CI `release.yml` auf .NET 10
- [ ] `dotnet publish` erfolgreich
- [ ] App startet und funktioniert
- [ ] SQLite-Interop lädt korrekt
- [ ] `FluxDB.zip` Struktur korrekt

---

**Letzte Aktualisierung:** 2026-08-05
**Status:** Planung — Umsetzung folgt