using CaptureDoc.Core.Export;

namespace CaptureDoc.Media.Tests;

public class OverlayExprTests
{
    [Fact]
    public void Lerp_builds_piecewise_linear_expression()
    {
        var e = OverlaySpec.Lerp(new List<(double, double)> { (0, 0), (2, 100) });
        Assert.Equal("if(lt(t,0),0,if(lt(t,2),(0+(100)*(t-0)/2),100))", e);
        Assert.Equal("5", OverlaySpec.Lerp(new List<(double, double)> { (1, 5) }));
    }

    [Fact]
    public void Filter_uses_relative_time_after_seek()
    {
        var o = new OverlaySpec();
        o.Effects.Add(new RegionEffect { Kind = RegionEffectKind.Blackout, X = 1, Y = 2, W = 3, H = 4, Start = 12, End = 15 });
        var f = o.BuildFilter("[0:v]", 100, 100, 30, offset: 10);
        Assert.Contains("between(t,2,5)", f);
        Assert.EndsWith("null[ov]", f);
    }
}

public class OverlayIntegrationTests
{
    private static FfmpegTools? Tools => FfmpegTools.Locate(AppContext.BaseDirectory);

    private static async Task<string> GreyClipAsync(FfmpegTools t, string dir)
    {
        var f = Path.Combine(dir, "grey.mkv");
        var r = await FfmpegRunner.RunAsync(t.Ffmpeg, new[] { "-hide_banner", "-y", "-f", "lavfi", "-i", "color=c=0x808080:s=320x240:r=10:d=6", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "10", "-pix_fmt", "yuv444p", f });
        Assert.True(r.Success, r.StdErrTail);
        return f;
    }

    private static async Task<string> RedPngAsync(FfmpegTools t, string dir)
    {
        var f = Path.Combine(dir, "red.png");
        var r = await FfmpegRunner.RunAsync(t.Ffmpeg, new[] { "-hide_banner", "-y", "-f", "lavfi", "-i", "color=c=red:s=40x40", "-frames:v", "1", f });
        Assert.True(r.Success, r.StdErrTail);
        return f;
    }

    /// <summary>RGB of one pixel at time t.</summary>
    private static async Task<(int r, int g, int b)> PixelAsync(FfmpegTools t, string file, double time, int x, int y)
    {
        using var ms = new MemoryStream();
        var r = await FfmpegRunner.RunAsync(t.Ffmpeg, new[] { "-hide_banner", "-loglevel", "error", "-ss", FfmpegRunner.F(time), "-i", file, "-frames:v", "1", "-vf", $"crop=2:2:{x - x % 2}:{y - y % 2},format=rgb24", "-f", "rawvideo", "-pix_fmt", "rgb24", "-" }, stdout: ms);
        Assert.True(r.Success, r.StdErrTail);
        var px = ms.ToArray();   // 2x2 rgb24, first pixel is enough
        return (px[0], px[1], px[2]);
    }

    [Fact]
    public async Task Timed_blackout_and_image_overlay_are_burned_in_only_inside_their_window()
    {
        if (Tools is not { } t) return;
        using var d = new TempDir();
        var src = await GreyClipAsync(t, d.Path);
        var png = await RedPngAsync(t, d.Path);
        var o = new OverlaySpec();
        o.Effects.Add(new RegionEffect { Kind = RegionEffectKind.Blackout, X = 10, Y = 10, W = 60, H = 60, Start = 1, End = 3 });
        o.Images.Add(new ImageOverlay { PngPath = png, X = 200, Y = 100, Start = 2, End = 4 });
        var outFile = d.File("o.mp4");
        var r = await new VideoExporter(t).ExportMp4Async(src, outFile, new ClipRange { Start = 0, End = 6 }, new Mp4Settings { Quality = VideoQuality.High, Overlay = o });
        Assert.True(r.Success, r.Message);

        var black = await PixelAsync(t, outFile, 2.0, 30, 30);
        Assert.True(black.r < 30 && black.g < 30 && black.b < 30, $"blackout {black}");
        var greyBefore = await PixelAsync(t, outFile, 0.3, 30, 30);
        Assert.InRange(greyBefore.r, 100, 160);
        var red = await PixelAsync(t, outFile, 3.0, 220, 120);
        Assert.True(red.r > 180 && red.g < 80, $"overlay {red}");
        var notYet = await PixelAsync(t, outFile, 1.0, 220, 120);
        Assert.InRange(notYet.g, 100, 160);
        Assert.InRange(r.Duration, 5.5, 6.5);
    }

