# FluxDB UI Redesign — WPF-UI (lepoco/wpfui)

> **Voraussetzung:** [PLAN-migration-net10.md](./PLAN-migration-net10.md) muss zuerst abgeschlossen sein.
> WPF-UI 4.x braucht .NET 8+.

## 1. Status Quo

| Aspekt | Aktuell | Problem |
|--------|---------|---------|
| Framework | net472 | Keine modernen NuGet-Packages |
| UI-Library | Keine | Inline-Styles, manuelles Dark-Theme |
| Architektur | Code-Behind (2000+ Zeilen in MainWindow.xaml.cs) | Untestbar, monolithisch |
| Icons | Segoe MDL2 Assets Unicode-Codepoints | Limitiert, veraltet |
| Theme | Nur Dark, hartcodiert | Kein Light-Mode |
| Dialoge | Standard WPF Window | Kein modernes Look & Feel |
| Splash | Transparentes Window mit Border-Radius-Trick | Fragil |

## 2. Zielbild

Eine **Windows 11-native** File-Management-App mit Fluent Design:
- `FluentWindow` als Hauptfenster mit Mica-Hintergrund
- `NavigationView` für Sidebar-Navigation
- `SymbolIcon` (Fluent System Icons) statt Unicode-Glyphs
- `InfoBar` für Status-Meldungen
- `Snackbar` für Toast-Notifications
- `ContentDialog` für modale Dialoge
- Dark/Light-Theme mit System-Sync
- MVVM mit `CommunityToolkit.Mvvm`
- `Microsoft.Extensions.DependencyInjection` für DI

## 3. Technologie-Stack

### 3.1 NuGet Packages

| Package | Version | Zweck |
|---------|---------|-------|
| `WPF-UI` | 4.2.0 | Fluent Design UI Framework |
| `CommunityToolkit.Mvvm` | 8.4.0 | MVVM Source Generators |
| `Microsoft.Extensions.DependencyInjection` | 10.0.0 | Dependency Injection |
| `Microsoft.Extensions.Hosting` | 10.0.0 | Hosting, Konfiguration, Logging |
| `System.Data.SQLite.Core` | 1.0.119.0 | SQLite (bereits vorhanden, aktualisiert) |

**Warum CommunityToolkit.Mvvm?**
- Source Generators für `[ObservableProperty]`, `[RelayCommand]`
- Kein Boilerplate mehr für INotifyPropertyChanged
- Messenger für ViewModel-Kommunikation
- Offiziell von Microsoft

### 3.2 WPF-UI Features im Überblick

| Feature | WPF-UI Control | Nutzung in FluxDB |
|---------|---------------|-------------------|
| Hauptfenster | `FluentWindow` | MainWindow, SettingsWindow |
| Navigation | `NavigationView` | Seiten-Navigation, Ordner-Baum |
| Icons | `SymbolIcon` | Datei-Icons, Toolbar-Buttons |
| Breadcrumbs | `BreadcrumbBar` | Pfad-Navigation |
| Info-Leiste | `InfoBar` | Status-Meldungen |
| Toast | `Snackbar` | Erfolg/Fehler-Meldungen |
| Dialoge | `ContentDialog` | Rename, Refresh, Delete-Bestätigung |
| Cards | `CardControl` | Datei-Ansicht (Grid-Mode) |
| Progress | `ProgressBar`, `ProgressRing` | Indizierung |
| Text | `TextBox`, `AutoSuggestBox` | Suche, Tags, Notes |
| Theme | `ThemeManager` | Dark/Light + Accent |

## 4. Neue Projektstruktur

