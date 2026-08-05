# FluxDB UI Redesign — Plan

## 1. Status Quo

| Aspekt | Aktuell | Problem |
|--------|---------|---------|
| Framework | .NET Framework 4.7.2 (net472) | Keine modernen .NET-Libraries (WPF-UI, CommunityToolkit) |
| Architektur | Code-Behind (kein MVVM) | Schwer testbar, monolithisch |
| Styling | Inline-Styles in jedem Fenster dupliziert | Unwartbar, inkonsistent |
| Theme | Nur Dark-Theme, hartkodierte Farben | Kein Light-Mode, keine Theme-Engine |
| Icons | Segoe MDL2 Assets (Unicode-Codepoints) | Veraltet, limitiert |
| Layout | DataGrid + Detail-Panel | Funktional aber altbacken |
| Dialoge | Standard WPF Window (RenameDialog, RefreshDialog) | Kein modernes Look & Feel |
| Splash | Eigenes Window mit Transparenz-Trick | Nicht Windows 11-konform |

---

## 2. Zielbild

Eine moderne, Windows 11-artige File-Management-App mit:
- **Material Design 3 Ästhetik** via MaterialDesignInXAML (net472-kompatibel)
- **MVVM-Architektur** mit selbstgebautem BaseViewModel (INotifyPropertyChanged)
- **Dark/Light-Theme** mit nahtlosem Umschalten
- **Moderne Navigation** mit Breadcrumbs, Sidebar, Command-Bar
- **Responsive Layouts** mit GridSplittern, anpassbaren Panels
- **Konsistente Dialoge** über MaterialDesign DialogHost
- **Performance** durch Virtualisierung, DataGrid-Optimierung

---

## 3. Technologie-Stack (net472-kompatibel)

### 3.1 NuGet Packages

| Package | Version | Begründung |
|---------|---------|------------|
| `MaterialDesignThemes` | 4.9.0 | Material Design 3 für WPF, net472-kompatibel |
| `MaterialDesignColors` | 2.1.4 | Farbpaletten, Theme-Engine |
| `Newtonsoft.Json` | 13.0.3 | Bereits vorhanden |
| `System.Data.SQLite.Core` | 1.0.118.0 | Bereits vorhanden |

**Warum MaterialDesignInXAML?**
- Aktiv maintained, net472-Support
- Riesige Control-Library (Cards, Chips, Snackbar, DialogHost, DrawerHost)
- Integrierte Dark/Light-Theme-Engine
- PackIcon (1000+ Icons, kein Segoe MDL2 mehr nötig)
- Ripple-Effekte, Shadows, Transitions
- Produktionserprobt (viele Enterprise-Apps)

### 3.2 Architektur-Änderungen

```
Vorher (Code-Behind):              Nachher (MVVM):
┌──────────────────┐               ┌──────────────────┐
│ MainWindow.xaml  │               │ MainWindow.xaml   │
│ MainWindow.xaml.cs│               │ MainWindow.xaml.cs│ (minimal)
└──────────────────┘               └──────┬───────────┘
        │                                │ DataContext
        ▼                                ▼
┌──────────────────┐               ┌──────────────────┐
│ ~2000 Zeilen     │               │ MainViewModel.cs │
│ Logik im Code-   │               │ NavigationVM.cs  │
│ Behind           │               │ SettingsVM.cs    │
└──────────────────┘               └──────────────────┘
                                           │
                                           ▼
                                   ┌──────────────────┐
                                   │ Services (gleich) │
                                   └──────────────────┘
```

