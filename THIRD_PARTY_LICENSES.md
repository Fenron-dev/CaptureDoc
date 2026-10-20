# Third-Party Licenses – CaptureDoc 0.1.0

Stand: 2026-10-20. CaptureDoc ist für die interne Nutzung bestimmt. **Interne Nutzung befreit nicht von Lizenzpflichten:**
Bei jeder Weitergabe einer Kopie (z. B. USB-Stick an externe Dienstleister, Kunden, Partner) müssen die unten genannten
Lizenztexte und Copyright-Hinweise mitgegeben werden. Diese Datei liegt deshalb immer im portablen Hauptordner.

## Im portablen Paket enthaltene Komponenten

| Komponente | Version | Lizenz | Copyright | Wo im Paket |
|---|---|---|---|---|
| .NET Runtime / BCL (self-contained) | 10.0.x (SDK 10.0.402) | MIT | © .NET Foundation and Contributors | in `CaptureDoc-Windows.exe` |
| Avalonia (+ Avalonia.Desktop, .Win32, .Skia, .HarfBuzz, .Themes.Fluent, .X11, .Native, .FreeDesktop, .Remote.Protocol) | 12.1.4 | MIT | © AvaloniaUI OÜ and Contributors | in EXE |
| Avalonia.Angle.Windows.Natives (ANGLE) | 2.1.27548.20260419 | BSD-3-Clause (ANGLE) | © The ANGLE Project Authors | `Runtime/win-x64/av_libglesv2.dll` |
| SkiaSharp (+ NativeAssets) | 3.119.4 | MIT | © Microsoft Corporation / .NET Foundation | EXE + `Runtime/win-x64/libSkiaSharp.dll` |
| Skia (in libSkiaSharp) | – | BSD-3-Clause | © Google LLC | `libSkiaSharp.dll` |
| HarfBuzzSharp (+ NativeAssets) | 8.3.1.3 | MIT | © Microsoft Corporation; HarfBuzz © Behdad Esfahbod et al. (Old MIT) | EXE + `Runtime/win-x64/libHarfBuzzSharp.dll` |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | © .NET Foundation and Contributors | EXE |
| MicroCom.Runtime | 0.11.6 | MIT | © Nikita Tsukanov | EXE |
| Tmds.DBus.Protocol | 0.94.1 | MIT | © Tom Deseyn | EXE (nur Linux genutzt) |

Alle Lizenzen erlauben Nutzung und Weitergabe; Pflicht ist das Beilegen des Lizenztextes und Copyright-Hinweises.
MIT-Lizenztext (gilt für alle als MIT markierten Einträge, jeweils mit obigem Copyright):

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

BSD-3-Clause (Skia, ANGLE): Weitergabe in Binärform nur mit Copyright-Hinweis, Bedingungsliste und Haftungsausschluss in der Dokumentation; Namen der Urheber dürfen nicht zur Bewerbung genutzt werden. Volltexte: https://opensource.org/license/bsd-3-clause

## Nur beim Entwickeln/Bauen verwendet (nicht im Paket)

| Komponente | Lizenz | Hinweis |
|---|---|---|
| xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1 | Apache-2.0 / MIT | nur Tests |
| Avalonia.BuildServices 11.3.2 | MIT | **sendet beim Build anonyme Telemetrie an AvaloniaUI.** Abgeschaltet über `AVALONIA_TELEMETRY_OPTOUT=1` im Build-Skript. Die fertige App sendet nichts. |
| Microsoft.NET.ILLink.Tasks | MIT | Build-Werkzeug |

## Referenzprojekte (nur analysiert, **kein Code übernommen**)

Greenshot (GPL-3.0), ShareX (GPL-3.0), Flameshot (GPL-3.0), ksnip (GPL-3.0), LosslessCut (GPL-2.0), OBS Studio (GPL-2.0),
ScreenToGif (MS-PL), Kap (MIT). Geprüfte Commits: `Data/DevVault/Research/References.md`. Da kein Code übernommen wurde,
entstehen aus diesen Projekten keine Lizenzpflichten.

## Geplant (noch nicht enthalten)

- **FFmpeg** (Phase 6, als externer Prozess): LGPL-2.1+ oder GPL (je nach Build). Bei Mitlieferung: Lizenztext, Quellcode-Angebot
  bzw. Link zur exakten Quelle und Build-Konfiguration beilegen. Für die Weitergabe einen LGPL-Build bevorzugen.