```
WPF/FluxDB/
├── App.xaml
├── App.xaml.cs                    ← DI-Setup, Theme-Init
├── GlobalUsings.cs
├── FluxDB.csproj
├── AssemblyInfo.cs
├── appsettings.json               ← App-Konfiguration
│
├── Models/
│   ├── AppSettings.cs             ← Unverändert (ggf. record)
│   ├── FileEntry.cs               ← INotifyPropertyChanged → [ObservableProperty]
│   └── GitHubRelease.cs           ← Unverändert
│
├── Services/
│   ├── DatabaseService.cs         ← Unverändert (nur DI-Registrierung)
│   ├── IndexerService.cs          ← Geringfügig: IProgress<T> statt Events
│   ├── LoggingService.cs          ← Wird durch ILogger<T> ersetzt
│   ├── SettingsService.cs         ← Wird durch IOptions<T> ersetzt
│   ├── ExportService.cs           ← Unverändert
│   └── ImportService.cs           ← Unverändert
│
├── ViewModels/
│   ├── MainViewModel.cs           ← Hauptlogik (aus MainWindow.xaml.cs)
│   ├── SettingsViewModel.cs       ← Settings-Logik
│   ├── RenameViewModel.cs         ← Rename-Dialog
│   ├── RefreshViewModel.cs        ← Refresh-Dialog
│   ├── NavigationViewModel.cs     ← Breadcrumb + History
│   └── DashboardViewModel.cs     ← Startseite (Empty-State)
│
├── Views/
│   ├── MainWindow.xaml/.cs
│   ├── SettingsWindow.xaml/.cs
│   ├── SplashWindow.xaml/.cs
│   └── Pages/
│       ├── DashboardPage.xaml     ← Empty-State / Startseite
│       ├── FileBrowserPage.xaml   ← Dateiliste + Preview
│       └── LogsPage.xaml         ← Eingebauter Log-Viewer
│
├── Controls/
│   ├── FileCard.xaml/.cs          ← Datei-Karte für Grid-View
│   ├── TagChip.xaml/.cs           ← Tag-Chip-Control
│   ├── PreviewPanel.xaml/.cs      ← Vorschau (Image/PDF/Text)
│   └── BreadcrumbPath.xaml/.cs    ← Custom Breadcrumb
│
├── Converters/
│   ├── BoolToVisibilityConverter.cs
│   ├── FileSizeConverter.cs
│   ├── FileTypeToIconConverter.cs
│   └── DateTimeToRelativeConverter.cs
│
└── Helpers/
    ├── VersionHelper.cs           ← Unverändert
    └── ServiceExtensions.cs       ← DI-Registrierung
```

## 5. Fenster-für-Fenster Redesign

### 5.1 App.xaml — WPF-UI Bootstrap

```xml
<Application x:Class="FluxDB.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepoco.com/wpfui/2022/xaml"
             Startup="OnStartup"
             Exit="OnExit">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ui:ThemesDictionary Theme="Dark" />
                <ui:ControlsDictionary />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

**App.xaml.cs:**
```csharp
public partial class App : Application
{
    private IHost _host;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<SettingsService>();
                services.AddSingleton<DatabaseService>();
                services.AddSingleton<IndexerService>();
                services.AddSingleton<ExportService>();
                services.AddSingleton<ImportService>();
                
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<NavigationViewModel>();
                
