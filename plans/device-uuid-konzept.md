# Device-UUID-Konzept für FluxDB

**Status:** Entwurf / Planung
**Datum:** 2026-08-15
**Branch-Kontext:** Vorbereitung für Sync-, Multi-Device- und Diagnose-Features

---

## 1. Ziel

Jedes Gerät, auf dem FluxDB läuft, erhält beim ersten Start eine eindeutige, persistente UUID. Diese ID ermöglicht es zukünftig:

- Datenbank-Einträge (`.fluxdb`) einem Erzeuger-Gerät zuzuordnen
- Export/Import- und Sync-Daten pro Gerät zu unterscheiden
- Einstellungen pro Gerät und Ordner sauber zu trennen
- Support-Logs pseudonym einem Gerät zuzuordnen
- Update- und Migrations-Status pro Gerät zu verfolgen

Die UUID ist **pseudonym** (kein Username, kein Pfad, keine Hardware-ID) und kann vom Benutzer zurückgesetzt werden, wird aber nicht versehentlich verloren gehen.

---

## 2. Anforderungen

| # | Anforderung | Begründung |
|---|---|---|
| R1 | UUID wird **einmalig beim ersten Start** generiert | Keine doppelten IDs, kein "Erraten" |
| R2 | UUID **überlebt** Settings-Reset, Reinstall, Update | Sonst verliert sie ihren Zweck |
| R3 | UUID ist **nicht trivial änderbar** (kein plain JSON) | Verhindert versehentliche Manipulation |
| R4 | UUID ist **pro Benutzer** (nicht pro Maschine) | Windows-Profile, keine Admin-Rechte nötig |
| R5 | UUID kann vom Benutzer **bewusst zurückgesetzt** werden | Privacy / Datenschutz |
| R6 | UUID wird **nicht an Dritte gesendet** ohne Opt-in | DSGVO, Vertrauen |
| R7 | Keine Hardware-ID (MAC, CPU, etc.) | Verhindert Tracking, stabil bei Hardware-Wechsel |

---

## 3. Speicherorte (Defense in Depth)

Die UUID wird an **drei Stellen** gespeichert, um versehentlichen Verlust zu verhindern:

### 3.1 Primär: Windows Registry (HKCU)

```
HKCU\Software\FluxDB\DeviceId
Typ: REG_SZ
Wert: z. B. "0198d69d-7b3e-7a91-9c2d-51dd90c53c17"
```

- Überlebt Reinstall, Settings-Reset, Update
- Nur für den aktuellen Benutzer (keine Admin-Rechte nötig)
- Nicht im Dateisystem sichtbar, nicht im Explorer sichtbar
- Kann via `regedit` geändert werden, aber nicht versehentlich

### 3.2 Sekundär: `%LOCALAPPDATA%\FluxDB\device.id`

```
%LOCALAPPDATA%\FluxDB\device.id
Inhalt: UUID als Plaintext (UTF-8)
```

- Fallback, falls Registry nicht verfügbar (z. B. portable Installation, Wine)
- Wird beim Start gelesen, wenn Registry leer ist
- Wird bei erstem Start erstellt

### 3.3 Tertiär: `settings.json` (nur Cache)

```json
{
  "DeviceId": "0198d69d-7b3e-7a91-9c2d-51dd90c53c17",
  ...
}
```

- Wird beim Start aus Registry/Datei gelesen und in `settings.json` gecacht
- Wird **nicht** als Quelle der Wahrheit verwendet
- Wird bei Export/Backup **nicht** mitgespeichert (oder als optional markiert)

**Lesereihenfolge beim Start:**

```
1. Registry lesen → gefunden? → Fertig
2. device.id lesen → gefunden? → In Registry schreiben → Fertig
3. settings.json lesen → gefunden? → In Registry + device.id schreiben → Fertig
4. Neue UUID generieren → In Registry + device.id + settings.json schreiben
```

---

## 4. Implementierung in FluxDB

### 4.1 Neuer Service: `DeviceIdentityService`

**Datei:** `WPF/FluxDB/Services/DeviceIdentityService.cs`

```csharp
public static class DeviceIdentityService
{
    private const string RegistryKeyPath = @"Software\FluxDB";
    private const string RegistryValueName = "DeviceId";
    private static readonly string DeviceIdFilePath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluxDB",
            "device.id"
        );

    public static string GetOrCreateDeviceId()
    {
        // 1. Registry
        // 2. device.id
        // 3. settings.json (Fallback)
        // 4. Neu generieren und persistieren
    }

    public static void ResetDeviceId()
    {
        // Löscht Registry + device.id + settings.json-Eintrag
        // Bei nächstem Start wird neue UUID generiert
    }
}
```