**Neue Namespace-Struktur:**
```
WPF/FluxDB/
├── App.xaml
├── App.xaml.cs
├── ViewModels/
│   ├── BaseViewModel.cs          ← INotifyPropertyChanged-Basis
│   ├── MainViewModel.cs          ← Hauptlogik
│   ├── SettingsViewModel.cs      ← Settings-Logik
│   ├── RenameViewModel.cs        ← RenameDialog-Logik
│   └── RefreshViewModel.cs       ← RefreshDialog-Logik
├── Views/
│   ├── MainWindow.xaml/.cs
│   ├── SettingsWindow.xaml/.cs
│   ├── SplashWindow.xaml/.cs
│   ├── RenameDialog.xaml/.cs
│   ├── RefreshDialog.xaml/.cs
│   └── Controls/                 ← Custom Controls
│       ├── FileCard.xaml/.cs
│       ├── TagChip.xaml/.cs
│       ├── PreviewPanel.xaml/.cs
│       └── BreadcrumbBar.xaml/.cs
├── Themes/
│   ├── Theme.xaml                ← Zentrale Theme-Ressourcen
│   ├── DarkTheme.xaml
│   ├── LightTheme.xaml
│   └── Styles.xaml               ← Globale Styles
├── Converters/
│   ├── BoolToVisibilityConverter.cs
│   ├── FileSizeConverter.cs
│   └── IconConverter.cs
├── Models/                       ← Unverändert
├── Services/                     ← Unverändert (nur kleine Anpassungen)
└── FluxDB.csproj
```

---

## 4. Fenster-für-Fenster Redesign

### 4.1 App.xaml — Globales Theme

**Änderungen:**
- MaterialDesignThemes in App.xaml ResourceDictionary mergen
- Dark/Light Theme-Umschaltung über `IThemeManager`
- Zentrale Styles für alle Basis-Controls
- `PaletteHelper` für Farb-Management

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <materialDesign:BundledTheme BaseTheme="Dark" 
                PrimaryColor="DeepPurple" SecondaryColor="Amber" />
            <ResourceDictionary Source="pack://application:,,,/Themes/Styles.xaml" />
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

### 4.2 SplashWindow — Moderner Splash-Screen

**Aktuell:** Transparentes Window mit manuellem Border-Radius, einfachem ProgressBar.

**Neu:**
- MaterialDesign `Card` mit `materialDesign:ShadowAssist.ShadowDepth="Depth5"`
- `PackIcon`-basiertes Logo statt PNG
- Animierter `ProgressBar` mit `IsIndeterminate`
- Subtiler `TextBlock` mit Lade-Status
- Sanfter Fade-In beim Start

**Layout:**
```
┌──────────────────────────────────────┐
│                                      │
│           [PackIcon: Logo]           │
│           FluxDB                     │
│           v1.x.x                     │
│                                      │
│   ┌──────────────────────────────┐   │
│   │ ████████████░░░░░░░░░░░░░░░░ │   │
│   └──────────────────────────────┘   │
│   Initialisiere Datenbank...         │
│                                      │
│              [Cancel]                │
└──────────────────────────────────────┘
```

### 4.3 MainWindow — Komplett-Neuaufbau

**Aktuell:** Grid mit 6 Rows, DataGrid, Side-Panel. Alles Code-Behind.

**Neu:** DrawerHost + Content-Grid mit 3 Zonen.

#### Zone 1: NavigationDrawer (links, einklappbar)
- Folder-Tree mit lazy loading
- Recent Folders Liste
- Pinned Folders
- Speicherplatz-Info (frei/belegt)

#### Zone 2: Hauptinhalt
**Header-Bar:**
```
[☰ Drawer] [◄ ► ▲ Nav] [📁 /Pfad/zum/Ordner ▾]  [🔍 Suchen...] [Filter ▾] [⚙]
```

**Content Panel (Card-basiert statt DataGrid):**
- **Listen-Ansicht:** `ListView` mit MaterialDesign `Card`-ItemTemplate
  - Jede Zeile: Icon + Name + Tags (Chips) + Größe + Datum
  - Hover: Subtiler Shadow-Effekt
  - Rechtsklick: MaterialDesign `PopupBox` Context-Menu
- **Grid-Ansicht (optional):** `WrapPanel` mit FileCards
  - Vorschaubild + Name + Typ-Badge
- **View-Umschalter:** [List ▦ Grid] Toggle-Buttons

#### Zone 3: Detail-Panel (rechts, einklappbar)
- **Preview:** `ContentControl` mit DataTemplate-Selektor (Image/PDF/Text/None)
- **Metadata:** Material `Card` mit:
  - Dateiname, Pfad, Größe, Datum
  - Tags als `Chip`-Liste (einzeln löschbar)
  - Notes als `TextBox` mit Material-Styling
