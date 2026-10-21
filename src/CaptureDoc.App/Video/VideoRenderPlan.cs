using CaptureDoc.App.Editor;
using CaptureDoc.App.Services;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Serialization;
using CaptureDoc.Media;

namespace CaptureDoc.App.Video;

/// <summary>
/// Turns an edit into something the exporters can read: a single unchanged clip stays the original file
/// (so lossless stream copy remains possible), everything else becomes one composed working file.
/// </summary>
public static class VideoRenderPlan
{
    public sealed record Prepared(string Source, ClipRange Clip, OverlaySpec? Overlay, bool Composed);

    public static string? ResolveClip(AppServices app, ProjectSession session, SequenceClip c) =>
        ProjectFile.ResolveMedia(session.FilePath, c.Path, app.Env.FromPortablePath);

    /// <summary>Multi-track sequence (0.5+).</summary>
    public static async Task<Prepared> PrepareSequenceAsync(AppServices app, ProjectSession session, VideoSequence seq, VideoOverlay? overlay,
        string workDir, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(workDir);
        var tools = app.Ffmpeg ?? throw new InvalidOperationException("FFmpeg fehlt.");
        var visible = seq.Tracks.Where(t => !t.Hidden).SelectMany(t => t.Clips).ToList();
        if (visible.Count == 0) throw new InvalidOperationException("Die Zeitleiste ist leer – es gibt nichts zu exportieren.");
        var hasOverlay = overlay is not null && (overlay.Page.Layers.Any(l => l is not ImageLayer { IsBackground: true }) || overlay.Zoom.Count > 0);

        // trivial: one untouched-placement video clip → original file + trim (stream copy possible)
        if (!hasOverlay && visible.Count == 1 && visible[0] is { Kind: ClipKind.Video, Start: < 0.001, Rect: null, Opacity: >= 0.999, Volume: >= 0.999 and <= 1.001 } only
            && seq.Tracks.First(t => t.Clips.Contains(only)) is { Muted: false } && !only.Muted
            && only.SourceWidth == seq.Width && only.SourceHeight == seq.Height)
        {
            var src = ResolveClip(app, session, only) ?? throw new InvalidOperationException($"Video „{only.Name}“ nicht gefunden: {only.Path}");
            return new(src, new ClipRange { Start = only.SourceIn, End = only.SourceOut }, null, false);
        }

        OverlaySpec? spec = hasOverlay ? await OverlayRenderer.PrepareAsync(session, overlay!, Path.Combine(workDir, "overlay")) : null;
        var exporter = new VideoExporter(tools);
        var s = new SequenceSpec
        {
            Width = seq.Width, Height = seq.Height, Fps = seq.Fps, Duration = seq.Duration,
            Background = "0x" + seq.Background.WithAlpha(255).ToHex()[1..7], Overlay = spec,
        };
        var n = 0;
        foreach (var track in seq.Tracks)
        {
            if (track.Hidden) continue;
            foreach (var c in track.Clips.OrderBy(c => c.Start))
            {
                n++;
                var r = SequenceOps.Placement(seq, c);
                string path; double inP = c.SourceIn; var audio = c.HasAudio && !c.Muted && !track.Muted;
                if (c.Kind == ClipKind.Slide && c.Slide is { } sl)
                {
                    progress?.Report($"Folie „{sl.Name}“ …");
                    var slideSpec = await Task.Run(() => OverlayRenderer.PrepareSlide(session, sl, Path.Combine(workDir, $"slide-{n:000}")), ct);
                    path = Path.Combine(workDir, $"slide-{n:000}.mkv");
                    await Task.Run(() => exporter.RenderSlideAsync(slideSpec, seq.Width, seq.Height, seq.Fps, path, ct), ct);
                    inP = 0; audio = false;
                }
                else path = ResolveClip(app, session, c) ?? throw new InvalidOperationException($"Video „{c.Name}“ nicht gefunden: {c.Path}");
                s.Clips.Add(new SequenceSpecClip
                {
                    Path = path, In = inP, Duration = c.Duration, Start = c.Start,
                    X = (int)Math.Round(r.X), Y = (int)Math.Round(r.Y), W = (int)Math.Round(r.Width), H = (int)Math.Round(r.Height),
                    Opacity = c.Opacity, Volume = c.Volume, Audio = audio,
                });
            }
        }
        var composed = Path.Combine(workDir, "composed.mkv");
        await Task.Run(() => exporter.RenderSequenceAsync(s, composed, progress, ct), ct);
        var info = await MediaProbe.ProbeAsync(tools, composed, ct);
        return new(composed, new ClipRange { Start = 0, End = info.Duration }, null, true);
    }

    /// <summary>Old single-video model (trim, cuts, slides on source time) – projects from 0.2–0.4 that were not opened since.</summary>
    public static async Task<Prepared> PrepareAsync(AppServices app, ProjectSession session, string file, ClipRange clip, VideoOverlay? overlay,
        string workDir, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(workDir);
        OverlaySpec? spec = null;
        if (overlay is not null && (overlay.Page.Layers.Any(l => l is not ImageLayer { IsBackground: true }) || overlay.Zoom.Count > 0))
            spec = await OverlayRenderer.PrepareAsync(session, overlay, Path.Combine(workDir, "overlay"));
        if (overlay is null || overlay.Slides.Count == 0) return new(file, clip, spec, false);
        progress?.Report("Bereite Folien vor …");
        var slides = await Task.Run(() => overlay.Slides.Select((s, i) => OverlayRenderer.PrepareSlide(session, s, Path.Combine(workDir, $"slide-{i:00}"))).ToList(), ct);
        var composed = Path.Combine(workDir, "composed.mkv");
        var tools = app.Ffmpeg ?? throw new InvalidOperationException("FFmpeg fehlt.");
        await Task.Run(() => new VideoExporter(tools).ComposeAsync(file, composed, clip, spec, slides, progress, ct), ct);
        var info = await MediaProbe.ProbeAsync(tools, composed, ct);
        return new(composed, new ClipRange { Start = 0, End = info.Duration }, null, true);
    }
}
