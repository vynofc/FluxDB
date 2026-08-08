# UI Enhancement Ideas — FluxDB

## 1. Tag-Chips statt TextBox

**Problem:** Tags werden aktuell als kommagetrennter String in einer `TextBox` eingegeben — fehleranfällig, unübersichtlich und keine visuelle Validierung.

**Lösung:** Ein Flow-Token-Control (Chips/Pills) im Detail-Panel:

- Jedes Tag wird als farbiger **Chip/Pill** mit X-Button dargestellt
- Neues Tag per **Enter** oder **Komma** hinzufügen
- Backspace auf leerem Feld entfernt das letzte Tag
- Autocomplete-Vorschläge aus bereits existierenden Tags in der DB

**Umsetzung:**
- `ItemsControl` mit `WrapPanel` als `ItemsPanelTemplate`
- `TagModel` (Name, Color) als Datenquelle
- `TagChip` als `UserControl` mit `SymbolIcon` (DismissCircle) zum Entfernen
- `TagInputBox` als separates `TextBox`-Element am Ende des Wraps für neue Tags

**UX-Vorteil:** Keine Tippfehler mehr, visuell ansprechend, schnelles Hinzufügen/Entfernen.

---

## 2. Thumbnail-/Grid-Ansicht

**Problem:** Nur Listenansicht (`DataGrid`-Zeilen) — für Bild- und Medienordner unpraktisch.

**Lösung:** Ein **Toggle-Button** (Liste ↔ Kacheln) in der Navigation-Bar, der zwischen zwei Views umschaltet:

| Listenansicht | Grid-Ansicht |
|---|---|
| `DataGrid` mit Spalten (Name, Typ, Größe, Tags, Modified) | `UniformGrid` / `WrapPanel` mit Kacheln |
| Sortierbar, kompakt, viele Infos | Große Icons/Thumbnails, visuelle Navigation |

**Grid-Ansicht Details:**
- `ItemsControl` mit `UniformGrid`-Panel (z.B. 4-6 Spalten, responsiv)
- Jede Kachel: großes Icon (64x64), Dateiname (abgeschnitten), Dateigröße
- Echte Thumbnails für Bilder (bereits via `GetShellThumbnail` vorhanden)
- Hover-Effekt (Scale-Animation, Border-Highlight)
- Rechtsklick-Kontextmenü wie in Listenansicht

**Umsetzung:**
- Neuer `FileGridView`-UserControl + `FileGridTile`-UserControl
- `ToggleSwitch` oder `Button` mit `SymbolIcon` (List24 ↔ Grid24) in Navigationsleiste
- `BooleanToVisibilityConverter` für Umschalten zwischen DataGrid und GridView
- Thumbnail-Cache (max 100 Einträge, LRU) für Performance

---

## 3. Ordnerbaum-Sidebar

**Problem:** Breadcrumbs allein sind für tiefe Ordnerstrukturen langsam. Man muss sich durchklicken, um 3-4 Ebenen tief zu navigieren.

**Lösung:** Ein einklappbarer **TreeView** links vom DataGrid.

**Layout:**
```
[ Sidebar (TreeView) | GridSplitter | DataGrid | GridSplitter | Detail-Panel ]
```

- Sidebar-Header mit "Folders"-Titel und Collapse-Button
- `TreeView` mit `HierarchicalDataTemplate`, lazy-loaded (Kinder erst bei Expand)
- Root-Node = aktueller Root-Ordner (`_currentRootFolder`)
- Ordner-Icons via `SymbolIcon` (Folder24, FolderOpen24 für expanded)
- Kontextmenü: Open, Refresh, New Folder
- Drag & Drop: Ordner aus Sidebar in DataGrid ziehen = verschieben

**Umsetzung:**
- `TreeView` mit `ItemContainerStyle` für Folder-Styling
- `LazyTreeNode`-Model mit `IsExpanded`-Property und `Children`-Liste
- `FolderTreeViewModel` mit `LoadChildrenAsync`-Methode
- Sidebar-Breite per `GridSplitter` anpassbar, default 250px
- Zustand in `AppSettings` speichern (Collapsed/Expanded, Breite)

---

## 4. Multi-Selektions-Info

**Problem:** Wenn mehrere Dateien ausgewählt sind, zeigt das Detail-Panel nichts an (leer oder "No file selected").

**Lösung:** Bei Multi-Selektion ein **aggregiertes Info-Panel** mit Batch-Operationen.

**Anzeige:**
```
┌─────────────────────────────┐
│ 5 Dateien ausgewählt         │
│ Gesamtgröße: 23.7 MB        │
│ Typen: 3 Images, 2 Docs     │
│                              │
│ [Tags zuweisen...]           │
│ [Löschen]  [Verschieben]     │
│ [Kopieren] [Exportieren]     │
└─────────────────────────────┘
```

**Batch-Operationen:**
- **Tags zuweisen:** Dialog zum Hinzufügen/Entfernen von Tags für alle ausgewählten Dateien
- **Löschen:** Alle markierten löschen (mit Count-Bestätigung)
- **Verschieben:** Ordnerdialog → verschiebt alle ausgewählten Dateien
- **Kopieren:** Ordnerdialog → kopiert alle ausgewählten Dateien
- **Exportieren:** Ausgewählte Dateien als JSON exportieren