- **Aktionen:** Open, Open Location, Delete Buttons

#### Status-Bar (unten)
- Snackbar-ähnlich für Meldungen
- File-Count Badge
- Indexing-Progress (Mini-ProgressBar)

**Grid-Layout:**
```
┌──────┬────────────────────────────────────────┬──────────┐
│      │ [Header: Logo, Breadcrumbs, Actions]   │          │
│      ├────────────────────────────────────────┤          │
│ Nav  │                                        │ Detail   │
│ Tree │  File List (Cards / DataGrid)          │ Panel    │
│      │                                        │          │
│      │                                        │          │
│      ├────────────────────────────────────────┤          │
│      │ [Status: 1.234 files · 45 GB]          │          │
└──────┴────────────────────────────────────────┴──────────┘
```

### 4.4 SettingsWindow — Modernes Settings

**Aktuell:** TabControl mit 2 Tabs, einfache Buttons.

**Neu:**
- MaterialDesign `ColorZone` für Header
- Vertikale Tab-Navigation links (Icon + Text) statt Horizontal-Tabs
- `Card`-basierte Setting-Sektionen
- Toggle-Switches statt Checkboxen
- `Expander` für fortgeschrittene Optionen

**Layout:**
```
┌──────────┬─────────────────────────────────────┐
│ ⚙ General│  General                              │
│          │  ┌─────────────────────────────────┐ │
│ 📦 Data  │  │ Appearance                       │ │
│          │  │ Theme: [Dark ▾]                  │ │
│ 📊 About │  │ Accent Color: [●●●●●●●●]        │ │
│          │  │ Version: 1.0.1                   │ │
│          │  └─────────────────────────────────┘ │
│          │  ┌─────────────────────────────────┐ │
│          │  │ Updates                          │ │
│          │  │ [✓] Auto-check on startup        │ │
│          │  │ Status: Up to date               │ │
│          │  └─────────────────────────────────┘ │
│          │              [Save]  [Cancel]        │
└──────────┴─────────────────────────────────────┘
```

### 4.5 RenameDialog — Moderner Dialog

**Aktuell:** Schmuckloses Window mit TextBox + OK/Cancel.

**Neu:**
- MaterialDesign `DialogHost` (modaler Dialog im MainWindow)
- Große `TextBox` mit Icon-Prefix (Datei-Icon)
- Preview des neuen Namens in Echtzeit
- Enter = OK, Escape = Cancel

**Layout:**
```
┌──────────────────────────────────┐
│  Rename                          │
│                                  │
│  📄 [Neuer Dateiname...........] │
│                                  │
│  Pfad: C:\Users\...\aktuell.txt  │
│                                  │
│              [Cancel]  [Rename]  │
└──────────────────────────────────┘
```

### 4.6 RefreshDialog — Moderner Dialog

**Aktuell:** Schmuckloses Window mit RadioButtons.

**Neu:**
- MaterialDesign `DialogHost`
- `RadioButton`-Liste mit Cards und Icons
- Folder-Picker mit Icon-Button

**Layout:**
```
┌──────────────────────────────────┐
│  Refresh Options                 │
│                                  │
│  ┌──────────────────────────────┐│
│  │ ● Entire Index               ││
│  │   Scan all files in root     ││
│  └──────────────────────────────┘│
│  ┌──────────────────────────────┐│
│  │ ○ Current Folder             ││
│  │   Only this view             ││
│  └──────────────────────────────┘│
│  ┌──────────────────────────────┐│
│  │ ○ Specific Folder            ││
│  │   [C:\Path\...]  [Browse]    ││
│  └──────────────────────────────┘│
│                                  │
│              [Cancel]  [Refresh] │
└──────────────────────────────────┘
```

---

## 5. Neue UI-Komponenten

### 5.1 TagChip Control
```xml
<UserControl x:Class="FluxDB.Views.Controls.TagChip">
    <materialDesign:Chip 
        Content="{Binding TagName}"
        Icon="{materialDesign:PackIcon Kind=Tag}"
        IsDeletable="True"
        DeleteClick="OnTagDelete"/>
</UserControl>
```

