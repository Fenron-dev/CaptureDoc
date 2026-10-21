---
tags: [feature, video]
status: Phase 6 + 7 implementiert, Video-Studio seit 0.4.0
---
# Video- und GIF-Editor („Video-Studio“)

Technik: [[ADR-006 Video mit FFmpeg]]. Code: `src/CaptureDoc.Media/` (`ScreenRecorder`, `VideoExporter`, `GifOptimizer`, `OverlaySpec`), UI `src/CaptureDoc.App/Video/` (`VideoEditorWindow`, `VideoTimeline`, `LiveDrawLayer`, `OverlayRenderer`, `VideoRenderPlan`). Bedienung: [[Video]].

## Aufnahme
- Bereich/Fenster/Bildschirm, Countdown, roter Rahmen + Timer-Leiste (aus der Aufnahme ausgeschlossen), Pause/Fortsetzen, FPS, Cursor, Mikrofon (Windows DirectShow; Systemton über „Stereomix“).
- **Zeitbasis**: Die Aufnahme-Uhr läuft nicht in Pausen. Nach dem Stopp wird pro Segment die echte Länge gemessen; mit der gemessenen Nachlaufzeit (`'q'` → Prozessende) wird die Startverzögerung von FFmpeg herausgerechnet (`ScreenRecorder.MapToVideoTime`). Gilt für Klicks und Live-Zeichnungen.
- **Mausklicks** (Windows: Low-Level-Hook nur während der Aufnahme) → Ring-Annotationen; Doppelklicks (< 0,5 s, ≤ 12 px) werden zusammengefasst. Klicks im Zeichenmodus werden ignoriert.
- **Live-Zeichnen** (`LiveDrawLayer`): transparentes Fenster exakt über dem Bereich, `WDA_EXCLUDEFROMCAPTURE` → nicht im Rohvideo. Zeiger-Modus = durchklickbar (`WS_EX_TRANSPARENT`), Zeichenmodus = fängt die Maus (Hintergrund mit Alpha 1, sonst gehen Klicks auf leere Pixel durch). Benutzt dieselbe `EditorCanvas` wie der Editor. Jede Zeichnung bekommt Start = Aufnahme-Uhr und Ende = Start + Einstellung (0 = bis „Alles ausblenden“). Werkzeuge und Deckkraft der Leiste in den Einstellungen.

## Mehrspur-Schnitt (0.5.0)
- Modell `VideoSequence` (Core/Model/Sequence.cs): Breite/Höhe/FPS/Hintergrund, Spuren (0 = Hauptspur unten), Clips mit Quelle, `SourceIn/Out`, `Start`, Platzierung (`Rect`, null = einpassen), Deckkraft, Lautstärke, Ton; Folien sind Clips (`ClipKind.Slide`).
- `SequenceOps`: `Split`, `SplitAll`, `RippleDelete` (Clips schneiden/entfernen/nachrücken; Annotationen im Bereich entfernen, übergreifende kürzen, spätere verschieben; Zoom ebenso), `RippleInsert` (Platz schaffen, übergreifende Annotationen in zwei Teile), `TrimStart/End` (Quellgrenzen), `LinkedAnnotations` (wandern beim Verschieben mit), `FromLegacy` (0.2–0.4 → Sequenz).
- Undo: ein Verlauf für Annotationen + Zoom + Sequenz (`ProjectSession.ReplaceHistory` mit eigenem Snapshot).
- Vorschau: je Quelle niedrig aufgelöste JPEG-Frames; das Bild aller Spuren wird je Zeitpunkt zusammengesetzt (`Compose`), beim Anhalten mit exakten Frames.
- Export: `VideoRenderPlan.PrepareSequenceAsync` → `VideoExporter.RenderSequenceAsync`: Farbfläche, je Clip `-ss/-t`-Eingang → `scale` → `setpts +Start` → `overlay … eof_action=pass:enable=between(t,…)`; Ton `adelay` + `amix`; dann Annotationen/Zoom (`OverlaySpec`, Eingangsnummern ab Clip-Anzahl). Ein einzelner unveränderter Clip ohne Annotationen geht direkt (Stream Copy bleibt möglich).

