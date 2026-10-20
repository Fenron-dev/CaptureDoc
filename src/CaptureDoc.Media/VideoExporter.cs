using CaptureDoc.Core.Export;
using CaptureDoc.Core.Storage;
using static CaptureDoc.Media.FfmpegRunner;

namespace CaptureDoc.Media;

/// <summary>Which part of the source to keep: [Start, End] minus cut-out ranges (seconds, source time).</summary>
public sealed class ClipRange
{
    public double Start { get; set; }
    public double End { get; set; }
    public List<(double From, double To)> Cuts { get; set; } = new();

    public double KeptDuration => Math.Max(0, End - Start - Cuts.Sum(c => Math.Max(0, Math.Min(c.To, End) - Math.Max(c.From, Start))));

    /// <summary>select filter (relative to Start because the input is seeked) or null when nothing is cut.</summary>
    internal string? SelectExpression()
    {
        var cuts = Cuts.Where(c => c.To > c.From && c.To > Start && c.From < End).OrderBy(c => c.From).ToList();
        if (cuts.Count == 0) return null;
        return string.Join("*", cuts.Select(c => $"not(between(t\\,{F(Math.Max(0, c.From - Start))}\\,{F(c.To - Start)}))"));
    }
}

public enum VideoEncoder { Auto, X264, Nvenc, Amf, Qsv }
public enum VideoQuality { High, Medium, Small }

public sealed class Mp4Settings
{
    public int? MaxWidth { get; set; }
    public int? MaxHeight { get; set; }
    public double? Fps { get; set; }
    public VideoQuality Quality { get; set; } = VideoQuality.High;
    public VideoEncoder Encoder { get; set; } = VideoEncoder.X264;
    public bool IncludeAudio { get; set; } = true;
    public int AudioKbps { get; set; } = 128;
    public long? MaxBytes { get; set; }
    public SizeLimitKind LimitKind { get; set; } = SizeLimitKind.None;
    public int MinWidth { get; set; } = 480;
}

public sealed class GifSettings
{
    public int? MaxWidth { get; set; } = 800;
    public int? MaxHeight { get; set; }
    public double Fps { get; set; } = 12;
    public double MinFps { get; set; } = 6;
    public int Colors { get; set; } = 256;
    public int MinColors { get; set; } = 32;
    public int MinWidth { get; set; } = 320;
    /// <summary>bayer, floyd_steinberg, sierra2_4a, none</summary>
    public string Dither { get; set; } = "sierra2_4a";
    /// <summary>0 = endless, -1 = play once, n = n repeats.</summary>
    public int Loop { get; set; }
    /// <summary>Drop near-identical frames (variable frame duration, much smaller files for UI recordings).</summary>
    public bool DropDuplicates { get; set; } = true;
    public long? MaxBytes { get; set; }
    public SizeLimitKind LimitKind { get; set; } = SizeLimitKind.None;
}

public sealed record VideoExportResult(bool Success, string? Path, long Bytes, int Width, int Height, double Duration, string Message);

public sealed class VideoExporter
{
    private readonly FfmpegTools _t;
    public VideoExporter(FfmpegTools tools) => _t = tools;

    private static List<string> SeekArgs(ClipRange c, string src) =>
        new() { "-hide_banner", "-y", "-ss", F(c.Start), "-to", F(c.End), "-i", src };

    // ------------------------------------------------------------------ GIF

    internal static string GifFilter(ClipRange c, GifParams p, GifSettings s)
    {
        var parts = new List<string>();
        if (c.SelectExpression() is { } sel) parts.Add($"select='{sel}',setpts=N/FRAME_RATE/TB");
        parts.Add($"fps={F(p.Fps)}");
        if (s.DropDuplicates) parts.Add("mpdecimate=hi=512:lo=256:frac=0.5");
        parts.Add($"scale={p.Width}:{p.Height}:flags=lanczos");
        parts.Add("split[a][b];[a]palettegen=max_colors=" + p.Colors + ":stats_mode=diff[p];[b][p]paletteuse=dither=" + s.Dither + ":diff_mode=rectangle");
        return string.Join(",", parts);
    }

