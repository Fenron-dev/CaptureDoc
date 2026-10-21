---
tags: [adr, plattform]
status: akzeptiert (ungetestet auf Zielsystemen)
date: 2026-10-21
---
# ADR-007 macOS und Linux über Systemwerkzeuge

## Entscheidung
Für den ersten Plattformausbau nutzt CaptureDoc **die Werkzeuge des Betriebssystems** statt eigener nativer Brücken (`CaptureDoc.Platform.Unix`):

| | macOS | Linux Wayland | Linux X11 |
|---|---|---|---|
| Screenshot | `/usr/sbin/screencapture` (je Display `-D`, respektiert TCC-Berechtigung) | `grim` → `gnome-screenshot` → `spectacle` | FFmpeg `x11grab` → ImageMagick `import` |
| Video | FFmpeg `avfoundation` (Screen-Index automatisch ermittelt, Klicks zeigt macOS selbst) | **noch nicht** (Portal + PipeWire geplant) | FFmpeg `x11grab`, Mikrofon über `pulse` |
| Zwischenablage | `osascript … «class PNGf»` | `wl-copy --type image/png` | `xclip -t image/png` |
| Monitore | aus Avalonia (`Screens`) | aus Avalonia | aus Avalonia |
| Fensterliste | – (Klick = Monitor) | – | – |
| Globale Hotkeys | – → System-Kürzel auf `CaptureDoc --capture region` | – (dto.) | – (dto.) |

Plattformneutrale Einzelinstanz: Sperrdatei `Data/Cache/instance.lock` + Befehlsordner `Data/Cache/ipc` (statt benannter Windows-Events, die es unter Unix nicht gibt). Weitere Starts reichen ihre Argumente weiter (`--capture region|window|active|screen|monitor|last|video`, `--studio`, Dateien).

## Begründung
- Keine Mac/Linux-Testumgebung verfügbar → möglichst wenig plattformspezifischer Code, der ungeprüft bleibt.
- Systemwerkzeuge kennen Berechtigungen und Sicherheitsmodelle (TCC, Wayland) bereits; CaptureDoc umgeht nichts.

## Alternativen (später)
- macOS: ScreenCaptureKit über kleine Swift-Bibliothek mit C-ABI (Fensterliste, Fenster ohne Überdeckung, Systemton).
- Linux: XDG Desktop Portal (`org.freedesktop.portal.Screenshot`/`ScreenCast`) über D-Bus (`Tmds.DBus` ist bereits als Avalonia-Abhängigkeit vorhanden) + PipeWire für Video.

## Status
Kompiliert und paketiert (`osx-arm64`, `linux-x64`), **nicht auf echter Hardware getestet**. Siehe [[Known-Problems]].