### 5.2 FileCard Control
```xml
<UserControl x:Class="FluxDB.Views.Controls.FileCard">
    <materialDesign:Card>
        <StackPanel>
            <Image Source="{Binding Thumbnail}" />
            <TextBlock Text="{Binding Name}" />
            <TagChip ItemsSource="{Binding Tags}" />
        </StackPanel>
    </materialDesign:Card>
</UserControl>
```

### 5.3 BreadcrumbBar Control
- Custom ItemsControl mit "/" Separator
- Jeder Breadcrumb ist ein klickbarer Button
- Overflow-Menü bei zu langen Pfaden

### 5.4 PreviewPanel Control
- DataTemplateSelector für Image/PDF/Text/None
- Zoom-Controls für Bilder
- Syntax-Highlighting für Code-Previews (einfach)

---

## 6. Theme-System

### 6.1 Zentrale Theme-Definition

```xml
<!-- Themes/Theme.xaml -->
<ResourceDictionary>
    <!-- Primär-Farben -->
    <SolidColorBrush x:Key="PrimaryColor" Color="{DynamicResource PrimaryHueMidBrush}" />
    <SolidColorBrush x:Key="SecondaryColor" Color="{DynamicResource SecondaryHueMidBrush}" />
    
    <!-- Surface-Farben (via MaterialDesign) -->
    <!-- MaterialDesignPaper, MaterialDesignCardBackground, etc. -->
    
    <!-- App-spezifische Farben -->
    <SolidColorBrush x:Key="FolderColor" Color="#DCB67A" />
    <SolidColorBrush x:Key="SuccessColor" Color="#4CAF50" />
    <SolidColorBrush x:Key="WarningColor" Color="#FF9800" />
    <SolidColorBrush x:Key="ErrorColor" Color="#F44336" />
</ResourceDictionary>
```

### 6.2 Theme-Umschaltung

```csharp
// In App.xaml.cs oder SettingsViewModel
var paletteHelper = new PaletteHelper();
var theme = paletteHelper.GetTheme();
theme.SetBaseTheme(isDark ? Theme.Dark : Theme.Light);
paletteHelper.SetTheme(theme);
```

### 6.3 Accent-Color-Picker
- 12 vordefinierte Material-Design-Farben
- Live-Vorschau beim Hovern
- Persistiert in `settings.json`

---

## 7. UX-Verbesserungen

### 7.1 Snackbar-Notifications
Statt MessageBox:
- Erfolg: "Datei kopiert" (grün)
- Fehler: "Kopieren fehlgeschlagen: Zugriff verweigert" (rot)
- Info: "Indizierung abgeschlossen: 1.234 Dateien" (blau)
- Auto-Dismiss nach 5 Sekunden

### 7.2 Animations & Transitions
- `materialDesign:TransitioningContent` für Tab-Wechsel
- Ripple-Effekte auf allen Buttons (automatisch via MaterialDesign)
- Sanftes Ein-/Ausblenden des Drawers
- ProgressBar-Animation beim Indexieren

### 7.3 Keyboard-Shortcuts (erweitert)
| Shortcut | Aktion |
|----------|--------|
| `Ctrl+B` | Sidebar toggle |
| `Ctrl+D` | Detail-Panel toggle |
| `Ctrl+Shift+N` | New Folder |
| `Ctrl+G` | Grid/List view toggle |
| `Ctrl+E` | Focus search |
| `F1` | Keyboard shortcuts help |

### 7.4 Drag & Drop (verbessert)
- Drop-Target-Highlight mit Material-Design-Effekt
- Drop-Zone-Indikator (welcher Ordner wird Ziel?)
- Visuelles Feedback: Icon-Animation beim Droppen

### 7.5 Empty-State
Wenn kein Ordner geladen:
- Große Illustration (PackIcon)
- "Ziehe einen Ordner hierher oder klicke 'Ordner öffnen'"
- Großer "Ordner öffnen" Button

---

## 8. Implementierungs-Phasen

### Phase 1: Foundation (≈ 4h)
1. MaterialDesignThemes NuGet installieren
2. `Themes/` Ordner + Theme.xaml anlegen
3. `App.xaml` auf MaterialDesign umstellen
4. `BaseViewModel.cs` erstellen
5. `Converters/` anlegen (BoolToVisibility, FileSize, Icon)

