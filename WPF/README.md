# WPF-Ordner

Dieser Ordner bündelt die Windows-Desktopkomponenten von FluxDB.

## Inhalte

- [FluxDB](FluxDB/) – die eigentliche WPF-Anwendung

## Aufbau

```text
WPF/
└── FluxDB/
    ├── App.xaml
    ├── Views/          # MainWindow, SplashWindow, SettingsWindow, Pages
    ├── ViewModels/
    ├── Services/
    ├── Models/
    ├── Converters/
    └── Dockerfile
```

Die WPF-App ist für die lokale Dateisuche, Indizierung, Tag-Verwaltung und die UI-Logik zuständig.
