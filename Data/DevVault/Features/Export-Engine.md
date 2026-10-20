---
tags: [feature, export]
status: Bild implementiert, GIF/Video geplant
---
# Export-Engine

Code: `Core/Export/SizeOptimizer.cs` (encoderunabhängig), `Rendering/ImageExporter.cs`, `App/Views/ImageExportDialog.cs`.

## Bild (implementiert)
PNG, JPEG, WebP (verlustfrei/verlustbehaftet). Max. Breite/Höhe (Seitenverhältnis immer erhalten, nie hochskaliert), Skalierung, Qualität, Transparenz, Zielgröße in KB als **weiches Ziel** oder **harte Obergrenze**, Live-Vorschau der echten Dateigröße.

Optimierung: Abmessungen einpassen → Binärsuche Qualität (verlustbehaftet) → schrittweise verkleinern bis Mindestbreite. Jede Kandidatengröße ist echt kodiert, nicht geschätzt. Nach dem Schreiben wird die Datei von der Platte neu gelesen (Größe + Abmessungen). Harte Grenze nicht erreichbar → **keine Datei** + Meldung mit dem kleinsten erreichbaren Wert.

Metadaten: Skia schreibt keine EXIF/XMP (Test prüft auf APP1-Marker). AVIF: SkiaSharp 3.119 kodiert kein AVIF → geplant über FFmpeg. SVG: geplant.

Profile: Original Quality, Documentation PNG, Obsidian Optimized, Small Screenshot, Email Attachment (hart 500 KB), Support Ticket (hart 2 MB), Custom; eigene Profile in `Data/Presets/export-profiles.json` (kopieren, bearbeiten).

## GIF/Video (geplant, Phase 6)
Gleicher `SizeOptimizer` mit FPS/Farbanzahl/Auflösung; FFmpeg `palettegen/paletteuse` (vgl. [[ShareX]]). Noch nicht implementiert.
