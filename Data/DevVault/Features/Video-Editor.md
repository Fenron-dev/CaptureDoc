---
tags: [feature, video]
status: Phase 6 implementiert, Phase 7 geplant
---
# Video- und GIF-Editor

Entscheidung und Technik: [[ADR-006 Video mit FFmpeg]]. Code: `src/CaptureDoc.Media/` (`ScreenRecorder`, `VideoExporter`, `GifOptimizer`, `Ffmpeg`), UI `src/CaptureDoc.App/Video/`.

## Implementiert (Phase 6)
- **Aufnahme**: Bereich/Fenster/Bildschirm über das bekannte Auswahl-Overlay, Countdown (Standard 3 s), roter Rahmen + Leiste mit Timer, Pause/Fortsetzen, Stopp, Abbrechen; FPS einstellbar (5–60), Mauszeiger an/aus, Mikrofon optional (DirectShow-Gerät wählbar). Hotkey `Strg+Umschalt+Druck`, Tray-Eintrag, Schaltfläche „● Video / GIF“.
- Aufnahmen landen in `Data/Media/Aufnahmen/` (MKV); Segmente nur temporär in `Data/Temp`.
- **Editor**: Vorschau (niedrig aufgelöste JPEG-Frames im Speicher, beim Anhalten scharfer Originalframe), Abspielen, Timeline mit Vorschaubildern und Zeitleiste, Start/Ende (I/O), Abschnitte herausschneiden (X), Pfeiltasten = Frame/Sekunde.
- **Export**: MP4 und GIF mit Profilen, Größen-, FPS- und Dateigrößenlimit (weich/hart, nachgemessen). Siehe [[Export-Engine]].
- **Frame → Editor / Frame → Anleitung**: Screenshot aus einem Video, voll bearbeitbar.

## Grenzen
- Systemton, Hervorhebung von Mausklicks, ddagrab/WGC: noch nicht.
- Aufnahmen sind noch kein Teil von `.capturedoc`-Projekten (Phase 7).

## Geplant (Phase 7)
Zeitgesteuerte Annotationen mit denselben Ebenen wie im Bildeditor (`LayerTiming`/`Keyframe` sind im Modell vorhanden), Zoom/Pan-Keyframes, Infoboxen; Rendering pro Frame mit dem `SceneRenderer`.