                services.AddSingleton<MainWindow>();
                services.AddSingleton<SettingsWindow>();
            })
            .Build();

        // Theme
        var theme = ThemeManager.GetAppTheme();
        // Load from settings: theme == "Light" ? ThemeType.Light : ThemeType.Dark

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
}
```

### 5.2 MainWindow — FluentWindow mit NavigationView

**Layout:**
```
┌─────────────────────────────────────────────────────────┐
│ FluxDB                        [🔍 Suchen...] [⚙] [— □ X]│  ← TitleBar
├──────────┬──────────────────────────────────────────────┤
│          │                                              │
│  📁 Home │  /Dokumente/Projekte/FluxDB          [+]    │  ← BreadcrumbBar
│  📂 Docs │  ┌──────────────────────────────────────┐   │
│  🖼 Bilder│  │ ⬜ Name        │ Typ  │ Größe │ Datum │   │
│  🎵 Musik │  │ ⬜ readme.md   │ MD   │ 2 KB  │ 05.08 │   │
│  🎬 Video │  │ ⬜ src/        │ Ord. │       │ 04.08 │   │
│  ⚙ Archiv│  │ ⬜ FluxDB.sln  │ SLN  │ 3 KB  │ 03.08 │   │
│          │  └──────────────────────────────────────┘   │
│  ─────── │                                              │
│  📌 Pin  │  ┌─────────── Detail ───────────┐           │
│  FluxDB  │  │ 📄 readme.md                  │           │
│          │  │ C:\Users\...\readme.md         │           │
│          │  │ 2.1 KB · 05.08.2026 14:32     │           │
│          │  │                               │           │
│          │  │ Tags: [flux] [db] [docs] [+]  │           │
│          │  │ Notes: Projekt-Dokumentation   │           │
│          │  │                               │           │
│          │  │ [Open] [Open Location]         │           │
│          │  └───────────────────────────────┘           │
├──────────┴──────────────────────────────────────────────┤
│ ⬡ 1.234 Dateien  ·  45 GB  ·  Zuletzt indiziert: 14:32 │  ← StatusBar
└─────────────────────────────────────────────────────────┘
```

**XAML-Struktur:**
```xml
<ui:FluentWindow
    x:Class="FluxDB.Views.MainWindow"
    xmlns:ui="http://schemas.lepoco.com/wpfui/2022/xaml"
    ExtendsContentIntoTitleBar="True"
    WindowCornerPreference="Round"
    WindowBackdropType="Mica">

    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <!-- Navigation Sidebar -->
        <ui:NavigationView
            Grid.Column="0"
            IsPaneOpen="True"
            PaneDisplayMode="LeftCompact"
            IsBackButtonVisible="Collapsed"
            MenuItemsSource="{Binding NavigationItems}"
            FooterMenuItemsSource="{Binding FooterItems}">
            
            <ui:NavigationView.ContentOverlay>
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>

                    <!-- Breadcrumb Bar -->
                    <ui:BreadcrumbBar
                        Grid.Row="0"
                        ItemsSource="{Binding Breadcrumbs}" />

                    <!-- Content Area -->
                    <Frame Grid.Row="1" 
                           x:Name="RootFrame"
                           NavigationUIVisibility="Hidden" />

                    <!-- Status Bar -->
                    <ui:InfoBar
                        Grid.Row="2"
                        IsOpen="{Binding IsInfoBarOpen}"
                        Title="{Binding StatusMessage}"
                        Severity="{Binding StatusSeverity}" />
                </Grid>
            </ui:NavigationView.ContentOverlay>
        </ui:NavigationView>
    </Grid>
</ui:FluentWindow>
```

**Navigation Items:**
```csharp
// MainViewModel.cs
public ObservableCollection<NavigationViewItem> NavigationItems { get; } = new()
{
    new() { Content = "Home", Icon = new SymbolIcon(SymbolRegular.Home24), Tag = "home" },
    new() { Content = "Documents", Icon = new SymbolIcon(SymbolRegular.Document24), Tag = "docs" },
    new() { Content = "Images", Icon = new SymbolIcon(SymbolRegular.Image24), Tag = "images" },
    new() { Content = "Audio", Icon = new SymbolIcon(SymbolRegular.MusicNote224), Tag = "audio" },
    new() { Content = "Video", Icon = new SymbolIcon(SymbolRegular.Video24), Tag = "video" },
    new() { Content = "Archives", Icon = new SymbolIcon(SymbolRegular.Box24), Tag = "archives" },
};