## Video-Studio (0.4.0, Layout wie gängige Screen-Recorder)
- **Zeichnen direkt im Video**: `EditorCanvas` mit `Underlay` = aktuelles Videobild (Vorschau-JPEG beim Abspielen, Vollbild beim Anhalten) und `LayerFilter` = Ebenen, die zur Abspielposition sichtbar sind (auch für Treffer). Effekte (Schwärzen, Verpixeln, Lupe) arbeiten damit in der Vorschau auf echten Pixeln. Neue Ebenen erhalten über `LayerCreated` (noch im selben Undo-Schritt) ihr Zeitfenster.
- Bewegte Annotationen werden über `DisplayOverride` an der interpolierten Stelle gezeigt; die ausgewählte bleibt an ihrer bearbeitbaren Position.
- **Spuren-Timeline** (`VideoTimeline`): Kopfspalte mit Namen, Lineal (ziehen = Bereich wählen, X = schneiden), Video (Thumbnails, Schnitte, Start/Ende), Zoom-Keyframes (ziehen), Folien (ziehen/Doppelklick), eine Spur je Ebene (verschieben/Ränder, Einrasten am Abspielkopf). Zeitachsen-Zoom mit Strg+Rad, Scrollen mit Umschalt+Rad/Scrollleiste. Zeit-Änderungen sind eine Undo-Transaktion.
- Rechts: Zeit & Reihenfolge der Auswahl + `PropertiesPanel`, `LayersPanel`, Zoom & Folien.
- **Zoom-Werkzeug**: Rechteck aufziehen = Keyframe mit diesem Ausschnitt (Seitenverhältnis des Videos).
- **Folien** (`VideoSlide` im Modell: `At` in Quellsekunden, `Duration`, eigene `Page` mit Hintergrundfarbe; Ebenenzeiten relativ zur Folie). Eigener Bearbeitungsmodus mit eigener Zeitachse; Vorlagen Leer/Titel/Zusammenfassung (Punkte erscheinen nacheinander).
- **Side-car**: Ohne Studio-Schritt werden Clip, Annotationen und Folien beim Schließen in `<Video>.capturedoc` gespeichert (normales Projekt mit einem Video-Schritt) und beim Öffnen geladen.

## Export
- Text/Pfeile/Formen/Schritte als transparente PNG-Overlays (`overlay … enable='between(t,a,b)'`, Bewegung über x/y-Ausdrücke), Schwärzen/Verpixeln/Weichzeichnen/Lupe direkt auf den Videopixeln. **Reihenfolge der Filterkette = Ebenenreihenfolge**; **Zoom (`zoompan`) als letzter Schritt**.
- Hervorhebung („multiply“) wird im Overlay-PNG mit 45 % Deckkraft gerendert (multiply auf transparentem Grund wäre deckend).
- **Folien** (`VideoExporter.ComposeAsync`): Hauptvideo mit Schnitten/Overlays rendern → je Folie ein Segment aus `color=`-Quelle + eigenen Overlays (+ `anullsrc` bei Ton) → Hauptvideo an den Einfügepunkten (Quellzeit → Ausgabezeit über die behaltenen Abschnitte) teilen und mit `concat` verbinden. Das Ergebnis ist eine Arbeitsdatei, die wie eine normale Quelle exportiert wird (`VideoRenderPlan`).

## In die Anleitung
„🎬 Video → Anleitung“ legt einen **Video-Schritt** an (Clip + Schnitte + Annotationen + Zoom + Folien, Bilder werden mitkopiert). Export: Obsidian/Markdown `![[…mp4|700]]`, HTML `<video controls poster>`, PDF als Standbild. „Frame → Anleitung/Editor“ übernimmt das Bild mit den sichtbaren Annotationen als Ebenen. Siehe [[Obsidian-Export]].

## Grenzen
- Videos werden im Projekt **referenziert**, nicht eingebettet.
- Vorschau ohne Ton, etwa 10 Bilder/s (Export vollständig).
- Keine Überblendungen zwischen Clips (harte Schnitte).
- Live-Zeichnen nur unter Windows (der Fenster-Ausschluss aus der Aufnahme ist Windows-spezifisch; sonst wären Zeichnungen doppelt im Video). Getestet headless (Zeichnen, Undo, Zeitumrechnung), nicht bei einer echten Aufnahme am Bildschirm.
- Linux/Wayland-Aufnahme fehlt ([[ADR-007 macOS und Linux]]).
