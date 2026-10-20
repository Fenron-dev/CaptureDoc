---
tags: [architektur, stack]
---
# Technologie-Stack

| Bereich | Wahl | Version |
|---|---|---|
| Sprache/Runtime | C# / .NET | SDK 10.0.402, `net10.0` |
| UI | Avalonia + Fluent-Theme | 12.1.4 |
| Grafik | SkiaSharp (+ HarfBuzzSharp) | 3.119.4 |
| MVVM | CommunityToolkit.Mvvm | 8.4.2 |
| Tests | xUnit | siehe `tests/*.csproj` |
| Medien (ab Phase 6) | FFmpeg-CLI (externer Prozess) | lokal vorhanden: 9.0.2 (Gyan full_build, GPL) |

Begründung und Alternativen (Rust/Tauri, C++/Qt): [[ADR-001 Technologie-Stack]].
Paketierung: [[ADR-002 Portable Paketierung]].

## Entwicklungsumgebung (Ist)
- Windows 11 Home 10.0.26200, .NET SDK benutzerlokal in `%USERPROFILE%\.dotnet` (nicht im PATH → Build-Skripte rufen `dotnet.exe` mit vollem Pfad auf).
- **Nicht verfügbar:** macOS- und Linux-Testumgebung → keine Aussagen über Funktion dort.
