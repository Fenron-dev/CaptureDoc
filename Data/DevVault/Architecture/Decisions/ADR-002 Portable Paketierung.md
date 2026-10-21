---
tags: [adr, portable]
status: akzeptiert
date: 2026-10-20
---
# ADR-002 Portable Paketierung

## Problem
.NET-Single-File-Bundles mit `IncludeNativeLibrariesForSelfExtract=true` entpacken native DLLs (Skia, HarfBuzz, ANGLE) nach `%TEMP%\.net\…`. Das widerspricht „alle Daten im App-Ordner“. Native AOT kann native Bibliotheken ebenfalls nicht einbetten.

## Entscheidung
- `CaptureDoc-Windows.exe` = self-contained Single-File (alle verwalteten Assemblies + .NET-Runtime eingebettet), **ohne** Selbstextraktion.
- Native Bibliotheken liegen in `Runtime/win-x64/` neben der EXE. Beim Start setzt `NativeRuntimeBootstrap` einen `DllImportResolver` (SkiaSharp, HarfBuzzSharp) und `SetDllDirectory` (ANGLE) auf diesen Ordner.
- Ergebnis: **keine** Extraktion in Temp, keine Installation, keine Adminrechte.

```
CaptureDoc/
├── CaptureDoc-Windows.exe
├── Runtime/win-x64/   (libSkiaSharp.dll, libHarfBuzzSharp.dll, av_libglesv2.dll)
├── Data/…
├── README.md
└── THIRD_PARTY_LICENSES.md
```

## Nachtrag 2026-10-20: Startzeit
Gemessen: komprimiertes Bundle ohne ReadyToRun 3,1–3,4 s bis zur UI. Mit `PublishReadyToRun=true` und `EnableCompressionInSingleFile=false` 1,2 s (erster Start 2,2 s), EXE dafür 121 MB statt 39 MB. **Entscheidung:** Startzeit hat Vorrang (Quick Capture). Größe wird in Phase 10 per Trimming/Native AOT angegangen.

## Nachtrag 2026-10-21: drei Plattformen in einem Ordner
`tools/publish.ps1` erzeugt aus Windows heraus:
```
CaptureDoc/
├── CaptureDoc-Windows.exe   + Runtime/win-x64/   (ReadyToRun)
├── CaptureDoc-Linux         + Runtime/linux-x64/ (libSkiaSharp.so, libHarfBuzzSharp.so)
├── CaptureDoc-macOS.app/Contents/{Info.plist, MacOS/CaptureDoc + *.dylib}
└── Data/ …
```
`-Zip` schreibt `CaptureDoc-<version>-portable.zip` mit Unix-Ausführungsrechten (Windows-Kopien verlieren +x) und **ohne Nutzerdaten** (nur DevVault, sonst leere Data-Ordner). macOS: Bundle ist unsigniert → auf dem Mac `codesign --force --deep -s - CaptureDoc-macOS.app`, ggf. `xattr -dr com.apple.quarantine`. ReadyToRun nur für Windows (Cross-Compile nicht verlässlich). Gemessen: Windows 121,6 MB, Linux 79,8 MB, ZIP 136 MB.

## Abweichung vom Zielkonzept
Ein zusätzlicher Ordner `Runtime/` statt „nur drei Dateien“. Begründung: native Bibliotheken müssen als Datei vorliegen; Alternative (Temp-Extraktion) verletzt die Portabilitätsanforderung stärker.

## macOS / Linux (paketiert seit 0.3.0, nicht getestet)
- macOS: `CaptureDoc-macOS.app/Contents/MacOS/…` (Bundle zwingend für Bildschirmaufnahme-Berechtigung via TCC, Info.plist mit `NSScreenCaptureUsageDescription`). Datenordner = Ordner, der das `.app` enthält (`PortableEnvironment` erkennt `.app/Contents/MacOS`). Ad-hoc-Signatur (`codesign -s -`) nötig auf Apple Silicon; TCC-Freigabe hängt an der Signatur → nach jedem Update ggf. erneute Freigabe. Quarantäne-Attribut beim Kopieren von USB-Stick (`xattr -dr com.apple.quarantine`).
- Linux: `CaptureDoc-Linux` + `Runtime/linux-x64/`; Wayland-Capture nur über XDG Desktop Portal (Benutzerdialog pro Aufnahme).

## Alternativen
- Selbstextraktion in `DOTNET_BUNDLE_EXTRACT_BASE_DIR` – Variable muss *vor* Prozessstart gesetzt sein → bräuchte Launcher. Verworfen.
- Native AOT – kürzere Startzeit, aber gleiches Native-DLL-Problem; Reflection-basiertes JSON müsste auf Source Generators umgestellt werden. → Phase 10.
