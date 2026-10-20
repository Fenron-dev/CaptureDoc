---
tags: [feature, capture]
status: implementiert (Windows)
---
# Quick Capture

![[Assets/Screenshots/quick-capture.png|560]]

**Implementiert:** Leiste (Bereich, Fenster, Monitor, alle Bildschirme, letzter Bereich), Verzögerung 0/3/5/10 s mit sichtbarem Countdown, Cursor ein/aus, Tray-Menü (Klick = Bereich), globale Hotkeys (`Druck`, `Alt+Druck`, `Strg+Druck`, `Umschalt+Druck`), Einzelinstanz (zweiter Start holt die Leiste nach vorn), Aktion nach Aufnahme (Editor / Zwischenablage / Datei / beides).

Ablauf (`Services/CaptureController.cs`): eigene Fenster ausblenden → ggf. Countdown → Desktop einfrieren (GDI) → Overlay je Monitor (`Views/RegionSelector.cs`) → Ausschnitt → Editor öffnet sofort mit dem dekodierten Bild, PNG-Kodierung läuft im Hintergrund.

Gemessen: Einfrieren eines 1600×900-Desktops 72–236 ms ([[Test-Results]]).

**Geplant:** WGC-Fensteraufnahme ohne Überdeckung, Bereich über mehrere Monitore, Scrolling Capture, Serienaufnahme, Lupe/Pixelraster im Overlay. Siehe [[Platform-Integration]], [[Open-Issues]].
