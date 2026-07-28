# Plugin-System Plan

## Überblick

FluxDB bekommt ein Plugin-System, mit dem Drittanbieter die Funktionalität erweitern können — z. B. benutzerdefinierte Exportformate, automatisierte Tag-Regeln, Datei-Vorschauen oder Integrationen mit externen Tools.

Drei Artefakte:
1. **Plugin Loader** (in FluxDB selbst) — entdeckt, lädt und verwaltet Plugins
2. **Plugin Template** (separates Projekt) — Vorlage zum Erstellen eigener Plugins
3. **PLUGIN.md** — vollständige Dokumentation für Plugin-Entwickler

---

## 1. Plugin-Architektur

### 1.1 Core Interfaces (neue Datei: `Plugin/IFluxDBPlugin.cs`)

```csharp
namespace FluxDB.Plugin
{
    public interface IFluxDBPlugin
    {
        string Name { get; }
        string Version { get; }
        string Author { get; }
        string Description { get; }
        void Initialize(IPluginContext context);
        void Shutdown();
    }
}
```

### 1.2 Plugin Context (neue Datei: `Plugin/IPluginContext.cs`)

```csharp
namespace FluxDB.Plugin
{
    public interface IPluginContext
    {
        // Core services
        DatabaseService Database { get; }
        ExportService Export { get; }
        SettingsService Settings { get; }

        // Convenience
        string CurrentRootFolder { get; }
        void Log(string message);

        // UI integration
        void RegisterMenuItem(string header, Action callback);
        void RegisterContextMenuItem(string header, Action<FileEntry> callback);
        void RegisterToolbarButton(string label, string icon, Action callback);

        // Events
        event EventHandler<FileEntry> FileIndexed;
        event EventHandler<FileEventArgs> FileSelected;
        event EventHandler<SearchEventArgs> SearchPerformed;
    }
}
```

### 1.3 Event Args (neue Datei: `Plugin/PluginEventArgs.cs`)

- `FileEventArgs` — enthält `FileEntry` und `string FolderPath`
- `SearchEventArgs` — enthält `string Query`, `List<FileEntry> Results`, `string FolderPath`

### 1.4 Plugin Loader (neue Datei: `Services/PluginService.cs`)

**Aufgaben:**
- Beim Start den Ordner `%LocalAppData%\FluxDB\Plugins\` scannen (oder `Plugins\` neben der EXE, beides versuchen)
- Alle `.dll`-Dateien laden und nach Typen suchen, die `IFluxDBPlugin` implementieren
- Plugins instanziieren und `Initialize()` aufrufen
- Beim Beenden `Shutdown()` auf allen geladenen Plugins aufrufen
- Fehler isolieren: ein fehlerhaftes Plugin darf andere nicht beeinträchtigen

**Wichtige Details:**
- Laden via `Assembly.LoadFrom()` mit try/catch pro Assembly
- Plugin-Metadaten aus dem Interface auslesen (Name, Version, Author)
- Plugin-Status tracken (Loaded, Failed, Disabled)
- `PluginService` als Singleton (wie `LoggingService`), da es app-weit einmalig ist
- Kein `AppDomain`-Isolation (zu komplex für .NET Framework 4.7.2, nicht nötig für v1)
- Dependency-Konflikt-Erkennung: vor dem Laden prüfen, ob referenzierte Assemblies vorhanden sind

### 1.5 Plugin Manager UI (Erweiterung in `SettingsWindow`)

Neuer Tab "Plugins" im Settings-Fenster:
- Liste aller geladenen Plugins mit Name, Version, Author, Status
- Checkbox zum Aktivieren/Deaktivieren (wird in `settings.json` unter `DisabledPlugins` gespeichert)
- "Plugins-Ordner öffnen"-Button
- "Neu laden"-Button (rescannt den Plugin-Ordner)

**Änderungen an `AppSettings`:**
Neues Feld `List<string> DisabledPlugins` (Plugins, die beim Start übersprungen werden).

---

## 2. Integration in FluxDB

### 2.1 Startup (Änderung in `MainWindow.xaml.cs`)

In `InitializeServices()`:
```csharp
PluginService.Initialize(_databaseService, _exportService, _settingsService);
```

In `Window_Closed` (oder `OnClosed`):
```csharp
PluginService.Shutdown();
```

### 2.2 Event-Hooks (Änderungen in `MainWindow.xaml.cs`)

- Nach `IndexerService_ProgressChanged` / wenn ein File fertig indexed ist → `PluginService.RaiseFileIndexed(fileEntry)`
- Bei `dgFiles_SelectionChanged` → `PluginService.RaiseFileSelected(selectedFile)`
- Nach `PerformSearch()` → `PluginService.RaiseSearchPerformed(query, results)`

### 2.3 UI-Menüpunkte (Änderung in `MainWindow.xaml`)

- Menü-Eintrag "Plugins" im Hauptmenü (neben File/Edit/View)
- Darunter: dynamisch generierte Einträge aus `PluginService.GetMenuItems()`
- Context-Menü in `dgFiles` erweitert um Plugin-Einträge aus `PluginService.GetContextMenuItems()`

### 2.4 Plugin-Ordner

- Hauptverzeichnis: `%LocalAppData%\FluxDB\Plugins\`
- Fallback: `{exeDir}\Plugins\`
- Wird beim ersten Start automatisch angelegt (leer)
- Plugin-DLLs werden hierhin kopiert/abgelegt

---

## 3. Plugin Template

### 3.1 Projektstruktur

```text
FluxDB-Plugin-Template/
├── PluginTemplate.csproj
├── MyPlugin.cs
├── PluginInfo.cs
├── README.md           (kurze Anleitung)
└── Properties/
    └── AssemblyInfo.cs
