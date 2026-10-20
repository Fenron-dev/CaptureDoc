---
tags: [test, ergebnisse]
date: 2026-10-20
---
# Testergebnisse (2026-10-20)

Umgebung: Windows 11 Home 10.0.26200, 1 Monitor 1600×900 physisch (Skalierung ≈ 110 %), .NET SDK 10.0.402.

## Automatisiert (tatsächlich ausgeführt)
| Projekt | Ergebnis |
|---|---|
| CaptureDoc.Core.Tests | **85/85 bestanden** |
| CaptureDoc.Rendering.Tests | **13/13 bestanden** |

## Akzeptanztests (manuell am portablen Build)
| # | Test | Ergebnis |
|---|---|---|
| 1 | Screenshot + 3 Annotationsebenen + Speichern | ✅ Pfeil, Rechteck, 2 Schritte, Text; `.capturedoc` 99 KB |
| 2 | Schließen, öffnen, alle Ebenen weiterbearbeiten | ✅ Text, Position, Farbe (Mehrfachauswahl), Undo/Redo |
| 3 | GIF ≤ 5 MB | ❌ **nicht möglich** – GIF noch nicht implementiert |
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
