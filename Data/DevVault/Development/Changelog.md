---
tags: [changelog]
---
# Changelog

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
