---
tags: [feature, matrix]
---
# Feature-Matrix

Legende: ✅ vorhanden (im Code beobachtet) · ◐ teilweise · – nicht vorhanden · ? nicht geprüft.
GS = Greenshot, SX = ShareX, STG = ScreenToGif, FS = Flameshot, KS = ksnip, LC = LosslessCut, KAP = Kap, OBS = OBS Studio.
Belege: [[Greenshot]], [[ShareX]], [[Other-Projects]].

| Funktion | GS | SX | STG | FS | KS | LC | KAP | OBS | Geeignetste Lösung | CaptureDoc |
|---|---|---|---|---|---|---|---|---|---|---|
| Bereich/Fenster/Bildschirm-Screenshot | ✅ | ✅ | ◐ | ✅ | ✅ | – | – | – | GDI BitBlt (MVP) + WGC (GS-Muster) | **eigen**, Phase 3 |
| Fenster ohne Überdeckung (WGC) | ✅ | ? | ? | – | – | – | – | ✅ | WGC (GS, OBS) | geplant Phase 6 |
| Region-Overlay mit Fenster-Snap | ✅ | ✅ | – | ✅ | ✅ | – | – | – | eingefrorenes Bild + Fenster-Rects | **eigen**, Phase 3 |
| Annotationen als Objekte | ✅ | ✅ | ◐ | ✅ | ✅ | – | – | – | Objektmodell (SX/GS) | **eigen**, Ebenen-Modell |
| Nummerierte Schritte | ✅ | ✅ | – | ✅ | ✅ | – | – | – | eigenes Objekt mit Auto-Nummer | **eigen** |
| Blur/Pixelate/Spotlight/Magnify | ✅ | ✅ | ◐ | ✅ | ✅ | – | – | – | Effektebene auf Komposit darunter | **eigen** |
| Ebenenpanel (Photoshop-artig) | – | ◐ | – | – | – | – | – | ◐ (Quellen) | – (keiner vollständig) | **eigen**, Kernfeature |
| Wiederbearbeitbares Projekt | ◐ (.greenshot binär) | ? | ✅ (.stg ZIP) | – | ◐ | ✅ (Projekt-JSON) | – | ✅ (Szenen) | ZIP+JSON (STG-Muster) | **eigen** `.capturedoc` |
| Undo/Redo | ✅ | ✅ (Memento) | ✅ | ✅ | ✅ | ✅ | – | – | Memento | **eigen** |
| Video-Aufnahme | – | ✅ (FFmpeg) | ✅ (DXGI) | – | – | – | ✅ (aperture) | ✅ | FFmpeg gdigrab/ddagrab | ✅ 0.2.0 (gdigrab) |
| GIF-Export mit Palette | – | ✅ (palettegen) | ✅ (eigener Encoder, Gifski) | – | – | – | ✅ | – | FFmpeg palettegen/paletteuse | ✅ 0.2.0 |
| Verlustfreier Schnitt | – | – | – | – | – | ✅ (`-c copy`) | – | – | Stream Copy (LC) | Phase 6/7 |
| Hardware-Encoding | – | ✅ (nvenc/amf/qsv) | ? | – | – | ✅ | ? | ✅ | FFmpeg-Encoderwahl | Phase 6 |
| Dateigrößen-Zielwert | – | – | ◐ | – | – | – | – | – | – (keiner mit Verifikation) | ✅ Bild, GIF, MP4 – iterativ + verifiziert |
| Obsidian-Export | – | – | – | – | – | – | – | – | – | **eigen**, Kernfeature |
| Dokumentations-Builder | – | – | – | – | – | – | – | – | – | **eigen** |
| Portable Nutzung | ✅ (Portable-Paket) | ✅ (portable) | ✅ | ◐ | ◐ | ✅ | – | ✅ (portable mode) | relative Datenpfade | **eigen**, Kernanforderung |
| Uploads/Cloud | ✅ (Plugins) | ✅ | ◐ | ✅ (imgur) | ✅ | – | ✅ | ✅ (Streaming) | – | **bewusst nicht** |

## Wiederverwendung
- **Keine Code-Übernahme** aus GPL-Projekten (GS, SX, FS, KS, LC, OBS). MS-PL (STG) und MIT (KAP) wären übernehmbar, aber relevante Teile sind WPF/Electron-spezifisch.
- Wiederverwendet werden **Bibliotheken**: Avalonia (MIT), SkiaSharp (MIT), CommunityToolkit.Mvvm (MIT), FFmpeg als externer Prozess (LGPL/GPL je nach Build). Details [[Licensing]].

## Probleme/Einschränkungen (aus Analyse)
- GDI-Capture kann HDR, DRM-geschützte Inhalte und manche GPU-Overlays nicht korrekt erfassen (**Schlussfolgerung** aus GS-Kommentar „copies what is displayed“).
- Fensteraufnahme per GDI enthält überdeckende Fenster → WGC nötig (GS `CapturesWindowContentOnly`).
- Kap ist seit 2024 inaktiv; Konzepte nur eingeschränkt aktuell.
