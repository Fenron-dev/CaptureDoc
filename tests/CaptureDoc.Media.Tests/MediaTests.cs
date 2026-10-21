using CaptureDoc.Core.Export;
using CaptureDoc.Core.Model;

namespace CaptureDoc.Media.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "capturedoc-mtests", Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string n) => System.IO.Path.Combine(Path, n);
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}

/// <summary>Pure logic, no ffmpeg needed.</summary>
public class OptimizerAndArgsTests
{
    [Fact]
    public void Gif_optimizer_reduces_colors_then_fps_then_size()
    {
        // fake: bytes ~ w*h*fps*(colors/256 * 0.5 + 0.5)
        long Fake(GifParams p) => (long)(p.Width * p.Height * p.Fps * (0.5 + 0.5 * p.Colors / 256.0));
        var seen = new List<GifParams>();
        var r = GifOptimizer.Optimize(new GifOptimizer.Request(1280, 720, 800, 450, 15, 6, 256, 32, 320, 1_500_000, SizeLimitKind.Hard),
            (p, _) => { seen.Add(p); return Fake(p); });
        Assert.True(r.Success);
        Assert.True(r.Bytes <= 1_500_000);
        Assert.Equal(800, seen[0].Width);                // no upscaling, fit 800x450
        Assert.Equal(450, seen[0].Height);
        Assert.True(seen[1].Colors < 256 || seen[1].Fps < 15);
        Assert.All(seen, p => Assert.True(p.Width % 2 == 0 && p.Height % 2 == 0));
    }

    [Fact]
    public void Gif_optimizer_reports_unreachable_hard_limit()
    {
        var r = GifOptimizer.Optimize(new GifOptimizer.Request(800, 600, null, null, 10, 8, 64, 64, 700, 100, SizeLimitKind.Hard), (p, _) => 10_000_000);
        Assert.False(r.Success);
        Assert.Contains("nicht erreichbar", r.Message);
    }

    [Fact]
    public void Clip_range_computes_kept_duration_and_select()
    {
        var c = new ClipRange { Start = 2, End = 12, Cuts = { (4, 6), (20, 30) } };
        Assert.Equal(8, c.KeptDuration, 3);
        Assert.Equal("not(between(t\\,2\\,4))", c.SelectExpression()); // commas escaped for the filtergraph (verified end-to-end in Mp4_trim_cut_and_size_limit)
        Assert.Null(new ClipRange { Start = 0, End = 5 }.SelectExpression());
    }

    [Fact]
    public void Segment_args_use_physical_region_and_even_size()
    {
        if (!OperatingSystem.IsWindows()) return;
        var a = ScreenRecorder.BuildSegmentArgs(new PixelRect(-1920, 10, 640, 360), 25, true, null, "o.mkv");
        Assert.Contains("gdigrab", a);
        Assert.Contains("-1920", a);
        Assert.Contains("640x360", a);
        Assert.Contains("yuv444p", a);
    }

    [Fact]
    public void Portable_ffmpeg_folder_is_preferred_over_path()
    {
        using var d = new TempDir();
        var dir = Directory.CreateDirectory(d.File("Runtime/ffmpeg")).FullName;
        var ext = OperatingSystem.IsWindows() ? ".exe" : "";
        File.WriteAllText(Path.Combine(dir, "ffmpeg" + ext), "");
        File.WriteAllText(Path.Combine(dir, "ffprobe" + ext), "");
        var t = FfmpegTools.Locate(d.Path)!;
        Assert.StartsWith("portabel", t.Source);
        Assert.Equal(Path.Combine(dir, "ffmpeg" + ext), t.Ffmpeg);
    }

    [Theory]
    [InlineData("frame=  10 fps=0.0 q=-1.0 size=  1kB time=00:00:01.50 bitrate=", 1.5)]
    [InlineData("time=01:02:03.00", 3723)]
    public void Ffmpeg_time_is_parsed(string line, double expected)
    {
        Assert.True(FfmpegRunner.TryParseTime(line, out var t));
        Assert.Equal(expected, t, 3);
    }

    [Fact]
    public void Jpeg_stream_is_split()
    {
        byte[] j1 = { 0xFF, 0xD8, 1, 2, 0xFF, 0xD9 }, j2 = { 0xFF, 0xD8, 3, 0xFF, 0xD9 };
        var frames = VideoExporter.SplitJpegs(j1.Concat(j2).ToArray());
        Assert.Equal(2, frames.Count);
        Assert.Equal(j2, frames[1]);
    }
}

