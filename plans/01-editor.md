# Plan 1: In-App Text Editor

**Prioritaet:** 1 (Quick Win, unabhaengig von allen anderen Features)

## Ziel

Textdateien (txt, md, log, json, etc.) direkt in FluxDB oeffnen, bearbeiten und
speichern koennen. Gespeichert wird normal in die Datei (nicht in die Datenbank).

## Umfang

- Neues Fenster `EditorWindow` (FluentWindow, wie die anderen Views)
- Oeffnen ueber:
  - Doppelklick auf Textdatei (statt Preview) oder Kontextmenue-Eintrag "Bearbeiten"
  - Button im Preview-Panel ("In Editor oeffnen")
- Editor-Kern: `TextBox` mit `AcceptsReturn`, Monospace-Font, Scrollbars
- Speichern: Ctrl+S + Toolbar-Button, Encoding beibehalten (Detection wie in der Preview)
- Ungespeicherte-Aenderungen-Warnung beim Schliessen (Dialog: Speichern / Verwerfen / Abbrechen)
- Read-only-Modus als Fallback, wenn die Datei nicht beschreibbar ist
- Nach dem Speichern: `modified_at` in der DB aktualisieren (bzw. Re-Index der einen Datei)

## Betroffene Dateien

| Datei | Aenderung |
|---|---|
| `WPF/FluxDB/Views/EditorWindow.xaml(.cs)` | Neu: Editor-Fenster |
| `WPF/FluxDB/Views/MainWindow.xaml.cs` | Oeffnen des Editors (Doppelklick/Kontextmenue/Preview-Button) |
| `WPF/FluxDB/Services/DatabaseService.cs` | Evtl. Helper zum Aktualisieren von `modified_at` einer Datei |

## DevSettings (neu)

| Key | Default | Beschreibung |
|---|---|---|
| `editor.maxfilesize.mb` | 10 | Maximale Dateigroesse, die im Editor geoeffnet wird |

## Schritte

1. `EditorWindow` XAML + Code-Behind (Laden mit Encoding-Detection, Speichern, Dirty-Flag, Schliessen-Dialog)
2. Integration in `MainWindow` (Oeffnen-Pfade)
3. DB-Aktualisierung nach Speichern
4. DevSetting `editor.maxfilesize.mb` registrieren
5. Build + manueller Test (txt/md, UTF-8 mit/ohne BOM, grosse Datei, read-only Datei)

## Spaeter (optional, nicht Teil dieses Plans)

- Syntax-Highlighting via AvalonEdit
- Tabs im Editor / mehrere Dateien gleichzeitig
- Editor auch fuer Notizen (Notes-Tabelle) verwenden
