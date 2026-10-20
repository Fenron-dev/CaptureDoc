---
tags: [roadmap]
---
# Roadmap

Priorität: Quick Capture → Ebeneneditor → Wiederbearbeitbare Projekte → Obsidian-Dokumentation → Video/GIF → Plattformausbau → Erweitert.
Status: [[Progress]] · Probleme: [[Open-Issues]] · Tests: [[Test-Results]].

| Phase | Inhalt | Status (2026-10-20) |
|---|---|---|
| 1 | Analyse, Feature-Matrix, Lizenzen, Architektur, Datenmodell, DevVault | ✅ |
| 2 | Solution, Avalonia-Shell, Portable Settings, Plattformabstraktion, Theme | ✅ |
| 3 | Windows-Capture, Bereichsauswahl, Fenster, Editor, Ebenenpanel, PNG, Zwischenablage, portable EXE | ✅ getestet |
| 4 | Projektformat, Gruppen, Eigenschaftenpanel, Undo/Redo, Schwärzung, Bildexportprofile | ✅ (offen: Drag & Drop in Ebenenliste, Drehen) |
| 5 | Obsidian-Export, Vorlagen, Konflikte, Vorschau, Documentation Builder, HTML mit Bild-Popup | ✅ (offen: PDF direkt) |
| 6 | Bildschirmvideo, Pause, Timeline, Trimmen/Schneiden, MP4/GIF mit Größenlimits | ✅ getestet (0.2.0) |
| 7 | Tutorial-Videoeditor: zeitgesteuerte Ebenen, Keyframes, Zoom/Pan, Videos im Projektformat | ⏳ nächste Phase |
| 8 | macOS (ScreenCaptureKit, Bundle, TCC) | geplant |
| 9 | Linux (Portal/PipeWire, X11) | geplant |
| 10 | Optimierung, Trimming/Native AOT, Release-Qualität | geplant |

## Nächste konkrete Schritte
1. Phase 7: Annotationsebenen mit Start/Ende im Video-Editor (gleiches Ebenenpanel), Rendering per Frame über `SceneRenderer` + FFmpeg-Pipe.
2. Videos und Schnittliste im `.capturedoc`-Projekt (Formatversion 2 inkl. Migration).
3. Mausklick-Hervorhebung, Systemton (WASAPI-Loopback), ddagrab/WGC als schnellere Quelle.
4. Mehrmonitor-Test (gemischte DPI), WGC-Fensteraufnahme ohne Überdeckung.
