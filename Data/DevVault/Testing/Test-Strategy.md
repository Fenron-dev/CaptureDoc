---
tags: [test]
---
# Teststrategie

- **Unit (xUnit, automatisch)**
  - `tests/CaptureDoc.Core.Tests`: Ebenenmodell, Sortierung, Gruppen, Undo/Redo, Projektformat inkl. Beschädigung/Konflikt/Migration/Sperre/Verschieben, Portabilität und Einstellungen, Größenoptimierer, Exportprofile, Obsidian (Markdown, Pfadprüfung, Konflikte, Linkprüfung), HTML, Dateinamen, Hotkey-Parser.
  - `tests/CaptureDoc.Rendering.Tests`: alle Ebenentypen, Schwärzung/Verpixeln/Weichzeichnen, Exportabmessungen, harte/weiche Größenlimits auf der Platte geprüft, EXIF-Freiheit, Speichern → Laden pixelgleich.
- **Manuell (GUI)** am portablen Build per Bildschirmsteuerung: Akzeptanztests 1–8.
- **Messung**: Startzeit („UI ready“ im Log), RAM, Leerlauf-CPU.
- Nicht verfügbare Umgebungen (macOS, Linux, Mehrmonitor, andere DPI-Stufen) werden nie als getestet gemeldet.

```bash
dotnet test tests/CaptureDoc.Core.Tests
dotnet test tests/CaptureDoc.Rendering.Tests
```