    [Fact]
    public async Task Overlay_times_follow_the_seek_offset_and_cuts()
    {
        if (Tools is not { } t) return;
        using var d = new TempDir();
        var src = await GreyClipAsync(t, d.Path);
        var o = new OverlaySpec();
        o.Effects.Add(new RegionEffect { Kind = RegionEffectKind.Blackout, X = 0, Y = 0, W = 80, H = 80, Start = 4, End = 5 });
        var outFile = d.File("cut.mkv");
        // keep 2..6 = 4 s; blackout in source 4..5 → output 2..3
        await new VideoExporter(t).RenderEditedAsync(src, outFile, new ClipRange { Start = 2, End = 6 }, overlay: o);
        var info = await MediaProbe.ProbeAsync(t, outFile);
        Assert.InRange(info.Duration, 3.6, 4.4);
        var at = await PixelAsync(t, outFile, 2.5, 20, 20);
        Assert.True(at.r < 30, $"{at}");
        var before = await PixelAsync(t, outFile, 1.0, 20, 20);
        Assert.True(before.r > 90, $"{before}");
    }

    [Fact]
    public async Task Zoom_keyframes_enlarge_the_view_at_the_right_time()
    {
        if (Tools is not { } t) return;
        using var d = new TempDir();
        var src = d.File("box.mkv");
        await FfmpegRunner.RunAsync(t.Ffmpeg, new[] { "-hide_banner", "-y", "-f", "lavfi", "-i", "color=c=0x808080:s=320x240:r=10:d=6", "-vf", "drawbox=x=0:y=0:w=40:h=40:color=red:t=fill", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "10", "-pix_fmt", "yuv444p", src });
        var o = new OverlaySpec();
        o.Zoom.Add(new ZoomPoint(0, 0, 0, 320, 240));
        o.Zoom.Add(new ZoomPoint(3, 0, 0, 320, 240));
        o.Zoom.Add(new ZoomPoint(4, 0, 0, 160, 120));    // 2x zoom on the top-left quarter from 4 s
        var outFile = d.File("zoom.mp4");
        var r = await new VideoExporter(t).ExportMp4Async(src, outFile, new ClipRange { Start = 0, End = 6 }, new Mp4Settings { Quality = VideoQuality.High, Overlay = o });
        Assert.True(r.Success, r.Message);
        Assert.InRange(r.Duration, 5.5, 6.5);
        var early = await PixelAsync(t, outFile, 1.0, 60, 60);
        Assert.True(early.g > 90, $"no zoom yet {early}");             // grey: box ends at 40 px
        var late = await PixelAsync(t, outFile, 5.0, 60, 60);
        Assert.True(late.r > 180 && late.g < 80, $"zoomed {late}");    // box now covers 0..80 px
    }

