# CaptureDoc

Portables Screenshot- und Dokumentations-Studio (interne Nutzung). Version 0.5.0 – Windows getestet, macOS/Linux enthalten (ungetestet).

- **Quick Capture:** Bereich / Fenster / Monitor / alle Bildschirme / letzter Bereich, Verzögerung, Cursor, Serienaufnahme, globale Tastenkürzel, Tray, Startbefehle.
- **Editor mit Ebenen:** Jede Annotation ist eine eigene, nachträglich bearbeitbare Ebene (Pfeil, Linie, Rechteck, Ellipse, Text, Infobox, Sprechblase, nummerierte Schritte, Hervorhebung, Spotlight, Lupe, Freihand, Schwärzen/Verpixeln/Weichzeichnen, eingefügte Bilder). Ebenenpanel mit Sichtbarkeit, Sperren, Drag & Drop, Gruppen, Deckkraft; Undo/Redo; nichtdestruktiver Zuschnitt; Stilvorlagen, Pipette, Rahmen & Schatten, Bild anheften.
- **Projekte `.capturedoc`:** ZIP + JSON, versioniert (v2, ältere Projekte werden migriert), Prüfsummen, atomares Speichern, Konflikterkennung, Auto-Save mit Absturz-Wiederherstellung.
- **Export:** PNG/JPEG/WebP mit max. Breite/Höhe und **geprüftem** Dateigrößenlimit (weich/hart), Exportprofile, Zwischenablage.
- **Video & GIF:** Bildschirmaufnahme mit Rahmen, Timer, Pause, Klick-Markierung und **Live-Zeichnen** (Windows); Video-Studio mit **Mehrspur-Schnitt ohne Einbrennen** (Teilen, Teile löschen/verschieben/auf andere Spuren, weitere Videos importieren und als Bild-im-Bild überlagern), Annotationen **direkt im Video** mit eigenen Spuren, Stift/Textmarker, Zoom/Schwenk, **Folien** für Titel und Zusammenfassungen; Export als MP4 oder GIF mit **geprüftem** Größenlimit. Benötigt FFmpeg (siehe unten).
- **Documentation Studio:** Schritt-für-Schritt-Anleitungen aus Bildern **und Videos**, 9 Vorlagen, Export nach **Obsidian** (Vorschau, Konfliktschutz, Linkprüfung, Videos eingebettet), Markdown, HTML (Bild-Popup, Video-Player) und **PDF**.
- Offline, keine Telemetrie, keine Uploads, keine Installation, keine Adminrechte.

Noch nicht enthalten bzw. ungetestet: macOS/Linux auf echter Hardware, Video unter Wayland, Systemton (Windows: Gerät „Stereomix“ verwenden). Details: `Data/DevVault/Development/Roadmap.md`, `Open-Issues.md`.

## Portabler Ordner

```
CaptureDoc/
├── CaptureDoc-Windows.exe      # Windows: starten, keine Installation
├── CaptureDoc-Linux            # Linux x64 (ungetestet)
├── CaptureDoc-macOS.app        # macOS Apple Silicon (ungetestet, unsigniert)
├── Runtime/win-x64|linux-x64/  # native Grafikbibliotheken
├── Runtime/ffmpeg/             # optional: ffmpeg + ffprobe für Video/GIF (nicht mitgeliefert)
├── Data/                       # Einstellungen, Projekte, Vorlagen, Logs (relativ, wandert mit)
│   ├── Settings/Shared|Windows|MacOS|Linux
│   ├── Projects/  Media/  Templates/  Presets/  Cache/  Temp/  Logs/
│   └── DevVault/               # Entwicklungsdoku, direkt in Obsidian öffnen
├── README.md
└── THIRD_PARTY_LICENSES.md
```

Der Ordner kann verschoben oder auf einen USB-Stick kopiert werden. Ist er schreibgeschützt, bietet CaptureDoc beim Start an,
einen Ordner im Benutzerprofil zu verwenden. Ausnahmen vom „alles im App-Ordner“: vom Benutzer gewählte Export-/Vault-Ziele,
die Zwischenablage und vom Betriebssystem verwaltete Daten.

**macOS:** nach dem Kopieren einmal `xattr -dr com.apple.quarantine CaptureDoc-macOS.app` und `codesign --force --deep -s - CaptureDoc-macOS.app`,
danach Bildschirmaufnahme in den Systemeinstellungen erlauben. **Linux:** benötigt `grim`/`slurp` (Wayland) bzw. `maim`/`xdotool` (X11) und `wl-copy`/`xclip`;
siehe `Data/DevVault/Architecture/Decisions/ADR-007 macOS und Linux.md`. Wird die Ausführungsberechtigung beim Kopieren verloren: `chmod +x CaptureDoc-Linux`.

## FFmpeg für Video & GIF

