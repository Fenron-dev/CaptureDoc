---
tags: [feature, obsidian]
status: implementiert
---
# Obsidian-Export

![[Assets/Screenshots/obsidian-export-vorschau.png|700]]

Code: `Core/Obsidian/` (`ObsidianExporter`, `MarkdownBuilder`, `GuideTemplate`, `SafeFileName`, `Vault`), UI `Views/StudioWindow.cs`, `Views/ObsidianExportDialog.cs`.

## Ablauf
1. **Plan** (ohne Schreibzugriff): Zielordner wird innerhalb des Vaults aufgelöst (kein `..`, nicht in `.obsidian`/`.trash`, keine Zeichen `# ^ [ ] |`), sichere Dateinamen, Bilder `<slug>-schritt-NN.png` in `Assets/`.
2. **Vorschau**: jede Datei mit Aktion NEU / AKTUALISIEREN / UNVERÄNDERT / NEUE VERSION / ÜBERSPRINGEN / ÜBERSCHREIBEN, das Markdown und ein Hinweis, welche Daten übertragen werden.
3. **Ausführen**: atomare Schreibvorgänge, danach Prüfung aller Embeds (fehlende Dateien werden gemeldet).

## Schutz manueller Änderungen
`.capturedoc-export.json` im Notizordner speichert je Export-Id die SHA-256 jeder geschriebenen Datei. Beim erneuten Export:
- Datei seit unserem Export unverändert → sicher aktualisieren.
- Datei verändert oder fremd → **Konflikt**; Standard „neue Version“ (`Anleitung (2).md`), alternativ überspringen oder nach Bestätigung überschreiben.
- Stabile `capturedoc-id` im Frontmatter; der nächste Export schreibt in die zuletzt verwendete Version.
- Nicht mehr verwendete Bilder werden nie gelöscht, nur gemeldet.

## Markdown
Frontmatter (title, tags, created, status, Vorlagen-Properties, capturedoc-id), Überschriften, Callouts `> [!info]`, Embeds `![[pfad|700]]` (Anzeigebreite getrennt von der Pixelgröße), Checklisten. Wahlweise Markdown-Links `![alt|700](Assets/x.png)` (URL-kodiert). 9 Vorlagen, als JSON in `Data/Templates/*.template.json` editierbar.

> [!warning] Unbekannt
> Die Darstellung in der Obsidian-App selbst wurde **nicht** geprüft (Testvault nicht in Obsidian geöffnet, um die Vault-Liste des Benutzers nicht zu verändern). Wikilinks nutzen den vollständigen vault-relativen Pfad, der nach Obsidians Auflösungslogik eindeutig sein sollte (Schlussfolgerung).

Zusätzlich: Markdown-Export in beliebige Ordner, HTML-Export (im Browser als PDF druckbar). Bidirektionale Synchronisation ist nicht Teil des MVP.
