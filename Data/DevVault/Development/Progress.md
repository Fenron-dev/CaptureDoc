---
tags: [fortschritt]
---
# Fortschritt

## 2026-10-21 – 0.4.0: Video-Studio
Nutzer-Feedback zu 0.3.1 umgesetzt: Seitenleiste nicht scrollbar (Ursache: `PropertiesPanel : ScrollViewer` ohne Control-Theme), Klickzeiten/Anzahl (Startverzögerung + Doppelklicks), Spuren sahen falsch aus (Vorschaubilder wurden über die Spuren gestreckt – neue Timeline), Annotieren direkt im Video, Layout nach Vorbild gängiger Recorder, Stift/Textmarker, Live-Zeichnen während der Aufnahme, Folien für Zusammenfassungen.
**Getestet**: 150 automatisierte Tests ([[Test-Results]]), Layout per Headless-Rendering als Bild geprüft. **Nicht geprüft**: Bedienung am echten Bildschirm, Live-Zeichnen bei einer echten Aufnahme.

## 2026-10-21 – 0.3.0: Schnitte, Video-Schritte, Phase 7–10
**Implementiert**
- Schnitte im Video-Editor sichtbar und umkehrbar (Chips mit ✕, Entf), „Schnitte anwenden“ erzeugt gekürzte Fassung; verlustfreies Schneiden (Stream Copy).
- Ganzes Video bzw. Clip als **Anleitungsschritt** (Obsidian/Markdown-Embed, HTML `<video>`, PDF-Standbild), Projektformat v2 mit Migration, Videopfade relativ/portabel und beim „Speichern unter“ umgerechnet.
- Phase 7: zeitgesteuerte Annotationen mit Bewegung, Zoom/Pan-Keyframes, Live-Vorschau, Export auf Videopixel (Schwärzung eingebrannt).
- Mausklick-Markierung als bearbeitbare Annotationen.
- Phase 8/9: macOS/Linux-Adapter über Systemwerkzeuge, plattformneutrale Einzelinstanz mit Startbefehlen, Multi-Plattform-Paket (3 Starter) + ZIP mit Ausführungsrechten, ohne Nutzerdaten.
- Phase 10: PDF-Export, Ebenen per Drag & Drop, Anheften, Pipette, Stilvorlagen, Bild einfügen, Rahmen & Schatten, Serienaufnahme, Renderer-Beschleunigung.

**Getestet**: 137 automatisierte Tests inkl. 4 Headless-UI-Tests ([[Test-Results]]). GUI-Test per Bildschirmsteuerung war für diese Version nicht freigegeben → manuelle Prüfung der neuen Oberflächenteile steht aus.

**Gefunden und behoben**: Pfeil-Schatten über Vollbild-Ebene (0,6 s je Pfeil bei 4K), Deadlock-Risiko beim GIF-Export von Video-Schritten, Zeitstempel nach `zoompan`, `Visual.Clip` überdeckt, Einzelinstanz unter Unix nicht lauffähig. [[Changelog]]

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
