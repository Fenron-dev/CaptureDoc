---
tags: [changelog]
---
# Changelog

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
