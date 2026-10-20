---
tags: [adr, editor]
status: akzeptiert
date: 2026-10-20
---
# ADR-004 Undo/Redo per Memento

## Entscheidung
Jede Bearbeitung läuft über `History.Execute(beschreibung, aktion)` bzw. `BeginTransaction`/`Commit` (für Ziehen/Skalieren). Vorher/Nachher-Zustand einer **Seite** wird als JSON-Snapshot gespeichert (Ebenen, Größe, Hintergrund). Bilddaten sind unveränderliche Assets und nicht Teil des Snapshots.

## Begründung
- Ein Mechanismus für alle Operationen (Gruppieren, Sortieren, Eigenschaften, Crop) → keine vergessenen Inverse-Operationen.
- Snapshots sind klein (KB), da Crop/Resize nichtdestruktiv sind (Seitenausschnitt + Transformation statt Pixeländerung). ShareX braucht dafür Vollbild-Bitmaps im Memento ([[ShareX]]) – wir nicht.
- Leere Änderungen (identischer Snapshot) erzeugen keinen Eintrag.
- Beschreibungen ermöglichen eine sichtbare Verlaufsliste.

## Alternativen
- Klassisches Command-Pattern mit Do/Undo je Operation: feingranularer, aber fehleranfälliger und mehr Code.
- Event Sourcing: für Kollaboration interessant, hier überdimensioniert.

## Grenzen
- Sehr viele Ebenen (>10 000) machen Snapshots teurer – nicht realistisch für Screenshots. Limit: 200 Einträge je Seite.
