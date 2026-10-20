---
tags: [research]
---
# Weitere Referenzprojekte

Commits siehe [[References]]. Kein Code übernommen.

## ScreenToGif (MS-PL)
- **Beobachtung:** Aufnahme über Desktop Duplication (`ScreenToGif/Capture/DirectImageCapture.cs`, `DirectChangedImageCapture.cs` – Variante, die nur geänderte Bereiche verarbeitet).
- Eigener GIF-Encoder (`ScreenToGif.Util/Codification/Gif/Encoder/LZWEncoder.cs`, `GifFile.cs`), zusätzlich Gifski- und KGySoft-Quantizer (`ViewModel/KGySoftGifOptionsViewModel.cs`, `Model/Structs/GifskiSettings.cs`).
- Projektformat `.stg` (ZIP-basiert, `UserControls/ExportPanel.xaml.cs` Z. ~310, ~1537).
- **Schlussfolgerung:** Bestätigt ZIP-Container als Projektformat; „changed region“-Erkennung ist für GIF-Größe entscheidend → Phase 6.

## Flameshot (GPL-3.0, C++/Qt)
- **Beobachtung:** Jedes Werkzeug ist ein eigenes Modul unter `src/tools/` (`arrow`, `circlecount`, `pixelate`, `marker`, `pin`, …) mit gemeinsamer Basis `abstracttwopointtool`/`abstractpathtool`.
- **Schlussfolgerung:** Werkzeug-als-Plugin-Muster → unsere `EditorTool`-Klassen in der App.

## ksnip (GPL-3.0, C++/Qt)
- **Beobachtung:** Annotationen kommen aus der separaten Bibliothek kImageAnnotator (`CMakeLists.txt` Z. 83–101).
- **Schlussfolgerung:** Trennung „Annotations-Engine“ vs. „Capture-App“ bestätigt unsere Aufteilung Core/Rendering/App.

## LosslessCut (GPL-2.0, Electron)
- **Beobachtung:** Verlustfreies Schneiden über `ffmpeg … -c copy -copyinkf` (`src/main/ffmpeg.ts` Z. ~240).
- **Schlussfolgerung:** Stream Copy für Schnitte ohne Effekte; framegenaue Schnitte nur an Keyframes → Neukodierung nur bei Bedarf. → [[Video-Editor]]

## Kap (MIT, Electron, macOS)
- **Beobachtung:** Aufnahme über `aperture` (`package.json`), Konvertierung über `ffmpeg-static`. Letzter Commit 2024-02 (**Schlussfolgerung:** wenig gepflegt).
- Relevanz: macOS-Aufnahme-UX (Plugins für Export).

## OBS Studio (GPL-2.0, C)
- **Beobachtung:** Plattform-Backends als Plugins: `plugins/win-capture` (u. a. `duplicator-monitor-capture.c`, `window-capture.c`, `cursor-capture.c`) und WGC in `libobs-winrt/winrt-capture.cpp`; macOS `plugins/mac-capture/mac-sck-video-capture.m` (ScreenCaptureKit), `mac-sck-audio-capture.m`; Linux `plugins/linux-pipewire/portal.c`, `pipewire.c` (XDG Desktop Portal + PipeWire).
- **Schlussfolgerung:** Bestätigt die API-Wahl je Plattform: WGC/DXGI (Windows), ScreenCaptureKit (macOS), Portal+PipeWire (Linux, Wayland). → [[Platform-Integration]]
