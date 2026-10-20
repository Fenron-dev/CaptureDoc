---
tags: [research, greenshot]
commit: a0f55d926e0efb45d4a2988b51f79f64fa463360
---
# Greenshot – Analyse

Lizenz GPL-3.0 → **kein Code übernommen**, nur Konzepte. Siehe [[Licensing]].

## Capture
- **Beobachtung:** Zwei austauschbare Backends hinter `IScreenCaptureBackend` (`src/Greenshot.Base/Capturing/IScreenCaptureBackend.cs`):
  - `GraphicsCaptureBackend` – Windows.Graphics.Capture, „captures windows without what covers them, HDR aware“ (`GraphicsCaptureBackend.cs`, `CapturesWindowContentOnly => true`).
  - `GdiCaptureBackend` – „GDI fallback: copies what is displayed (BitBlt from the desktop)“ (`GdiCaptureBackend.cs`, `CopyFromScreen`, BitBlt Z. ~172).
- **Schlussfolgerung:** Backend-Abstraktion mit GDI-Fallback ist bewährt → übernommen als Konzept in [[Platform-Integration]].

## Editor
- **Beobachtung:** Objektmodell aus „Containern“ auf einer `Surface` (`src/Greenshot.Editor/Drawing/Surface.cs`, `DrawableContainer.cs`, `StepLabelContainer.cs`, `ObfuscateContainer.cs`, `ArrowContainer.cs`).
- Nummerierte Schritte sind eigene Container (`StepLabelContainer`) → Vorbild für unsere `StepLayer`.

## Dateiformat `.greenshot`
- **Beobachtung** (`src/Greenshot.Editor/FileFormatHandlers/GreenshotFileFormatHandler.cs`, `SaveToStream`/`LoadSurface`): PNG des Originals, danach binär serialisierte Elemente (`SaveElementsToStream`), dann Int64-Länge, dann ASCII-Marker `GreenshotXX.YY`. Laden liest die letzten 14 Byte als Marker und springt zurück.
- **Schlussfolgerung:** Format ist an .NET-Binärserialisierung gebunden, nicht plattformübergreifend lesbar, ohne Integritätsprüfung, keine Migration → **nicht geeignet** für CaptureDoc. Entscheidung: ZIP+JSON, siehe [[ADR-003 Projektformat]].
- Positiv: Projekt bleibt als normales PNG öffnenbar (Bild vorne). Wir legen deshalb ein `thumbnail.png` in den Container.

## Weiteres
- Restart-Manager speichert offene Editoren als `.greenshot` und stellt sie wieder her (`src/Greenshot/Helpers/RestartManagerHelper.cs`) → Vorbild für Crash-Recovery ([[Data-Model#Auto-Save und Wiederherstellung]]).
- Schreibtest vor Speicherung (`src/Greenshot/SelfService/OutputPathCheck.cs`, `.greenshot-write-test-*.tmp`) → übernommen als Konzept für Prüfung des portablen Datenordners.
- Neuer `Greenshot.Mcp`-Ordner und Browser-Extension vorhanden (**Unbekannt**: nicht analysiert, nicht relevant für MVP).
