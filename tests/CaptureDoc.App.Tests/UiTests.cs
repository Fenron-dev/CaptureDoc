using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CaptureDoc.App.Editor;
using CaptureDoc.App.Services;
using CaptureDoc.App.Video;
using CaptureDoc.App.Views;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Portable;
using CaptureDoc.Media;

[assembly: AvaloniaTestApplication(typeof(CaptureDoc.App.Tests.TestAppBuilder))]

namespace CaptureDoc.App.Tests;

public sealed class TestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>UI tests without a screen (Avalonia headless). Real ffmpeg is used when available.</summary>
public class UiTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "capturedoc-uitests", Guid.NewGuid().ToString("N"));
    private static AppServices? _app;

    private static AppServices App
    {
        get
        {
            if (_app is not null) return _app;
            var env = new PortableEnvironment(Root);
            env.EnsureWritable();
            ProjectSession.ToPortable = env.ToPortablePath;
            ProjectSession.FromPortable = env.FromPortablePath;
            return _app = new AppServices(env);
        }
    }

    private static string? MakeClip()
    {
        if (App.Ffmpeg is not { } t) return null;
        var f = Path.Combine(Root, "clip.mkv");
        if (File.Exists(f)) return f;
        // never block the UI thread on an async call that continues on it → run on the thread pool
        var r = Task.Run(() => FfmpegRunner.RunAsync(t.Ffmpeg, new[] { "-hide_banner", "-y", "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=10:duration=8", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv444p", f })).GetAwaiter().GetResult();
        Assert.True(r.Success, r.StdErrTail);
        return f;
    }

    private static void Pump(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
    }

    private static void PumpUntil(Func<bool> condition, int timeoutMs = 15000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(30); }
        Assert.True(condition(), "Timeout");
    }

    private static List<string> Texts(Visual root) =>
        root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").Where(t => t.Length > 0).ToList();

    private static void Press(Window w, Key key, PhysicalKey pk, string text)
    {
        w.KeyPressQwerty(pk, RawInputModifiers.None);
        w.KeyReleaseQwerty(pk, RawInputModifiers.None);
    }

    [AvaloniaFact]
    public void Gdi_captures_a_region_in_physical_pixels_and_mouse_hook_installs()
    {
        if (!OperatingSystem.IsWindows()) return;
        var cap = new CaptureDoc.Platform.Windows.GdiCaptureService();
        var vs = cap.VirtualScreen;
        Assert.True(vs.Width > 0 && vs.Height > 0);
        var raw = cap.Capture(new CaptureDoc.Core.Model.PixelRect(vs.X + 10, vs.Y + 10, 123, 45), includeCursor: false);
        Assert.Equal(123, raw.Width);
        Assert.Equal(45, raw.Height);
        Assert.All(Enumerable.Range(0, 20), i => Assert.Equal(255, raw.Pixels[i * 4 + 3]));   // opaque
        Assert.NotEmpty(cap.GetMonitors());
        using var hook = new CaptureDoc.Platform.Windows.WindowsMouseHook();
        Assert.True(hook.IsActive);
    }

    [AvaloniaFact]
    public void Video_editor_marks_and_removes_cuts_with_keyboard()
    {
        if (MakeClip() is not { } clip) return;
        var w = new VideoEditorWindow(App, clip);
        w.Show();
        PumpUntil(() => Texts(w).Any(t => t.Contains("320×240")));
        Pump(300);
        // seek to 2 s, X, seek to 4 s, X  → one cut 2..4
        Press(w, Key.Right, PhysicalKey.ArrowRight, "");    // focus window
        for (var i = 0; i < 2; i++) w.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Press(w, Key.X, PhysicalKey.X, "x");
        for (var i = 0; i < 2; i++) w.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Press(w, Key.X, PhysicalKey.X, "x");
        Pump(200);
        Assert.Contains(Texts(w), t => t.StartsWith("Wird entfernt"));
        Assert.Contains(Texts(w), t => t.Contains("1 Schnitt(e)"));
        // Delete inside the cut removes it again
        w.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
        Press(w, Key.Delete, PhysicalKey.Delete, "");
        Pump(200);
        Assert.Contains(Texts(w), t => t.Contains("0 Schnitt(e)"));
        Assert.DoesNotContain(Texts(w), t => t.StartsWith("Wird entfernt"));
        w.Close();
    }

    [AvaloniaFact]
    public void Studio_adds_video_step_with_panel_and_badge()
    {
        if (MakeClip() is not { } clip) return;
        var studio = new StudioWindow(App);
        studio.Show();
        Pump(200);
        var png = Task.Run(() => new VideoExporter(App.Ffmpeg!).ExtractFrameAsync(clip, 0.1)).GetAwaiter().GetResult();
        var img = SkiaSharp.SKImage.FromEncodedData(png)!;
        studio.AddVideoStep(clip, new VideoRef { Start = 1, End = 7, Cuts = { new TimeCut { From = 3, To = 4 } } }, img, png, "Clip-Schritt");
        Pump(300);
        var texts = Texts(studio);
        Assert.Contains(texts, t => t.Contains("🎬 Video 5,0 s") || t.Contains("🎬 Video 5.0 s"));
        Assert.Contains(texts, t => t.Contains("🎬 Video-Schritt"));
        var step = studio.Session.Project.Guide.Steps.Single();
        Assert.NotNull(step.PageId);                       // poster page exists and is editable
        Assert.Equal("./clip.mkv".Length > 0, !Path.IsPathRooted(step.Video!.Path) || File.Exists(step.Video.Path));
        studio.Close();
    }

    [AvaloniaFact]
    public void Annotation_tracks_can_be_dragged_and_reordered()
    {
        if (MakeClip() is not { } clip) return;
        var overlay = new VideoOverlay { Page = new Core.Model.Page { Width = 320, Height = 240, Background = ColorRgba.Transparent } };
        var step = new StepLayer { Center = new PointD(35, 35), Number = 1, Name = "Nummer", Timing = new LayerTiming { StartSeconds = 2, EndSeconds = 5 } };
        var redact = new RedactLayer { Rect = new RectD(0, 0, 100, 100), Name = "Schwärzung", Timing = new LayerTiming { StartSeconds = 0, EndSeconds = 8 } };
        overlay.Page.Layers.Add(step);
        overlay.Page.Layers.Add(redact);       // on top → would cover the number
        var gs = new GuideStep { Title = "V", Video = new VideoRef { Path = clip, Start = 0, End = 8, Overlay = overlay } };
        var w = new VideoEditorWindow(App, clip, gs);
        w.Show();
        PumpUntil(() => Texts(w).Any(t => t.Contains("320×240")));
        Pump(300);
        var tl = w.GetVisualDescendants().OfType<TimelineControl>().Single();
        Assert.Equal(2, tl.Lanes.Count);
        Assert.Equal(redact.Id, tl.Lanes[0].Id);            // top layer first
        Assert.True(tl.Lanes[0].IsEffect);

        // drag the end edge of the number track (lane 1, 2..5 s) to 7 s
        double X(double t) => tl.Bounds.Width * t / 8.0;
        var y = TimelineControl.StripBottom + 4 + (TimelineControl.LaneH + TimelineControl.LaneGap) * 1 + TimelineControl.LaneH / 2;
        Point P(double t) => tl.TranslatePoint(new Point(X(t), y), w)!.Value;
        w.MouseDown(P(5), MouseButton.Left);
        w.MouseMove(P(6));
        w.MouseMove(P(7));
        w.MouseUp(P(7), MouseButton.Left);
        Pump(200);
        Assert.InRange(step.Timing!.EndSeconds, 6.8, 7.2);
        Assert.Equal(2, step.Timing.StartSeconds, 2);

        // move the whole bar 1 s earlier
        w.MouseDown(P(4), MouseButton.Left);
        w.MouseMove(P(3.5));
        w.MouseMove(P(3));
        w.MouseUp(P(3), MouseButton.Left);
        Pump(200);
        Assert.InRange(step.Timing.StartSeconds, 0.8, 1.2);
        Assert.InRange(step.Timing.EndSeconds - step.Timing.StartSeconds, 4.9, 5.1);

        // ▼ on the redaction card (first card) puts it below the number
        var down = w.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "▼");
        down.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump(200);
        Assert.True(overlay.Page.Layers.IndexOf(redact) < overlay.Page.Layers.IndexOf(step));
        Assert.Equal(step.Id, tl.Lanes[0].Id);
        var spec = OverlayRenderer.Prepare(new ProjectSession(new CaptureProject(), new Core.Serialization.AssetStore()), overlay, Path.Combine(Root, "ord-" + Guid.NewGuid().ToString("N")));
        Assert.True(spec.Effects.Single().Order < spec.Images.Single().Order);   // number drawn after the blackout
        w.Close();
    }

    [AvaloniaFact]
    public async Task Video_step_with_timed_annotation_renders_for_export()
    {
        if (MakeClip() is not { } clip) return;
        var session = new ProjectSession(new CaptureProject(), new Core.Serialization.AssetStore());
        var overlay = new VideoOverlay { Page = new Core.Model.Page { Width = 320, Height = 240, Background = ColorRgba.Transparent } };
        overlay.Page.Layers.Add(new TextLayer { Rect = new RectD(20, 20, 0, 0), Text = "Hier klicken", Style = TextStyle.InfoBox, Background = ColorRgba.White, Timing = new LayerTiming { StartSeconds = 2, EndSeconds = 5 } });
        overlay.Page.Layers.Add(new RedactLayer { Rect = new RectD(200, 150, 80, 60), Timing = new LayerTiming { StartSeconds = 0, EndSeconds = 8 } });
        overlay.Page.Layers[0].SetBounds(Rendering.TextLayout.FitBox((TextLayer)overlay.Page.Layers[0]));
        session.Project.Guide.Steps.Add(new GuideStep { Title = "Video", Video = new VideoRef { Path = clip, Start = 1, End = 6, Overlay = overlay, MaxBytes = null } });
        var work = Path.Combine(Root, "work-" + Guid.NewGuid().ToString("N"));
        var media = await Task.Run(() => VideoStepRenderer.RenderAllAsync(App, session, work, null));
        var file = media.Values.Single().FilePath;
        var info = await Task.Run(() => MediaProbe.ProbeAsync(App.Ffmpeg!, file));
        Assert.InRange(info.Duration, 4.5, 5.5);
        Assert.Equal("h264", info.VideoCodec);
        Assert.True(Directory.GetFiles(work, "layer-*.png", SearchOption.AllDirectories).Length == 1); // text as PNG, redaction as ffmpeg effect
    }
}
