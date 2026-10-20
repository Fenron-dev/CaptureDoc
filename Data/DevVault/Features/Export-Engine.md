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

## GIF (implementiert)
Einstellungen: max. Breite/Höhe, FPS, Farben, Dithering (sierra2_4a, floyd_steinberg, bayer, none), Wiederholung, „unveränderte Frames entfernen“ (`mpdecimate`, variable Frame-Dauer), Dateigrößenlimit weich/hart, Mindest-FPS, Mindestbreite.
`GifOptimizer`: jede Variante wird echt erzeugt und gemessen; Reihenfolge Farben → FPS → Auflösung. Harte Grenze nicht erreichbar → keine Datei + Meldung mit dem kleinsten erreichbaren Ergebnis.
Beleg: 10-s-Testclip 1280×720 → **800×450, 15 fps, 128 Farben, 4,19 MB ≤ 5 MB** ([[Test-Results]]).

## MP4 (implementiert)
Auflösung, FPS, Qualitätsstufe (CRF), Encoder (libx264, NVENC, AMF, Quick Sync – bei Fehler automatisch libx264), Ton an/aus, Dateigrößenlimit über Zielbitrate mit bis zu 5 Nachregelungen; zu wenig Bitrate → Auflösung sinkt bis Mindestbreite. Ausgabe H.264 4:2:0 + `faststart` (überall abspielbar).
Profile: High Quality Video, Small MP4 (≤ 10 MB), E-Mail-Anhang (≤ 20 MB), Support Ticket (≤ 50 MB), Benutzerdefiniert.

Noch offen: verlustfreier Stream-Copy-Schnitt für importierte 4:2:0-Videos (LosslessCut-Ansatz) – derzeit wird beim Export immer neu kodiert.