public ObservableCollection<NavigationViewItem> FooterItems { get; } = new()
{
    new() { Content = "Settings", Icon = new SymbolIcon(SymbolRegular.Settings24), Tag = "settings" },
};
```

### 5.3 FileBrowserPage — Die Haupt-Ansicht

```xml
<Page x:Class="FluxDB.Views.Pages.FileBrowserPage">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>   <!-- Toolbar -->
            <RowDefinition Height="*"/>      <!-- File List -->
            <RowDefinition Height="Auto"/>   <!-- Detail Panel (toggle) -->
        </Grid.RowDefinitions>

        <!-- Toolbar -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,12">
            <ui:Button Icon="{ui:SymbolIcon FolderOpen24}" Content="Open Folder" />
            <ui:Button Icon="{ui:SymbolIcon ArrowSync24}" Content="Refresh" />
            <ui:ToggleButton Icon="{ui:SymbolIcon List24}" IsChecked="True" />
            <ui:ToggleButton Icon="{ui:SymbolIcon Grid24}" />
            <ui:AutoSuggestBox Width="300" PlaceholderText="Search files..." />
        </StackPanel>

        <!-- File List -->
        <ui:ListView Grid.Row="1"
                     ItemsSource="{Binding Files}"
                     SelectedItem="{Binding SelectedFile, Mode=TwoWay}"
                     VirtualizingStackPanel.IsVirtualizing="True">
            <ui:ListView.View>
                <GridView>
                    <GridViewColumn Header="" Width="40">
                        <GridViewColumn.CellTemplate>
                            <DataTemplate>
                                <ui:SymbolIcon 
                                    Symbol="{Binding Icon}" 
                                    Foreground="{Binding IconColor}" />
                            </DataTemplate>
                        </GridViewColumn.CellTemplate>
                    </GridViewColumn>
                    <GridViewColumn Header="Name" Width="250" 
                        DisplayMemberBinding="{Binding Name}" />
                    <GridViewColumn Header="Type" Width="80" 
                        DisplayMemberBinding="{Binding TypeDisplay}" />
                    <GridViewColumn Header="Size" Width="100" 
                        DisplayMemberBinding="{Binding SizeDisplay}" />
                    <GridViewColumn Header="Modified" Width="140" 
                        DisplayMemberBinding="{Binding ModifiedAt}" />
                    <GridViewColumn Header="Tags" Width="150">
                        <GridViewColumn.CellTemplate>
                            <DataTemplate>
                                <ItemsControl ItemsSource="{Binding Tags}">
                                    <ItemsControl.ItemsPanel>
                                        <ItemsPanelTemplate>
                                            <WrapPanel/>
                                        </ItemsPanelTemplate>
                                    </ItemsControl.ItemsPanel>
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <Border CornerRadius="2" 
                                                    Background="{DynamicResource AccentFillColorDefaultBrush}"
                                                    Padding="4,2" Margin="1">
                                                <TextBlock Text="{Binding}" FontSize="11" />
                                            </Border>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </DataTemplate>
                        </GridViewColumn.CellTemplate>
                    </GridViewColumn>
                </GridView>
            </ui:ListView.View>
        </ui:ListView>

        <!-- Detail Panel -->
        <Border Grid.Row="2" Margin="0,12,0,0" 
                Visibility="{Binding IsDetailOpen, Converter={StaticResource BoolToVisibility}}">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="2*"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>

                <!-- Preview -->
                <local:PreviewPanel Grid.Column="0" 
                    FilePath="{Binding SelectedFile.Path}" />

                <!-- Metadata -->
                <StackPanel Grid.Column="1" Margin="16,0,0,0">
                    <TextBlock Text="Name" Style="{StaticResource CaptionTextBlockStyle}" />
                    <TextBlock Text="{Binding SelectedFile.Name}" />
                    
                    <TextBlock Text="Path" Margin="0,12,0,0" />
                    <TextBox Text="{Binding SelectedFile.Path}" IsReadOnly="True" />
                    
                    <TextBlock Text="Tags" Margin="0,12,0,0" />
                    <TextBox Text="{Binding SelectedFile.TagsText}" />
                    
                    <TextBlock Text="Notes" Margin="0,12,0,0" />
                    <TextBox Text="{Binding SelectedFile.Note}" 
                             AcceptsReturn="True" Height="100" />
                    
                    <ui:Button Content="Save" Margin="0,12,0,0" />
                </StackPanel>
            </Grid>
        </Border>
    </Grid>
