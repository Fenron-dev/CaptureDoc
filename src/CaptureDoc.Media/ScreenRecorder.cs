using System.Diagnostics;
using System.Text;
using CaptureDoc.Core.Model;

namespace CaptureDoc.Media;

public sealed class RecordingOptions
{
    /// <summary>Area in virtual-screen physical pixels.</summary>
    public required PixelRect Region { get; init; }
    public int Fps { get; init; } = 30;
    public bool DrawMouse { get; init; } = true;
    /// <summary>DirectShow microphone name (Windows) or null.</summary>
    public string? Microphone { get; init; }
    /// <summary>Working directory for segments (portable Data/Temp/...).</summary>
    public required string WorkDirectory { get; init; }
}

public enum RecorderState { Idle, Recording, Paused, Finished, Failed }

/// <summary>
/// Records the screen with ffmpeg in segments: Pause ends the current segment, Resume starts a new one,
/// Stop joins all segments losslessly (concat, stream copy). Nothing is recorded while paused.
/// Intermediate codec: H.264 4:4:4 (sharp text), MKV (survives crashes).
/// </summary>
public sealed class ScreenRecorder : IAsyncDisposable
{
    private readonly FfmpegTools _tools;
    private readonly RecordingOptions _o;
    private readonly List<string> _segments = new();
    // recording clock at start/end of each segment, and the real segment lengths (after stop)
    private readonly List<(double Start, double End)> _segClock = new();
    // seconds ffmpeg kept capturing after the clock stopped (until 'q' was processed)
    private readonly List<double> _segTail = new();
    private List<double>? _segDur;
    private Process? _proc;
    private readonly Stopwatch _clock = new();
    private readonly StringBuilder _log = new();

    public RecorderState State { get; private set; } = RecorderState.Idle;
    public TimeSpan Elapsed => _clock.Elapsed;
    public PixelRect EffectiveRegion { get; }
    public string? Error { get; private set; }
    public event EventHandler? StateChanged;

    public ScreenRecorder(FfmpegTools tools, RecordingOptions options)
    {
        _tools = tools; _o = options;
        // H.264 needs even dimensions
        var r = options.Region;
        EffectiveRegion = r with { Width = Math.Max(2, r.Width & ~1), Height = Math.Max(2, r.Height & ~1) };
        Directory.CreateDirectory(options.WorkDirectory);
    }

    /// <summary>macOS: avfoundation device index of the screen and its origin (set by the app before recording).</summary>
    public static int MacScreenDevice { get; set; } = 1;
    public static (int X, int Y) MacScreenOrigin { get; set; }

    internal static List<string> BuildSegmentArgs(PixelRect r, int fps, bool mouse, string? mic, string output)
    {
        var a = new List<string> { "-hide_banner", "-loglevel", "warning", "-y" };
        if (OperatingSystem.IsWindows())
        {
            a.AddRange(new[]
            {
                "-thread_queue_size", "512", "-f", "gdigrab", "-framerate", fps.ToString(), "-draw_mouse", mouse ? "1" : "0",
                "-offset_x", r.X.ToString(), "-offset_y", r.Y.ToString(), "-video_size", $"{r.Width}x{r.Height}", "-i", "desktop",
            });
            if (mic is not null) a.AddRange(new[] { "-thread_queue_size", "512", "-f", "dshow", "-i", "audio=" + mic });
        }
        else if (OperatingSystem.IsMacOS())
        {
            // avfoundation captures a whole screen in pixels; the area is cropped. Mouse clicks are highlighted by macOS.
            a.AddRange(new[] { "-f", "avfoundation", "-capture_cursor", mouse ? "1" : "0", "-capture_mouse_clicks", "1", "-framerate", fps.ToString(), "-i", $"{MacScreenDevice}:{(mic is null ? "none" : mic)}" });
            a.AddRange(new[] { "-vf", $"crop={r.Width}:{r.Height}:{r.X - MacScreenOrigin.X}:{r.Y - MacScreenOrigin.Y}" });
        }
        else
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
                throw new PlatformNotSupportedException("Videoaufnahme unter Wayland ist noch nicht implementiert (geplant: XDG-Portal + PipeWire). Unter X11/XWayland funktioniert x11grab.");
            var display = Environment.GetEnvironmentVariable("DISPLAY") ?? ":0";
            a.AddRange(new[] { "-f", "x11grab", "-draw_mouse", mouse ? "1" : "0", "-framerate", fps.ToString(), "-video_size", $"{r.Width}x{r.Height}", "-i", $"{display}+{r.X},{r.Y}" });
            if (mic is not null) a.AddRange(new[] { "-f", "pulse", "-i", mic });
        }

