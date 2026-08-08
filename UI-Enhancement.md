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

## 2. Ordnerbaum-Sidebar

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

## 3. Multi-Selektions-Info

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

## 4. Themen-Verwaltung in Settings

**Problem:** Aktuell gibt es nur Dark und Light als Themes. Der Theme-Wechsel erfordert das Öffnen des Settings-Fensters. Keine Möglichkeit, eigene Akzentfarben oder zusätzliche vordefinierte Themes zu wählen.

**Lösung:** Eine **Theme-Sektion** im Settings-Fenster mit mehreren vordefinierten Themes und Akzentfarben.

**Verfügbare Themes:**
- **Dark** (Standard) — Dunkler Hintergrund, helle Schrift
- **Light** — Heller Hintergrund, dunkle Schrift
- **High Contrast** — Maximale Kontraste für Barrierefreiheit
- **OLED Dark** — Reines Schwarz (#000) für OLED-Displays (Stromsparend)
- **Sepia** — Warme Brauntöne, augenschonend

**Zusätzlich:**
- **Akzentfarbe**-Auswahl (Dropdown oder Color-Picker) — beeinflusst Buttons, Links, Hervorhebungen
- Live-Vorschau im Settings-Fenster (Theme wird sofort angewendet)
- Theme-Persistenz in `settings.json` (`Theme` + `AccentColor`)

**Umsetzung:**
- `ComboBox` im Settings-Fenster für Theme-Auswahl
- `ColorPicker`-Control oder vordefinierte Farb-Buttons für Akzentfarbe
- `ApplicationThemeManager.Apply()` mit `ThemeType` (Dark/Light/HighContrast)
- `ResourceDictionary`-Merge für benutzerdefinierte Akzentfarben via `SolidColorBrush`-Overrides
- OLED-Theme via `ResourceDictionary` mit `Background=#000000` und `CardBackground=#111111`
- Sepia-Theme via `ResourceDictionary` mit warmen Farbtönen

**UX-Vorteil:** Personalisierung, Barrierefreiheit (High Contrast), OLED-Stromsparen, augenschonendes Arbeiten (Sepia).

---

## 5. Icons ganz links im DataGrid

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

## 6. Sortier-Indikator

**Problem:** Das DataGrid unterstützt Sortierung per Spaltenklick, aber es gibt keine visuelle Rückmeldung, welche Spalte aktuell sortiert ist und in welcher Richtung (aufsteigend/absteigend). Der Nutzer muss raten oder sich merken, wonach sortiert wurde.

**Lösung:** Pfeil-Icons (▲/▼) im Spaltenheader der aktuell sortierten Spalte, die die Sortierrichtung anzeigen.

**Umsetzung:**
- `DataGridTemplateColumn` mit `HeaderTemplate` für jede Spalte
- `SortDirection`-Binding an `DataGrid.Items.SortDescriptions`
- `SymbolIcon` (ArrowUp24 / ArrowDown24) rechtsbündig im Header
- `DataGrid.Sorting`-Event nutzen, um einen eigenen SortDirection-Tracker zu aktualisieren
- Alternativ: WPF-UI `DataGrid`-Styling mit `DataGridColumnHeader`-Template überschreiben

**UX-Vorteil:** Sofort erkennbar, wonach sortiert ist — konsistent mit Windows Explorer.

---

## 7. Favoriten/Pins

**Problem:** Häufig genutzte Unterordner sind tief in der Ordnerstruktur verschachtelt. Jedes Mal muss man über Breadcrumbs oder Sidebar mehrere Ebenen navigieren, um dorthin zu gelangen.

**Lösung:** Eine „Favoriten“-Sektion (gepinnte Ordner) in der Sidebar oder als Dropdown in der Navigation-Bar. Ordner können per Pin-Icon angeheftet werden.

**Umsetzung:**
- `PinnedFolder`-Model mit `Path`, `Name`, `PinnedAt`
- Pin/Unpin-Button in der Navigation-Bar (SymbolRegular.Pin24 / PinOff24)
- Favoriten-Liste in `AppSettings` speichern (max 20 Einträge)
- Anzeige als Liste unterhalb des TreeViews in der Sidebar oder als Dropdown (`SplitButton`)
- Drag & Drop: Ordner aus DataGrid auf Favoriten-Liste ziehen = anheften
- Rechtsklick auf Favorit → „Entfernen“ oder „In neuem Tab öffnen“

**UX-Vorteil:** Schnellzugriff auf Arbeitsordner ohne Navigation, personalisierbar.

---

## 8. Tastenkürzel-Cheat-Sheet

**Problem:** FluxDB hat viele Tastenkürzel (Alt+Links/Rechts/Hoch, Backspace, Enter, F5, Ctrl+F, F8, Ctrl+C/V/X, F2, Entf, etc.), aber keine Dokumentation davon in der App. Neue Nutzer kennen sie nicht.

**Lösung:** Ein Overlay/Popup (`Ctrl+K`), das alle verfügbaren Tastenkürzel in einer übersichtlichen Tabelle anzeigt.

**Umsetzung:**
- `cheatSheetPopup` als `Popup` oder `ContentDialog` mit semi-transparentem Overlay
- Tabelle mit Spalten: Tastenkürzel | Aktion | Kontext
- Kategorien: Navigation, Datei-Operationen, Suche, Allgemein
- `Esc` oder erneutes `Ctrl+K` schließt das Overlay
- `KeyBinding` auf `Ctrl+K` im `MainWindow`
- Dark-Mode-kompatibles Styling (passend zum aktuellen Theme)

**UX-Vorteil:** Entdeckbarkeit aller Shortcuts, kein Auswendiglernen nötig, professioneller Eindruck.

---

## 9. Lazy-Loading / Virtualisierung im DataGrid

**Problem:** Bei Ordnern mit 100.000+ Dateien wird das DataGrid träge — alle Zeilen werden beim ersten Laden gerendert, was zu langen Ladezeiten und hohem RAM-Verbrauch führt.

**Lösung:** UI-Virtualisierung aktivieren, sodass nur die sichtbaren Zeilen gerendert werden. Optional: gestaffeltes Laden (erst 500 Dateien anzeigen, Rest bei Scroll).

**Umsetzung:**
- `DataGrid.EnableRowVirtualization="True"` und `EnableColumnVirtualization="True"`
- `VirtualizingStackPanel.IsVirtualizing="True"` im `ItemsPanelTemplate`
- `VirtualizingStackPanel.VirtualizationMode="Recycling"` für bessere Scroll-Performance
- Fixed row height (`RowHeight="28"`) für korrekte Virtualisierung
- Optional: `DataGrid.ItemsSource` mit `CollectionView` und `DeferRefresh` für Batch-Updates
- Bei Extremfällen: Server-seitiges Paging via `VirtualizingPanel` mit `IList`-basiertem Data-Virtualization

**UX-Vorteil:** Flüssiges Scrollen auch bei riesigen Ordnern, sofortiges Öffnen ohne Wartezeit.

---

## 10. Spalten-Konfiguration

**Problem:** Nicht alle Spalten sind für jeden Nutzer relevant. Manche brauchen „Tags“ und „Größe“, andere nur „Name“ und „Datum“. Aktuell sind alle Spalten fix sichtbar.

**Lösung:** Rechtsklick auf den DataGrid-Header öffnet ein Kontextmenü mit Checkboxen zum Ein-/Ausblenden einzelner Spalten.

**Umsetzung:**
- `ContextMenu` auf `DataGridColumnHeader` via `ColumnHeaderStyle`
- Für jede Spalte ein `MenuItem` mit `IsCheckable="True"` und `IsChecked`-Binding an `Column.Visibility`
- `Visibility`-Binding: `Visible` wenn gecheckt, `Collapsed` wenn nicht
- `ColumnVisibilityModel` pro Spalte mit `IsVisible`-Property
- Zustand in `AppSettings` oder eigener `ColumnSettings`-Sektion speichern
- „Alle anzeigen“ / „Zurücksetzen“-Eintrag im Kontextmenü

**UX-Vorteil:** Personalisierbare Ansicht, weniger horizontales Scrollen, Fokus auf relevante Daten.

---

## 11. Toast-Benachrichtigungen

**Problem:** Aktionen wie Kopieren, Löschen, oder Index-Abschluss geben aktuell kein visuelles Feedback außer der Statusbar. Der Nutzer bemerkt nicht immer, dass eine Aktion erfolgreich war.

**Lösung:** Toast/Snackbar-Benachrichtigungen via `ISnackbarService` von WPF-UI für zeitkritische Feedbacks.

**Umsetzung:**
- `ISnackbarService` via DI registrieren (falls DI umgestellt wird) oder manuell instanziieren
- `SnackbarPresenter` im `MainWindow` XAML einbinden
- Benachrichtigungen für:
  - „5 Dateien kopiert“ (nach Ctrl+C)
  - „3 Dateien gelöscht“ (nach Delete)
  - „Indexierung abgeschlossen: 12.345 Dateien“ (nach Indexer-Fertigstellung)
  - „Ordner umbenannt“ (nach Rename)
  - „Fehler beim Löschen: datei.txt ist schreibgeschützt“
- `SnackbarOptions` mit Icon, Dauer (3-5 Sekunden), und optionalem Action-Button („Rückgängig“)
- `ControlAppearance` an aktuelles Theme anpassen (Dark/Light)

**UX-Vorteil:** Sofortiges, unaufdringliches Feedback — Nutzer weiß immer, was gerade passiert ist.

---

## Zusammenfassung aller Ideen

| # | Idee | Aufwand | Phase |
|---|---|---|---|
| 1 | Tag-Chips statt TextBox | Mittel | 2 |
| 2 | Ordnerbaum-Sidebar | Groß | 3 |
| 3 | Multi-Selektions-Info | Mittel | 2 |
| 4 | Themen-Verwaltung in Settings | Mittel | 1 |
| 5 | Icons ganz links im DataGrid | Klein | 1 |
| 6 | Sortier-Indikator | Klein | 4 |
| 7 | Favoriten/Pins | Mittel | 3 |
| 8 | Tastenkürzel-Cheat-Sheet | Klein | 1 |
| 9 | Lazy-Loading / Virtualisierung | Mittel | 4 |
| 10 | Spalten-Konfiguration | Klein | 4 |
| 11 | Toast-Benachrichtigungen | Mittel | 4 |

---

## Priorisierte Roadmap

```
Phase 1 (Quick Wins):   #5 (DataGrid-Icons), #4 (Themen-Verwaltung), #8 (Shortcut-Sheet)
Phase 2 (Core UX):      #1 (Tag-Chips), #3 (Multi-Select-Info)
Phase 3 (Navigation):   #2 (Ordnerbaum-Sidebar), #7 (Favoriten/Pins)
Phase 4 (Polish):       #6 (Sortier-Indikator), #10 (Spalten-Konfiguration), #11 (Toast-Benachrichtigungen)
```