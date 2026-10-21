using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CaptureDoc.Media;

/// <summary>
/// FFmpeg is used as an external process (never linked) – see DevVault Licensing.
/// Search order: CAPTUREDOC_FFMPEG, &lt;AppRoot&gt;/Runtime/ffmpeg/, &lt;AppRoot&gt;/Runtime/&lt;rid&gt;/, PATH.
/// </summary>
public sealed record FfmpegTools(string Ffmpeg, string Ffprobe, string Source)
{
    public const string OverrideVariable = "CAPTUREDOC_FFMPEG";
    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    public static FfmpegTools? Locate(string appRoot)
    {
        var candidates = new List<(string dir, string source)>();
        var env = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(env))
            candidates.Add((File.Exists(env) ? Path.GetDirectoryName(env)! : env, "Umgebungsvariable " + OverrideVariable));
        candidates.Add((Path.Combine(appRoot, "Runtime", "ffmpeg"), "portabel (Runtime/ffmpeg)"));
        candidates.Add((Path.Combine(appRoot, "Runtime", "ffmpeg", "bin"), "portabel (Runtime/ffmpeg/bin)"));
        foreach (var p in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            candidates.Add((p.Trim('"'), "System-PATH"));

        foreach (var (dir, source) in candidates)
        {
            try
            {
                var f = Path.Combine(dir, Exe("ffmpeg")); var p = Path.Combine(dir, Exe("ffprobe"));
                if (File.Exists(f) && File.Exists(p)) return new FfmpegTools(f, p, source);
            }
            catch (ArgumentException) { }
        }
        return null;
    }
}

public sealed record FfmpegResult(int ExitCode, string StdErrTail)
{
    public bool Success => ExitCode == 0;
}

/// <summary>Runs ffmpeg/ffprobe with an argument list (no shell, no quoting problems).</summary>
public static class FfmpegRunner
{
    public static async Task<FfmpegResult> RunAsync(string exe, IReadOnlyList<string> args, CancellationToken ct = default,
        Action<double>? progressSeconds = null, Stream? stdout = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardError = true, RedirectStandardOutput = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg konnte nicht gestartet werden.");
        var tail = new Queue<string>();
        var errTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await p.StandardError.ReadLineAsync()) is not null)
            {
                lock (tail) { tail.Enqueue(line); if (tail.Count > 400) tail.Dequeue(); }
                if (progressSeconds is not null && TryParseTime(line, out var t)) progressSeconds(t);
            }
        });
        var outTask = stdout is not null ? p.StandardOutput.BaseStream.CopyToAsync(stdout) : p.StandardOutput.ReadToEndAsync();
        using (ct.Register(() => { try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }))
        {
            await p.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(errTask, outTask);
        }
        ct.ThrowIfCancellationRequested();
        string tailText; lock (tail) tailText = string.Join("\n", tail);
        return new FfmpegResult(p.ExitCode, tailText);
    }

    /// <summary>Parses "time=00:01:02.50" from ffmpeg's status line.</summary>
    internal static bool TryParseTime(string line, out double seconds)
    {
        seconds = 0;
        var i = line.IndexOf("time=", StringComparison.Ordinal);
        if (i < 0) return false;
        var s = line[(i + 5)..].Split(' ')[0];
        return TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var ts) && (seconds = ts.TotalSeconds) >= 0;
    }

    public static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

public sealed record MediaInfo(double Duration, int Width, int Height, double Fps, bool HasAudio, string VideoCodec, string PixelFormat = "")
{
    /// <summary>Stream copy into MP4 is possible without re-encoding (widely playable H.264 4:2:0).</summary>
    public bool CanStreamCopyToMp4 => VideoCodec == "h264" && PixelFormat is "yuv420p" or "yuvj420p";
}

public static class MediaProbe
{
    public static async Task<MediaInfo> ProbeAsync(FfmpegTools tools, string file, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        var r = await FfmpegRunner.RunAsync(tools.Ffprobe, new[]
        {
            "-v", "error", "-show_entries", "format=duration:stream=codec_type,codec_name,width,height,avg_frame_rate,r_frame_rate,duration,pix_fmt",
            "-of", "json", file,
        }, ct, stdout: ms);
        if (!r.Success) throw new InvalidDataException("Datei konnte nicht gelesen werden: " + r.StdErrTail);
        using var doc = JsonDocument.Parse(ms.ToArray());
        double dur = 0; int w = 0, h = 0; double fps = 0; var audio = false; var codec = ""; var pix = "";
        if (doc.RootElement.TryGetProperty("format", out var fmt) && fmt.TryGetProperty("duration", out var d))
            double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out dur);
        foreach (var s in doc.RootElement.GetProperty("streams").EnumerateArray())
        {
            var type = s.GetProperty("codec_type").GetString();
            if (type == "audio") audio = true;
            if (type != "video" || w > 0) continue;
            w = s.GetProperty("width").GetInt32(); h = s.GetProperty("height").GetInt32();
            codec = s.TryGetProperty("codec_name", out var c) ? c.GetString() ?? "" : "";
            pix = s.TryGetProperty("pix_fmt", out var pf) ? pf.GetString() ?? "" : "";
            fps = ParseRate(s.TryGetProperty("avg_frame_rate", out var a) ? a.GetString() : null);
            if (fps <= 0) fps = ParseRate(s.TryGetProperty("r_frame_rate", out var rr) ? rr.GetString() : null);
            if (dur <= 0 && s.TryGetProperty("duration", out var sd)) double.TryParse(sd.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out dur);
        }
        if (w == 0) throw new InvalidDataException("Keine Videospur gefunden.");
        return new MediaInfo(dur, w, h, fps, audio, codec, pix);
    }

    internal static double ParseRate(string? r)
    {
        if (string.IsNullOrEmpty(r)) return 0;
        var parts = r.Split('/');
        if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var dd) && dd > 0) return n / dd;
        return double.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>macOS: avfoundation index of "Capture screen 0" (screens follow the cameras in the device list).</summary>
    public static async Task<int?> FindMacScreenDeviceAsync(FfmpegTools tools, CancellationToken ct = default)
    {
        var r = await FfmpegRunner.RunAsync(tools.Ffmpeg, new[] { "-hide_banner", "-f", "avfoundation", "-list_devices", "true", "-i", "" }, ct);
        foreach (var line in r.StdErrTail.Split('\n'))
        {
            var i = line.IndexOf("] [", StringComparison.Ordinal);
            if (i < 0 || !line.Contains("Capture screen", StringComparison.Ordinal)) continue;
            var close = line.IndexOf(']', i + 3);
            if (close > 0 && int.TryParse(line[(i + 3)..close], out var idx)) return idx;
        }
        return null;
    }

    /// <summary>Lists DirectShow audio devices (microphones) on Windows.</summary>
    public static async Task<List<string>> ListMicrophonesAsync(FfmpegTools tools, CancellationToken ct = default)
    {
        var list = new List<string>();
        if (!OperatingSystem.IsWindows()) return list;
        var r = await FfmpegRunner.RunAsync(tools.Ffmpeg, new[] { "-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy" }, ct);
        foreach (var line in r.StdErrTail.Split('\n'))
        {
            var q1 = line.IndexOf('"'); var q2 = line.LastIndexOf('"');
            if (q1 >= 0 && q2 > q1 && line.Contains("(audio)", StringComparison.Ordinal)) list.Add(line[(q1 + 1)..q2]);
        }
        return list;
    }
}
