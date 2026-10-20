---
tags: [feature, editor]
status: implementiert
---
# Bildeditor

Werkzeuge: Auswahl, Pfeil, Linie, Rechteck/Ellipse/abgerundet, Text (Inline-Bearbeitung), Infobox, Sprechblase mit Spitze, nummerierte Schritte (Auto-Nummer), Hervorheben (Multiply), Spotlight (rechteckig/elliptisch), Lupe (vergrößerter Ausschnitt), Freihand (geglättet), Schwärzen, Verpixeln (Blockmittelwert), Weichzeichnen, Zuschneiden (nichtdestruktiv).

Seite: Skalieren (Bild + alle Ebenen), Leinwandgröße (zentriert), Leinwandfarbe. Zoom (Strg+Mausrad, 100 %, Einpassen). HiDPI: 100 % = 1 Bildpixel pro physischem Bildschirmpixel.

Ein Renderer für Vorschau und Export (`CaptureDoc.Rendering/SceneRenderer.cs`) → identisches Ergebnis. Effekt-Ebenen arbeiten auf dem Komposit darunter.

![[Assets/Screenshots/editor-ebenen.png|700]]

**Offen:** „Bildausschnitte kombinieren“ geht über eingefügte Bildebenen, aber noch ohne eigenen Dialog; Werkzeugvorlagen nur als globaler Standardstil; Sticker/Symbole; Farbpipette. Siehe [[Open-Issues]].
