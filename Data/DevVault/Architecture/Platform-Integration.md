---
tags: [architektur, plattform]
---
# Plattform-Integration

## Interfaces (`CaptureDoc.Core/Platform`)
| Interface | Zweck |
|---|---|
| `IScreenCaptureService` | Monitore, Fensterliste (Z-Order), Vordergrundfenster, Aufnahme virtueller Desktop / Rechteck (physische Pixel, BGRA) |
| `IClipboardImageService` | Bild in Zwischenablage (Windows: `CF_DIB` + registriertes Format `PNG`) |
| `IGlobalHotkeyService` | systemweite Tastenkürzel |

## Windows (implementiert, Phase 3)
- Capture: GDI BitBlt + CAPTUREBLT ([[ADR-005 Capture-Backend Windows]]).
- DPI: Manifest `PerMonitorV2`; alle Koordinaten in physischen Pixeln des virtuellen Desktops (kann negative Koordinaten haben, z. B. Monitor links vom Hauptmonitor).
- Fensterliste: `EnumWindows` + `IsWindowVisible` + `DWMWA_CLOAKED` (versteckte UWP-Fenster aussortieren) + `DWMWA_EXTENDED_FRAME_BOUNDS` (ohne unsichtbaren Resize-Rand).
- Hotkeys: `RegisterHotKey` auf einem eigenen Thread mit Message-Loop (kein sichtbares Fenster nötig).
- Geplant: WGC (Fenster ohne Überdeckung, Video), DXGI/ddagrab für Video.

## macOS / Linux (seit 0.3.0 implementiert, ungetestet)
Umsetzung über Systemwerkzeuge – Details, Tabelle und Grenzen: [[ADR-007 macOS und Linux]]. Startbefehle für System-Tastenkürzel: `CaptureDoc --capture region|window|active|screen|monitor|last|video`, `--studio`.

## macOS – spätere native Lösung
- ScreenCaptureKit (`SCScreenshotManager` für Einzelbilder ab macOS 14, `SCStream` für Video); Berechtigung „Bildschirmaufnahme“ (TCC), Prüfung via `CGPreflightScreenCaptureAccess`.
- Anbindung: kleine native Swift-/ObjC-Bibliothek mit C-ABI oder ObjC-Runtime-P/Invoke.
- Hotkeys: `RegisterEventHotKey` (Carbon) – funktioniert ohne Bedienungshilfen-Freigabe.

## Linux – spätere native Lösung
- Wayland: XDG Desktop Portal `org.freedesktop.portal.Screenshot` / `ScreenCast` + PipeWire (wie OBS `plugins/linux-pipewire/portal.c`). Globale Hotkeys nur über Portal `GlobalShortcuts` (nicht überall verfügbar).
- X11: XGetImage/XShm als Fallback.

## Multi-Monitor & Skalierung
- Overlay zur Bereichsauswahl: **ein Fenster pro Monitor** (vermeidet Probleme mit gemischter Skalierung), jedes zeigt seinen Ausschnitt des eingefrorenen Desktopbildes 1:1 in physischen Pixeln.
- MVP-Einschränkung: Bereichsauswahl innerhalb eines Monitors; „Gesamter Bildschirm“ erfasst alle Monitore.
