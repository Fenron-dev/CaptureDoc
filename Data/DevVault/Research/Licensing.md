---
tags: [research, lizenz]
---
# Lizenzprüfung

Stand 2026-10-20. Keine Rechtsberatung – technische Einordnung.

## Referenzprojekte (nur Konzepte, kein Code)
| Projekt | Lizenz | Folge für CaptureDoc |
|---|---|---|
| Greenshot, ShareX, Flameshot, ksnip | GPL-3.0 | Code-Übernahme würde CaptureDoc bei Weitergabe GPL-3.0-pflichtig machen. **Entscheidung:** Clean-Room, nur Konzepte/Ideen (nicht urheberrechtlich geschützt). |
| LosslessCut, OBS | GPL-2.0 | wie oben |
| ScreenToGif | MS-PL | Übernahme möglich mit Lizenzhinweis; bei Weitergabe im Quellcode nur unter MS-PL. Nicht genutzt. |
| Kap | MIT | Übernahme mit Copyright-Hinweis möglich. Nicht genutzt. |

## Eingesetzte Komponenten
Vollständige Liste mit Versionen: `THIRD_PARTY_LICENSES.md` im Repo-Wurzelverzeichnis.

| Komponente | Lizenz | Pflichten |
|---|---|---|
| .NET Runtime (self-contained eingebettet) | MIT | Lizenz- und Copyright-Hinweis beilegen |
| Avalonia 12.1.4 | MIT | Hinweis beilegen |
| SkiaSharp 3.119.4 (+ native Skia, HarfBuzzSharp) | MIT / BSD-3 (Skia) / MIT (HarfBuzz) | Hinweise beilegen |
| CommunityToolkit.Mvvm 8.4.2 | MIT | Hinweis beilegen |
| ANGLE (`av_libglesv2.dll`, über Avalonia) | BSD-3 | Hinweis beilegen |
| xUnit (nur Tests, nicht ausgeliefert) | Apache-2.0 | – |
| FFmpeg (Phase 6, externer Prozess, **nicht** gebündelt im MVP) | LGPL-2.1+ bzw. GPL bei `--enable-gpl` (z. B. libx264) | Bei Mitlieferung: Lizenztext, Quellangebot, Build-Konfiguration dokumentieren |

## Interne Nutzung ≠ Lizenzfreiheit
- GPL-Pflichten greifen bei **Weitergabe/„Conveying“** (GPL-3.0 §6). Reine interne Nutzung in derselben Organisation gilt nach überwiegender Auffassung nicht als Weitergabe – **aber**: Weitergabe an Dritte (Kunden, Partner, externe Dienstleister, USB-Stick an Fremdfirma) ist Weitergabe.
- MIT/BSD-Hinweispflichten gelten bei jeder Weitergabe einer Kopie → `THIRD_PARTY_LICENSES.md` liegt immer im portablen Ordner.
- FFmpeg-GPL-Builds (z. B. Gyan „full_build“, auf diesem Rechner installiert) dürfen bei Weitergabe nur mit GPL-Pflichten mitgeliefert werden → für eine spätere Bündelung LGPL-Build bevorzugen.

Siehe [[ADR-001 Technologie-Stack]], [[Feature-Matrix]].
