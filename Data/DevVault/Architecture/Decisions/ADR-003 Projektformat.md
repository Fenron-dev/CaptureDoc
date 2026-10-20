---
tags: [adr, format]
status: akzeptiert
date: 2026-10-20
---
# ADR-003 Projektformat `.capturedoc`

## Entscheidung
ZIP-Container (Deflate) mit JSON-Metadaten und inhaltsadressierten Assets:

```
mimetype            "application/x-capturedoc+zip" (erster Eintrag, unkomprimiert)
manifest.json       Format, formatVersion, appVersion, projectId, revision, savedAt, SHA-256 je Eintrag
project.json        Seiten, Ebenen, Anleitung, Asset-Index, Einstellungen
assets/<sha256>.png Originalbilder (Dateiname = Inhalts-Hash → Deduplizierung)
thumbnail.png       gerenderte Vorschau (max. 512 px)
```

## Eigenschaften
- **Versioniert:** `formatVersion` (aktuell 1). Ältere Versionen werden über eine Migrationskette auf JSON-Ebene (`IProjectMigration`) angehoben; neuere Versionen werden mit klarer Meldung abgelehnt.
- **Fehlererkennung:** SHA-256 jedes Eintrags im Manifest; beim Laden geprüft. Beschädigte Assets werden gemeldet, das Projekt öffnet trotzdem, wenn `project.json` intakt ist.
- **Atomar:** Schreiben in Temp-Datei im selben Ordner → Datei wird neu geöffnet und validiert → `File.Move(overwrite)` (atomarer Rename auf demselben Volume). Die alte Datei wird nie vor erfolgreicher Validierung ersetzt.
- **Konflikte:** Vor dem Ersetzen wird die `revision` der Zieldatei mit der geladenen verglichen → `ProjectConflictException`, wenn ein anderer Prozess/Rechner zwischenzeitlich gespeichert hat.
- **Sperre:** `<datei>.lock` mit `FileShare.None` + `DeleteOnClose`; veraltete Sperren (Prozess nicht mehr vorhanden) werden erkannt.
- **Verschiebbar:** Keine absoluten Pfade im Projekt; Bilder sind eingebettet. Videos (Phase 6) als eingebettete Datei oder projektrelative Referenz.
- **Datenschutz:** Originalbilder bleiben enthalten, auch wenn eine Schwärzungsebene darüber liegt → UI-Warnung beim Teilen/Speichern, siehe [[Layer-System#Schwärzung]].

## Alternativen
- Greenshot-Ansatz (PNG + Binärserialisierung, [[Greenshot]]) – nicht plattformübergreifend, keine Integritätsprüfung.
- SQLite-Datei – robust, aber schlechter manuell inspizierbar; ZIP ist mit Standardwerkzeugen lesbar.
- Ordnerprojekt – fehleranfälliger beim Kopieren/Teilen.