</Page>
```

### 5.4 DashboardPage — Empty State / Home

```xml
<Page x:Class="FluxDB.Views.Pages.DashboardPage">
    <Grid VerticalAlignment="Center" HorizontalAlignment="Center">
        <StackPanel HorizontalAlignment="Center">
            <ui:SymbolIcon Symbol="FolderOpen48" 
                           Width="96" Height="96" 
                           Foreground="{DynamicResource AccentFillColorDefaultBrush}" />
            <TextBlock Text="FluxDB" 
                       FontSize="28" FontWeight="SemiBold" 
                       HorizontalAlignment="Center" Margin="0,16,0,8" />
            <TextBlock Text="Drop a folder or click to get started" 
                       FontSize="14" Opacity="0.7" 
                       HorizontalAlignment="Center" Margin="0,0,0,24" />
            <ui:Button Content="Open Folder" 
                       Icon="{ui:SymbolIcon FolderOpen24}" 
                       Appearance="Accent" 
                       Width="200" 
                       HorizontalAlignment="Center" />
            
            <TextBlock Text="Recent Folders" 
                       FontSize="16" FontWeight="SemiBold" 
                       Margin="0,32,0,12" />
            <ItemsControl ItemsSource="{Binding RecentFolders}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <ui:Button Content="{Binding}" 
                                   Appearance="Transparent" 
                                   Icon="{ui:SymbolIcon Folder24}" />
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
    </Grid>
</Page>
```

### 5.5 SettingsWindow — FluentWindow mit Navigation

```xml
<ui:FluentWindow
    x:Class="FluxDB.Views.SettingsWindow"
    Width="700" Height="500"
    WindowBackdropType="Mica">

    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="200"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <!-- Settings Navigation -->
        <ui:NavigationView
            Grid.Column="0"
            PaneDisplayMode="Left"
            IsPaneToggleButtonVisible="False"
            IsBackButtonVisible="Collapsed">
            <ui:NavigationView.MenuItems>
                <ui:NavigationViewItem Content="General" 
                    Icon="{ui:SymbolIcon Settings24}" Tag="general" />
                <ui:NavigationViewItem Content="Appearance" 
                    Icon="{ui:SymbolIcon PaintBrush24}" Tag="appearance" />
                <ui:NavigationViewItem Content="Data" 
                    Icon="{ui:SymbolIcon Database24}" Tag="data" />
                <ui:NavigationViewItem Content="About" 
                    Icon="{ui:SymbolIcon Info24}" Tag="about" />
            </ui:NavigationView.MenuItems>
        </ui:NavigationView>

        <!-- Content Area -->
        <Frame Grid.Column="1" x:Name="SettingsFrame" />
    </Grid>
</ui:FluentWindow>
```

**Settings Pages:**
- `GeneralPage` — Version, Auto-Update
- `AppearancePage` — Theme (Dark/Light/System), Accent Color
- `DataPage` — Export/Import
- `AboutPage` — Version, GitHub Link

### 5.6 SplashWindow — Moderner Splash

```xml
<ui:FluentWindow
    x:Class="FluxDB.Views.SplashWindow"
    Width="400" Height="300"
    WindowBackdropType="Mica"
    WindowCornerPreference="Round"
    ExtendsContentIntoTitleBar="True">

    <ui:TitleBar Visibility="Collapsed" />

    <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center">
        <ui:SymbolIcon Symbol="Database24" 
                       Width="64" Height="64" 
                       Foreground="{DynamicResource AccentFillColorDefaultBrush}" />
        <TextBlock Text="FluxDB" 
                   FontSize="32" FontWeight="Bold" 
                   HorizontalAlignment="Center" Margin="0,16,0,4" />
        <TextBlock Text="{Binding Version}" 
                   FontSize="12" Opacity="0.6" 
                   HorizontalAlignment="Center" Margin="0,0,0,24" />
        <ui:ProgressRing IsActive="True" Width="32" Height="32" />
        <TextBlock Text="{Binding StatusMessage}" 
                   FontSize="13" 
                   HorizontalAlignment="Center" Margin="0,12,0,0" />
    </StackPanel>
