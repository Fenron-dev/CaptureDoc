---
tags: [feature, video]
status: Phase 6 + 7 implementiert
---
# Video- und GIF-Editor

Technik: [[ADR-006 Video mit FFmpeg]]. Code: `src/CaptureDoc.Media/` (`ScreenRecorder`, `VideoExporter`, `GifOptimizer`, `OverlaySpec`), UI `src/CaptureDoc.App/Video/`.

## Aufnahme
Bereich/Fenster/Bildschirm, Countdown, roter Rahmen + Timer-Leiste (aus der Aufnahme ausgeschlossen), Pause/Fortsetzen, FPS, Cursor, Mikrofon (Windows DirectShow; Systemton über „Stereomix“), **Mausklicks werden als bearbeitbare Ring-Annotationen** im Editor angelegt (Windows: Low-Level-Maus-Hook nur während der Aufnahme; macOS: Klicks von avfoundation).

## Schneiden
- Start/Ende (I/O), Abschnitte markieren (X zweimal); markierte Abschnitte erscheinen als Chips „✂ von–bis“ mit **✕ zum Entfernen**, Entf löscht den Schnitt unter dem Abspielkopf.
- **„✂ Schnitte anwenden“** schreibt eine neue, gekürzte Fassung (`…_geschnitten.mkv`) und öffnet sie – das Original bleibt erhalten. (Vorher wurden Schnitte nur beim Export angewendet, was in der Oberfläche nicht erkennbar war.)
- MP4-Export „verlustfrei schneiden“ (Stream Copy, LosslessCut-Ansatz) für H.264-4:2:0-Videos; Schnittpunkte liegen dann auf Keyframes.

## Zeitgesteuerte Annotationen (Phase 7)
- „✎ Annotationen bearbeiten…“ öffnet den normalen Ebenen-Editor mit dem aktuellen Videobild als gesperrter Vorlage (wird nicht exportiert).
- Jede Ebene hat **von/bis** (Buttons ⇤/⇥ = Abspielposition) und optional **Bewegung** bis zu einer Zielposition (Keyframes).
- **Zoom & Schwenk:** Klick ins Vorschaubild setzt den Mittelpunkt, „🔍 Keyframe hier“ mit Faktor 1–4×, weiche Übergänge.
- Live-Vorschau der Annotationen und des Zoom-Ausschnitts über dem Video.
- Export: Text/Pfeile/Formen/Schritte als transparente PNG-Overlays (`overlay … enable='between(t,a,b)'`, Bewegung über x/y-Ausdrücke), Schwärzen/Verpixeln/Weichzeichnen/Lupe direkt auf den Videopixeln, Zoom über `zoompan` (Zeitstempel danach neu gesetzt).

## In die Anleitung
„🎬 Video → Anleitung“ legt einen **Video-Schritt** an (Clip + Schnitte + Annotationen, Posterbild als bearbeitbare Seite). Im Studio: Format MP4/GIF, max. Breite, Ziel-MB, Ton; „✂ Schneiden / Annotieren…“ öffnet den Clip wieder. Export: Obsidian/Markdown `![[…mp4|700]]`, HTML `<video controls poster>`, PDF als Standbild mit Hinweis. Siehe [[Obsidian-Export]].

## Grenzen
- Videos werden im Projekt **referenziert** (relativ zur Projektdatei bzw. portabel), nicht eingebettet → beim Weitergeben des Projekts die Videodatei mitgeben.
- Linux/Wayland-Aufnahme fehlt ([[ADR-007 macOS und Linux]]).
