# Installationsanforderungen

## Voraussetzungen

- **.NET 10 SDK** (für die WPF-App) — https://dotnet.microsoft.com/download
- **Go** (für Installer und Log-Viewer) — siehe `go.mod` der jeweiligen Komponente
  (Installer: Go 1.26+, Log-Viewer: Go 1.24+)

## Automatisches Setup (Windows)

Das zentrale Build-Skript [build.bat](../build.bat) richtet alle Abhängigkeiten ein:

```powershell
build.bat 1
```

Das Skript prüft die .NET- und Go-Installationen, führt `dotnet restore` für die
WPF-App aus und lädt die Go-Module für Installer und Log-Viewer.

## Manuelles Setup

```powershell
# NuGet-Pakete der WPF-App
dotnet restore WPF/FluxDB/FluxDB.csproj

# Go-Module des Installers
cd Installer
go mod download

# Go-Module des Log-Viewers
cd ../Log_Viewer
go mod download
```

Alternativ bietet der Installer ein eigenes Requirements-Skript, das die Go-Toolchain
prüft, `go mod tidy` ausführt und den Installer direkt baut:

```powershell
cd Installer
requirements.bat    # Windows
bash requirements.sh  # Linux/macOS
```
