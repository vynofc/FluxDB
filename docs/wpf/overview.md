# WPF-App-Übersicht

## Ziel

Die WPF-App ist die Hauptoberfläche von FluxDB. Sie übernimmt:

- Ordner- und Dateisuche
- Indexierung lokaler Verzeichnisse
- Tag-Verwaltung
- Vorschau und Export
- Einstellungen und Update-Checks

## Aufbau

- UI-Dateien wie App.xaml, MainWindow.xaml und die Dialogfenster
- Models für Dateien, Tags und Einstellungen
- Services für Datenbank, Indexierung, Export und Logging

## Build

Aus dem Repository-Root:

```powershell
nuget restore WPF/FluxDB/FluxDB.csproj
msbuild WPF/FluxDB/FluxDB.csproj /p:Configuration=Release /p:Platform="Any CPU"
```
