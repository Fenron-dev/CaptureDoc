---
tags: [anleitung]
---
# Video & GIF

Voraussetzung: FFmpeg. Portabel `ffmpeg.exe` + `ffprobe.exe` nach `Runtime\ffmpeg\` legen (oder FFmpeg installiert im PATH). Einstellungen ⚙ zeigen, welches FFmpeg gefunden wurde.

## Aufnehmen
1. **● Video / GIF** (oder `Strg+Umschalt+Druck`, Tray-Menü) → Bereich ziehen oder Fenster anklicken.
2. Nach dem Countdown läuft die Aufnahme: roter Rahmen + Leiste mit Timer. Beides ist **nicht** im Video zu sehen.
3. **❚❚ Pause** / **▶ Weiter** – in der Pause wird nichts aufgenommen. **■ Stopp** öffnet den Video-Editor, **✕** verwirft.
4. **Live zeichnen** (zweite Zeile der Leiste): 🖱 Zeiger = Programm normal bedienen · ✎ Stift · 🖍 Textmarker · ➜ Pfeil · ▭ Rahmen · ◯ Kreis · ① Nummer · T Text · Farben · −/+ Stärke · ↶ rückgängig · 🧹 alles ausblenden. Zeichnungen verschwinden nach der eingestellten Zeit (Standard 6 s). Sie stehen danach als normale Annotationen mit Zeitfenster im Editor („Live: …“) und sind voll bearbeitbar. Welche Werkzeuge sichtbar sind und wie transparent die Leiste ist: Einstellungen → Video.
5. Mausklicks (Einstellung „Mausklicks markieren“) werden als Ringe angelegt, Doppelklicks zu einer Markierung zusammengefasst. Beim Zeichnen werden keine Klicks markiert.

Aufnahmen liegen in `Data/Media/Aufnahmen`. Annotationen, Schnitte und Folien einer Aufnahme speichert CaptureDoc beim Schließen automatisch in `<Aufnahme>.capturedoc` daneben – beim nächsten Öffnen ist alles wieder da (die Datei lässt sich auch im Studio öffnen).

## Der Video-Editor
- **Links Werkzeuge**, wie im Bildeditor: Auswahl, Pfeil, Rahmen, Kreis, Text, Infobox, Schritt, Stift, Textmarker, Hervorhebung, Spotlight, Lupe, Schwärzen, Verpixeln, ⤢ Zoom, 🖼 Bild, ▣ Folie. Darüber Farbe, Stärke, Schriftgröße, ↶ ↷.
- **Direkt ins Video zeichnen**: Eine neue Annotation erscheint ab der Abspielposition für 4 s (änderbar unter „Zeit & Reihenfolge“). Angezeigt und anklickbar sind nur Annotationen, die zum aktuellen Zeitpunkt sichtbar sind. Schwärzen, Verpixeln und Lupe wirken in der Vorschau auf das echte Videobild.
- **Rechts**: *Eigenschaften* (oben Zeitfenster von/bis mit ⇤/⇥ = Abspielposition, Bewegung, ▲/▼ Reihenfolge; darunter Farbe, Stärke, Text …), *Ebenen* (Liste mit Sichtbarkeit, Sperren, Deckkraft, Drag & Drop), *Zoom & Folien*.
- **Unten Spuren**: Zeitlineal · 🎬 Video (Vorschaubilder, rote Schnitte, grüne Start-/Endmarken) · 🔍 Zoom-Keyframes · ▣ Folien · eine Spur je Annotation (oben = vorne). Balken ziehen = verschieben, Ränder ziehen = Beginn/Ende (rastet am Abspielkopf ein), Spur anklicken = Annotation auswählen und hinspringen. `Strg`+Mausrad zoomt die Zeitachse, `Umschalt`+Mausrad scrollt.

## Schneiden
- **Auf dem Zeitlineal einen Bereich ziehen** und **X** drücken (oder „✂ Schnitt“) = Abschnitt entfernen. Alternativ zweimal X an Beginn und Ende.
- Entfernte Abschnitte stehen als Liste unter der Leiste – **✕** nimmt sie zurück, **Entf** entfernt die Markierung unter dem Abspielkopf. **[ Start hier** (I) / **Ende hier ]** (O) kürzen Anfang/Ende.
- Schnitte wirken beim Export immer. **✂ Schnitte anwenden** schreibt zusätzlich eine neue, kürzere Datei (Annotationen und Folien werden dabei eingebrannt – nur nötig, wenn du die gekürzte Datei separat brauchst).

## Ebenen und Reihenfolge
Eine Schwärzung verdeckt nur Ebenen **unter** ihr – liegt eine Nummer darüber, bleibt sie sichtbar. Reihenfolge mit ▲/▼, `Strg+]` / `Strg+[` oder per Drag & Drop im Tab *Ebenen*. Zoom wirkt auf alles zusammen, Schwärzungen bleiben auf ihrem Inhalt.

## Zoom & Schwenk
Werkzeug **⤢** (Z): im Video ein Rechteck aufziehen = dieser Ausschnitt ab der Abspielposition. Klicken statt ziehen = Mittelpunkt mit dem Faktor aus dem Tab. Zwischen Keyframes wird weich gefahren; Keyframes auf der Zoom-Spur verschieben, mit Entf löschen.

## Folien (Titel, Zusammenfassung, Checkliste)
**▣ Folie** links (oder Tab *Zoom & Folien*): Leer, Titel oder Zusammenfassung an der Abspielposition einfügen. Die Folie öffnet sich in einem eigenen Bearbeitungsmodus: Hintergrundfarbe, Dauer, Name; Texte, Bilder, Pfeile usw. mit denselben Werkzeugen. Jedes Element hat eine Spur – so erscheinen Punkte nacheinander wie in einer Präsentation. **✓ Zurück zum Video** beendet den Modus; die Folie steht als Block auf der Folien-Spur (verschieben = Position ändern, Doppelklick = bearbeiten). Beim Export pausiert das Video für die Dauer der Folie.

## Exportieren und in die Anleitung
- **⇩ GIF…** / **⇩ MP4…**: Profil, max. Größe in MB, „Harte Obergrenze“. Die echte Dateigröße wird geprüft. Annotationen, Zoom und Folien werden eingebrannt.
- **🎬 Video → Anleitung** bzw. **✓ In Anleitung übernehmen**: alles bleibt als Video-Schritt bearbeitbar. Export: Obsidian/Markdown eingebettet, HTML mit Player, PDF als Standbild.
- **➕ Frame → Anleitung** / **🖼 Frame → Editor**: aktuelles Bild **mit den sichtbaren Annotationen als Ebenen** – im Folienmodus die Folie.
- Das Quellvideo bleibt eine eigene Datei neben dem Projekt – beim Weitergeben mitkopieren.

## Verlustfrei schneiden
Im MP4-Dialog „verlustfrei schneiden“: ohne Neukodierung, sehr schnell. Nur ohne Annotationen/Zoom/Folien und ohne Größenänderung; Schnitte landen auf dem nächsten Keyframe.