### Phase 2: Shell & Navigation (≈ 6h)
6. `MainViewModel.cs` — Grundgerüst
7. `MainWindow.xaml` — DrawerHost + Grid-Struktur
8. `Views/Controls/BreadcrumbBar.xaml`
9. Header-Bar mit Command-Buttons
10. Navigation-Tree in Sidebar

### Phase 3: File-Liste & Preview (≈ 8h)
11. `Views/Controls/FileCard.xaml` (Grid-View)
12. DataGrid → ListView-Migration (List-View)
13. View-Umschalter (List/Grid)
14. `Views/Controls/PreviewPanel.xaml` (Image/PDF/Text)
15. `Views/Controls/TagChip.xaml` + Tag-Editor
16. Detail-Panel mit Metadata, Tags, Notes

### Phase 4: Dialoge & Settings (≈ 4h)
17. `SettingsViewModel.cs` + `SettingsWindow.xaml` Redesign
18. RenameDialog → DialogHost
19. RefreshDialog → DialogHost
20. Theme-Umschaltung + Accent-Picker

### Phase 5: Polish (≈ 4h)
21. SplashWindow Redesign
22. Snackbar-Notifications
23. Empty-State
24. Shortcuts & Tooltips
25. Drag & Drop Verbesserung
26. Performance-Tuning (Virtualisierung)

### Phase 6: Testing & Cleanup (≈ 2h)
27. Build-Verifikation
28. Dark/Light Theme-Wechsel testen
29. Alle Dialoge durchtesten
30. Memory-Leaks prüfen (Event-Handler, DataContext)

---

## 9. Risiken & Mitigation

| Risiko | Impact | Mitigation |
|--------|--------|------------|
| **net472-Limitierung** — Keine modernen C# Features | Medium | BaseViewModel selbst bauen, Relays manuell |
| **MaterialDesign + DataGrid** — DataGrid-Styling in MD ist komplex | Medium | DataGrid nur für List-View; Grid-View mit ItemsControl |
| **Performance** — Card-basierte Views sind schwerer als DataGrid | Medium | UI-Virtualization, `VirtualizingStackPanel` |
| **Code-Behind → MVVM** — Große Refactoring-Änderung | High | Schrittweise: erst ViewModels, dann XAML-Bindings |
| **PDF Preview** — WebBrowser-Control in MaterialDesign | Low | Preview-Panel isoliert, funktioniert unabhängig |
| **SQLite Threading** — MVVM + async DB-Zugriffe | Medium | `Task.Run` + `Dispatcher.Invoke` wie bisher |

---

## 10. Offene Fragen

1. **Grid-View (Card-Ansicht) optional?** Oder Standard?
   → Optional, Toggle-Button. Default: List-View (wie aktuell).

2. **Light-Theme als Default?** Oder Dark?
   → Dark-Theme als Default (wie aktuell), Light optional.

3. **Sidebar immer sichtbar?** Oder einklappbar?
   → Einklappbar, gemerkt in Settings.

4. **Datei-Icons weiterhin Unicode?** Oder PackIcon?
   → PackIcon wo möglich, Fallback auf Unicode für Custom-Types.

5. **Rückwärtskompatibilität der Settings?**
   → `settings.json` bleibt gleich, nur neue Keys hinzufügen.

---

## 11. Ziel

| Metrik | Aktuell | Ziel |
|--------|---------|------|
| LOC in Code-Behind | ~2000 | <200 pro View |
| Style-Duplikation | 3 Fenster | 0 (zentrales Theme) |
| Theme-Unterstützung | Nur Dark | Dark + Light + Accent |
| Dialog-Konsistenz | Unterschiedlich | Einheitlich (DialogHost) |
| Fenster-Layout | 6 Grid-Rows | Drawer + Content + Detail |
| UI-Library | Keine | MaterialDesignInXAML |
| Architektur | Code-Behind | MVVM |
| Animations | Keine | Ripple, Transitions, Fade |

---

**Letzte Aktualisierung:** 2026-08-05
**Status:** Planung — Umsetzung folgt