---
tags: [feature, bewertung]
---
# Weitere Funktionen – Bewertung (Abschnitt 14)

| Funktion | Stand | Bewertung / Weg |
|---|---|---|
| Screenshot-Pinning | ✅ 📌 im Editor (Strg+P), immer im Vordergrund, Mausrad = Größe | – |
| Farbpipette | ✅ Werkzeug 💧 (K), übernimmt Farbe für Auswahl + neue Elemente | – |
| Wiederverwendbare Stilvorlagen | ✅ 5 eingebaut + eigene speichern | – |
| Bildausschnitte kombinieren | ✅ 🖼＋ Bild als Ebene einfügen, Copy/Paste von Ebenen | – |
| Schatten & Rahmen | ✅ „Rahmen & Schatten“ (transparenter Rand, Schatten, Ecken) | – |
| Serienaufnahme | ✅ Tray/Studio: N Bilder im Abstand → Schritte | – |
| Mausklick-Hervorhebung (Video) | ✅ als bearbeitbare Annotationen | – |
| Scrolling Capture | geplant | Windows: Fenster per `WM_VSCROLL`/Mausrad scrollen + Überlappung per Bildvergleich stitchen; heikel bei animierten Inhalten |
| OCR / Textkopie | geplant | Windows.Media.Ocr (offline, WinRT) bzw. Tesseract lokal; keine Cloud |
| QR-Erkennung | geplant | ZXing.Net (Apache-2.0), rein lokal |
| Bildvergleich / Vorher-Nachher | geplant | zwei Seiten nebeneinander oder Schieberegler-Ansicht im HTML-Export |
| Lineal / Maßlinien | geplant | Linienebene mit Pixelbeschriftung |
| Wasserzeichen | geplant | Exportprofil-Option (Textebene beim Rendern) |
| Automatische Dokumentationsschritte, Erkennung von Bildschirmwechseln | geplant | Szenenwechsel per FFmpeg `select='gt(scene,0.3)'` → Frame-Vorschläge |
| Lokale KI | offen | nur lokale Modelle, optional |
| TTRPG-Raster, Kartenannotationen | geplant | Rasterebene (Hex/Quadrat) als eigener Ebenentyp |
| Sticker | teilweise | Emoji über das Textwerkzeug (✓ ✗ ⚠ ★ ➜) |

Grundsatz: keine dieser Funktionen verzögert die priorisierten Abläufe; alle bleiben offline.
