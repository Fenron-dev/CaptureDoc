using CaptureDoc.App.Editor;
using CaptureDoc.App.Services;
using CaptureDoc.Core.Export;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Obsidian;
using CaptureDoc.Core.Serialization;
using CaptureDoc.Media;

namespace CaptureDoc.App.Video;

/// <summary>Renders the video clip of a guide step (trim, cuts, overlays) to MP4 or GIF for export.</summary>
public static class VideoStepRenderer
{
    public static string? Resolve(AppServices app, ProjectSession session, VideoRef v) =>
        ProjectFile.ResolveMedia(session.FilePath, v.Path, app.Env.FromPortablePath);

    public static string Store(AppServices app, ProjectSession session, string file) =>
        ProjectFile.ToProjectRelative(session.FilePath, file, app.Env.ToPortablePath);

    public static ClipRange ToClip(VideoRef v) =>
        new() { Start = v.Start, End = v.End, Cuts = v.Cuts.Select(c => (c.From, c.To)).ToList() };

    public static void SetClip(VideoRef v, ClipRange c)
    {
        v.Start = c.Start; v.End = c.End;
        v.Cuts = c.Cuts.Select(x => new TimeCut { From = x.From, To = x.To }).ToList();
    }

    /// <summary>Renders all video steps into <paramref name="workDir"/>. Throws with a readable message on failure.</summary>
    public static async Task<Dictionary<Guid, RenderedMedia>> RenderAllAsync(AppServices app, ProjectSession session, string workDir, IProgress<string>? progress, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, RenderedMedia>();
        var steps = session.Project.Guide.Steps.Where(s => s.Video is not null).ToList();
        if (steps.Count == 0) return result;
        if (app.Ffmpeg is not { } tools) throw new InvalidOperationException("Für Video-Schritte wird FFmpeg benötigt (Runtime\\ffmpeg\\ oder PATH).");
        Directory.CreateDirectory(workDir);
        var exporter = new VideoExporter(tools);
        var i = 0;
        foreach (var s in steps)
        {
            i++;
            var v = s.Video!;
            var src = Resolve(app, session, v) ?? throw new InvalidOperationException($"Video für Schritt „{s.Title}“ nicht gefunden: {v.Path}");
            var ext = v.Format == VideoOutputFormat.Gif ? ".gif" : ".mp4";
            var output = Path.Combine(workDir, $"step-{i:00}{ext}");
            var clip = ToClip(v);
            var overlay = v.Overlay is { } ov && ov.Page.Layers.Count > 0
                ? await OverlayRenderer.PrepareAsync(session, ov, Path.Combine(workDir, $"overlay-{i:00}"))
                : null;
            progress?.Report($"Video {i}/{steps.Count}: {s.Title} …");
            VideoExportResult r;
            if (v.Format == VideoOutputFormat.Gif)
                r = await exporter.ExportGifAsync(src, output, clip, new GifSettings
                {
                    MaxWidth = v.MaxWidth, Fps = 12, MaxBytes = v.MaxBytes, LimitKind = v.MaxBytes is null ? SizeLimitKind.None : SizeLimitKind.Soft, Overlay = overlay,
                }, progress, ct);
            else
                r = await exporter.ExportMp4Async(src, output, clip, new Mp4Settings
                {
                    MaxWidth = v.MaxWidth, Quality = VideoQuality.Medium, IncludeAudio = v.IncludeAudio,
                    MaxBytes = v.MaxBytes, LimitKind = v.MaxBytes is null ? SizeLimitKind.None : SizeLimitKind.Soft, Overlay = overlay,
                }, progress, ct);
            if (!r.Success || r.Path is null) throw new InvalidOperationException($"Video für Schritt „{s.Title}“: {r.Message}");
            result[s.Id] = new RenderedMedia(r.Path, ext, v.Format == VideoOutputFormat.Mp4);
        }
        return result;
    }
}
