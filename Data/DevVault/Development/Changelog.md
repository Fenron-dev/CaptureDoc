---
tags: [changelog]
---
# Changelog

## 0.5.0 – 2026-10-21
- **Mehrspur-Schnitt, nichts wird eingebrannt**: Das Video ist eine Sequenz aus Clips auf Spuren. **✂ Teilen (S)** schneidet an der Abspielposition in zwei Clips; jeder Teil lässt sich einzeln **entfernen** (auf der Hauptspur rückt alles nach – auch Annotationen und Zoom), **verschieben**, an den Rändern **kürzen** oder **auf eine andere Spur ziehen**. Strg+Z macht alles rückgängig. Die Originaldateien bleiben unverändert.
- **Weitere Videos importieren**: an der Abspielposition einfügen (Rest rückt nach), ans Ende anhängen oder als **Bild-im-Bild** auf eigener Spur darüberlegen; Position/Größe direkt im Video ziehen oder per Vorlage (Ecken, Mitte, Vollbild), Deckkraft, Lautstärke, Ton aus. Spuren ausblenden/stumm schalten.
- **Folien sind Clips** auf der Hauptspur (Einfügen schiebt den Rest nach hinten, Dauer ändern ebenso).
- Annotationen, die ganz in einem Clip liegen, wandern beim Verschieben mit (abschaltbar).
- Bereich auf dem Lineal + X entfernt den Bereich aus allen Spuren; ⇤ Anfang / Ende ⇥ kürzen.
- Export setzt alle Spuren zusammen (Lücken = Hintergrund, Ton aller Clips gemischt); ein einzelner unveränderter Clip wird weiter direkt exportiert (verlustfreies Schneiden bleibt möglich).
- Projektformat **v3** (Sequenz im Video-Schritt). Projekte aus 0.2–0.4 werden beim Öffnen umgestellt: Trim, Schnitte und Folien werden zu Clips, Annotationen und Zoom auf die neue Zeit umgerechnet.
- „✂ Schnitte anwenden“ (Einbrennen) entfällt.

## 0.4.0 – 2026-10-21
- **Video-Studio neu**: Werkzeuge links, Zeichnen **direkt im Video** (Vorschau nur der zur Abspielposition sichtbaren Annotationen, Schwärzen/Verpixeln/Lupe auf echten Pixeln), rechts Zeit & Reihenfolge + Eigenschaften, Ebenen, Zoom & Folien; der separate Annotations-Editor entfällt.
- **Spuren-Timeline**: Spurnamen, Lineal (Bereich ziehen + X = schneiden), Video, Zoom-Keyframes, Folien und eine Spur je Annotation; Einrasten am Abspielkopf, Zeitachsen-Zoom (Strg+Rad), Undo für Zeitänderungen.
- **Folien**: leere Frames mit Hintergrundfarbe (Vorlagen Leer/Titel/Zusammenfassung) mit eigener Zeitachse – Elemente erscheinen nacheinander; werden beim Export zwischen die Videoabschnitte gesetzt (Video pausiert, Ton bleibt synchron).
- **Zoom-Werkzeug**: Ausschnitt als Rechteck im Video aufziehen.
- **Live-Zeichnen während der Aufnahme** (Windows): Stift, Textmarker, Pfeil, Rahmen, Kreis, Nummer, Text, Farben, Stärke, Rückgängig, Ausblenden – nicht im Rohvideo, sondern als bearbeitbare Annotationen; Leiste halbtransparent, Werkzeuge in den Einstellungen wählbar.
- **Stift und Textmarker** (auch im Bildeditor, Taste G): Farbe, Stärke, Deckung.
- **Klick-Markierungen**: FFmpeg-Startverzögerung wird pro Segment herausgerechnet, Doppelklicks werden zusammengefasst, keine Markierung beim Zeichnen.
- **Annotationen einer Aufnahme bleiben erhalten**: automatisch in `<Aufnahme>.capturedoc` neben dem Video.
- **Frame → Anleitung/Editor** übernimmt die sichtbaren Annotationen als Ebenen.
- Behoben: **Eigenschaften-Leiste im Editor scrollte nicht** (abgeleiteter `ScrollViewer` ohne Template) – Linienstärke usw. waren unerreichbar; Eigenschaften zeigten nach Seitenwechsel die alte Auswahl.

## 0.3.1 – 2026-10-21
- **Annotations-Spuren** unter der Video-Timeline: je Annotation ein Balken, ziehen = zeitlich verschieben, Ränder = Beginn/Ende; Klick wählt die Annotation in der Seitenleiste.
- **Ebenenreihenfolge im Video** wie im Screenshot-Editor: ▲/▼ je Annotation; eine Schwärzung verdeckt nur, was unter ihr liegt (z. B. Nummer darüber bleibt sichtbar).
- Behoben: Zoom wurde *vor* Schwärzungen/Annotationen angewendet → bei gezoomten Stellen saß die Schwärzung an der falschen Stelle (Datenschutzrisiko). Zoom kommt jetzt zuletzt.
- Behoben: Absturz der Seitenleiste beim Neuaufbau (Zoom-Keyframe setzen, Annotation löschen) – Zoom-Regler hing noch am alten Platz.

