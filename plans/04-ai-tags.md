# Plan 4: AI-generierte Tags (Gemini, Cloud)

**Prioritaet:** 4 (unabhaengig, braucht nur Settings + API-Call)

## Ziel

Auf Knopfdruck 1-3 Tag-Vorschlaege pro Datei von einer KI (Google Gemini, jeweils
aktuelles Modell) generieren lassen. Vorschlaege werden dem Nutzer gezeigt und
koennen uebernommen werden (kein stillschweigendes Auto-Tagging).

## API-Key-Verwaltung

- Neues Feld in den Einstellungen unter "Preferences": "Gemini API-Key"
- Speicherung **verschluesselt** in `settings.json`: `ProtectedData.Protect()`
  (DPAPI, `CurrentUser`-Scope), abgelegt als Base64-String, z.B. `GeminiApiKeyProtected`
- Entschluesselung nur zur Laufzeit im Speicher; Key nie im Klartext loggen
- Ohne hinterlegten Key ist das Feature deaktiviert (UI-Hinweis + Link zu den Einstellungen)

## Funktionsumfang

- Kontextmenue auf Datei(en): "Tags vorschlagen (AI)"
- Input fuer die KI:
  - immer: Dateiname + Extension
  - bei Textdateien (txt, md, csv, json, ...): zusaetzlich Dateianfang
    (Limit ueber DevSetting, Default 2000 Zeichen)
- Prompt: strikte Ausgabe als JSON-Array von 1-3 kurzen Tags (lowercase, keine Leerzeichen)
- Antwort-Parsing defensiv (JSON extrahieren, ungueltige Eintraege verwerfen)
- Vorschlags-Dialog: Tags als Chips, per Klick auswaehlen, "Uebernehmen" fuegt sie
  ueber die bestehende Tag-Logik in `DatabaseService` hinzu (lowercase-trimmed,
  Duplikate ignorieren)
- Mehrfachauswahl: sequentiell mit Fortschrittsanzeige und Abbruch

## Technik

- HTTP via `HttpClient` auf die Gemini REST-API (generativelanguage.googleapis.com),
  Modellname konfigurierbar ueber DevSetting (Default: aktuelles Flash-Modell,
  z.B. `gemini-2.0-flash`)
- Neuer `AiTagService` in der WPF-App (kein Service-Prozess noetig)
- Fehlerfaelle: kein Key, kein Netz, Rate-Limit (429), ungueltige Antwort —
  jeweils verstaendliche Meldung in der UI + Log-Eintrag
- Privacy-Hinweis in den Einstellungen: Dateinamen/Inhalte werden an Google gesendet

## DevSettings (neu)

| Key | Default | Beschreibung |
|---|---|---|
| `ai.model` | gemini-2.0-flash | Gemini-Modellname |
| `ai.context.maxchars` | 2000 | Max. Zeichen Dateiinhalt im Prompt |
| `ai.tags.max` | 3 | Maximale Anzahl vorgeschlagener Tags |
| `ai.request.timeout.sec` | 30 | HTTP-Timeout |

## Betroffene Dateien

| Datei | Aenderung |
|---|---|
| `WPF/FluxDB/Services/AiTagService.cs` | Neu: Gemini-Client, Prompt, Parsing |
| `WPF/FluxDB/Models/AppSettings.cs` | `GeminiApiKeyProtected`-Feld |
| `WPF/FluxDB/Services/SettingsService.cs` | DPAPI Protect/Unprotect-Helper |
| `WPF/FluxDB/Views/SettingsWindow.xaml(.cs)` | Key-Eingabefeld unter Preferences + Privacy-Hinweis |
| `WPF/FluxDB/Views/MainWindow.xaml(.cs)` | Kontextmenue-Eintrag + Vorschlags-Dialog |

## Spaeter (optional, nicht Teil dieses Plans)

- Zentraler Key-Server: kleiner Webserver, der API-Keys auf die Device-UUID
  (`DeviceIdentityService`) speichert; FluxDB fragt den Key per API ab statt ihn
  lokal zu speichern. Bewusst zurueckgestellt (Infrastruktur + Vertrauensfrage).
- Auto-Tagging als Workflow-Aktion (Plan 3)
- Lokale Modelle (Ollama) als alternativer Provider