</ui:FluentWindow>
```

### 5.7 ContentDialog für Rename/Refresh/Delete

```csharp
// In MainViewModel.cs
[RelayCommand]
private async Task RenameFile()
{
    var dialog = new ContentDialog
    {
        Title = "Rename",
        Content = new TextBox { Text = SelectedFile.Name },
        PrimaryButtonText = "Rename",
        CloseButtonText = "Cancel"
    };
    
    var result = await dialog.ShowAsync();
    if (result == ContentDialogResult.Primary)
    {
        // Rename logic
    }
}
```

## 6. MVVM mit CommunityToolkit.Mvvm

### 6.1 BaseViewModel (nicht mehr nötig!)

CommunityToolkit.Mvvm generiert den Code:

```csharp
public partial class MainViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly IndexerService _indexer;
    private readonly ISnackbarService _snackbar;

    [ObservableProperty]
    private ObservableCollection<FileEntry> _files = new();

    [ObservableProperty]
    private FileEntry _selectedFile;

    [ObservableProperty]
    private bool _isIndexing;

    public MainViewModel(DatabaseService db, IndexerService indexer, ISnackbarService snackbar)
    {
        _db = db;
        _indexer = indexer;
        _snackbar = snackbar;
    }

    [RelayCommand]
    private async Task OpenFolder()
    {
        IsIndexing = true;
        try
        {
            var files = await Task.Run(() => _db.GetAllFiles());
            Files = new ObservableCollection<FileEntry>(files);
            _snackbar.Show("Index loaded", "1.234 files", ControlAppearance.Success);
        }
        catch (Exception ex)
        {
            _snackbar.Show("Error", ex.Message, ControlAppearance.Danger);
        }
        finally
        {
            IsIndexing = false;
        }
    }

    [RelayCommand]
    private void DeleteFile()
    {
        // ...
    }
}
```

**Was CommunityToolkit.Mvvm generiert:**
- `Files` Property → `ObservableProperty` generiert INotifyPropertyChanged
- `OpenFolderCommand` → `RelayCommand` generiert `ICommand`
- `DeleteFileCommand` → dito
- Alles Partial → du schreibst das Feld, der Generator den Rest

### 6.2 DI-Registrierung

```csharp
// Helpers/ServiceExtensions.cs
public static class ServiceExtensions
{
    public static IServiceCollection AddFluxDB(this IServiceCollection services)
    {
        // Services
        services.AddSingleton<SettingsService>();
        services.AddSingleton<DatabaseService>();
        services.AddSingleton<IndexerService>();
        services.AddSingleton<ExportService>();
        services.AddSingleton<ImportService>();
        
        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<NavigationViewModel>();
        
        // Windows
        services.AddSingleton<MainWindow>();
        services.AddSingleton<SettingsWindow>();
        
        // WPF-UI Services
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();
        
        return services;
    }
}
```

## 7. WPF-UI Theme-System

### 7.1 Theme-Initialisierung

```csharp
// App.xaml.cs
private void ApplyTheme(string themeName)
{
    var theme = themeName switch
    {
        "Light" => ThemeType.Light,
        "Dark" => ThemeType.Dark,
        _ => ThemeType.Dark
    };
    
    ThemeManager.Apply(theme);
    ThemeManager.ApplySystemTheme(); // Windows-System-Theme folgen
}
```

### 7.2 Accent Color

```csharp
// In SettingsViewModel
public void ChangeAccent(Color accentColor)
{
    ThemeManager.Apply(
        ThemeManager.GetAppTheme(),
        ThemeManager.GetSystemTheme(),
        accentColor
    );
}
```

WPF-UI bietet 12 vordefinierte Accent-Farben (wie Windows 11 Personalization).

## 8. Snackbar-Service

```csharp
// In jedem ViewModel via DI
public partial class MainViewModel
{
    private readonly ISnackbarService _snackbar;

    private void ShowSuccess(string message)
    {
        _snackbar.Show(
            "Success",
            message,
            ControlAppearance.Success,
            new SymbolIcon(SymbolRegular.CheckmarkCircle24),
            TimeSpan.FromSeconds(4)
        );
    }

