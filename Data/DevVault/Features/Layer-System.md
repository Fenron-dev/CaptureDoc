---
tags: [feature, ebenen]
status: implementiert
---
# Ebenensystem

Modell: [[Data-Model]]. Undo: [[ADR-004 Undo-Redo]]. UI: `Editor/LayersPanel.cs`, `Editor/PropertiesPanel.cs`.

| Funktion | Status |
|---|---|
| Auswählen (Klick, Strg+Klick; Liste mit Strg/Umschalt) | ✅ |
| Sichtbarkeit, Sperren | ✅ |
| Reihenfolge ▲▼, ganz nach vorn/hinten (Strg+Umschalt+[ ]) – nie unter das Original | ✅ |
| Umbenennen, Duplizieren (neue Ids, auch in Gruppen), Löschen | ✅ |
| Gruppieren / Gruppe auflösen (Deckkraft und Sichtbarkeit werden übernommen) | ✅ |
| Deckkraft (nicht für Schwärzungen) | ✅ |
| Verschieben, Skalieren (8 Griffe; Linien: Endpunkte; Umschalt = Seitenverhältnis/45°) | ✅ |
| Eigenschaften nachträglich (Farbe, Stärke, Schrift, Nummer, Radius, Stil …) | ✅ |
| Copy/Paste zwischen Projekten (Strg+Umschalt+C / Strg+V, inkl. Bilddaten) | ✅ innerhalb der App |
| Sortieren per Drag & Drop in der Liste | geplant |
| Drehen | geplant |

## Schwärzung
Exportierte Bilder sind flach – die Schwärzung ist eingebrannt (Test `Blackout_is_burned_in_and_original_is_not_recoverable` und GUI-Test, [[Test-Results]]). Die **Projektdatei** enthält das Original weiterhin → Warnung beim Speichern; die Ebene trägt ⚠ im Panel.
