# FluxDB

FluxDB ist ein Windows-basiertes Toolset aus einer WPF-Anwendung, einem Installer und einem Log-Viewer.

## Komponenten

- [WPF/FluxDB](WPF/FluxDB/) – Hauptanwendung mit Dateisuche, Indizierung, Tags, Vorschau und Einstellungen
- [Installer](Installer/) – Go-basierter Installer für die Verteilung
- [Log_Viewer](Log_Viewer/) – separater Log-Viewer für die App

## Projektstruktur

```text
.
├── WPF/
│   └── FluxDB/        # WPF-App
├── Installer/         # Go-Installer
├── Log_Viewer/        # Go-Log-Viewer
├── docs/              # zusätzliche Dokumentation
├── .github/workflows/ # CI/CD
└── build.bat / build.sh
```

## Funktionen

- Indizierung lokaler Ordner und schnelle Suche
- Dateiverwaltung mit Tagging und Vorschau
- Export von Indexdaten und Logging
- Update-Prüfungen und zentrale Einstellungen

## Setup

Die Abhängigkeiten lassen sich mit einem der folgenden Skripte installieren:

- Windows: [install-requirements.bat](install-requirements.bat)
- Linux/macOS: [install-requirements.sh](install-requirements.sh)

Weitere Details sind in [docs/install-requirements.md](docs/install-requirements.md) beschrieben.

## Build

Alle Build-Skripte schreiben ihre Artefakte in den Root-Ordner [bin](bin/).

### WPF-App

```powershell
nuget restore WPF/FluxDB/FluxDB.csproj
msbuild WPF/FluxDB/FluxDB.csproj /p:Configuration=Release /p:Platform="Any CPU" /p:OutDir=bin\
```

### Installer und Log-Viewer

```powershell
build.bat
```

Ergebnis: `bin/FluxDB.exe`, `bin/FluxDB-Installer.exe`, `bin/components/Log_Viewer.exe` sowie die nativen SQLite-Unterordner.

Weitere Details finden Sie in [docs/README.md](docs/README.md).