    private void ShowError(string message)
    {
        _snackbar.Show(
            "Error",
            message,
            ControlAppearance.Danger,
            new SymbolIcon(SymbolRegular.ErrorCircle24),
            TimeSpan.FromSeconds(6)
        );
    }
}
```

## 9. Keyboard Shortcuts

```csharp
// MainWindow.xaml.cs
protected override void OnKeyDown(KeyEventArgs e)
{
    base.OnKeyDown(e);
    
    var vm = DataContext as MainViewModel;
    
    switch (e.Key)
    {
        case Key.F5:
            vm.RefreshCommand.Execute(null);
            break;
        case Key.F2:
            vm.RenameCommand.Execute(null);
            break;
        case Key.Delete:
            vm.DeleteCommand.Execute(null);
            break;
        case Key.B when Keyboard.Modifiers == ModifierKeys.Control:
            vm.ToggleSidebarCommand.Execute(null);
            break;
        case Key.D when Keyboard.Modifiers == ModifierKeys.Control:
            vm.ToggleDetailPanelCommand.Execute(null);
            break;
        case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
            vm.FocusSearchCommand.Execute(null);
            break;
    }
}
```

## 10. Drag & Drop

```csharp
// MainWindow.xaml.cs
private void MainWindow_Drop(object sender, DragEventArgs e)
{
    if (e.Data.GetDataPresent(DataFormats.FileDrop))
    {
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        var folder = files.FirstOrDefault();
        if (Directory.Exists(folder))
        {
            (DataContext as MainViewModel)?.OpenFolderCommand.Execute(folder);
        }
    }
}
```

## 11. Preview Panel

```xml
<!-- Controls/PreviewPanel.xaml -->
<UserControl x:Class="FluxDB.Views.Controls.PreviewPanel">
    <Grid>
        <!-- Image Preview -->
        <Image x:Name="ImagePreview" 
               Source="{Binding ImageSource}" 
               Visibility="Collapsed" />
        
        <!-- PDF Preview via WebView2 -->
        <wv2:WebView2 x:Name="PdfPreview" 
                       Visibility="Collapsed" />
        
        <!-- Text Preview -->
        <TextBox x:Name="TextPreview" 
                 IsReadOnly="True" 
                 Visibility="Collapsed" />
        
        <!-- No Preview -->
        <ui:SymbolIcon Symbol="EyeOff24" 
                       Visibility="Collapsed" 
                       x:Name="NoPreview" />
    </Grid>