    [Fact]
    public async Task Moving_overlay_and_zoom_keyframes_render()
    {
        if (Tools is not { } t) return;
        using var d = new TempDir();
        var src = await GreyClipAsync(t, d.Path);
        var png = await RedPngAsync(t, d.Path);
        var o = new OverlaySpec();
        o.Images.Add(new ImageOverlay { PngPath = png, Start = 0, End = 6, Path = { new PathPoint(0, 0, 0), new PathPoint(4, 260, 180) } });
        o.Zoom.Add(new ZoomPoint(0, 0, 0, 320, 240));
        o.Zoom.Add(new ZoomPoint(5, 80, 60, 160, 120));
        var r = await new VideoExporter(t).ExportGifAsync(src, d.File("z.gif"), new ClipRange { Start = 0, End = 6 }, new GifSettings { MaxWidth = 320, Fps = 10, DropDuplicates = false, Overlay = o });
        Assert.True(r.Success, r.Message);
        Assert.Equal(320, r.Width);
        Assert.InRange(r.Duration, 5.0, 6.6);
    }

    [Fact]
    public async Task Blackout_only_covers_layers_below_it()
    {
        if (Tools is not { } t) return;
        using var d = new TempDir();
        var src = await GreyClipAsync(t, d.Path);
        var png = await RedPngAsync(t, d.Path);
        var o = new OverlaySpec();
        // red marker A (order 0) below the blackout, marker B (order 2) above it – both inside the box
        o.Images.Add(new ImageOverlay { PngPath = png, X = 20, Y = 20, Order = 0 });
        o.Effects.Add(new RegionEffect { Kind = RegionEffectKind.Blackout, X = 0, Y = 0, W = 160, H = 120, Order = 1 });
        o.Images.Add(new ImageOverlay { PngPath = png, X = 100, Y = 60, Order = 2 });
        var outFile = d.File("order.mp4");
        var r = await new VideoExporter(t).ExportMp4Async(src, outFile, new ClipRange { Start = 0, End = 2 }, new Mp4Settings { Quality = VideoQuality.High, Overlay = o });
        Assert.True(r.Success, r.Message);
        var a = await PixelAsync(t, outFile, 1.0, 40, 40);
        Assert.True(a.r < 30, $"marker below blackout must be hidden {a}");
        var b = await PixelAsync(t, outFile, 1.0, 120, 80);
        Assert.True(b.r > 180 && b.g < 80, $"marker above blackout stays visible {b}");
    }

    [Fact]
    public async Task Blackout_stays_on_its_content_when_zoomed()
    {
        if (Tools is not { } t) return;
        using var d = new TempDir();
        var src = await GreyClipAsync(t, d.Path);
        var o = new OverlaySpec();
        o.Effects.Add(new RegionEffect { Kind = RegionEffectKind.Blackout, X = 0, Y = 0, W = 40, H = 40 });
        o.Zoom.Add(new ZoomPoint(0, 0, 0, 160, 120));   // 2x on the top-left quarter the whole time
        var outFile = d.File("zb.mp4");
        var r = await new VideoExporter(t).ExportMp4Async(src, outFile, new ClipRange { Start = 0, End = 2 }, new Mp4Settings { Quality = VideoQuality.High, Overlay = o });
        Assert.True(r.Success, r.Message);
        var inside = await PixelAsync(t, outFile, 1.0, 60, 60);   // source 30/30 → zoomed 60/60
        Assert.True(inside.r < 30, $"zoomed blackout {inside}");
        var outside = await PixelAsync(t, outFile, 1.0, 100, 100); // source 50/50 → grey
        Assert.True(outside.r > 90, $"{outside}");
    }

