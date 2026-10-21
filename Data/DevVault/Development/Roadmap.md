---
tags: [roadmap]
---
# Roadmap

Priorität: Quick Capture → Ebeneneditor → Wiederbearbeitbare Projekte → Obsidian-Dokumentation → Video/GIF → Plattformausbau → Erweitert.
Status: [[Progress]] · Probleme: [[Open-Issues]] · Tests: [[Test-Results]].

| Phase | Inhalt | Status (2026-10-21, v0.5.0) |
|---|---|---|
| 1 | Analyse, Feature-Matrix, Lizenzen, Architektur, Datenmodell, DevVault | ✅ |
| 2 | Solution, Avalonia-Shell, Portable Settings, Plattformabstraktion, Theme | ✅ |
| 3 | Windows-Capture, Bereichsauswahl, Fenster, Editor, Ebenenpanel, PNG, Zwischenablage, portable EXE | ✅ getestet |
| 4 | Projektformat, Gruppen, Eigenschaften, Undo/Redo, Schwärzung, Exportprofile, Drag & Drop der Ebenen | ✅ (offen: Drehen) |
| 5 | Obsidian-Export, Vorlagen, Konflikte, Vorschau, Documentation Builder, HTML (Popup, Video), PDF | ✅ |
| 6 | Bildschirmvideo, Pause, Schnitte (inkl. Anwenden/Entfernen), Stream Copy, MP4/GIF mit Größenlimits, Klick-Markierung | ✅ getestet |
| 7 | Annotationen direkt im Video, **Mehrspur-Schnitt** (Teilen, Ripple, Import, Bild-im-Bild), Zoom/Pan, Folien, Live-Zeichnen, Video-Schritte | ✅ getestet (Logik, FFmpeg-Pixeltests, Headless-UI) |
| 8 | macOS: screencapture, avfoundation, Zwischenablage, .app-Bundle mit Info.plist | ◐ implementiert + paketiert, **ungetestet** |
| 9 | Linux: Wayland-Tools / X11, Zwischenablage, Paket | ◐ implementiert + paketiert, **ungetestet**; Wayland-Video fehlt |
| 10 | Performance (Renderer 17× schneller bei Pfeilen, Drag-Cache), Pinning, Pipette, Stilvorlagen, Serien, Rahmen/Schatten | ◐ weitere Punkte siehe [[Later-Features]] |

## Nächste konkrete Schritte
1. Test auf einem Mac (Apple Silicon) und einem Linux-Desktop (GNOME/Wayland und X11), Fehler beheben, ad-hoc signieren.
2. macOS: ScreenCaptureKit-Brücke (Fensterliste, Systemton); Linux: XDG-Portal + PipeWire für Wayland-Video.
3. WGC-Backend unter Windows (Fenster ohne Überdeckung), Mehrmonitor-Test mit gemischter Skalierung.
4. Trimming/Native AOT zur Verkleinerung der EXE, OCR und Scrolling Capture.
