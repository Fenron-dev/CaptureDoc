---
tags: [offen, risiken]
---
# Offene Punkte & Risiken

| # | Thema | Art | Notiz |
|---|---|---|---|
| 1 | Zeitgesteuerte Annotationen im Video, Zoom/Pan | Feature (Phase 7) | Modell vorbereitet; Video-Basis seit 0.2.0 vorhanden |
| 1a | Videos nicht im `.capturedoc`-Projekt | Feature (Phase 7) | Aufnahmen liegen als MKV in `Data/Media/Aufnahmen` |
| 1b | Systemton, Mausklick-Hervorhebung | Feature | Mikrofon funktioniert (DirectShow); Systemton braucht WASAPI-Loopback |
| 1c | Stream-Copy-Schnitt ohne Neukodierung | Feature | derzeit wird beim Export immer neu kodiert |
| 1d | FFmpeg nicht im Paket | Entscheidung | bewusst (Lizenz/Größe), Hinweis in App und README |
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
