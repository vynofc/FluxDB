# Plan 2: FluxDB Service (Tray-Hintergrundprozess)

**Prioritaet:** 2 (Fundament fuer Workflows, Plan 3)

## Ziel

Ein separater Windows-Prozess, der dauerhaft im System-Tray laeuft und
Hintergrundaufgaben uebernimmt (zuerst: geplante Workflows ausfuehren, spaeter:
zentrale DB-Schreibzugriffe, Web-API).

## Architektur

- Neue Komponente: `Service/` (C# Konsolen-App, `net10.0-windows`, eigene .csproj,
  Teil von `FluxDB.sln`)
- Tray-Icon via `System.Windows.Forms.NotifyIcon` (UseWindowsForms)
- Autostart: Registry Run-Key (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`),
  aktivierbar in den App-Einstellungen (Default: aus)
- Kommunikation App <-> Service: Named Pipe (`FluxDB.Service`) mit einfachem
  JSON-Nachrichtenprotokoll (Zeilen-JSON), z.B.:
  - App -> Service: `ping`, `workflow.list`, `workflow.run-now {id}`, `status`
  - Service -> App: Antworten + Events (`workflow.started`, `workflow.finished`)
- Logging: eigener Log-Service, schreibt nach `%LOCALAPPDATA%\FluxDB\service.log`
- Device-Identity: nutzt `DeviceIdentityService` (Code-Sharing, siehe unten)

## DB-Zugriff (wichtigster Designpunkt)

App und Service greifen auf dieselbe `.fluxdb`-SQLite-Datei zu. WAL erlaubt
parallele Reads, aber nur einen Writer. Strategie fuer diesen Plan:

- Service schreibt nur im Rahmen von Workflows und mit Retry-/Busy-Handling
  (`busy_timeout`, Retry mit Backoff wie `CommitBatchWithRetry`)
- Beim Index-Refresh durch den Service signalisiert der Service der App ueber die
  Pipe, dass sie ihre Ansicht neu laden soll
- Laengerfristig (nicht Teil dieses Plans): Schreibzugriffe komplett ueber den
  Service buendeln

## Code-Sharing

Gemeinsamer Code (DatabaseService, IndexerService, Models, DeviceIdentityService,
LoggingService, VersionHelper) wird in ein neues Projekt `FluxDB.Shared`
(net10.0-windows Klassenbibliothek) verschoben, referenziert von WPF-App und
Service. Alternativ (einfacherer Start): Shared-Compile-Items (Linked Files) im
Service-Projekt. Entscheidung beim Implementieren; Shared-Projekt ist sauberer.

## Schritte

1. `Service/`-Projekt anlegen, zur Solution hinzufuegen, Tray-Grundgeruest
   (Icon, Kontextmenue: Status / FluxDB oeffnen / Beenden)
2. Code-Sharing aufsetzen (FluxDB.Shared oder Linked Files)
3. Named-Pipe-Server im Service + Client-Helper in der WPF-App
4. Autostart-Registrierung + Einstellung in `SettingsWindow`
5. App-Verhalten: Beim Start pruefen, ob Service laeuft (Pipe-Ping), sonst Hinweis
6. Build-Skript (`build.bat`) und Release-Workflow um `FluxDB-Service.exe` erweitern
7. Manueller Test: Autostart, Tray-Menue, Pipe-Kommunikation, paralleler Betrieb mit App

## Betroffene/neue Dateien

| Datei | Aenderung |
|---|---|
| `Service/` | Neu: Service-Projekt |
| `FluxDB.sln` | Service (+ ggf. Shared) hinzufuegen |
| `WPF/FluxDB/Services/ServiceClient.cs` | Neu: Pipe-Client |
| `WPF/FluxDB/Views/SettingsWindow.xaml(.cs)` | Autostart-Einstellung |
| `build.bat`, `.github/workflows/release.yml` | Service mitbauen/paketieren |

## Nicht Teil dieses Plans

- Workflow-Engine selbst (Plan 3)
- Webserver/REST-API im Service (Plan 3, Phase Web-App)
- Installer-Integration (Service optional mitinstallieren) — Folgetask