```

### 3.2 `.csproj` (wesentliche Teile)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <AssemblyName>MyFluxDBPlugin</AssemblyName>
    <OutputType>Library</OutputType>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="FluxDB">
      <HintPath>..\FluxDB\bin\Release\FluxDB.exe</HintPath>
    </Reference>
  </ItemGroup>
</Project>
```

### 3.3 `MyPlugin.cs` (Beispiel-Plugin)

Ein einfaches Plugin, das:
- `Initialize()` eine Log-Nachricht schreibt
- Auf `FileIndexed`-Event reagiert und große Dateien (>100 MB) automatisch mit dem Tag "large" versieht
- Einen Menüeintrag "Plugin: Show Stats" registriert, der ein MessageBox mit Statistiken anzeigt

### 3.4 Build & Deploy

- Build mit `dotnet build -c Release` (oder `msbuild`)
- Ausgabe-DLL sowie alle Abhängigkeiten in den `Plugins\`-Ordner kopieren
- FluxDB erkennt das Plugin beim nächsten Start automatisch

---

## 4. PLUGIN.md — Inhalt

### 4.1 Struktur der Dokumentation

1. **Quick Start** — 3-Schritte-Anleitung zum Erstellen des ersten Plugins
2. **Plugin Interface** — Detailbeschreibung von `IFluxDBPlugin`
3. **Plugin Context** — Alle verfügbaren Services und APIs mit Code-Beispielen
4. **Events** — Alle Events mit Beispielen
5. **UI-Integration** — Menüeinträge, Toolbar-Buttons, Context-Menüs
6. **Datenbank-Zugriff** — Wie man über `DatabaseService` auf Files, Tags, Notes zugreift
7. **Export-Service** — Wie man den Export-Service nutzt
8. **Fehlerbehandlung** — Best Practices (try/catch, keine Exceptions in Events werfen)
9. **Build & Deployment** — Schritt-für-Schritt: Template klonen, bauen, im Plugins-Ordner ablegen
10. **Beispiel-Plugins** — 3 vollständige Code-Beispiele:
    - **AutoTagger**: Taggt Dateien anhand von Regeln (z. B. `*.pdf` → "document")
    - **FileStats**: Zeigt Statistiken über die indizierten Dateien in einem MessageBox
    - **CustomExporter**: Exportiert die Datenbank als CSV
11. **API-Referenz** — Vollständige Referenz aller Interfaces, Klassen und Methoden
12. **FAQ / Troubleshooting**

### 4.2 Wichtige Hinweise in der Doku

- Thread-Safety: DB-Zugriffe immer im selben Thread, UI-Updates nur via `Dispatcher`
- Keine langen Operationen in Event-Handlern (UI-Thread-Blockierung)
- Plugin-DLLs müssen für .NET Framework 4.7.2 kompiliert sein
- Newtonsoft.Json 13.0.3 ist als Abhängigkeit verfügbar (über FluxDB referenziert)
- Plugin-Name muss eindeutig sein (sonst wird nur das erste geladen)
- Logging über `context.Log()` nutzen, nicht `Console.WriteLine`

---

## 5. Umsetzungsreihenfolge

### Phase 1: Core
1. `Plugin/IFluxDBPlugin.cs` — Interface
2. `Plugin/IPluginContext.cs` — Context Interface
3. `Plugin/PluginEventArgs.cs` — Event Args
4. `Plugin/PluginContext.cs` — Konkrete Implementierung
5. `Services/PluginService.cs` — Loader (Singleton)

### Phase 2: Integration
6. `MainWindow.xaml.cs` — PluginService initialisieren, Events triggern
7. `MainWindow.xaml` — Menü-Einträge für Plugins
8. `AppSettings.cs` — `DisabledPlugins`-Feld hinzufügen
9. `SettingsWindow.xaml/cs` — Plugin-Tab mit UI

### Phase 3: Template & Docs
10. `FluxDB-Plugin-Template/` — Projekt-Vorlage erstellen
11. `PLUGIN.md` — Vollständige Dokumentation schreiben
12. `README.md` — Hinweis auf Plugin-System ergänzen

### Phase 4: Testing
13. Beispiel-Plugin (AutoTagger) bauen und testen
14. Fehlerfälle testen (fehlende DLL, kaputtes Plugin, Interface-Änderungen)