/// <summary>Integration tests with the real ffmpeg (skipped when ffmpeg is not installed).</summary>
public class FfmpegIntegrationTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static FfmpegTools? Tools => FfmpegTools.Locate(AppContext.BaseDirectory);

    /// <summary>Screen-like test clip: moving pattern + noise region + text, 1280x720.</summary>
    private static async Task<string> MakeClipAsync(FfmpegTools t, string dir, double seconds = 10, bool audio = false)
    {
        var f = Path.Combine(dir, "src.mkv");
        var args = new List<string> { "-hide_banner", "-y", "-f", "lavfi", "-i", $"testsrc2=size=1280x720:rate=30:duration={seconds}" };
        if (audio) args.AddRange(new[] { "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}" });
        args.AddRange(new[] { "-c:v", "libx264", "-preset", "ultrafast", "-crf", "18", "-pix_fmt", "yuv444p" });
        if (audio) args.AddRange(new[] { "-c:a", "aac" });
        args.Add(f);
        var r = await FfmpegRunner.RunAsync(t.Ffmpeg, args);
        Assert.True(r.Success, r.StdErrTail);
        return f;
    }

    [Fact]
    public async Task Acceptance_gif_with_max_800x450_and_hard_5mb_limit_is_verified()
    {
        if (Tools is not { } t) return;
        using var d = new TempDir();
        var src = await MakeClipAsync(t, d.Path);
        var out1 = d.File("demo.gif");
        var s = new GifSettings { MaxWidth = 800, MaxHeight = 450, Fps = 15, MaxBytes = 5L * 1024 * 1024, LimitKind = SizeLimitKind.Hard, DropDuplicates = false };
        var r = await new VideoExporter(t).ExportGifAsync(src, out1, new ClipRange { Start = 0, End = 10 }, s);
        Assert.True(r.Success, r.Message);
        output.WriteLine("ACCEPTANCE3: " + r.Message);
        var len = new FileInfo(out1).Length;
        Assert.True(len <= 5L * 1024 * 1024, $"{len} bytes");
        Assert.True(r.Width <= 800 && r.Height <= 450, $"{r.Width}x{r.Height}");
        var info = await MediaProbe.ProbeAsync(t, out1);
        Assert.Equal("gif", info.VideoCodec);
        Assert.InRange(info.Duration, 9.0, 10.5);
    }

    [Fact]
    public async Task Gif_tight_limit_forces_iterative_reduction()
    {
        if (Tools is not { } t) return;
        using var d = new TempDir();
        var src = await MakeClipAsync(t, d.Path, 4);
        var s = new GifSettings { MaxWidth = 800, Fps = 15, MaxBytes = 400 * 1024, LimitKind = SizeLimitKind.Hard, DropDuplicates = false, MinWidth = 200, MinFps = 5 };
        var r = await new VideoExporter(t).ExportGifAsync(src, d.File("small.gif"), new ClipRange { Start = 0, End = 4 }, s);
        if (r.Success) Assert.True(new FileInfo(d.File("small.gif")).Length <= 400 * 1024);
        else Assert.False(File.Exists(d.File("small.gif")));   // hard limit: nothing written
    }

    [Fact]
    public async Task Mp4_trim_cut_and_size_limit()
    {
        if (Tools is not { } t) return;
        using var d = new TempDir();
        var src = await MakeClipAsync(t, d.Path, 10, audio: true);
        var outFile = d.File("clip.mp4");
        var clip = new ClipRange { Start = 1, End = 9, Cuts = { (3, 5) } };   // 8 s minus 2 s = 6 s
        var r = await new VideoExporter(t).ExportMp4Async(src, outFile, clip, new Mp4Settings { MaxWidth = 960, MaxBytes = 600 * 1024, LimitKind = SizeLimitKind.Hard });
        Assert.True(r.Success, r.Message);
        Assert.True(new FileInfo(outFile).Length <= 600 * 1024);
        var info = await MediaProbe.ProbeAsync(t, outFile);
        Assert.Equal(960, info.Width);
        Assert.InRange(info.Duration, 5.6, 6.5);
        Assert.Equal("h264", info.VideoCodec);
        Assert.True(info.HasAudio);
    }

    [Fact]
    public async Task Stream_copy_cuts_without_reencoding()
    {
        if (Tools is not { } t) return;
        using var d = new TempDir();
        var src = d.File("h264.mp4");
        // 4:2:0 H.264 with a keyframe every second
        var mk = await FfmpegRunner.RunAsync(t.Ffmpeg, new[] { "-hide_banner", "-y", "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=10", "-c:v", "libx264", "-preset", "ultrafast", "-g", "25", "-pix_fmt", "yuv420p", src });
        Assert.True(mk.Success, mk.StdErrTail);
        Assert.True((await MediaProbe.ProbeAsync(t, src)).CanStreamCopyToMp4);
        var outFile = d.File("copy.mp4");
        var clip = new ClipRange { Start = 1, End = 9, Cuts = { (3, 5) } };     // keep 1..3 and 5..9 = 6 s
        var r = await new VideoExporter(t).ExportMp4Async(src, outFile, clip, new Mp4Settings { StreamCopy = true });
        Assert.True(r.Success, r.Message);
        Assert.InRange(r.Duration, 5.5, 6.6);
        Assert.Contains("Verlustfrei", r.Message);
    }

    [Fact]
    public void Kept_segments_respect_cuts()
    {
        var s = VideoExporter.KeptSegments(new ClipRange { Start = 0, End = 10, Cuts = { (2, 3), (6, 8) } });
        Assert.Equal(new[] { (0d, 2d), (3d, 6d), (8d, 10d) }, s);
    }

    [Fact]
    public async Task Frames_can_be_extracted()
    {
        if (Tools is not { } t) return;
        using var d = new TempDir();
        var src = await MakeClipAsync(t, d.Path, 3);
        var ex = new VideoExporter(t);
        var png = await ex.ExtractFrameAsync(src, 1.2);
        Assert.Equal(0x89, png[0]);
        var frames = await ex.ExtractPreviewFramesAsync(src, 5, 320);
        Assert.InRange(frames.Count, 14, 16);
    }

    [Fact]
    public async Task Short_screen_recording_with_pause_produces_joined_file()
    {
        if (Tools is not { } t || !OperatingSystem.IsWindows()) return;
        using var d = new TempDir();
        var rec = new ScreenRecorder(t, new RecordingOptions { Region = new PixelRect(0, 0, 321, 241), Fps = 15, WorkDirectory = d.File("work") });
        Assert.Equal(320, rec.EffectiveRegion.Width);   // made even
        rec.Start();
        await Task.Delay(1500);
        await rec.PauseAsync();
        await Task.Delay(800);                           // paused time must not be recorded
        rec.Resume();
        await Task.Delay(1500);
        var file = await rec.StopAsync(d.File("rec.mkv"));
        var info = await MediaProbe.ProbeAsync(t, file);
        Assert.Equal(320, info.Width);
        Assert.Equal(240, info.Height);
        Assert.InRange(info.Duration, 2.0, 3.6);
        // the end of the recording clock maps to the end of the joined video (start delay removed)
        output.WriteLine($"clock {rec.Elapsed.TotalSeconds:0.00} s → video {rec.MapToVideoTime(rec.Elapsed.TotalSeconds):0.00} s, file {info.Duration:0.00} s");
        Assert.InRange(rec.MapToVideoTime(rec.Elapsed.TotalSeconds), info.Duration - 0.25, info.Duration + 0.05);
        Assert.True(rec.MapToVideoTime(1.6) > 1.0);      // shortly after the pause → in the second segment
        rec.Cleanup();
    }

    [Fact]
    public void Clock_times_are_mapped_per_segment_without_start_delay()
    {
        var seg = new List<(double, double)> { (0, 5), (5, 9) };
        var dur = new List<double> { 4.6, 3.8 };          // ffmpeg started 0.4 s / 0.2 s late
        Assert.Equal(1.6, ScreenRecorder.MapClock(2, seg, dur), 3);
        Assert.Equal(0, ScreenRecorder.MapClock(0.1, seg, dur), 3);
        Assert.Equal(5.4, ScreenRecorder.MapClock(6, seg, dur), 3);
        Assert.Equal(8.4, ScreenRecorder.MapClock(9, seg, dur), 3);
        // ffmpeg kept recording 0.3 s after the clock stopped → its start delay was 0.3 s larger
        Assert.Equal(1.3, ScreenRecorder.MapClock(2, seg, dur, new[] { 0.3, 0.0 }), 3);
    }
}
