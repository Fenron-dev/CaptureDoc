---
tags: [offen, risiken]
---
# Offene Punkte & Risiken

| # | Thema | Art | Notiz |
|---|---|---|---|
| 1 | Videos im Projekt nur referenziert | Einschränkung | bei Weitergabe Videodatei mitgeben; Einbetten optional geplant |
| 1b | Systemton | Teilweise | Windows: Gerät „Stereomix“ wählen; echte WASAPI-Loopback-Aufnahme fehlt |
| 1d | FFmpeg nicht im Paket | Entscheidung | bewusst (Lizenz/Größe), Hinweis in App und README |
| 2 | macOS/Linux ungetestet | Testlücke | Adapter + Pakete vorhanden ([[ADR-007 macOS und Linux]]); Wayland-Video fehlt; keine Fensterliste/Hotkeys |
| 2a | Neue 0.3.0-Oberfläche nicht per Bildschirm geprüft | Testlücke | Headless-Tests vorhanden; manueller Durchgang ausstehend |
| 2b | Klick-Markierungen leicht verfrüht | Einschränkung | Startverzögerung von FFmpeg (~0,2–0,5 s) wird nicht herausgerechnet; Markierung im Editor verschiebbar |
| 3 | Fensteraufnahme mit Überdeckung | Einschränkung | GDI nimmt sichtbaren Inhalt auf; WGC geplant |
| 4 | Bereich über mehrere Monitore | Einschränkung | Auswahl derzeit je Monitor; Mehrmonitor nicht getestet (nur 1 Monitor vorhanden) |
| 5 | Hotkey-Auslösung | Testlücke | Registrierung erfolgreich; Auslösen per Druck-Taste nicht automatisiert prüfbar |
| 6 | Obsidian-Darstellung | Testlücke | Markdown + Links geprüft, nicht in der Obsidian-App angesehen |
| 7 | EXE-Größe 121 MB | Kompromiss | Trimming/Native AOT in Phase 10 |
| 9 | Ebenen drehen | Feature | Drag & Drop-Sortierung seit 0.3.0 vorhanden |
| 10 | Erster Klick in inaktiven Fenstern | UX | wurde teils nur als Fensteraktivierung gewertet (Export-Button) |
| 12 | Avalonia-Build-Telemetrie | Datenschutz (Build) | `Avalonia.BuildServices` sendet beim Bauen anonyme Daten; Opt-out in `tools/publish.ps1`, frühere Builds vom 2026-10-20 liefen ohne |
| 13 | macOS-Bundle unsigniert | Plattform | ad-hoc signieren auf dem Mac, siehe [[ADR-002 Portable Paketierung]] |

Erledigt in 0.3.0: PDF-Direktexport, Drag & Drop der Ebenen, Neuzeichnen großer Bilder (Drag-Cache, gemessen), Phase-7-Annotationen, Stream Copy, Klick-Markierung.
