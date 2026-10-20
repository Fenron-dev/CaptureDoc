---
tags: [adr]
status: akzeptiert
date: 2026-10-20
---
# ADR-001 Technologie-Stack

## Entscheidung
C# / **.NET 10** (SDK 10.0.402), **Avalonia 12.1.4**, **SkiaSharp 3.119.4**, CommunityToolkit.Mvvm 8.4.2, xUnit. FFmpeg ab Phase 6 als externer Prozess.

## Begründung
- ShareX setzt exakt diese Kombination für seinen neuen Editor ein ([[ShareX]]) → bekannte Kompatibilität der Versionen.
- Avalonia rendert selbst mit Skia → **derselbe Renderer** für Editor-Vorschau und Export (WYSIWYG, kein zweiter Grafikpfad).
- Ein Codebestand für Windows/macOS/Linux; Plattformteile über Adapter ([[Platform-Integration]]).
- Self-contained-Publishing ohne Runtime-Installation möglich ([[ADR-002 Portable Paketierung]]).

## Alternativen
| Option | Pro | Contra |
|---|---|---|
| Rust + Tauri | sehr kleine Binaries, gute Performance | WebView-UI: Pixel-Editor in Canvas/WebGL nötig, Text-Rendering-Unterschiede je WebView (WebView2/WKWebView/WebKitGTK), WebView2-Runtime auf Windows ggf. nicht portabel; Team-Stack C# |
| C++ / Qt 6 | bewährt (Flameshot, ksnip, OBS), native Performance | LGPL/kommerzielle Lizenzfragen bei statischem Linken, höherer Entwicklungsaufwand, langsamere Iteration |
| WPF/WinForms | ausgereift (Greenshot, ShareX alt, STG) | nur Windows |
| **Avalonia** | Cross-Platform, XAML/MVVM, Skia-Zugriff | macOS/Linux-Capture weiterhin nativ nötig (für alle Optionen gleich) |

## Folgen
- Native Capture je OS über P/Invoke bzw. später ObjC-/Swift-Bridge (macOS ScreenCaptureKit) und D-Bus (Linux Portal).
- Native AOT wird in Phase 10 geprüft (Avalonia 12 unterstützt es mit kompilierten Bindings); MVP nutzt JIT-Single-File.
