# Plan 3: Workflows (Scheduler + Web-Steuerung)

**Prioritaet:** 3 (baut auf Plan 2, dem Service, auf)

## Ziel

Geplante Aufgaben ("Workflows"), die der Service automatisch ausfuehrt, z.B.
"Index-Refresh taeglich um 9:00" oder "diese Dateien per OCR analysieren um 10:00".
Workflows sind lokal definiert und editierbar; optional koennen sie ueber eine
zentrale Web-App verwaltet werden, von der FluxDB sie per REST-API abruft.

## Phase A: Lokale Workflow-Engine

### Datenmodell

Gespeichert in `%LOCALAPPDATA%\FluxDB\workflows.json`:

```
Workflow {
  id: uuid
  name: string
  enabled: bool
  trigger: { type: "daily" | "interval" | "once", time: "09:00", intervalMinutes: n, runAt: datetime }
  action: { type: "refresh-index" | "ocr", rootFolder: string, paths: [..], options: {...} }
  lastRunAt, lastResult
}
```

### Engine im Service

- Scheduler-Loop im Service (Timer, minuetlicher Tick; faellige Workflows ausfuehren)
- Persistenz von `lastRunAt`/`lastResult`, Catch-up-Verhalten: verpasste Runs
  (Service war aus) werden beim Start einmal nachgeholt (konfigurierbar)
- Action-Typen, stufenweise:
  1. `refresh-index` — IndexerService.ScanFolderAsync auf den konfigurierten Root-Ordner
  2. `ocr` — Texterkennung auf Bilder/PDFs in den konfigurierten Pfaden;
     Engine: Windows.Media.Ocr (Windows-eigen, keine Extra-Dependency);
     Ergebnis landet als Notiz bzw. in einer neuen `ocr_text`-Spalte/Tabelle
- Status/Events ueber die Named Pipe an die App (Plan 2)

### UI in der App

- Neuer Bereich "Workflows" (Fenster oder Seite): Liste, Anlegen/Bearbeiten/Loeschen,
  Aktivieren/Deaktivieren, "Jetzt ausfuehren", letzte Laeufe anzeigen
- Bearbeitung lokal in der App; der Service laedt `workflows.json` bei Aenderung neu
  (FileSystemWatcher oder Pipe-Nachricht `workflows.reload`)

## Phase B: Web-App + REST-Sync

- Zentrale Web-App (separates Projekt, ausserhalb dieses Repos moeglich):
  Workflow-Verwaltung im Browser, Speicherung serverseitig
- REST-API: `GET /api/devices/{uuid}/workflows` liefert die Workflow-Liste des Geraets
- FluxDB-Service pollt die API in Intervallen (DevSetting, Default 15 min) mit
  `If-None-Match`/ETag, merged Server-Workflows mit lokalen (Server gewinnt bei
  Konflikt; lokale Workflows ohne Server-Bezug bleiben unberuehrt)
- Identity: `DeviceIdentityService`-UUID; zusaetzlich Pairing-Token, damit nicht
  jeder mit bekannter UUID die Workflows aendern kann
- Offline-Verhalten: letzter Stand bleibt lokal gecacht und laeuft weiter;
  Sync-Fehler werden nur geloggt
- Konfiguration in den App-Einstellungen: Server-URL, Pairing-Token, Sync an/aus

## DevSettings (neu)

| Key | Default | Beschreibung |
|---|---|---|
| `workflows.tick.seconds` | 60 | Scheduler-Tickintervall |
| `workflows.catchup.enabled` | true | Verpasste Runs beim Start nachholen |
| `workflows.sync.interval.min` | 15 | Poll-Intervall der Web-API |
| `workflows.ocr.language` | (System) | OCR-Sprache (Windows.Media.Ocr Language-Tag) |

## Schritte

1. Workflow-Modell + Persistenz (`workflows.json`, Laden/Speichern mit Locking)
2. Scheduler-Loop im Service + `refresh-index`-Action
3. App-UI: Workflow-Verwaltung + Pipe-Kommunikation (Status, run-now, reload)
4. `ocr`-Action mit Windows.Media.Ocr + DB-Ablage der Ergebnisse
5. Phase B: Sync-Client im Service (Polling, ETag, Merge, Pairing-Token)
6. Web-App + API (separates Projekt; hier nur die Client-Seite sicherstellen)
7. Tests: Scheduler-Timing, Catch-up, paralleler DB-Zugriff App/Service, Sync-Merge

## Abhaengigkeiten

- Plan 2 (Service) muss fuer Phase A stehen
- Phase B braucht Hosting-Entscheidung (wo laeuft die Web-App?)
