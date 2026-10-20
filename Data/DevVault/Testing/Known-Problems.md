---
tags: [test, probleme]
---
# Bekannte Probleme

Behobene Fehler mit Ursache und Lösung: [[Changelog]]. Offene Punkte: [[Open-Issues]].

- **EXE-Größe**: ReadyToRun verdreifacht die EXE (121 MB). Ursache: vorkompilierter Code für alle Assemblies. Abhilfe später: Trimming/Native AOT ([[ADR-002 Portable Paketierung]]).
- **Windows-11-Druck-Taste**: Belegt das Snipping Tool die Druck-Taste, schlägt die Registrierung fehl → Leiste zeigt „belegt!“; andere Kürzel in den Einstellungen wählen.
- **GDI-Aufnahme**: HDR- oder DRM-Inhalte können falsch oder schwarz erscheinen.
- **Weichzeichnen** ist bei geringer Stärke keine sichere Schwärzung (Hinweis in der UI).
