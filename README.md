# CaptureDoc

Portable Screenshot- und Dokumentations-Studio (interne Nutzung). Version 0.1.0 – erster nutzbarer Windows-MVP.

- **Quick Capture:** Bereich / Fenster / Monitor / alle Bildschirme / letzter Bereich, Verzögerung, Cursor, globale Tastenkürzel, Tray.
- **Editor mit Ebenen:** Jede Annotation ist eine eigene, nachträglich bearbeitbare Ebene (Pfeil, Linie, Rechteck, Ellipse, Text, Infobox, Sprechblase, nummerierte Schritte, Hervorhebung, Spotlight, Lupe, Freihand, Schwärzen/Verpixeln/Weichzeichnen). Ebenenpanel mit Sichtbarkeit, Sperren, Reihenfolge, Gruppen, Deckkraft; Undo/Redo; nichtdestruktiver Zuschnitt.
- **Projekte `.capturedoc`:** ZIP + JSON, versioniert, Prüfsummen, atomares Speichern, Konflikterkennung, Auto-Save mit Absturz-Wiederherstellung.
- **Export:** PNG/JPEG/WebP mit max. Breite/Höhe und **geprüftem** Dateigrößenlimit (weich/hart), Exportprofile, Zwischenablage.
- **Documentation Studio:** Schritt-für-Schritt-Anleitungen, 9 Vorlagen, Export nach **Obsidian** (Vorschau, Konfliktschutz für manuelle Änderungen, Linkprüfung), Markdown und HTML.
- Offline, keine Telemetrie, keine Uploads, keine Installation, keine Adminrechte.

Noch nicht enthalten: Video/GIF (Phase 6), macOS/Linux-Aufnahme (Phase 8/9), PDF-Direktexport (HTML → Browser → „Als PDF drucken“). Details: `Data/DevVault/Development/Roadmap.md`.

## Portabler Ordner

```
CaptureDoc/
├── CaptureDoc-Windows.exe      # starten – keine Installation nötig
├── Runtime/win-x64/            # native Grafikbibliotheken (Skia, HarfBuzz, ANGLE)
├── Data/                       # alle Einstellungen, Projekte, Vorlagen, Logs (relativ, wandert mit)
│   ├── Settings/Shared|Windows|MacOS|Linux
│   ├── Projects/  Media/  Templates/  Presets/  Cache/  Temp/  Logs/
│   └── DevVault/               # Entwicklungsdoku, direkt in Obsidian öffnen
├── README.md
└── THIRD_PARTY_LICENSES.md
```

Der Ordner kann verschoben oder auf einen USB-Stick kopiert werden. Ist er schreibgeschützt, bietet CaptureDoc beim Start an,
einen Ordner im Benutzerprofil zu verwenden. Ausnahmen vom „alles im App-Ordner“: vom Benutzer gewählte Export-/Vault-Ziele,
die Zwischenablage und von Windows selbst verwaltete Daten.

## Bedienung (Kurz)

| Aktion | Weg |
|---|---|
| Bereich aufnehmen | `Druck` · Tray-Klick · Leiste „Bereich“ – ziehen = Bereich, Klick = Fenster/Bildschirm, Esc = Abbruch |
| Aktives Fenster / Bildschirm / letzter Bereich | `Alt+Druck` / `Strg+Druck` / `Umschalt+Druck` |
| Werkzeuge im Editor | V Auswahl · A Pfeil · L Linie · R Rechteck · E Ellipse · T Text · I Infobox · U Sprechblase · N Schritt · H Hervorheben · S Spotlight · M Lupe · F Freihand · B Schwärzen · P Verpixeln · W Weichzeichnen · C Zuschneiden |
| Bearbeiten | Strg+Z/Y · Entf · Pfeiltasten (Umschalt = 10 px) · Strg+D duplizieren · Strg+G / Strg+Umschalt+G gruppieren · Strg+[ / ] Reihenfolge · Doppelklick/F2 Text bearbeiten |
| Ausgabe | Strg+C Bild kopieren · Strg+E Export-Dialog · Strg+Umschalt+E schnell speichern · Strg+S Projekt speichern · Strg+Umschalt+C/V Ebenen kopieren/einfügen |

Windows 11: Ist „Druck-Taste zum Öffnen des Bildschirmausschnitts verwenden“ aktiv, belegt das Snipping Tool die Druck-Taste →
in den Einstellungen andere Kürzel wählen (die Leiste zeigt „belegt!“ an).

**Datenschutz:** Schwärzungen werden in exportierten Bildern eingebrannt. Die Projektdatei enthält das Original darunter weiterhin –
Projektdateien mit vertraulichen Inhalten nicht weitergeben (CaptureDoc warnt beim Speichern).

## Entwickeln

Voraussetzung: .NET SDK 10 (hier benutzerlokal unter `%USERPROFILE%\.dotnet`).

```bash
dotnet test tests/CaptureDoc.Core.Tests
dotnet test tests/CaptureDoc.Rendering.Tests
powershell -ExecutionPolicy Bypass -File tools/publish-windows.ps1   # → dist/CaptureDoc
```

Struktur: `src/CaptureDoc.Core` (Modell, Projektformat, Obsidian, keine UI) · `src/CaptureDoc.Rendering` (SkiaSharp, Export) ·
`src/CaptureDoc.Platform.Windows` (GDI-Capture, Hotkeys, Zwischenablage) · `src/CaptureDoc.App` (Avalonia-UI) · `tests/`.
Architektur, Entscheidungen, Fortschritt und Testergebnisse: `Data/DevVault/Home.md`.

Lizenzen: siehe `THIRD_PARTY_LICENSES.md`.
