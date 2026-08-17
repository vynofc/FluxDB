# Plan 5: Tabs (mehrere Orte gleichzeitig)

**Prioritaet:** 5 (groesster Refactor, zum Schluss)

## Ziel

Optional aktivierbare Tabs im Hauptfenster: mehrere Ordner gleichzeitig offen,
zwischen ihnen wechseln; auch Settings (und spaeter Editor/Workflows) koennen als
Tab geoeffnet werden.

## Kernproblem

Der gesamte Navigations- und Ansichtszustand liegt heute im `MainWindow`-Code-Behind:
`_backHistory`, `_forwardHistory`, `_currentRootFolder`, `_currentViewFolder`,
Filter, Suche, Auswahl. Zusaetzlich wird `DatabaseService` bei jedem
Root-Ordner-Wechsel disposed und neu erstellt — mit Tabs muss pro Root-Ordner
eine eigene Verbindung leben.

## Design

### Tab-Modell

```
BrowserTab {
  id: uuid
  kind: "folder" | "settings" | ...
  title: string
  // nur folder:
  rootFolder, viewFolder
  backHistory, forwardHistory
  filter, searchText
  databaseService, indexerService  // pro Root-Ordner
}
```

### Refactor-Schritte

1. **State extrahieren:** neuen `FolderBrowserState`-Typ (oder `FolderTabViewModel`,
   weiterhin code-behind-orientiert, kein MVVM-Zwang) mit allem
   Navigations-/Ansichtszustand; `MainWindow` haelt nur noch die aktive Instanz
2. **DB-Verbindungen pro Root-Ordner:** `DatabaseService`/`IndexerService` werden
   pro Folder-Tab erstellt und mit dem Tab disposed; der bisherige
   "Dispose-bei-Wechsel"-Pfad in `InitializeDatabaseForFolderAsync` wird durch
   "Tab wechseln = State wechseln" ersetzt
3. **Tab-Leiste:** WPF-UI `TabControl` (oder eigene Leiste) ueber dem Content;
   "+"-Button, Schliessen-X, Mittelklick zum Schliessen, Drag zum Umsortieren
   (optional)
4. **Settings als Tab:** `SettingsWindow` von `FluentWindow` auf `UserControl`
   umbauen (oder Inhalt in ein Control extrahieren, Fenster bleibt als Fallback)
5. **Feature-Flag:** Einstellung "Tabs aktivieren" (Default: aus); bei aus verhaelt
   sich die App exakt wie bisher (ein impliziter Tab)
6. **Persistenz:** offene Tabs + aktiver Tab in `settings.json`
   (`PersistenceOptions` erweitern), Wiederherstellung beim Start

### Shortcuts

| Shortcut | Aktion |
|---|---|
| Ctrl+T | Neuer Tab (gleicher Ordner) |
| Ctrl+W | Tab schliessen |
| Ctrl+Tab / Ctrl+Shift+Tab | Naechster/vorheriger Tab |
| Ctrl+1..9 | Direktwahl |

## Risiken / zu pruefende Punkte

- **Event-Subscriptions:** IndexerService-Events werden pro Tab abonniert; beim
  Tab-Schliessen sauber abmelden (sonst Leaks/Doppel-Updates)
- **Preview/Detail-Panel:** gehoert zum Tab-State (Auswahl pro Tab merken)
- **Speicher:** jede offene DB-Verbindung kostet Ressourcen — Tabs beim Schliessen
  wirklich dispose-en
- **Service (Plan 2):** parallele DB-Zugriffe App/Service bleiben unveraendert;
  Tabs aendern daran nichts

## Betroffene Dateien

| Datei | Aenderung |
|---|---|
| `WPF/FluxDB/Views/MainWindow.xaml(.cs)` | Tab-Leiste, State-Extraktion, Shortcuts |
| `WPF/FluxDB/Models/AppSettings.cs` | Tab-Persistenz, `TabsEnabled` |
| `WPF/FluxDB/Views/SettingsWindow.xaml(.cs)` | Als Control nutzbar machen + Tabs-Einstellung |
| `WPF/FluxDB/Services/SettingsService.cs` | Persistenz der offenen Tabs |

## Schritte

1. `FolderBrowserState` extrahieren, MainWindow darauf umstellen (Verhalten unveraendert)
2. Tab-Leiste + Tab-Lebenszyklus (neu/schliessen/wechseln)
3. Pro-Tab-DB-Verbindungen + sauberes Dispose
4. Settings als Tab oeffnen
5. Feature-Flag + Persistenz + Shortcuts
6. Manueller Test: viele Tabs, Ordnerwechsel pro Tab, Indexing im Hintergrund-Tab,
   Neustart mit Wiederherstellung