</UserControl>
```

## 12. Icons

WPF-UI verwendet **Fluent System Icons** (SymbolRegular/SymbolFilled enum):

| Dateityp | SymbolIcon |
|----------|-----------|
| Ordner | `SymbolRegular.Folder24` |
| Bild | `SymbolRegular.Image24` |
| Audio | `SymbolRegular.MusicNote224` |
| Video | `SymbolRegular.Video24` |
| PDF | `SymbolRegular.DocumentPdf24` |
| Word | `SymbolRegular.Document24` |
| Excel | `SymbolRegular.DocumentData24` |
| ZIP | `SymbolRegular.Box24` |
| Code | `SymbolRegular.Code24` |
| Unbekannt | `SymbolRegular.Document24` |

**Mapping in FileEntry:**
```csharp
public partial class FileEntry
{
    public SymbolRegular Icon => _extension?.ToLower() switch
    {
        ".jpg" or ".png" or ".gif" => SymbolRegular.Image24,
        ".mp3" or ".wav" => SymbolRegular.MusicNote224,
        ".mp4" or ".avi" => SymbolRegular.Video24,
        ".pdf" => SymbolRegular.DocumentPdf24,
        ".zip" or ".rar" => SymbolRegular.Box24,
        ".cs" or ".js" or ".py" => SymbolRegular.Code24,
        _ => IsFolder ? SymbolRegular.Folder24 : SymbolRegular.Document24
    };
}
```

## 13. Implementierungs-Phasen

### Phase 1: Foundation (≈ 3h)
1. WPF-UI + CommunityToolkit.Mvvm NuGet installieren
2. `App.xaml` auf WPF-UI Bootstrap umstellen
3. DI mit `Microsoft.Extensions.Hosting` einrichten
4. `GlobalUsings.cs` anlegen
5. `Helpers/ServiceExtensions.cs` mit DI-Registrierung

### Phase 2: Shell & Navigation (≈ 4h)
6. `MainWindow.xaml` → `FluentWindow` mit `NavigationView`
7. `MainViewModel.cs` — Grundgerüst mit `[ObservableProperty]`
8. `DashboardPage` (Empty State / Home)
9. `NavigationViewModel` — Breadcrumbs + History
10. Theme-Umschaltung (Dark/Light)

### Phase 3: File Browser (≈ 6h)
11. `FileBrowserPage.xaml` — ListView mit GridView
12. `FileEntry` → `[ObservableProperty]` für Properties
13. `MainViewModel` — File-Loading, Filtering, Sorting
14. `PreviewPanel` Control
15. Detail-Panel mit Tags, Notes
16. `FileCard` Control für Grid-View (optional)

### Phase 4: Dialoge & Settings (≈ 4h)
17. `SettingsWindow.xaml` → `FluentWindow` mit Navigation
18. Settings Pages (General, Appearance, Data, About)
19. `ContentDialog` für Rename, Refresh, Delete
20. `SnackbarService` Integration

### Phase 5: Splash & Polish (≈ 3h)
21. `SplashWindow.xaml` → `FluentWindow` mit `ProgressRing`
22. Drag & Drop
23. Keyboard Shortcuts
24. `InfoBar` für Status-Meldungen
25. Leere Zustände, Loading-States

### Phase 6: Testing & Cleanup (≈ 2h)
26. Build-Verifikation
27. Dark/Light Theme-Wechsel testen
28. Alle Dialoge + Snackbars testen
29. DI-Container validieren
30. Alter Code aufräumen (nicht mehr benötigte Styles, Code-Behind)

**Gesamtaufwand: ≈ 22h**

## 14. Was fliegt raus?

| Datei | Grund |
|-------|-------|
| `RenameDialog.xaml/.cs` | Ersetzt durch `ContentDialog` |
| `RefreshDialog.xaml/.cs` | Ersetzt durch `ContentDialog` |
| `SplashWindow.xaml/.cs` (alt) | Vollständig neu geschrieben |
| `SettingsWindow.xaml/.cs` (alt) | Vollständig neu geschrieben |
| `MainWindow.xaml` (alt) | Vollständig neu geschrieben |
| `MainWindow.xaml.cs` (alt, ~2000 Zeilen) | Aufgeteilt in ViewModels |
| Alle Inline-Styles in Window.Resources | Ersetzt durch WPF-UI Theme |
| Segoe MDL2 Unicode Icons | Ersetzt durch SymbolIcon |

## 15. Risiken

| Risiko | Impact | Mitigation |
|--------|--------|------------|
| **WPF-UI + DataGrid** — WPF-UI hat kein eigenes DataGrid | Low | Standard WPF ListView/GridView, WPF-UI-styled |
| **WebView2 für PDF** — Benötigt WebView2 Runtime | Medium | Fallback auf alten WebBrowser oder kein Preview |
| **MVVM-Lernkurve** | Medium | CommunityToolkit.Mvvm ist extrem einfach (Source Generators) |
| **DI-Komplexität** | Low | `Microsoft.Extensions.DependencyInjection` ist Standard |
| **Performance** — lvvm vs Code-Behind | Low | WPF-UI ist performant, Virtualisierung bleibt |
| **LoggingService-Migration** | Medium | `ILogger<T>` ist Standard, aber aufwändig → LoggingService vorerst behalten |

## 16. Vorher/Nachher

| Metrik | Vorher | Nachher |
|--------|--------|---------|
| UI-Library | Keine | WPF-UI 4.2 |
| Architektur | Code-Behind | MVVM (CommunityToolkit) |
| Code-Behind LOC | ~2500 | ~300 |
| ViewModel LOC | 0 | ~1500 |
| Theme | Dark only | Dark/Light/System + 12 Accents |
| Icons | Unicode Glyphs | Fluent System Icons (SymbolIcon) |
| Dialoge | 3 separate Windows | ContentDialog (inline) |
| Notifications | MessageBox | Snackbar + InfoBar |
| Fenster-Design | Standard WPF Window | FluentWindow (Mica, Rounded Corners) |
| DI | Keine | Microsoft.Extensions.DependencyInjection |
| Testbarkeit | 0 | Gut (ViewModels unabhängig testbar) |

---

**Letzte Aktualisierung:** 2026-08-05
**Status:** Planung — abhängig von PLAN-migration-net10.md