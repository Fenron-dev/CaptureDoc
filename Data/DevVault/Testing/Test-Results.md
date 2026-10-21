---
tags: [test, ergebnisse]
date: 2026-10-21
---
# Testergebnisse (Stand 2026-10-21)

Umgebung: Windows 11 Home 10.0.26200, 1 Monitor 1600×900 physisch (Skalierung ≈ 110 %), .NET SDK 10.0.402.

## Automatisiert (tatsächlich ausgeführt, Stand 0.4.0, 2026-10-21)
| Projekt | Ergebnis |
|---|---|
| CaptureDoc.Core.Tests | **95/95 bestanden** (u. a. Video-Schritte, Folien + Textmarker speichern/laden, Projektformat v2, Einzelinstanz/IPC) |
| CaptureDoc.Rendering.Tests | **17/17 bestanden** (u. a. PDF, Rahmen/Schatten, Drag-Cache pixelgleich, Performance) |
| CaptureDoc.Media.Tests (echtes FFmpeg 9.0.2) | **27/27 bestanden** (u. a. Folien einfügen mit/ohne Ton und zeitversetzten Elementen pixelgeprüft, Uhr→Videozeit bei echter Aufnahme mit Pause, Overlays/Zoom, Reihenfolge) |
| CaptureDoc.App.Tests (Avalonia Headless) | **11/11 bestanden** (Schnitte per Tastatur, Zeichnen direkt im Video mit Zeitfenster, Textmarker, Spuren ziehen + Undo + Reihenfolge, Folienmodus, Side-car speichern/laden, Live-Zeichnen → Annotationen, Eigenschaften-Leiste scrollt, Video-Schritt mit Folie, GDI + Maus-Hook) + 1 Screenshot-Helfer (nur mit `CD_SHOT_DIR`) |

## 0.3.0 – nicht geprüft
- Oberfläche der neuen Funktionen per Bildschirmsteuerung (für diese Version nicht freigegeben) – nur headless.
- macOS- und Linux-Pakete: gebaut, nicht gestartet (keine Testgeräte).

## Performance 0.3.0
| Messung (Release, 4K 3840×2160) | Wert |
|---|---|
| Seite mit 20 Ebenen inkl. Effekten | 2 599 ms → **148 ms** (Ursache: Pfeil-Schatten über Vollbild-Ebene) |
| Ziehen mit Drag-Cache (12 Schatten-Pfeile + Effekt) | 377 ms → **70 ms** pro Bewegung, pixelgleich |

## Phase 6 – Video/GIF (manuell am portablen Build)
- Aufnahme Bereich → Countdown → Rahmen + Leiste → Pause/Weiter → Stopp → Editor ✅ (364×352 bzw. 330×220 px, 30 fps)
- Rahmen- und Leistenfenster: `affinity=0x11` (aus Aufnahme ausgeschlossen) und im exportierten GIF nachweislich nicht enthalten ✅
- Trimmen 10,1–20,1 s → GIF „Optimized“: 364×352, 10,1 s, 824 KB, Limit geprüft ✅
- MP4 High Quality: 330×220, 55,5 s, 80,7 KB ✅
- HTML-Export mit Bild-Popup im Browser: Öffnen, Einpassen/1:1, ←/→, Esc ✅

## Akzeptanztests (manuell am portablen Build)
| # | Test | Ergebnis |
|---|---|---|
| 1 | Screenshot + 3 Annotationsebenen + Speichern | ✅ Pfeil, Rechteck, 2 Schritte, Text; `.capturedoc` 99 KB |
| 2 | Schließen, öffnen, alle Ebenen weiterbearbeiten | ✅ Text, Position, Farbe (Mehrfachauswahl), Undo/Redo |
| 3 | GIF ≤ 5 MB | ✅ (0.2.0) automatisiert mit FFmpeg: Testclip 1280×720, 10 s → **800×450, 15 fps, 128 Farben, 4,19 MB**, Größe und Dauer von der Datei nachgemessen |
| 4 | Bild max. 800 px Breite | ✅ automatisiert (1600 → 800×450); manuell JPEG max. 300 px + harte Grenze 8 KB → 300×192 px, **8 102 Bytes**, unabhängig mit System.Drawing nachgemessen |
| 5 | Anleitung in Vault exportieren, Links prüfen | ✅ Testvault, 2 Dateien, Linkprüfung ok |
| 6 | Re-Export nach manueller Änderung | ✅ Original mit manueller Ergänzung unverändert, `Drucker konfigurieren (2).md` geschrieben |
| 7 | Portablen Ordner verschieben, Projekte öffnen | ✅ Kopie an anderem Pfad nutzt eigenen `Data`-Ordner, Projekt mit allen Ebenen geöffnet |
| 8 | Schwärzung nicht rekonstruierbar | ✅ automatisiert + exportiertes Vault-Bild: 1 312 geprüfte Pixel im Bereich alle (0,0,0) |

## Weitere manuelle Prüfungen
- Zwischenablage: Formate `DeviceIndependentBitmap`, `PNG`; 512×327 ✅
- Absturz-Wiederherstellung nach hartem Beenden ✅ (nach Fix: Speichern ohne falschen Konflikt)
- Einzelinstanz ✅ · Fenster-Hervorhebung im Fenstermodus ✅
- Keine Entpackung nach `%TEMP%\.net` ✅ (Runtime-Ordner im Log)
- Globale Hotkeys: Registrierung ✅, Auslösung ❓ (Druck-Taste nicht automatisierbar)

## Performance
| Messung | Wert |
|---|---|
| Start bis UI (ReadyToRun) | 1 212–1 223 ms warm, 2 194 ms erster Start (ohne ReadyToRun: 3 110–3 428 ms) |
| Arbeitsspeicher Leerlauf | ~178 MB Working Set, ~110 MB privat |
| CPU Leerlauf | 0–47 ms CPU-Zeit über 5 s (≈ 0–1 %) |
| Desktop einfrieren 1600×900 | 72–236 ms |
