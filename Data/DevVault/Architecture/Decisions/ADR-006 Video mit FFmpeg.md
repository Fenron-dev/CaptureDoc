---
tags: [adr, video]
status: akzeptiert
date: 2026-10-20
---
# ADR-006 Video & GIF über externes FFmpeg

## Entscheidung
- FFmpeg wird als **externer Prozess** aufgerufen (Argumentliste, keine Shell), nie gelinkt. Projekt `CaptureDoc.Media`.
- Suche: `CAPTUREDOC_FFMPEG` → `Runtime/ffmpeg/` (portabel) → `Runtime/ffmpeg/bin/` → `PATH`. **Nicht mitgeliefert** (Lizenz, Größe); fehlt es, erklärt die App, wohin `ffmpeg.exe`/`ffprobe.exe` gehören.
- Aufnahme Windows: `gdigrab` mit physischen Koordinaten des virtuellen Desktops (gemessen: identisch zur GDI-Screenshot-Aufnahme, PSNR 37,7 dB bei korrektem vs. 15,6 dB bei 1 px Versatz). Zwischenformat H.264 **4:4:4** CRF 18 in **MKV** (scharfer Text, absturzrobust).
- Pause = Segment beenden, Fortsetzen = neues Segment, Stopp = `concat` mit Stream Copy (verlustfrei). In der Pause wird nichts aufgenommen.
- Sichtbarkeit: roter Rahmen (ein transparentes, durchklickbares Fenster) + Leiste mit Timer; beide per `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` aus der Aufnahme ausgeschlossen (geprüft: `affinity=0x11`, nicht im Video sichtbar).
- Export: MP4 (libx264 oder NVENC/AMF/QSV mit Rückfall auf libx264), GIF zweistufig `palettegen`/`paletteuse` (`stats_mode=diff`, `diff_mode=rectangle`), optional `mpdecimate` für unveränderte Frames.
- Größenlimits: GIF iterativ (Farben → FPS → Auflösung), MP4 über Zielbitrate mit Nachregelung; echte Dateigröße wird immer nachgemessen.

## Alternativen
| Option | Grund für Ablehnung (vorerst) |
|---|---|
| FFmpeg-Bibliotheken linken (FFmpeg.AutoGen) | LGPL-Pflichten beim Linken, native DLLs (~100 MB), komplexer |
| Windows.Graphics.Capture + MediaFoundation | nur Windows, deutlich mehr Code; als Option für Phase 10 |
| Eigener GIF-Encoder (wie ScreenToGif) | palettegen/paletteuse liefert sehr gute Qualität ohne eigenen Code |
| ddagrab | schneller (GPU), aber Koordinaten relativ zum Ausgabegerät und Zuordnung DXGI-Output ↔ Monitor nicht trivial → später |

## Folgen
- Ohne FFmpeg funktioniert alles außer Video/GIF.
- Bei Weitergabe mit beigelegtem FFmpeg: dessen Lizenz (LGPL/GPL des konkreten Builds) beachten ([[Licensing]]).