**Umsetzung:**
- `MultiSelectionPanel`-UserControl, sichtbar wenn `dgFiles.SelectedItems.Count > 1`
- `BoolToVisibilityConverter` mit `ConverterParameter` für `Count > 1`
- `BatchTagDialog`-Window (Checkbox-Liste existierender Tags + neues Tag-Feld)
- Berechnung der Gesamtgröße und Typ-Verteilung via LINQ

---

## 5. Theme-Schnellumschalter + Header-Modernisierung

**Problem:** Theme-Wechsel erfordert Öffnen des Settings-Fensters → mehrere Klicks. Header wirkt etwas beengt.

**Lösung A — Theme-Schnellumschalter:**
- Ein **Sonne/Mond-Icon-Button** (SymbolRegular.WeatherSunny24 / WeatherMoon24) rechts im Header
- Klick toggled zwischen Dark/Light via `ApplicationThemeManager.Apply()`
- Sofortige visuelle Rückmeldung (Icon-Wechsel + `Storyboard`-Animation)
- Zustand in `SettingsService` speichern

**Lösung B — Header-Modernisierung:**
- Pfad-Anzeige (`txtCurrentFolder`) als **abgeschnittener Pfad** mit Tooltip (vollständig)
- "Select Folder"-Button als Icon-Button (FolderAdd24) statt Text-Button
- Refresh-Button als Icon-Button (ArrowSync24)
- Settings-Button als Zahnrad-Icon (Settings24)
- Kompaktere Abstände (`Padding="12,8"`), kleinere Titel-Schrift (20px)

**Umsetzung:**
- `ThemeToggleCommand` in `MainWindow` (oder `SettingsViewModel`)
- `EventHandler` für `ApplicationThemeManager.Changed` für Icon-Update
- `ToolTipService.ShowDuration` auf `20000` für Pfad-Tooltip
- `MultiBinding` mit `StringTrimmingConverter` für Pfad-Anzeige

---

## 6. Icons ganz links im DataGrid

**Problem:** Im DataGrid sind Dateien nur über die Typ-Spalte (Text) identifizierbar. Der Nutzer muss den Text lesen, statt das Icon auf einen Blick zu erkennen.

**Lösung:** Eine Icon-Spalte als erste Spalte (vor "Name") mit festem, schmalem Platz für das zum Dateityp passende Symbol.

- Icon-Spalte ganz links mit fester Breite (~28px)
- Symbole via `SymbolIcon` (WPF-UI): `Folder24` für Ordner, `Image24` für Bilder, `Document24` für Docs, `Code24` für Code-Dateien, etc.
- Farbcodierung passend zum Typ: Ordner = Gelb/Orange, Bilder = Blau, Dokumente = Grün, Code = Lila, Archive = Rot
- Icon-Auswahl basierend auf `FileEntry.Extension` (Logik bereits in `FileEntry.IconSymbol` vorhanden)

**Umsetzung:**
- `DataGridTemplateColumn` mit `SymbolIcon` als `CellTemplate` in `dgFiles`
- `IconSymbol`-Property in `FileEntry` (bereits vorhanden) direkt per Binding nutzen
- `IconColorBrush`-Property in `FileEntry` (bereits vorhanden) für Typ-Farben nutzen
- Spaltenbreite fix auf `28` + `Padding="4,0"` setzen, `CanUserResize="False"`

**UX-Vorteil:** Schnellere visuelle Unterscheidung von Dateitypen, konsistent mit Windows Explorer, keine Text-Lesearbeit für die Typ-Erkennung nötig.

---

## Weitere Ideen

| # | Idee | Aufwand |
|---|---|---|
| 8 | **Sortier-Indikator** — Pfeil-Icons in DataGrid-Headern für aktuelle Sortierung | Klein |
| 9 | **Favoriten/Pins** — Ordner anheften (Pin-Icon), immer sichtbar in Sidebar | Mittel |
| 10 | **Tastenkürzel-Cheat-Sheet** — `Ctrl+K` Overlay mit allen Shortcuts | Klein |
| 11 | **Lazy-Loading im DataGrid** — `DataGridVirtualization` für 100k+ Dateien | Mittel |
| 12 | **Spalten-Konfiguration** — Spalten per Rechtsklick auf Header ein-/ausblenden | Klein |
| 14 | **Toast-Benachrichtigungen** — `ISnackbarService` für Copy/Delete/Indexing-Feedback | Mittel |

---

## Priorisierte Roadmap

```
Phase 1 (Quick Wins):   #6 (DataGrid-Icons), #5 (Theme-Toggle), #10 (Shortcut-Sheet)
Phase 2 (Core UX):      #1 (Tag-Chips), #4 (Multi-Select-Info)
Phase 3 (Navigation):   #3 (Ordnerbaum-Sidebar), #2 (Grid-Ansicht)
Phase 4 (Polish):       #12+#14
```