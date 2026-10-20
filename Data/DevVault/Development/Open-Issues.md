---
tags: [offen, risiken]
---
# Offene Punkte & Risiken

| # | Thema | Art | Notiz |
|---|---|---|---|
| 1 | Video/GIF fehlt | Feature (Phase 6) | Akzeptanztest 3 (GIF ≤ 5 MB) daher **nicht** erfüllt |
| 2 | macOS/Linux-Aufnahme | Feature (Phase 8/9) | Interfaces vorhanden; Aufnahme meldet „nicht implementiert“. Nicht getestet (keine Umgebung) |
| 3 | Fensteraufnahme mit Überdeckung | Einschränkung | GDI nimmt sichtbaren Inhalt auf; WGC geplant |
| 4 | Bereich über mehrere Monitore | Einschränkung | Auswahl derzeit je Monitor; Mehrmonitor nicht getestet (nur 1 Monitor vorhanden) |
| 5 | Hotkey-Auslösung | Testlücke | Registrierung erfolgreich; Auslösen per Druck-Taste nicht automatisiert prüfbar |
| 6 | Obsidian-Darstellung | Testlücke | Markdown + Links geprüft, nicht in der Obsidian-App angesehen |
| 7 | EXE-Größe 121 MB | Kompromiss | Trimming/Native AOT in Phase 10 |
| 8 | PDF-Direktexport | Feature | aktuell HTML → Browser „Als PDF drucken“ |
| 9 | Ebenen per Drag & Drop sortieren, Drehen | Feature | |
| 10 | Erster Klick in inaktiven Fenstern | UX | wurde teils nur als Fensteraktivierung gewertet (Export-Button) |
| 11 | Neuzeichnen großer Bilder bei jeder Mausbewegung | Performance | ganzseitig; bei 4K+ noch nicht gemessen |
| 12 | Avalonia-Build-Telemetrie | Datenschutz (Build) | `Avalonia.BuildServices` sendet beim Bauen anonyme Daten; Opt-out im Skript seit 0.1.0-Ende, frühere Builds dieses Tages liefen ohne |
| 13 | macOS-Bundle, Signatur, TCC | Plattform | Konzept in [[ADR-002 Portable Paketierung]] |