    public async Task<VideoExportResult> ExportGifAsync(string src, string output, ClipRange clip, GifSettings s, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var info = await MediaProbe.ProbeAsync(_t, src, ct);
        var work = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, $".gif-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            var req = new GifOptimizer.Request(info.Width, info.Height, s.MaxWidth, s.MaxHeight, Math.Min(s.Fps, Math.Max(1, info.Fps)), s.MinFps,
                s.Colors, s.MinColors, s.MinWidth, s.MaxBytes, s.MaxBytes is null ? SizeLimitKind.None : s.LimitKind);
            string? error = null;
            var result = GifOptimizer.Optimize(req, (p, attempt) =>
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"GIF-Versuch {attempt + 1}: {p.Width}×{p.Height}, {p.Fps:0.#} fps, {p.Colors} Farben …");
                var file = Path.Combine(work, $"try-{attempt}.gif");
                var args = SeekArgs(clip, src);
                args.AddRange(new[] { "-an", "-filter_complex", GifFilter(clip, p, s), "-fps_mode", s.DropDuplicates ? "vfr" : "cfr", "-loop", s.Loop.ToString(), file });
                var r = RunAsync(_t.Ffmpeg, args, ct).GetAwaiter().GetResult();
                if (!r.Success) { error = r.StdErrTail; return long.MaxValue; }
                return new FileInfo(file).Length;
            });
            if (error is not null && result.Bytes == long.MaxValue) return new(false, null, 0, 0, 0, 0, "GIF-Export fehlgeschlagen: " + error);
            if (!result.Success && s.LimitKind == SizeLimitKind.Hard && s.MaxBytes is not null)
                return new(false, null, result.Bytes, result.Parameters.Width, result.Parameters.Height, clip.KeptDuration, result.Message);

            var best = Path.Combine(work, $"try-{result.BestAttempt}.gif");
            File.Move(best, output, overwrite: true);
            return await VerifyAsync(output, s.MaxBytes, s.LimitKind, result.Message, ct);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // ------------------------------------------------------------------ MP4

    internal static List<string> EncoderArgs(VideoEncoder e, VideoQuality q, long? bitrate)
    {
        var crf = q switch { VideoQuality.High => 20, VideoQuality.Medium => 26, _ => 31 };
        var a = new List<string>();
        switch (e)
        {
            case VideoEncoder.Nvenc:
                a.AddRange(new[] { "-c:v", "h264_nvenc", "-preset", "p5" });
                a.AddRange(bitrate is { } b1 ? new[] { "-b:v", b1.ToString(), "-maxrate", (b1 * 3 / 2).ToString(), "-bufsize", (b1 * 2).ToString() } : new[] { "-rc", "vbr", "-cq", (crf + 2).ToString() });
                break;
            case VideoEncoder.Amf:
                a.AddRange(new[] { "-c:v", "h264_amf", "-quality", "quality" });
                a.AddRange(bitrate is { } b2 ? new[] { "-b:v", b2.ToString(), "-maxrate", (b2 * 3 / 2).ToString(), "-bufsize", (b2 * 2).ToString() } : new[] { "-rc", "cqp", "-qp_i", crf.ToString(), "-qp_p", (crf + 2).ToString() });
                break;
            case VideoEncoder.Qsv:
                a.AddRange(new[] { "-c:v", "h264_qsv", "-preset", "medium" });
                a.AddRange(bitrate is { } b3 ? new[] { "-b:v", b3.ToString(), "-maxrate", (b3 * 3 / 2).ToString() } : new[] { "-global_quality", (crf + 2).ToString() });
                break;
            default:
                a.AddRange(new[] { "-c:v", "libx264", "-preset", "medium", "-tune", "stillimage" });
                a.AddRange(bitrate is { } b4 ? new[] { "-b:v", b4.ToString(), "-maxrate", (b4 * 3 / 2).ToString(), "-bufsize", (b4 * 2).ToString() } : new[] { "-crf", crf.ToString() });
                break;
        }
        a.AddRange(new[] { "-pix_fmt", "yuv420p", "-movflags", "+faststart" });
        return a;
    }

    public async Task<VideoExportResult> ExportMp4Async(string src, string output, ClipRange clip, Mp4Settings s, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var info = await MediaProbe.ProbeAsync(_t, src, ct);
        var duration = Math.Max(0.1, clip.KeptDuration);
        var (w, h) = SizeOptimizer.FitDimensions(info.Width, info.Height, s.MaxWidth, s.MaxHeight, 1.0);
        var audio = s.IncludeAudio && info.HasAudio;
        var encoder = s.Encoder == VideoEncoder.Auto ? VideoEncoder.X264 : s.Encoder;
        long? bitrate = null;
        if (s.MaxBytes is { } max && s.LimitKind != SizeLimitKind.None)
        {
            bitrate = (long)(max * 8 * 0.94 / duration) - (audio ? s.AudioKbps * 1000L : 0);
            // too little bitrate for this resolution → reduce resolution (~0.08 bit per pixel and frame as floor)
            var fps = s.Fps ?? info.Fps;
            while (bitrate < w * h * fps * 0.04 && w > s.MinWidth)
            {
                var nw = Math.Max(s.MinWidth, (int)(w * 0.8));
                h = (int)Math.Round(h * nw / (double)w); w = nw;
            }
            if (bitrate < 50_000) bitrate = 50_000;
        }
        w &= ~1; h &= ~1;

        var tmp = output + ".part.mp4";
        string? lastMsg = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            progress?.Report($"MP4-Versuch {attempt + 1}: {w}×{h}" + (bitrate is { } br ? $", {br / 1000} kbit/s" : "") + " …");
            var args = SeekArgs(clip, src);
            var vf = new List<string>();
            if (clip.SelectExpression() is { } sel) vf.Add($"select='{sel}',setpts=N/FRAME_RATE/TB");
            if (s.Fps is { } f) vf.Add($"fps={F(f)}");
            vf.Add($"scale={w}:{h}:flags=lanczos");
            args.AddRange(new[] { "-vf", string.Join(",", vf) });
            args.AddRange(EncoderArgs(encoder, s.Quality, bitrate));
            if (audio)
            {
                if (clip.SelectExpression() is { } asel) args.AddRange(new[] { "-af", $"aselect='{asel}',asetpts=N/SR/TB" });
                args.AddRange(new[] { "-c:a", "aac", "-b:a", s.AudioKbps + "k" });
            }
            else args.Add("-an");
            args.Add(tmp);
            var r = await RunAsync(_t.Ffmpeg, args, ct, t => progress?.Report($"Kodiere … {Math.Min(100, t / duration * 100):0} %"));
            if (!r.Success)
            {
                if (encoder != VideoEncoder.X264) { lastMsg = $"Hardware-Encoder {encoder} nicht verfügbar – verwende libx264."; encoder = VideoEncoder.X264; attempt--; continue; }
                return new(false, null, 0, w, h, duration, "MP4-Export fehlgeschlagen: " + r.StdErrTail);
            }
            var size = new FileInfo(tmp).Length;
            if (s.MaxBytes is not { } limit || s.LimitKind == SizeLimitKind.None || size <= limit)
            {
                File.Move(tmp, output, overwrite: true);
                return await VerifyAsync(output, s.MaxBytes, s.LimitKind, lastMsg ?? "OK", ct);
            }
            bitrate = (long)(bitrate!.Value * (limit / (double)size) * 0.93);
            lastMsg = $"Versuch {attempt + 1} war {SizeOptimizer.FormatBytes(size)} – Bitrate reduziert.";
        }
        if (s.LimitKind == SizeLimitKind.Soft && File.Exists(tmp))
        {
            File.Move(tmp, output, overwrite: true);
            return await VerifyAsync(output, s.MaxBytes, s.LimitKind, "Weiches Limit nicht erreicht – kleinste Variante gespeichert.", ct);
        }
        try { File.Delete(tmp); } catch (IOException) { }
        return new(false, null, 0, w, h, duration, $"Größenlimit {SizeOptimizer.FormatBytes(s.MaxBytes ?? 0)} mit {s.MinWidth} px Mindestbreite nicht erreichbar. Clip kürzen oder Limit erhöhen.");
    }

    /// <summary>Re-reads the written file: real size, dimensions and duration.</summary>
    private async Task<VideoExportResult> VerifyAsync(string file, long? maxBytes, SizeLimitKind kind, string note, CancellationToken ct)
    {
        var len = new FileInfo(file).Length;
        var info = await MediaProbe.ProbeAsync(_t, file, ct);
        var met = maxBytes is null || kind == SizeLimitKind.None || len <= maxBytes;
        if (!met && kind == SizeLimitKind.Hard)
        {
            File.Delete(file);
            return new(false, null, len, info.Width, info.Height, info.Duration, $"Datei war {SizeOptimizer.FormatBytes(len)} und wurde wegen der harten Grenze verworfen.");
        }
        var msg = $"{info.Width}×{info.Height}, {info.Duration:0.0} s, {SizeOptimizer.FormatBytes(len)}" +
                  (maxBytes is { } m && kind != SizeLimitKind.None ? (met ? $" – Limit {SizeOptimizer.FormatBytes(m)} eingehalten (geprüft)." : $" – Limit {SizeOptimizer.FormatBytes(m)} NICHT eingehalten.") : ".") +
                  (note is "OK" ? "" : " " + note);
        return new(true, file, len, info.Width, info.Height, info.Duration, msg);
    }

    // ------------------------------------------------------------------ frames

    public async Task<byte[]> ExtractFrameAsync(string src, double seconds, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        var r = await RunAsync(_t.Ffmpeg, new[] { "-hide_banner", "-loglevel", "error", "-ss", F(Math.Max(0, seconds)), "-i", src, "-frames:v", "1", "-f", "image2pipe", "-c:v", "png", "-" }, ct, stdout: ms);
        if (!r.Success || ms.Length == 0) throw new InvalidDataException("Frame konnte nicht gelesen werden: " + r.StdErrTail);
        return ms.ToArray();
    }

    /// <summary>Low-resolution JPEG frames for preview/scrubbing (kept small: a few MB for minutes of video).</summary>
    public async Task<List<byte[]>> ExtractPreviewFramesAsync(string src, double fps, int width, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        var r = await RunAsync(_t.Ffmpeg, new[] { "-hide_banner", "-loglevel", "error", "-i", src, "-an", "-vf", $"fps={F(fps)},scale={width}:-2", "-f", "image2pipe", "-c:v", "mjpeg", "-q:v", "5", "-" }, ct, stdout: ms);
        if (!r.Success) throw new InvalidDataException("Vorschau konnte nicht erzeugt werden: " + r.StdErrTail);
        return SplitJpegs(ms.ToArray());
    }

    internal static List<byte[]> SplitJpegs(byte[] data)
    {
        var list = new List<byte[]>();
        var start = -1;
        for (var i = 0; i < data.Length - 1; i++)
        {
            if (data[i] != 0xFF) continue;
            if (data[i + 1] == 0xD8 && start < 0) start = i;
            else if (data[i + 1] == 0xD9 && start >= 0)
            {
                list.Add(data[start..(i + 2)]);
                start = -1; i++;
            }
        }
        return list;
    }
}
