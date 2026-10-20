---
tags: [feature, video]
status: geplant (Phase 6/7)
---
# Video- und GIF-Editor (geplant)

Noch **nicht implementiert**. Vorbereitet: `LayerTiming` + `Keyframe` im Ebenenmodell ([[Data-Model]]), encoderunabhängiger `SizeOptimizer` ([[Export-Engine]]).

Konzept:
- Aufnahme: FFmpeg `ddagrab` (Desktop Duplication) bzw. WGC; sichtbarer Aufnahmerahmen + Timer, Pause/Fortsetzen, Mikrofon/Systemaudio optional. Keine versteckte Aufnahme.
- Nie ganze Videos im RAM: Segmente in `Data/Temp`, Vorschau über Proxy-Frames.
- Schnitt: Stream Copy (`-c copy`, vgl. LosslessCut in [[Other-Projects]]) wenn möglich; Neukodierung nur bei Effekten oder framegenauen Schnitten.
- Zeitgesteuerte Annotationen = dieselben Ebenen wie im Bildeditor + Timing/Keyframes; Rendering pro Frame mit dem gleichen `SceneRenderer`.
- GIF: `palettegen`/`paletteuse`, iterative Größenoptimierung (FPS → Farben → Auflösung), Prüfung der echten Dateigröße.
