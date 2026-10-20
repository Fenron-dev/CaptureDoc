---
tags: [research, sharex]
commit: 4b8e66cfab23c54bf4a9d5c2bd7d10c7b2561358
---
# ShareX – Analyse

Lizenz GPL-3.0 → **kein Code übernommen**. Siehe [[Licensing]].

## Wichtigster Befund: Avalonia-Editor
- **Beobachtung:** ShareX enthält jetzt `ShareX.Avalonia`, `ShareX.ImageEditor`, `ShareX.ImageEditor.App`. Paketversionen in `Directory.Packages.props`: **Avalonia 12.1.4**, **SkiaSharp 3.119.4**, CommunityToolkit.Mvvm 8.4.2; `ShareX.ImageEditor.csproj` zielt auf `net10.0`.
- **Schlussfolgerung:** Der von uns bevorzugte Stack (.NET 10 + Avalonia 12 + SkiaSharp 3) wird in einem großen Screenshot-Projekt produktiv eingesetzt. Wir übernehmen dieselben Paketversionen (bekannt kompatibel). → [[ADR-001 Technologie-Stack]]

## Annotationsmodell
- **Beobachtung** (`ShareX.ImageEditor/Core/Annotations/…`): Basisklasse `Annotation`; Effekt-Annotationen `BlurAnnotation`, `PixelateAnnotation`, `SpotlightAnnotation`, `MagnifyAnnotation`, `HighlightAnnotation`; Formen inkl. `ArrowAnnotation`, `FreehandAnnotation`, `CropAnnotation`; Text: `TextAnnotation`, `NumberAnnotation` (Eigenschaften `Number`, `StepType`, `Radius`, `TailPoint`), `SpeechBalloonAnnotation`.
- `PixelateAnnotation`: „Downscale then upscale“ (Z. ~122) auf dem Quellbild.
- **Undo:** Memento-Muster (`Core/History/EditorMemento.cs`): Kopie der Annotationsliste + Canvasgröße + optional Vollbild-Bitmap „for destructive operations like crop/cutout“.
- **Schlussfolgerung:** Memento statt feingranularer Commands ist für Annotationseditoren praktikabel. Wir gehen einen Schritt weiter: Crop ist bei uns nichtdestruktiv (Seitenausschnitt), daher brauchen Mementos nie Bitmaps. → [[ADR-004 Undo-Redo]]

## Capture & Aufnahme
- **Beobachtung:** Screenshot über BitBlt (`ShareX.ScreenCaptureLib/Screenshot.cs`, `CaptureRectangleNative`), Transparenz-Variante `Screenshot_Transparent.cs`.
- Video/GIF über externes FFmpeg-CLI: Eingabegeräte `gdigrab` und `ddagrab` (Desktop Duplication) in `ScreenRecording/FFmpegCaptureDevice.cs`, `ScreenRecordingOptions.cs` (Z. ~115, ~158).
- GIF: zweistufig mit `palettegen`/`paletteuse` inkl. `stats_mode` und `dither` (`ScreenRecording/ScreenRecorder.cs` Z. ~270–274).
- Hardware-Encoder-Auswahl `h264_nvenc`, `h264_amf`, `h264_qsv`, `hevc_*` (`ShareX.Tools/Tools/VideoConverter/VideoConverterOptions.cs`).
- **Schlussfolgerung:** FFmpeg als externer Prozess (nicht gelinkt) ist für Video/GIF der pragmatische Weg und lizenzrechtlich sauber trennbar. → [[Video-Editor]], [[Export-Engine]]

## Nicht übernommen
- Uploader-Bibliothek (`ShareX.UploadersLib`), Verlauf, Steam – widerspricht „keine automatischen Uploads“.
