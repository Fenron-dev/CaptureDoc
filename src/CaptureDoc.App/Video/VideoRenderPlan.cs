using CaptureDoc.App.Editor;
using CaptureDoc.App.Services;
using CaptureDoc.Core.Model;
using CaptureDoc.Media;

namespace CaptureDoc.App.Video;

/// <summary>
/// Turns "video file + clip + annotations + slides" into something the exporters can read:
/// without slides the original file with an overlay spec, with slides one composed working file.
/// </summary>
public static class VideoRenderPlan
{
    public sealed record Prepared(string Source, ClipRange Clip, OverlaySpec? Overlay, bool Composed);

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
