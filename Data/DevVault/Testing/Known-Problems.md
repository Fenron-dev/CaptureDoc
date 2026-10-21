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
- **Klick-Markierungen** erscheinen etwas früher als der Klick im Video (FFmpeg-Startverzögerung) – im Editor verschiebbar.
- **„Schnitte anwenden“ mit Annotationen** brennt die Annotationen ein (Rückfrage); verlustfreies Schneiden ist nur ohne Annotationen/Zoom möglich und schneidet an Keyframes (± bis zu 1 s ungenau).
