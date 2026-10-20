---
tags: [fortschritt]
---
# Fortschritt

## 2026-10-20 (Abend) – 0.2.0: HTML-Bild-Popup + Phase 6
**Implementiert**
- HTML-Export: Klick auf ein Bild öffnet es groß (eingepasst), Klick/Leertaste = 1:1, ←/→ blättern, Esc schließen; offline, ohne externe Skripte; ohne JavaScript öffnet der Link das Bild.
- Neues Projekt `CaptureDoc.Media`: FFmpeg-Suche (portabel zuerst), Aufnahme in Segmenten mit Pause, Zusammenfügen ohne Qualitätsverlust, MP4/GIF-Export mit geprüften Größenlimits, Frame-Extraktion. [[ADR-006 Video mit FFmpeg]]
- Aufnahme-UI mit rotem Rahmen und Timer-Leiste (aus der Aufnahme ausgeschlossen), Video-Editor mit Timeline, Trimmen und Schnitten, Exportdialog mit Profilen, Frame → Editor/Anleitung, Einstellungen für FPS/Countdown/Cursor/Mikrofon, Hotkey `Strg+Umschalt+Druck`.

**Getestet**: 112 automatisierte Tests (davon 13 mit echtem FFmpeg), GUI-Tests der Aufnahme und beider Exporte, Akzeptanztest 3 ✅. [[Test-Results]]

**Gefunden und behoben**: Rahmenfenster zu hoch (Windows-Mindestgröße) → ein transparentes Fenster; Leiste falsch positioniert (Größe vor Layout unbekannt) → Positionierung nach dem Layout. [[Changelog]]

## 2026-10-20 – Phase 1–3, Kern von Phase 4 und 5
**Implementiert**
- Analyse der 8 Referenzprojekte, [[Feature-Matrix]], [[Licensing]], 5 ADRs.
- Solution: Core / Rendering / Platform.Windows / App + 2 Testprojekte.
- Portable Umgebung: Datenordner neben der EXE, Schreibtest, Benutzerordner-Fallback, getrennte OS-Einstellungen, relative Pfade.
- Windows-Capture (GDI), Overlay je Monitor mit Fenster-Snap, Hotkeys, Tray, Einzelinstanz, Zwischenablage (DIB + PNG).
- Editor mit 16 Werkzeugen, Ebenenpanel, Eigenschaftenpanel, Undo/Redo, nichtdestruktiver Zuschnitt, Skalieren/Leinwand.
- Projektformat `.capturedoc` inkl. Prüfsummen, Migration, Konflikterkennung, Sperre, Auto-Save/Wiederherstellung.
- Bildexport mit geprüften Größenlimits, Exportprofile.
- Documentation Studio + Obsidian-, Markdown- und HTML-Export mit Konfliktschutz.
- Portable Paketierung: `CaptureDoc-Windows.exe` + `Runtime/win-x64` (keine Temp-Extraktion).

**Getestet**: [[Test-Results]] – 98 automatisierte Tests, GUI-Tests der Akzeptanzkriterien 1, 2, 4–8. Akzeptanz 3 (GIF) offen.

**Entscheidung**: ReadyToRun ohne Bundle-Kompression (Start 1,2 s statt 3,2 s; EXE 121 MB statt 39 MB) – [[Known-Problems]].

**Nächstes**: [[Roadmap]].
