---
tags: [test, probleme]
---
# Bekannte Probleme

Behobene Fehler mit Ursache und Lösung: [[Changelog]]. Offene Punkte: [[Open-Issues]].

- **EXE-Größe**: ReadyToRun verdreifacht die EXE (121 MB). Ursache: vorkompilierter Code für alle Assemblies. Abhilfe später: Trimming/Native AOT ([[ADR-002 Portable Paketierung]]).
- **Windows-11-Druck-Taste**: Belegt das Snipping Tool die Druck-Taste, schlägt die Registrierung fehl → Leiste zeigt „belegt!“; andere Kürzel in den Einstellungen wählen.
- **GDI-Aufnahme**: HDR- oder DRM-Inhalte können falsch oder schwarz erscheinen.
- **Weichzeichnen** ist bei geringer Stärke keine sichere Schwärzung (Hinweis in der UI).
- **macOS/Linux** (seit 0.3.0 im Paket): nie auf echter Hardware gestartet. Fensterliste und globale Tastenkürzel fehlen (Startbefehle `--capture …` über System-Kürzel nutzen), Video unter Wayland meldet „nicht unterstützt“ ([[ADR-007 macOS und Linux]]).
- **Videos in Projekten** werden referenziert, nicht eingebettet: beim Weitergeben eines Projekts das Video (relativer Pfad) mitkopieren, sonst zeigt der Schritt „Video fehlt“.
- **Klick-Markierungen**: Startverzögerung wird seit 0.4.0 pro Segment herausgerechnet; Rest-Ungenauigkeit etwa ein Bild.
- **Live-Zeichnungen** sind nicht im Rohvideo, sondern erscheinen erst im Editor/Export – das ist Absicht (bearbeitbar).
- **Textmarker im Video** werden halbtransparent überlagert, nicht „multipliziert“ wie ein echter Marker; dunkle Schrift wird dadurch leicht eingefärbt.
- **Verlustfreies Schneiden** (Stream Copy) gibt es nur für einen einzelnen, unveränderten Clip ohne Annotationen/Zoom; Schnittpunkte liegen dann auf Keyframes (± bis zu 1 s). Mehrere Clips/Spuren werden immer neu kodiert.
- **Video-Vorschau im Editor** ohne Ton und mit etwa 10 Bildern/s; der Export enthält Ton und volle Bildrate.
