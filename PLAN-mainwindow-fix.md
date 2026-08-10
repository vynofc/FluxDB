# PLAN: MainWindow UI & Logic Fix — Abgeschlossen/Verworfen

> **Hinweis (2026-08):** Dieses Dokument beschreibt einen früheren Refactoring-Versuch,
> der `MainWindow` auf MVVM mit DI (`App.Host.Services.GetRequiredService<MainWindow>()`)
> umstellen sollte. Dieser Ansatz wurde **verworfen**. Die aktuelle Architektur nutzt
> bewusst **Code-Behind ohne DI** — siehe [AGENTS.md](AGENTS.md), Abschnitt
> "Wichtiger Architektur-Hinweis: DI ist definiert, aber nicht verwendet".
>
> Die unten beschriebenen Crash-Logs und DI-bezogenen TODOs sind daher nicht mehr relevant.
> Das Dokument bleibt als historische Referenz erhalten.

## Historischer Status: In Progress — Build erfolgreich (0 Errors), Crash beim Start

### Letzter Stand (2026-08-05 23:40)

Die App crashte beim Übergang vom SplashScreen zum MainWindow:
```
[2026-08-05 23:40:42.411] Startup: Checking for updates
[2026-08-05 23:40:42.969] Startup CRITICAL failure: Object reference not set to an instance of an object.
```

Der Crash trat in `App.Host.Services.GetRequiredService<MainWindow>()` auf —
dieser DI-basierte Startup-Pfad existiert nicht mehr. `App.xaml` startet jetzt
direkt `SplashWindow` via `StartupUri`, und `MainWindow` wird per `new MainWindow()`
erstellt (siehe AGENTS.md "Tatsächlicher Startup-Flow").