    [Fact]
    public async Task Slides_are_inserted_with_their_own_timed_elements()
    {
        if (Tools is not { } t) return;
        using var d = new TempDir();
        var src = await GreyClipAsync(t, d.Path);                 // 6 s grey, no audio
        var png = await RedPngAsync(t, d.Path);
        var slideOverlay = new OverlaySpec();
        slideOverlay.Images.Add(new ImageOverlay { PngPath = png, X = 100, Y = 100, Start = 1 });   // appears 1 s into the slide
        var slides = new List<SlideSpec> { new() { At = 3, Duration = 2, Color = "0x0000FF", Overlay = slideOverlay } };
        var clip = new ClipRange { Start = 1, End = 6, Cuts = { (4.5, 5) } };   // kept 1..4.5 + 5..6 = 4.5 s; slide at source 3 → output 2
        Assert.Equal(2, VideoExporter.OutputTime(clip, 3), 3);
        var outFile = d.File("composed.mkv");
        await new VideoExporter(t).ComposeAsync(src, outFile, clip, null, slides);
        var info = await MediaProbe.ProbeAsync(t, outFile);
        Assert.InRange(info.Duration, 6.2, 6.8);                // 4.5 + 2
        var before = await PixelAsync(t, outFile, 1.0, 20, 20);
        Assert.InRange(before.r, 100, 160);                       // grey video
        var slide = await PixelAsync(t, outFile, 2.5, 20, 20);
        Assert.True(slide.b > 180 && slide.r < 60, $"slide colour {slide}");
        var notYet = await PixelAsync(t, outFile, 2.5, 120, 120);
        Assert.True(notYet.b > 180, $"element not yet visible {notYet}");
        var shown = await PixelAsync(t, outFile, 3.5, 120, 120);
        Assert.True(shown.r > 180 && shown.b < 80, $"element visible after 1 s {shown}");
        var after = await PixelAsync(t, outFile, 5.0, 20, 20);
        Assert.InRange(after.r, 100, 160);                        // back to the video
    }

    [Fact]
    public async Task Slides_keep_the_audio_track_in_sync()
    {
        if (Tools is not { } t) return;
        using var d = new TempDir();
        var src = d.File("tone.mkv");
        var mk = await FfmpegRunner.RunAsync(t.Ffmpeg, new[] { "-hide_banner", "-y", "-f", "lavfi", "-i", "color=c=0x808080:s=320x240:r=10:d=4", "-f", "lavfi", "-i", "sine=f=440:r=44100:d=4",
            "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv444p", "-c:a", "aac", "-shortest", src });
        Assert.True(mk.Success, mk.StdErrTail);
        var outFile = d.File("composed-audio.mkv");
        await new VideoExporter(t).ComposeAsync(src, outFile, new ClipRange { Start = 0, End = 4 }, null,
            new List<SlideSpec> { new() { At = 0, Duration = 1.5 }, new() { At = 4, Duration = 1 } });   // title slide at the start, one at the end
        var info = await MediaProbe.ProbeAsync(t, outFile);
        Assert.True(info.HasAudio);
        Assert.InRange(info.Duration, 6.2, 6.8);
        var title = await PixelAsync(t, outFile, 0.5, 20, 20);
        Assert.True(title.r < 50, $"dark title slide {title}");
        var mid = await PixelAsync(t, outFile, 3.0, 20, 20);
        Assert.InRange(mid.r, 100, 160);
    }

    [Fact]
    public async Task Pixelate_and_magnify_effects_render()
    {
        if (Tools is not { } t) return;
        using var d = new TempDir();
        var src = d.File("test.mkv");
        await FfmpegRunner.RunAsync(t.Ffmpeg, new[] { "-hide_banner", "-y", "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=10:duration=3", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv444p", src });
        var o = new OverlaySpec();
        o.Effects.Add(new RegionEffect { Kind = RegionEffectKind.Pixelate, X = 0, Y = 0, W = 100, H = 100, Strength = 10 });
        o.Effects.Add(new RegionEffect { Kind = RegionEffectKind.Blur, X = 120, Y = 0, W = 80, H = 80, Strength = 8 });
        o.Effects.Add(new RegionEffect { Kind = RegionEffectKind.Magnify, X = 200, Y = 150, W = 40, H = 40, DX = 220, DY = 10, DW = 80, DH = 80 });
        var r = await new VideoExporter(t).ExportMp4Async(src, d.File("fx.mp4"), new ClipRange { Start = 0, End = 3 }, new Mp4Settings { Overlay = o });
        Assert.True(r.Success, r.Message);
    }
}
