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
└── build.bat          # zentrales Build-Skript (Windows)
```

## Funktionen

- Indizierung lokaler Ordner und schnelle Suche
- Dateiverwaltung mit Tagging und Vorschau
- Export von Indexdaten und Logging
- Update-Prüfungen und zentrale Einstellungen

## Setup

Das zentrale Build-Skript [build.bat](build.bat) bietet über Option `1` eine automatische
Installation aller Abhängigkeiten (NuGet-Pakete der WPF-App, Go-Module von Installer und Log-Viewer):

```powershell
build.bat 1
```

Alternativ lässt sich das Setup manuell ausführen:

```powershell
dotnet restore WPF/FluxDB/FluxDB.csproj
cd Installer && go mod download && cd ..
cd Log_Viewer && go mod download && cd ..
```

Weitere Details sind in [docs/install-requirements.md](docs/install-requirements.md) beschrieben.

## Build

Alle Build-Skripte schreiben ihre Artefakte in den Root-Ordner [bin](bin/).

### WPF-App

```powershell
dotnet restore WPF/FluxDB/FluxDB.csproj
dotnet build WPF/FluxDB/FluxDB.csproj -c Release

# Publish (self-contained output nach bin/)
dotnet publish WPF/FluxDB/FluxDB.csproj -c Release -o bin\
```

### Installer, Log-Viewer und Release-Paket

```powershell
build.bat        # interaktives Menü
build.bat 3      # nur Installer
build.bat 4      # nur Log-Viewer
build.bat 5      # komplettes Release-Paket (WPF + Installer + Log-Viewer + ZIP)
```

Ergebnis: `bin/FluxDB.exe`, `bin/FluxDB-Installer.exe`, `bin/components/Log_Viewer.exe` sowie die nativen SQLite-Unterordner (`bin/x64/`, `bin/x86/`). Nach Option 5 liegt zusätzlich `FluxDB.zip` im Repo-Root.

Weitere Details finden Sie in [docs/README.md](docs/README.md).