**Wichtige Punkte:**
- **Static class** (passt zum aktuellen `LoggingService`-Pattern)
- **Lazy initialization**: Wird erst beim ersten Zugriff generiert
- **Thread-safe**: Lock um den Generierungs- und Schreibvorgang
- **Fehlerbehandlung**: Wenn Registry nicht schreibbar ist (z. B. eingeschränkte Rechte), nur device.id verwenden und loggen

### 4.2 Integration in `App.OnStartup`

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    // Bestehender Code...
    var deviceId = DeviceIdentityService.GetOrCreateDeviceId();
    LoggingService.Log($"Device ID: {deviceId}");
    // Restlicher Startup...
}
```

### 4.3 Integration in `SettingsService`

- `AppSettings` erhält eine neue Property `DeviceId` (nur Cache)
- `SettingsService` liest beim Laden zuerst `DeviceIdentityService.GetOrCreateDeviceId()` und schreibt den Wert in `AppSettings.DeviceId`
- Beim Speichern wird `DeviceId` **nicht** aus `settings.json` gelesen, sondern immer frisch von `DeviceIdentityService` geholt

### 4.4 Integration in `DatabaseService`

Die `.fluxdb`-Datei erhält eine neue Tabelle `device_info`:

```sql
CREATE TABLE IF NOT EXISTS device_info (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    device_id TEXT NOT NULL,
    created_at TEXT NOT NULL,
    last_seen_at TEXT NOT NULL
);
```

- Beim Öffnen einer Datenbank wird `device_id` und `last_seen_at` aktualisiert
- `created_at` bleibt beim ersten Gerät, das die DB erstellt hat
- Zukünftig kann `device_id` in `files`, `tags`, `notes` als Spalte hinzugefügt werden, um Änderungen pro Gerät zu tracken

### 4.5 Integration in `LoggingService`

- Jeder Log-Eintrag erhält optional die Device-ID als Präfix oder Feld
- Beispiel: `[2026-08-15 14:32:01] [Device: 0198d69d-...] [INFO] Folder opened: C:\Data`
- Bei Support-Anfragen kann der Benutzer die Device-ID nennen, ohne Username/Pfade preiszugeben

---

## 5. Zukünftige Anwendungsfälle

### 5.1 Multi-Device-Sync (Vorausschau)

Wenn FluxDB später Sync zwischen Geräten unterstützt:

- Jede Änderung in der `.fluxdb` wird mit `device_id` und `timestamp` versehen
- Sync-Algorithmus kann Konflikte auflösen: "Gerät A hat Datei gelöscht, Gerät B hat Tag hinzugefügt"
- `device_info` Tabelle zeigt, welche Geräte die DB zuletzt geändert haben

### 5.2 Export/Import mit Geräte-Kontext

- Export enthält `device_id` des Exporteurs
- Import kann prüfen: "Diese Daten kommen von einem anderen Gerät, wie behandeln?"
- Tags/Notes können pro Gerät oder global importiert werden

### 5.3 Diagnose und Support

- `LoggingService` kann Device-ID in Log-Datei schreiben
- Bei Bug-Reports kann der Benutzer die Device-ID nennen
- Entwickler können in Logs nach Device-ID suchen, ohne PII zu sehen

### 5.4 Update- und Migrations-Status

- Installer/Auto-Update kann in Registry schreiben: "Migration auf Version X abgeschlossen"
- Bei erneutem Start wird nicht doppelt migriert
- `DeviceIdentityService` kann `last_seen_at` in der DB aktualisieren

---

## 6. Datenschutz und Sicherheit

| Aspekt | Maßnahme |
|---|---|
| **Pseudonymität** | UUID ist zufällig, keine Hardware-ID, kein Username |
| **Opt-out** | Benutzer kann Device-ID in den Einstellungen zurücksetzen |
| **Kein Tracking** | UUID wird nicht an GitHub/API gesendet, nur lokal gespeichert |
| **Kein Export** | `DeviceId` wird nicht in Export-Dateien gespeichert (oder als optional markiert) |
| **Transparenz** | In den Einstellungen wird angezeigt: "Geräte-ID: 0198d69d-... (zum Zurücksetzen klicken)" |

---

## 7. Zusätzliche Ideen und Erweiterungen

### 7.1 Geräte-Namen (Optional)

Zusätzlich zur UUID kann der Benutzer einen frei wählbaren Geräte-Namen vergeben:

```
HKCU\Software\FluxDB\DeviceName
Typ: REG_SZ
Wert: z. B. "Arbeits-PC", "Laptop privat"
```

- Wird in der UI angezeigt: "Gerät: Arbeits-PC (0198d69d-...)"
- Macht Multi-Device-Szenarien benutzerfreundlicher
- Kann in `settings.json` gespeichert werden (nicht kritisch)

### 7.2 Device-Fingerprinting (Optional, mit Vorsicht)

Für sehr spezifische Diagnose-Zwecke könnte ein **opt-in** Fingerprint erstellt werden:

- Hash aus: OS-Version, .NET-Version, Bildschirmauflösung, CPU-Kern-Anzahl
- **Nicht** für Tracking, sondern für: "Dieser Bug tritt nur auf Windows 11 mit 4K-Display auf"
- Muss explizit aktiviert werden, standardmäßig aus

### 7.3 Geräteübergreifende Einstellungen (Cloud-Sync)

Wenn später Cloud-Sync geplant ist:

- `settings.json` könnte pro Gerät in der Cloud gespeichert werden
- `DeviceId` identifiziert, welche Einstellungen zu welchem Gerät gehören
- `RecentFolders` können geräteübergreifend synchronisiert werden

### 7.4 Offline-Lizenzierung / Feature-Flags

- `DeviceId` könnte für lokale Feature-Flags verwendet werden: "Dieses Gerät hat Beta-Features aktiviert"
- Keine Server-Verbindung nötig, nur lokale Registry-Prüfung

### 7.5 Multi-User-Support (Windows)

- Da UUID pro Benutzer (HKCU) gespeichert wird, hat jeder Windows-Benutzer seine eigene Device-ID
- Auf einem gemeinsam genutzten PC können mehrere Benutzer FluxDB verwenden, ohne Konflikte

---

## 8. Offene Fragen / Entscheidungen

| Frage | Optionen | Empfehlung |
|---|---|---|
| Soll `DeviceId` in Export-Dateien gespeichert werden? | Ja / Nein / Optional | **Nein** (Privacy) oder als optional markiert |
| Soll `DeviceId` in der UI angezeigt werden? | Ja / Nein / Nur in Dev-Einstellungen | **Nur in Dev-Einstellungen** oder als kurzer Hash |
| Soll `DeviceId` beim Deinstallieren gelöscht werden? | Ja / Nein | **Nein** (überlebt Reinstall, gewünscht) |
| Soll es einen "Reset Device ID"-Button geben? | Ja / Nein | **Ja**, in Dev-Einstellungen mit Warnung |
| Soll `DeviceId` in Log-Dateien immer mitgeschrieben werden? | Ja / Nein / Nur Debug-Mode | **Nur Debug-Mode** oder als kurzer Hash |

---

## 9. Migrationspfad

Da es aktuell keine `DeviceId` gibt, ist keine Migration nötig. Die Implementierung erfolgt in einem Schritt:

1. `DeviceIdentityService` hinzufügen
2. `App.OnStartup` integrieren
3. `SettingsService` anpassen (DeviceId als Cache)
4. `DatabaseService` um `device_info` Tabelle erweitern
5. Optional: Dev-Einstellungen um "Reset Device ID" erweitern

**Keine Breaking Changes**, keine Änderungen an bestehenden APIs.

---

## 10. Zusammenfassung

| Vorteil | Beschreibung |
|---|---|
| **Persistenz** | UUID überlebt Reinstall, Update, Settings-Reset |
| **Pseudonymität** | Keine Hardware-ID, kein Username, DSGVO-konform |
| **Vorbereitung für Sync** | Multi-Device-Szenarien werden möglich |
| **Diagnose** | Logs können pseudonym zugeordnet werden |
| **Benutzerkontrolle** | Reset möglich, kein verstecktes Tracking |
| **Windows-Integration** | Nutzt HKCU, keine Admin-Rechte nötig |

Die Implementierung ist **low-risk** (keine Breaking Changes) und **high-value** (Vorbereitung für zukünftige Features). Sie sollte als separates Feature implementiert werden, nicht als Teil eines anderen Refactorings.
