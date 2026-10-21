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
    public OverlaySpec? Overlay { get; set; }
    /// <summary>Copy the kept sections without re-encoding (cuts snap to keyframes). Only when nothing else changes.</summary>
    public bool StreamCopy { get; set; }
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
    public OverlaySpec? Overlay { get; set; }
}

public sealed record VideoExportResult(bool Success, string? Path, long Bytes, int Width, int Height, double Duration, string Message);

public sealed class VideoExporter
{
    private readonly FfmpegTools _t;
    public VideoExporter(FfmpegTools tools) => _t = tools;

    private static List<string> SeekArgs(ClipRange c, string src) =>
        new() { "-hide_banner", "-y", "-ss", F(c.Start), "-to", F(c.End), "-i", src };

    // ------------------------------------------------------------------ GIF

    internal static string GifFilter(ClipRange c, GifParams p, GifSettings s, string? overlayChain = null)
    {
        var parts = new List<string>();
        if (c.SelectExpression() is { } sel) parts.Add($"select='{sel}',setpts=N/FRAME_RATE/TB");
        parts.Add($"fps={F(p.Fps)}");
        if (s.DropDuplicates) parts.Add("mpdecimate=hi=512:lo=256:frac=0.5");
        parts.Add($"scale={p.Width}:{p.Height}:flags=lanczos");
        parts.Add("split[a][b];[a]palettegen=max_colors=" + p.Colors + ":stats_mode=diff[p];[b][p]paletteuse=dither=" + s.Dither + ":diff_mode=rectangle");
        var chain = string.Join(",", parts);
        return overlayChain is null ? chain : overlayChain + ";[ov]" + chain;
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
            // the optimizer calls ffmpeg synchronously → always on the thread pool, never on a UI thread
            var result = await Task.Run(() => GifOptimizer.Optimize(req, (p, attempt) =>
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"GIF-Versuch {attempt + 1}: {p.Width}×{p.Height}, {p.Fps:0.#} fps, {p.Colors} Farben …");
                var file = Path.Combine(work, $"try-{attempt}.gif");
                var args = SeekArgs(clip, src);
                string? ovChain = null;
                if (s.Overlay is { IsEmpty: false } ov)
                {
                    args.AddRange(ov.InputArgs());
                    ovChain = ov.BuildFilter("[0:v]", info.Width, info.Height, info.Fps, clip.Start);
                }
                args.AddRange(new[] { "-an", "-filter_complex", GifFilter(clip, p, s, ovChain), "-fps_mode", s.DropDuplicates ? "vfr" : "cfr", "-loop", s.Loop.ToString(), file });
                var r = RunAsync(_t.Ffmpeg, args, ct).GetAwaiter().GetResult();
                if (!r.Success) { error = r.StdErrTail; return long.MaxValue; }
                return new FileInfo(file).Length;
            }), ct);
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
        if (s.StreamCopy)
        {
            if (!info.CanStreamCopyToMp4 || s.Overlay is { IsEmpty: false } || s.MaxWidth is not null || s.MaxHeight is not null || s.Fps is not null || s.MaxBytes is not null)
                return new(false, null, 0, info.Width, info.Height, 0, "Verlustfreies Schneiden ist nur für H.264-4:2:0-Videos ohne Annotationen, Skalierung, FPS-Änderung oder Größenlimit möglich.");
            return await StreamCopyAsync(src, output, clip, s.IncludeAudio && info.HasAudio, progress, ct);
        }
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
            if (s.Overlay is { IsEmpty: false } ov)
            {
                args.AddRange(ov.InputArgs());
                args.AddRange(new[] { "-filter_complex", ov.BuildFilter("[0:v]", info.Width, info.Height, info.Fps, clip.Start) + ";[ov]" + string.Join(",", vf) + "[vout]", "-map", "[vout]" });
                if (audio) args.AddRange(new[] { "-map", "0:a?" });
            }
            else args.AddRange(new[] { "-vf", string.Join(",", vf) });
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

    /// <summary>Kept sections of a clip (start..end minus cuts), in source seconds.</summary>
    internal static List<(double From, double To)> KeptSegments(ClipRange c)
    {
        var segs = new List<(double, double)>();
        var t = c.Start;
        foreach (var (a, b) in c.Cuts.Where(x => x.To > c.Start && x.From < c.End).OrderBy(x => x.From))
        {
            if (a > t) segs.Add((t, Math.Min(a, c.End)));
            t = Math.Max(t, b);
        }
        if (c.End > t) segs.Add((t, c.End));
        return segs.Where(s => s.Item2 - s.Item1 > 0.05).ToList();
    }

    /// <summary>LosslessCut approach: copy each kept section (-c copy), then concat. No quality loss; cut points snap to keyframes.</summary>
    private async Task<VideoExportResult> StreamCopyAsync(string src, string output, ClipRange clip, bool audio, IProgress<string>? progress, CancellationToken ct)
    {
        var segs = KeptSegments(clip);
        if (segs.Count == 0) return new(false, null, 0, 0, 0, 0, "Nach den Schnitten bleibt nichts übrig.");
        var work = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, $".copy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            var parts = new List<string>();
            for (var i = 0; i < segs.Count; i++)
            {
                progress?.Report($"Kopiere Abschnitt {i + 1}/{segs.Count} (ohne Neukodierung) …");
                var part = Path.Combine(work, $"part-{i:000}.mp4");
                var args = new List<string> { "-hide_banner", "-y", "-ss", F(segs[i].From), "-to", F(segs[i].To), "-i", src, "-map", "0:v:0" };
                if (audio) args.AddRange(new[] { "-map", "0:a:0?" });
                args.AddRange(new[] { "-c", "copy", "-avoid_negative_ts", "make_zero", part });
                var r = await RunAsync(_t.Ffmpeg, args, ct);
                if (!r.Success) return new(false, null, 0, 0, 0, 0, "Kopieren fehlgeschlagen: " + r.StdErrTail);
                parts.Add(part);
            }
            var list = Path.Combine(work, "list.txt");
            await File.WriteAllLinesAsync(list, parts.Select(p => "file '" + p.Replace("\\", "/").Replace("'", "'\\''") + "'"), ct);
            var c = await RunAsync(_t.Ffmpeg, new[] { "-hide_banner", "-y", "-f", "concat", "-safe", "0", "-i", list, "-c", "copy", "-movflags", "+faststart", output }, ct);
            if (!c.Success) return new(false, null, 0, 0, 0, 0, "Zusammenfügen fehlgeschlagen: " + c.StdErrTail);
            return await VerifyAsync(output, null, SizeLimitKind.None, "Verlustfrei kopiert – Schnittpunkte liegen auf Keyframes.", ct);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
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

    /// <summary>
    /// Applies trim and cuts and writes a new, shorter working file in the recording quality
    /// (H.264 4:4:4, MKV). The source file is not touched.
    /// </summary>
    public async Task<string> RenderEditedAsync(string src, string output, ClipRange clip, IProgress<string>? progress = null, CancellationToken ct = default, OverlaySpec? overlay = null)
    {
        var info = await MediaProbe.ProbeAsync(_t, src, ct);
        var args = SeekArgs(clip, src);
        var sel = clip.SelectExpression();
        if (overlay is { IsEmpty: false })
        {
            args.AddRange(overlay.InputArgs());
            var tail = sel is not null ? $";[ov]select='{sel}',setpts=N/FRAME_RATE/TB[vout]" : ";[ov]null[vout]";
            args.AddRange(new[] { "-filter_complex", overlay.BuildFilter("[0:v]", info.Width, info.Height, info.Fps, clip.Start) + tail, "-map", "[vout]" });
            if (info.HasAudio) args.AddRange(new[] { "-map", "0:a?" });
        }
        else if (sel is not null) args.AddRange(new[] { "-vf", $"select='{sel}',setpts=N/FRAME_RATE/TB" });
        args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "16", "-pix_fmt", "yuv444p" });
        if (info.HasAudio)
        {
            if (sel is not null) args.AddRange(new[] { "-af", $"aselect='{sel}',asetpts=N/SR/TB" });
            args.AddRange(new[] { "-c:a", "aac", "-b:a", "160k" });
        }
        args.Add(output);
        var duration = Math.Max(0.1, clip.KeptDuration);
        var r = await RunAsync(_t.Ffmpeg, args, ct, t => progress?.Report($"Schneide … {Math.Min(100, t / duration * 100):0} %"));
        if (!r.Success) throw new InvalidOperationException("Schneiden fehlgeschlagen: " + r.StdErrTail);
        return output;
    }

    // ------------------------------------------------------------------ slides

    /// <summary>Output time (after trim and cuts) of a source time.</summary>
    public static double OutputTime(ClipRange c, double sourceTime)
    {
        var t = Math.Clamp(sourceTime, c.Start, c.End);
        return KeptSegments(c).Sum(s => Math.Max(0, Math.Min(s.To, t) - s.From));
    }

    /// <summary>
    /// Builds one working file (H.264 4:4:4, MKV): the edited video (trim, cuts, overlays, zoom) with the slides
    /// inserted at their positions. Each slide is a coloured frame with its own timed overlay images.
    /// The result is then exported like a normal source.
    /// </summary>
    public async Task<string> ComposeAsync(string src, string output, ClipRange clip, OverlaySpec? overlay, IReadOnlyList<SlideSpec> slides,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var work = output + ".parts";
        Directory.CreateDirectory(work);
        try
        {
            var main = Path.Combine(work, "main.mkv");
            await RenderEditedAsync(src, main, clip, progress, ct, overlay);
            var mi = await MediaProbe.ProbeAsync(_t, main, ct);
            var fps = mi.Fps > 0 ? mi.Fps : 30;
            var audio = mi.HasAudio;
            var ordered = slides.Where(s => s.Duration > 0.05).Select(s => (Slide: s, At: Math.Min(mi.Duration, OutputTime(clip, s.At)))).OrderBy(x => x.At).ToList();

            var segs = new List<string>();
            for (var i = 0; i < ordered.Count; i++)
            {
                var s = ordered[i].Slide;
                progress?.Report($"Folie {i + 1}/{ordered.Count} …");
                var seg = Path.Combine(work, $"slide-{i:00}.mkv");
                var a = new List<string> { "-hide_banner", "-y", "-f", "lavfi", "-i", $"color=c={s.Color}:s={mi.Width}x{mi.Height}:r={F(fps)}:d={F(s.Duration)}" };
                a.AddRange(s.Overlay.InputArgs());
                var audioInput = 1 + s.Overlay.Images.Count;
                if (audio) a.AddRange(new[] { "-f", "lavfi", "-t", F(s.Duration), "-i", "anullsrc=r=48000:cl=stereo" });
                a.AddRange(new[] { "-filter_complex", s.Overlay.BuildFilter("[0:v]", mi.Width, mi.Height, fps, 0) + ";[ov]format=yuv444p[vout]", "-map", "[vout]" });
                if (audio) a.AddRange(new[] { "-map", $"{audioInput}:a", "-c:a", "aac", "-b:a", "160k" });
                a.AddRange(new[] { "-t", F(s.Duration), "-c:v", "libx264", "-preset", "veryfast", "-crf", "16", "-pix_fmt", "yuv444p", seg });
                var r = await RunAsync(_t.Ffmpeg, a, ct);
                if (!r.Success) throw new InvalidOperationException("Folie konnte nicht erzeugt werden: " + r.StdErrTail);
                segs.Add(seg);
            }

            // main pieces between the insertion points, slides in between → concat
            var cuts = new List<double> { 0 };
            cuts.AddRange(ordered.Select(o => o.At));
            cuts.Add(mi.Duration);
            var pieces = new List<(double A, double B)>();
            for (var i = 0; i < cuts.Count - 1; i++) pieces.Add((cuts[i], cuts[i + 1]));

            var args = new List<string> { "-hide_banner", "-y", "-i", main };
            foreach (var seg in segs) { args.Add("-i"); args.Add(seg); }
            var fc = new System.Text.StringBuilder();
            fc.Append($"[0:v]split={pieces.Count}" + string.Concat(Enumerable.Range(0, pieces.Count).Select(k => $"[mv{k}]")) + ";");
            if (audio) fc.Append($"[0:a]asplit={pieces.Count}" + string.Concat(Enumerable.Range(0, pieces.Count).Select(k => $"[ma{k}]")) + ";");
            const string vnorm = "setsar=1,format=yuv444p";
            const string anorm = "aresample=48000,aformat=channel_layouts=stereo";
            var seq = new List<string>();
            for (var k = 0; k < pieces.Count; k++)
            {
                var (pa, pb) = pieces[k];
                var empty = pb - pa < 0.02;
                fc.Append($"[mv{k}]trim=start={F(pa)}:end={F(Math.Max(pb, pa + 0.001))},setpts=PTS-STARTPTS,fps={F(fps)},{vnorm}[v{k}];");
                if (audio) fc.Append($"[ma{k}]atrim=start={F(pa)}:end={F(Math.Max(pb, pa + 0.001))},asetpts=PTS-STARTPTS,{anorm}[a{k}];");
                if (empty) { fc.Append($"[v{k}]nullsink;"); if (audio) fc.Append($"[a{k}]anullsink;"); }
                else seq.Add(audio ? $"[v{k}][a{k}]" : $"[v{k}]");
                if (k < segs.Count)
                {
                    fc.Append($"[{k + 1}:v]fps={F(fps)},{vnorm}[s{k}];");
                    if (audio) fc.Append($"[{k + 1}:a]{anorm}[sa{k}];");
                    seq.Add(audio ? $"[s{k}][sa{k}]" : $"[s{k}]");
                }
            }
            fc.Append(string.Concat(seq) + $"concat=n={seq.Count}:v=1:a={(audio ? 1 : 0)}[cv]" + (audio ? "[ca]" : ""));
            args.AddRange(new[] { "-filter_complex", fc.ToString(), "-map", "[cv]" });
            if (audio) args.AddRange(new[] { "-map", "[ca]", "-c:a", "aac", "-b:a", "160k" });
            args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "16", "-pix_fmt", "yuv444p", output });
            var total = mi.Duration + ordered.Sum(o => o.Slide.Duration);
            var c = await RunAsync(_t.Ffmpeg, args, ct, t => progress?.Report($"Füge Folien ein … {Math.Min(100, t / Math.Max(0.1, total) * 100):0} %"));
            if (!c.Success) throw new InvalidOperationException("Folien konnten nicht eingefügt werden: " + c.StdErrTail);
            return output;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // ------------------------------------------------------------------ sequences

    /// <summary>A slide as a short video (coloured frame + its timed elements, no audio).</summary>
    public async Task RenderSlideAsync(SlideSpec s, int width, int height, double fps, string output, CancellationToken ct = default)
    {
        var a = new List<string> { "-hide_banner", "-y", "-f", "lavfi", "-i", $"color=c={s.Color}:s={width}x{height}:r={F(fps)}:d={F(s.Duration)}" };
        a.AddRange(s.Overlay.InputArgs());
        a.AddRange(new[] { "-filter_complex", s.Overlay.BuildFilter("[0:v]", width, height, fps, 0) + ";[ov]format=yuv444p[vout]", "-map", "[vout]" });
        a.AddRange(new[] { "-t", F(s.Duration), "-c:v", "libx264", "-preset", "veryfast", "-crf", "16", "-pix_fmt", "yuv444p", output });
        var r = await RunAsync(_t.Ffmpeg, a, ct);
        if (!r.Success) throw new InvalidOperationException("Folie konnte nicht erzeugt werden: " + r.StdErrTail);
    }

    /// <summary>
    /// Renders a multi-track sequence into one working file (H.264 4:4:4, MKV): every clip is trimmed, scaled to its
    /// rectangle and laid over the canvas at its time (gaps show the background), audio of all clips is mixed,
    /// then annotations and zoom are applied to the result.
    /// </summary>
    public async Task<string> RenderSequenceAsync(SequenceSpec spec, string output, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var w = spec.Width & ~1; var h = spec.Height & ~1;
        var dur = Math.Max(0.1, spec.Duration);
        var fps = spec.Fps > 0 ? spec.Fps : 30;
        var args = new List<string> { "-hide_banner", "-y" };
        foreach (var c in spec.Clips) args.AddRange(new[] { "-ss", F(Math.Max(0, c.In)), "-t", F(Math.Max(0.05, c.Duration)), "-i", c.Path });
        if (spec.Overlay is { IsEmpty: false } ov) args.AddRange(ov.InputArgs());

        var fc = new System.Text.StringBuilder();
        fc.Append($"color=c={spec.Background}:s={w}x{h}:r={F(fps)}:d={F(dur)},format=yuv444p[b0];");
        var cur = "[b0]";
        for (var i = 0; i < spec.Clips.Count; i++)
        {
            var c = spec.Clips[i];
            var cw = Math.Max(2, c.W & ~1); var ch = Math.Max(2, c.H & ~1);
            var alpha = c.Opacity < 0.999 ? $",colorchannelmixer=aa={F(Math.Clamp(c.Opacity, 0, 1))}" : "";
            fc.Append($"[{i}:v]fps={F(fps)},scale={cw}:{ch}:flags=lanczos,setsar=1,format=yuva444p{alpha},setpts=PTS-STARTPTS+{F(c.Start)}/TB[c{i}];");
            var next = $"[b{i + 1}]";
            fc.Append($"{cur}[c{i}]overlay=x={c.X}:y={c.Y}:eof_action=pass:enable='between(t,{F(c.Start)},{F(c.Start + c.Duration)})'{next};");
            cur = next;
        }
        if (spec.Overlay is { IsEmpty: false } o) fc.Append(o.BuildFilter(cur, w, h, fps, 0, spec.Clips.Count) + ";[ov]");
        else fc.Append(cur);
        fc.Append("format=yuv444p[vout]");

        var audio = spec.Clips.Select((c, i) => (c, i)).Where(x => x.c.Audio && x.c.Volume > 0.001).ToList();
        if (audio.Count > 0)
        {
            fc.Append(';');
            foreach (var (c, i) in audio)
            {
                var ms = (long)Math.Round(c.Start * 1000);
                fc.Append($"[{i}:a]asetpts=PTS-STARTPTS,aresample=48000,aformat=channel_layouts=stereo,volume={F(c.Volume)},adelay={ms}:all=1[a{i}];");
            }
            fc.Append(string.Concat(audio.Select(x => $"[a{x.i}]")) + (audio.Count > 1 ? $"amix=inputs={audio.Count}:normalize=0:duration=longest," : "") + "apad[aout]");
        }
        args.AddRange(new[] { "-filter_complex", fc.ToString(), "-map", "[vout]" });
        if (audio.Count > 0) args.AddRange(new[] { "-map", "[aout]", "-c:a", "aac", "-b:a", "160k" });
        args.AddRange(new[] { "-t", F(dur), "-r", F(fps), "-c:v", "libx264", "-preset", "veryfast", "-crf", "16", "-pix_fmt", "yuv444p", output });
        var r = await RunAsync(_t.Ffmpeg, args, ct, t => progress?.Report($"Setze Video zusammen … {Math.Min(100, t / dur * 100):0} %"));
        if (!r.Success) throw new InvalidOperationException("Video konnte nicht zusammengesetzt werden: " + r.StdErrTail);
        return output;
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