## 0.3.0 – 2026-10-21
- **Video-Editor**: markierte Schnitte als Liste mit ✕ (Markierung zurücknehmen), Entf im Schnitt, „✂ Schnitte anwenden“ → neue gekürzte Fassung; MP4 „verlustfrei schneiden“ (Stream Copy).
- **Video-Schritte in Anleitungen**: Clip mit Schnitten/Annotationen als Schritt; Export nach Obsidian/Markdown (eingebettetes MP4/GIF), HTML (`<video>` mit Poster), PDF (Standbild + Hinweis). Projektformat **v2** (Migration von v1 automatisch).
- **Phase 7**: zeitgesteuerte Annotationen (alle Ebenentypen), Bewegung, Zoom/Pan-Keyframes, Live-Vorschau.
- **Mausklicks** werden während der Aufnahme als bearbeitbare Ring-Annotationen erfasst.
- **macOS/Linux**: Capture, Video (macOS/X11), Zwischenablage über Systemwerkzeuge; .app-Bundle; plattformneutrale Einzelinstanz; Startbefehle `--capture …`, `--studio`.
- **Paket**: `tools/publish.ps1` baut Windows + Linux + macOS in einen Ordner, `-Zip` erzeugt ein ZIP mit Ausführungsrechten und ohne Nutzerdaten.
- **Editor**: Ebenen per Drag & Drop, 📌 Anheften, 💧 Pipette, Stilvorlagen, Bild einfügen, Rahmen & Schatten; **Serienaufnahme** ins Studio; **PDF-Export**.
- **Performance**: Pfeile mit Schatten 25× schneller (Zwischenebene auf Pfeilbereich begrenzt), Drag-Cache für große Bilder (4K: 377 → 70 ms je Bewegung, pixelgleich).
- Behoben: Deadlock-Gefahr beim GIF-Export aus dem Studio (synchroner FFmpeg-Aufruf auf dem UI-Thread), falsche GIF-Dauer nach `zoompan`, `TimelineControl.Clip` verdeckte `Visual.Clip`, Start unter macOS/Linux (benannte Events nicht verfügbar), ZIP enthielt Logs/Nutzerdaten.

## 0.2.0 – 2026-10-20
- **HTML-Export**: Bild-Popup (Lightbox) mit Einpassen/1:1, Blättern, Tastatursteuerung; funktioniert offline.
- **Video/GIF (Phase 6)**: Bildschirmaufnahme mit Pause, Video-Editor (Timeline, Start/Ende, Schnitte), Export MP4 und GIF mit geprüften Größenlimits, Frame → Editor/Anleitung. Benötigt externes FFmpeg ([[ADR-006 Video mit FFmpeg]]).
- Einstellungen: Video-FPS, Countdown, Cursor, Mikrofon; neuer Hotkey wird in bestehende Einstellungen automatisch ergänzt.
- Behoben: Rahmen der Aufnahme bestand aus vier Fenstern, die Windows auf mind. 39 px Höhe vergrößerte (lagen damit über dem Bereich) → ein transparentes, durchklickbares Fenster; Aufnahmeleiste wurde vor dem Layout positioniert und landete oben links → Positionierung nach dem ersten Layout.

## 0.1.0 – 2026-10-20
Erster nutzbarer Windows-MVP (Quick Capture, Ebeneneditor, Projekte, Bildexport, Obsidian-Export). Details: [[Progress]].

Während der GUI-Tests gefunden und behoben (Ursache → Lösung):
- Absturz beim Öffnen des Editors: Render-Optionen wurden im Render-Durchlauf gesetzt (Avalonia 12 verbietet das) → `DrawingContext.PushRenderOptions`.
- Getippter Text landete nicht im neuen Textfeld: Fokus wurde vor Ende der Mausverarbeitung gesetzt → Fokus per Dispatcher nach dem Layout.
- Einfacher Text brach nach dem Bearbeiten falsch um: alte Breite blieb erhalten → einfacher Text hat automatische Breite.
- Quick-Capture-Leiste lag nach der Aufnahme über dem Editor → Editor wird nach dem Wiedereinblenden zuletzt aktiviert.
- Klick in der Ebenenliste fügte zur Auswahl hinzu: Toggle-Modus → Standard-Mehrfachauswahl (Strg/Umschalt).
- Studio-/Editorfenster größer als kleine Bildschirme (Titelleiste verdeckt) → werden in den Arbeitsbereich eingepasst.
- Wiederhergestellte Projekte zeigten auf die Sicherungskopie → zeigen auf das Original und übernehmen dessen Revision.
- Unbehandelte UI-Fehler beendeten die App → werden protokolliert und gemeldet, offene Arbeit bleibt erhalten.
- Beschädigte Umlaute nach einer Bearbeitung mit PowerShell 5.1 (ANSI-Standard) → Datei neu geschrieben; Regel: Quelltext nie mit `Set-Content` ohne `-Encoding` bearbeiten.
