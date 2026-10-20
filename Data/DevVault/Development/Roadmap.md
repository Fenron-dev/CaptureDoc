---
tags: [roadmap]
---
# Roadmap

Priorität: Quick Capture → Ebeneneditor → Wiederbearbeitbare Projekte → Obsidian-Dokumentation → Video/GIF → Plattformausbau → Erweitert.
Status: [[Progress]] · Probleme: [[Open-Issues]] · Tests: [[Test-Results]].

| Phase | Inhalt | Status (2026-10-20) |
|---|---|---|
| 1 | Analyse, Feature-Matrix, Lizenzen, Architektur, Datenmodell, DevVault | ✅ |
| 2 | Solution, Avalonia-Shell, Portable Settings, Plattformabstraktion, Theme (System/Hell/Dunkel) | ✅ |
| 3 | Windows-Capture, Bereichsauswahl, Fenster, Editor, Pfeil/Text/Rechteck/Schritte, Ebenenpanel, PNG, Zwischenablage, portable EXE | ✅ getestet |
| 4 | Projektformat, Gruppen, Eigenschaftenpanel, Undo/Redo, Schwärzung, Bildexportprofile | ✅ (offen: Drag & Drop in Ebenenliste, Drehen) |
| 5 | Obsidian-Export, Vorlagen, Konflikte, Vorschau, Documentation Builder | ✅ (offen: PDF direkt, Vorlagen-Editor in der UI) |
| 6 | Video/GIF-Aufnahme, Trimmen, GIF-Optimierung, Größenlimits | ⏳ nächste Phase |
| 7 | Tutorial-Videoeditor: Timeline-Ebenen, Keyframes, Zoom | geplant (Modell vorbereitet) |
| 8 | macOS (ScreenCaptureKit, Bundle, TCC) | geplant (Architektur vorbereitet) |
| 9 | Linux (Portal/PipeWire, X11) | geplant |
| 10 | Optimierung, Trimming/Native AOT, Release-Qualität | geplant |

## Nächste konkrete Schritte
1. Phase 6: FFmpeg-Aufnahme (ddagrab) mit sichtbarem Rahmen + Timer, MP4/GIF-Export mit `SizeOptimizer` → Akzeptanztest 3.
2. WGC-Backend für Fensteraufnahme ohne Überdeckung.
3. Mehrmonitor-Test (zweiter Monitor, gemischte DPI) und Bereichsauswahl über Monitorgrenzen.
4. Größe der EXE reduzieren (Trimming), PDF-Export.
