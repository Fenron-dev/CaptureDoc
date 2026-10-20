---
tags: [adr, windows, capture]
status: akzeptiert
date: 2026-10-20
---
# ADR-005 Capture-Backend Windows (MVP: GDI)

## Entscheidung
MVP nutzt **GDI BitBlt mit CAPTUREBLT** auf den virtuellen Desktop (alle Monitore, physische Pixel, Prozess ist Per-Monitor-DPI-aware v2). Bereich, Fenster und Monitor werden aus diesem eingefrorenen Bild ausgeschnitten. Cursor optional via `GetCursorInfo` + `DrawIconEx`.

## Begründung
- Sofort verfügbar ohne WinRT-Projektion, kein GPU-Device nötig, minimale Startzeit.
- Greenshot nutzt GDI als Fallback, ShareX als Standard ([[Greenshot]], [[ShareX]]).
- „Eingefrorenes Bild“ ermöglicht Bereichsauswahl über Menüs/Tooltips hinweg (wie bei allen Referenzen).

## Bekannte Grenzen
- Fenster werden so aufgenommen, wie sie sichtbar sind (inkl. Überdeckung).
- HDR-Inhalte, geschützte Inhalte (DRM) werden ggf. schwarz/falsch dargestellt.

## Nächster Schritt
Windows.Graphics.Capture (WGC) als zweites Backend hinter `IScreenCaptureService` in Phase 6 (wird für Video ohnehin benötigt; zeigt den gelben Aufnahmerahmen des Systems → Betriebssystemanzeige respektiert).
