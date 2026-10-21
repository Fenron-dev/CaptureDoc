---
tags: [anleitung]
---
# Video & GIF

Voraussetzung: FFmpeg. Portabel `ffmpeg.exe` + `ffprobe.exe` nach `Runtime\ffmpeg\` legen (oder FFmpeg installiert im PATH). Einstellungen ⚙ zeigen, welches FFmpeg gefunden wurde.

1. **● Video / GIF** (oder `Strg+Umschalt+Druck`, Tray-Menü) → Bereich ziehen oder Fenster anklicken.
2. Nach dem Countdown läuft die Aufnahme: roter Rahmen + Leiste mit Timer. Beides ist **nicht** im Video zu sehen.
3. **❚❚ Pause** / **▶ Weiter** – in der Pause wird nichts aufgenommen. **■ Stopp** öffnet den Video-Editor, **✕** verwirft.
4. Im Editor: Leertaste = Abspielen, Klick in die Timeline = Springen, **[ Start hier** (I), **Ende hier ]** (O), **✂ Schnitt von/bis** (X, zweimal) markiert einen Abschnitt zum Entfernen (rot, „✂ entfernt“). Markierungen stehen als Liste unter der Timeline – **✕** nimmt eine zurück, **Entf** entfernt die Markierung unter der Abspielmarke. **✂ Schnitte anwenden** schneidet die Abschnitte wirklich heraus und öffnet die gekürzte Fassung (Original bleibt erhalten).
5. **⇩ GIF…** oder **⇩ MP4…**: Profil wählen, ggf. max. Größe in MB und „Harte Obergrenze“. CaptureDoc probiert so lange, bis die Datei passt – oder sagt, warum es nicht geht.
6. **🖼 Frame → Editor** / **➕ Frame → Anleitung** macht aus dem aktuellen Bild einen bearbeitbaren Screenshot.

Aufnahmen liegen in `Data/Media/Aufnahmen`.

## Annotationen, Zoom und Klicks
- Unter der Timeline hat jede Annotation eine **Spur** (oben = vorderste Ebene). Balken ziehen = zeitlich verschieben, linken/rechten Rand ziehen = Beginn/Ende. Graue Balken mit ■ sind Schwärzungen/Lupen.
- **Reihenfolge** in der Seitenleiste mit ▲/▼ (oder im Ebenen-Editor). Eine Schwärzung verdeckt nur Ebenen *unter* ihr – Nummern oder Pfeile darüber bleiben sichtbar. Zoom wirkt auf alles zusammen, Schwärzungen bleiben auf ihrem Inhalt.
- Seitenleiste **Annotationen**: „✎ Bearbeiten“ öffnet das aktuelle Bild im Ebeneneditor; jede Ebene bekommt eine Von/Bis-Zeit und optional eine Bewegung (Start-/Endposition). Schwärzen/Verpixeln wirken auf die Videopixel.
- **Zoom**: Keyframe an der Abspielmarke setzen (Stärke, Mittelpunkt); zwischen Keyframes wird weich gefahren.
- Ist „Mausklicks markieren“ in den Einstellungen aktiv, erscheinen Klicks als Ringe – sie sind normale Annotationen (verschieben, löschen).

## Video in die Anleitung
- **✓ In Anleitung übernehmen** (aus dem Studio geöffnet) bzw. **🎬 Video → Anleitung**: Start/Ende, Schnitte, Annotationen und Zoom werden als **Video-Schritt** gespeichert – nichts wird sofort gerendert.
- Im Studio: **🎬 Video hinzufügen…** nimmt eine vorhandene Datei auf. Im Schritt: Format (MP4/GIF), max. Breite, max. MB, Ton.
- Export: Obsidian/Markdown bettet den fertig geschnittenen Clip ein, HTML zeigt einen Player mit Vorschaubild, PDF ein Standbild mit Hinweis.
- Das Quellvideo bleibt eine eigene Datei neben dem Projekt – beim Weitergeben mitkopieren.

## Verlustfrei schneiden
Im MP4-Dialog „verlustfrei schneiden“: ohne Neukodierung, sehr schnell, gleiche Qualität. Nur ohne Annotationen/Zoom und ohne Größenänderung; Schnitte landen auf dem nächsten Keyframe.