CaptureDoc ruft FFmpeg als externes Programm auf und liefert es nicht mit. Entweder `ffmpeg` und `ffprobe` nach
`Runtime/ffmpeg/` kopieren (portabel), FFmpeg installieren (PATH) oder `CAPTUREDOC_FFMPEG` setzen. Bei Weitergabe eines Ordners mit FFmpeg dessen Lizenz beilegen
(siehe `THIRD_PARTY_LICENSES.md`).

## Videos in Anleitungen

Im Video-Editor „✓ In Anleitung übernehmen“ bzw. im Studio „🎬 Video hinzufügen…“. Der Schritt verweist auf das Video; beim Export wird der
geschnittene Clip (MP4 oder GIF) erzeugt und eingebunden: Obsidian `![[….mp4]]`, HTML `<video>` mit Vorschaubild, PDF als Standbild mit Hinweis.
Das Video liegt neben dem Projekt (relativer Pfad) – bei Weitergabe des Projekts mitgeben.

## Bedienung (Kurz)

| Aktion | Weg |
|---|---|
| Bereich aufnehmen | `Druck` · Tray-Klick · Leiste „Bereich“ – ziehen = Bereich, Klick = Fenster/Bildschirm, Esc = Abbruch |
| Aktives Fenster / Bildschirm / letzter Bereich | `Alt+Druck` / `Strg+Druck` / `Umschalt+Druck` |
| Video aufnehmen | `Strg+Umschalt+Druck` · Leiste „● Video / GIF“ · Tray – Pause/Stopp in der Aufnahmeleiste |
| Video-Studio | Leertaste abspielen · S teilen · Entf Clip/Annotation entfernen · Lineal ziehen + X = Bereich entfernen · I/O Anfang/Ende entfernen · ,/. Bild · ←/→ Bild bzw. Auswahl schieben · Z Zoom-Werkzeug · Strg+Z rückgängig · Strg+Mausrad Zeitachse |
| Werkzeuge im Editor | V Auswahl · A Pfeil · L Linie · R Rechteck · E Ellipse · T Text · I Infobox · U Sprechblase · N Schritt · H Hervorheben · S Spotlight · M Lupe · F Stift · G Textmarker · B Schwärzen · P Verpixeln · W Weichzeichnen · C Zuschneiden · K Pipette |
| Bearbeiten | Strg+Z/Y · Entf · Pfeiltasten (Umschalt = 10 px) · Strg+D duplizieren · Strg+G / Strg+Umschalt+G gruppieren · Strg+[ / ] Reihenfolge · Doppelklick/F2 Text bearbeiten |
| Ausgabe | Strg+C Bild kopieren · Strg+E Export-Dialog · Strg+Umschalt+E schnell speichern · Strg+S Projekt speichern · Strg+P anheften · Strg+Umschalt+C/V Ebenen kopieren/einfügen |

**Startbefehle** (für Tastenkürzel unter macOS/Linux oder Skripte; eine laufende Instanz übernimmt den Befehl):
`CaptureDoc-Windows.exe --capture region|window|active|screen|monitor|last|video`, `--studio`, `--show`, oder Dateien (`.capturedoc`, Bilder, Videos).

Windows 11: Ist „Druck-Taste zum Öffnen des Bildschirmausschnitts verwenden“ aktiv, belegt das Snipping Tool die Druck-Taste →
in den Einstellungen andere Kürzel wählen (die Leiste zeigt „belegt!“ an).

**Datenschutz:** Schwärzungen werden in exportierten Bildern und Videos eingebrannt. Die Projektdatei enthält das Original darunter weiterhin –
Projektdateien mit vertraulichen Inhalten nicht weitergeben (CaptureDoc warnt beim Speichern).

## Entwickeln

Voraussetzung: .NET SDK 10 (hier benutzerlokal unter `%USERPROFILE%\.dotnet`).

```bash
dotnet test tests/CaptureDoc.Core.Tests
dotnet test tests/CaptureDoc.Rendering.Tests
dotnet test tests/CaptureDoc.Media.Tests      # nutzt echtes FFmpeg, falls vorhanden
dotnet run --project tests/CaptureDoc.App.Tests   # Headless-UI-Tests (xunit v3)
powershell -ExecutionPolicy Bypass -File tools/publish.ps1 -Zip   # → dist/CaptureDoc (+ ZIP), Optionen: -Out, -Platforms
```

Struktur: `src/CaptureDoc.Core` (Modell, Projektformat, Obsidian, keine UI) · `src/CaptureDoc.Rendering` (SkiaSharp, Export, PDF) ·
`src/CaptureDoc.Media` (FFmpeg: Aufnahme, MP4/GIF, Overlays) · `src/CaptureDoc.Platform.Windows` · `src/CaptureDoc.Platform.Unix` (macOS/Linux über Systemwerkzeuge) · `src/CaptureDoc.App` (Avalonia-UI) · `tests/`.
Architektur, Entscheidungen, Fortschritt und Testergebnisse: `Data/DevVault/Home.md`.

Lizenzen: siehe `THIRD_PARTY_LICENSES.md`.