        a.AddRange(new[] { "-c:v", "libx264", "-preset", "ultrafast", "-crf", "18", "-pix_fmt", "yuv444p", "-g", (fps * 2).ToString() });
        if (mic is not null) a.AddRange(new[] { "-c:a", "aac", "-b:a", "160k" });
        a.Add(output);
        return a;
    }

    public void Start()
    {
        if (State is RecorderState.Recording) return;
        var seg = Path.Combine(_o.WorkDirectory, $"segment-{_segments.Count:000}.mkv");
        var psi = new ProcessStartInfo(_tools.Ffmpeg)
        {
            RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var x in BuildSegmentArgs(EffectiveRegion, _o.Fps, _o.DrawMouse, _o.Microphone, seg)) psi.ArgumentList.Add(x);
        var p = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg konnte nicht gestartet werden.");
        p.EnableRaisingEvents = true;
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (_log) _log.AppendLine(e.Data); };
        p.BeginErrorReadLine();
        p.Exited += (_, _) =>
        {
            if (ReferenceEquals(_proc, p) && State == RecorderState.Recording)
            {
                _clock.Stop();
                Error = "Die Aufnahme wurde unerwartet beendet: " + LogTail();
                State = RecorderState.Failed;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        _proc = p;
        _segments.Add(seg);
        _segClock.Add((_clock.Elapsed.TotalSeconds, double.NaN));
        _clock.Start();
        State = RecorderState.Recording;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private string LogTail() { lock (_log) { var s = _log.ToString(); return s.Length > 800 ? s[^800..] : s; } }

    /// <summary>Ends the current segment gracefully ('q' makes ffmpeg finalize the file).</summary>
    private async Task EndSegmentAsync()
    {
        var p = _proc;
        _proc = null;
        if (p is null) return;
        if (_segClock.Count > 0 && double.IsNaN(_segClock[^1].End)) _segClock[^1] = _segClock[^1] with { End = _clock.Elapsed.TotalSeconds };
        var tail = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (!p.HasExited)
            {
                await p.StandardInput.WriteAsync('q');
                await p.StandardInput.FlushAsync();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                try { await p.WaitForExitAsync(cts.Token); }
                catch (OperationCanceledException) { p.Kill(true); }
            }
        }
        catch (IOException) { /* already exited */ }
        finally
        {
            p.Dispose();
            // finishing the file takes a moment too → count only half of the wait as captured time
            _segTail.Add(Math.Min(0.5, tail.Elapsed.TotalSeconds / 2));
        }
    }

    public async Task PauseAsync()
    {
        if (State != RecorderState.Recording) return;
        State = RecorderState.Paused;
        _clock.Stop();
        await EndSegmentAsync();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume() { if (State == RecorderState.Paused) Start(); }

    /// <summary>Stops and joins segments into <paramref name="outputFile"/> (stream copy, no quality loss).</summary>
    public async Task<string> StopAsync(string outputFile, CancellationToken ct = default)
    {
        var failed = State == RecorderState.Failed;
        State = RecorderState.Finished;
        _clock.Stop();
        await EndSegmentAsync();
        var existing = _segments.Where(s => File.Exists(s) && new FileInfo(s).Length > 1024).ToList();
        if (existing.Count == 0)
            throw new InvalidOperationException("Es wurde nichts aufgenommen." + (failed ? " " + Error : ""));
        // real lengths → clock times (clicks, live drawings) can be mapped to video times
        var durs = new List<double>();
        foreach (var s in _segments)
        {
            double d = 0;
            if (existing.Contains(s)) { try { d = (await MediaProbe.ProbeAsync(_tools, s, ct)).Duration; } catch (InvalidDataException) { } }
            durs.Add(d);
        }
        _segDur = durs;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);
        if (existing.Count == 1)
        {
            File.Move(existing[0], outputFile, overwrite: true);
        }
        else
        {
            var list = Path.Combine(_o.WorkDirectory, "segments.txt");
            await File.WriteAllLinesAsync(list, existing.Select(s => "file '" + s.Replace("\\", "/").Replace("'", "'\\''") + "'"), ct);
            var r = await FfmpegRunner.RunAsync(_tools.Ffmpeg, new[] { "-hide_banner", "-y", "-f", "concat", "-safe", "0", "-i", list, "-c", "copy", outputFile }, ct);
            if (!r.Success) throw new InvalidOperationException("Segmente konnten nicht zusammengefügt werden: " + r.StdErrTail);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        return outputFile;
    }

    /// <summary>
    /// Maps the recording clock (what <see cref="Elapsed"/> showed, pauses excluded) to the time in the joined video.
    /// ffmpeg starts capturing a little after the process start; that delay is measured per segment
    /// (clock length minus real length) and removed. Valid after <see cref="StopAsync"/>.
    /// </summary>
    public double MapToVideoTime(double clock) => MapClock(clock, _segClock, _segDur, _segTail);

    internal static double MapClock(double clock, IReadOnlyList<(double Start, double End)> seg, IReadOnlyList<double>? dur, IReadOnlyList<double>? tail = null)
    {
        if (dur is null || dur.Count != seg.Count || seg.Count == 0) return clock;
        double before = 0;
        for (var i = 0; i < seg.Count; i++)
        {
            var (s, e) = seg[i];
            if (double.IsNaN(e)) e = s + dur[i];
            if (clock <= e || i == seg.Count - 1)
            {
                // captured clock span = [s + lag, e + tail] and has the real length dur[i]
                var t = tail is not null && i < tail.Count ? tail[i] : 0;
                var lag = Math.Max(0, (e + t - s) - dur[i]);
                return before + Math.Clamp(clock - s - lag, 0, dur[i]);
            }
            before += dur[i];
        }
        return before;
    }

    /// <summary>Discards everything recorded so far.</summary>
    public async Task CancelAsync()
    {
        State = RecorderState.Finished;
        _clock.Stop();
        await EndSegmentAsync();
        Cleanup();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Cleanup()
    {
        try { if (Directory.Exists(_o.WorkDirectory)) Directory.Delete(_o.WorkDirectory, true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_proc is not null) await EndSegmentAsync();
    }
}
