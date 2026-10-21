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
    public void Split_and_delete_with_keyboard_closes_the_gap_and_undo_restores_it()
    {
        if (MakeClip() is not { } clip) return;
        var dir = Path.Combine(Root, "split-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var video = Path.Combine(dir, "v.mkv");
        File.Copy(clip, video);
        var w = new VideoEditorWindow(App, video) { Width = 1440, Height = 900 };
        w.Show();
        PumpUntil(() => Texts(w).Any(t => t.Contains("320×240")));
        Pump(300);
        var tl = w.GetVisualDescendants().OfType<VideoTimeline>().Single();
        // playhead to 3 s, S = split
        for (var i = 0; i < 3; i++) w.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Press(w, Key.S, PhysicalKey.S, "s");
        Pump(200);
        Assert.Equal(2, w.Sequence.AllClips.Count());
        Assert.Equal(8, w.Sequence.Duration, 1);
        // click the second part on the main track and delete it
        var second = w.Sequence.Tracks[0].Clips[1];
        Point P(double t) => tl.TranslatePoint(new Point(tl.XOf(t), tl.TrackCenterY(0)), w)!.Value;
        w.MouseDown(P(5), MouseButton.Left); w.MouseUp(P(5), MouseButton.Left);
        Pump(200);
        Press(w, Key.Delete, PhysicalKey.Delete, "");
        Pump(200);
        Assert.Single(w.Sequence.AllClips);
        Assert.Equal(3, w.Sequence.Duration, 1);
        Assert.DoesNotContain(w.Sequence.AllClips, c => c.Id == second.Id);
        // undo brings the part back, nothing was burned in
        w.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Pump(200);
        Assert.Equal(2, w.Sequence.AllClips.Count());
        Assert.Equal(8, w.Sequence.Duration, 1);
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

    private static VideoEditorWindow OpenEditor(string clip, VideoOverlay? overlay = null)
    {
        var gs = overlay is null ? null : new GuideStep { Title = "V", Video = new VideoRef { Path = clip, Start = 0, End = 8, Overlay = overlay } };
        var studio = gs is null ? null : new StudioWindow(App);
        if (gs is not null) studio!.Session.Project.Guide.Steps.Add(gs);
        var w = new VideoEditorWindow(App, clip, gs, studio) { Width = 1440, Height = 900 };
        w.Show();
        PumpUntil(() => Texts(w).Any(t => t.Contains("320×240")));
        Pump(400);
        return w;
    }

    internal static void ShotVideoEditor(string outDir)
    {
        if (MakeClip() is not { } clip) return;
        var overlay = new VideoOverlay { Page = new Core.Model.Page { Width = 320, Height = 240, Background = ColorRgba.Transparent } };
        overlay.Page.Layers.Add(new StepLayer { Center = new PointD(60, 60), Number = 1, Name = "Schritt 1", Timing = new LayerTiming { StartSeconds = 1, EndSeconds = 6 } });
        overlay.Page.Layers.Add(new LineLayer { Start = new PointD(200, 40), End = new PointD(120, 110), Stroke = ColorRgba.Red, StrokeWidth = 5, EndCap = LineCap.Arrow, Name = "Pfeil", Timing = new LayerTiming { StartSeconds = 2, EndSeconds = 5 } });
        overlay.Page.Layers.Add(new RedactLayer { Rect = new RectD(220, 150, 80, 50), Mode = RedactMode.Pixelate, Strength = 10, Name = "Verpixeln", Timing = new LayerTiming { StartSeconds = 0, EndSeconds = 8 } });
        overlay.Page.Layers.Add(new FreehandLayer { Highlighter = true, Stroke = ColorRgba.Yellow.WithAlpha(110), StrokeWidth = 16, Points = { new PointD(30, 200), new PointD(120, 205), new PointD(190, 200) }, Name = "Marker", Timing = new LayerTiming { StartSeconds = 3, EndSeconds = 7 } });
        overlay.Zoom.Add(new ZoomKeyframe { TimeSeconds = 4, View = new RectD(0, 0, 320, 240) });
        overlay.Zoom.Add(new ZoomKeyframe { TimeSeconds = 5, View = new RectD(40, 30, 160, 120) });
        overlay.Slides.Add(new VideoSlide { At = 6, Duration = 4, Name = "Zusammenfassung", Page = new Core.Model.Page { Width = 320, Height = 240, Background = new ColorRgba(28, 30, 38) } });
        var w = OpenEditor(clip, overlay);
        var imp = w.ImportVideoAsync("overlay", clip);
        PumpUntil(() => imp.IsCompleted);
        w.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        w.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        w.SelectClip(null);
        w.SplitAtPlayhead();
        w.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        w.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        w.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Pump(1500);
        var canvas = w.GetVisualDescendants().OfType<EditorCanvas>().Single();
        canvas.Select(new[] { overlay.Page.Layers[1].Id });
        Pump(300);
        w.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "video-editor.png"));
        var slide = w.AddSlide(2);
        Pump(500);
        w.CaptureRenderedFrame()?.Save(Path.Combine(outDir, "video-slide.png"));
        w.Close();
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
        var w = OpenEditor(clip, overlay);
        var tl = w.GetVisualDescendants().OfType<VideoTimeline>().Single();
        Assert.Equal(2, tl.Lanes.Count);
        Assert.Equal(redact.Id, tl.Lanes[0].Id);            // top layer first
        Assert.True(tl.Lanes[0].IsEffect);

        Point P(int lane, double t) => tl.TranslatePoint(new Point(tl.XOf(t), tl.LaneCenterY(lane)), w)!.Value;
        // drag the end edge of the number track (lane 1, 2..5 s) to 7 s
        w.MouseDown(P(1, 5), MouseButton.Left);
        w.MouseMove(P(1, 6));
        w.MouseMove(P(1, 7));
        w.MouseUp(P(1, 7), MouseButton.Left);
        Pump(200);
        Assert.InRange(step.Timing!.EndSeconds, 6.8, 7.2);
        Assert.Equal(2, step.Timing.StartSeconds, 2);
        // move the whole bar 1 s earlier
        w.MouseDown(P(1, 4), MouseButton.Left);
        w.MouseMove(P(1, 3.5));
        w.MouseMove(P(1, 3));
        w.MouseUp(P(1, 3), MouseButton.Left);
        Pump(200);
        Assert.InRange(step.Timing.StartSeconds, 0.8, 1.2);
        Assert.InRange(step.Timing.EndSeconds - step.Timing.StartSeconds, 4.9, 5.1);
        // the edit is one undo step
        var canvas = w.GetVisualDescendants().OfType<EditorCanvas>().Single();
        Assert.Contains("Zeitfenster", canvas.History.UndoDescription);

        // click the redaction track → selected on the canvas; "▼ Nach hinten" puts it below the number
        w.MouseDown(P(0, 4), MouseButton.Left); w.MouseUp(P(0, 4), MouseButton.Left);
        Pump(200);
        Assert.Contains(redact.Id, canvas.Selection);
        var down = w.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "▼ Nach hinten");
        down.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump(200);
        var layers = canvas.Page.Layers;
        Assert.True(layers.FindIndex(l => l.Id == redact.Id) < layers.FindIndex(l => l.Id == step.Id));
        Assert.Equal(step.Id, tl.Lanes[0].Id);
        var spec = OverlayRenderer.Prepare(new ProjectSession(new CaptureProject(), new Core.Serialization.AssetStore()), new VideoOverlay { Page = canvas.Page }, Path.Combine(Root, "ord-" + Guid.NewGuid().ToString("N")));
        Assert.True(spec.Effects.Single().Order < spec.Images.Single().Order);   // number drawn after the blackout
        w.Close();
    }

    [AvaloniaFact]
    public void Drawing_on_the_video_creates_a_timed_annotation_at_the_playhead()
    {
        if (MakeClip() is not { } clip) return;
        App.Settings.Shared.VideoAnnotationSeconds = 4;
        var w = OpenEditor(clip, new VideoOverlay());
        var canvas = w.GetVisualDescendants().OfType<EditorCanvas>().Single();
        // playhead to 3 s (Shift+→ = 1 s), rectangle tool (R)
        for (var i = 0; i < 3; i++) w.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Press(w, Key.R, PhysicalKey.R, "r");
        Pump(200);
        Assert.Equal(Tool.Rectangle, canvas.Tool);
        Point C(double x, double y) => canvas.TranslatePoint(new Point(x * canvas.ViewScale, y * canvas.ViewScale), w)!.Value;
        w.MouseDown(C(40, 40), MouseButton.Left);
        w.MouseMove(C(80, 70));
        w.MouseMove(C(140, 120));
        w.MouseUp(C(140, 120), MouseButton.Left);
        Pump(200);
        var shape = canvas.Page.Layers.OfType<ShapeLayer>().Single();
        Assert.NotNull(shape.Timing);
        Assert.InRange(shape.Timing!.StartSeconds, 2.9, 3.1);
        Assert.InRange(shape.Timing.EndSeconds, 6.9, 7.1);
        var tl = w.GetVisualDescendants().OfType<VideoTimeline>().Single();
        Assert.Single(tl.Lanes);
        // at 1 s the annotation is not visible (and not clickable)
        Press(w, Key.Escape, PhysicalKey.Escape, "");
        w.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);
        Pump(100);
        Assert.Empty(canvas.ViewPage().Layers);
        // marker pen: semi-transparent
        Press(w, Key.G, PhysicalKey.G, "g");
        Assert.Equal(Tool.Marker, canvas.Tool);
        w.MouseDown(C(20, 200), MouseButton.Left); w.MouseMove(C(100, 205)); w.MouseMove(C(200, 210)); w.MouseUp(C(200, 210), MouseButton.Left);
        Pump(200);
        var marker = canvas.Page.Layers.OfType<FreehandLayer>().Single();
        Assert.True(marker.Highlighter);
        Assert.True(marker.Stroke.A < 200);
        Assert.InRange(marker.Timing!.StartSeconds, -0.01, 0.01);
        w.Close();
    }

    [AvaloniaFact]
    public void Slides_are_edited_on_their_own_timeline()
    {
        if (MakeClip() is not { } clip) return;
        var overlay = new VideoOverlay();
        var w = OpenEditor(clip, overlay);
        var slide = w.AddSlide(2);              // summary: title + 3 checklist items
        Pump(200);
        var tl = w.GetVisualDescendants().OfType<VideoTimeline>().Single();
        Assert.NotNull(tl.SlideMode);
        Assert.Equal(4, tl.Lanes.Count);
        Assert.Equal(slide.Duration, tl.Duration, 2);
        var items = slide.Page.Layers.OfType<TextLayer>().Skip(1).Select(t => t.Timing!.StartSeconds).ToList();
        Assert.True(items.SequenceEqual(items.OrderBy(x => x)) && items[0] < items[^1]);   // appear one after another
        w.ExitSlide();
        Pump(200);
        Assert.Null(tl.SlideMode);
        Assert.Single(tl.Clips, c => c.IsSlide);
        Assert.Empty(overlay.Slides);                       // slides are clips of the sequence now
        Assert.Equal(16, w.Sequence.Duration, 1);           // 8 s video + 8 s summary slide
        w.Close();
    }

    [AvaloniaFact]
    public void Live_drawings_become_timed_annotations()
    {
        if (App.Ffmpeg is null) return;
        App.Settings.Shared.LiveAnnotationSeconds = 5;
        var rec = new ScreenRecorder(App.Ffmpeg, new RecordingOptions { Region = new CaptureDoc.Core.Model.PixelRect(0, 0, 400, 300), WorkDirectory = Path.Combine(Root, "live-" + Guid.NewGuid().ToString("N")) });
        var live = new LiveDrawLayer(App, rec, rec.EffectiveRegion);
        live.Show();
        Pump(200);
        Assert.False(live.DrawingActive);
        live.SetTool(Tool.Freehand);
        Assert.True(live.DrawingActive);
        var w = live.Window; var c = live.Canvas;
        Point P(double x, double y) => c.TranslatePoint(new Point(x * c.ViewScale, y * c.ViewScale), w)!.Value;
        w.MouseDown(P(20, 20), MouseButton.Left); w.MouseMove(P(80, 60)); w.MouseMove(P(150, 90)); w.MouseUp(P(150, 90), MouseButton.Left);
        live.SetTool(Tool.Arrow);
        w.MouseDown(P(200, 200), MouseButton.Left); w.MouseMove(P(250, 150)); w.MouseMove(P(300, 100)); w.MouseUp(P(300, 100), MouseButton.Left);
        Pump(100);
        Assert.Equal(2, live.Count);
        live.Undo();
        Assert.Equal(1, live.Count);
        live.SetTool(null);
        // recording clock 0 → video +1.5 s (e.g. ffmpeg start delay), video is 10 s long
        var layers = live.TakeLayers(t => t + 1.5, 10);
        live.Close();
        var l = Assert.Single(layers);
        Assert.IsType<FreehandLayer>(l);
        Assert.StartsWith("Live:", l.Name);
        Assert.Equal(1.5, l.Timing!.StartSeconds, 2);
        Assert.Equal(6.5, l.Timing.EndSeconds, 2);
    }

    [AvaloniaFact]
    public void Annotations_of_a_recording_are_kept_next_to_the_video()
    {
        if (MakeClip() is not { } clip) return;
        var dir = Path.Combine(Root, "sidecar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var video = Path.Combine(dir, "Aufnahme.mkv");
        File.Copy(clip, video);
        var w = new VideoEditorWindow(App, video) { Width = 1440, Height = 900 };
        w.Show();
        PumpUntil(() => Texts(w).Any(t => t.Contains("320×240")));
        Pump(300);
        var canvas = w.GetVisualDescendants().OfType<EditorCanvas>().Single();
        canvas.Apply("Test", () => canvas.Page.Layers.Add(new StepLayer { Center = new PointD(50, 50), Name = "Nummer", Timing = new LayerTiming { StartSeconds = 1, EndSeconds = 3 } }));
        w.AddSlide(1);
        w.ExitSlide();
        w.Close();
        Pump(200);
        var sidecar = VideoEditorWindow.SidecarPath(video);
        Assert.True(File.Exists(sidecar));
        var w2 = new VideoEditorWindow(App, video) { Width = 1440, Height = 900 };
        w2.Show();
        PumpUntil(() => Texts(w2).Any(t => t.Contains("320×240")));
        Pump(300);
        var tl = w2.GetVisualDescendants().OfType<VideoTimeline>().Single();
        Assert.Contains(tl.Lanes, l => l.Name == "Nummer");
        Assert.Single(tl.Clips, c => c.IsSlide);
        Assert.Contains(Texts(w2), t => t.Contains("Annotationen aus Aufnahme.capturedoc geladen"));
        w2.Close();
    }

    [AvaloniaFact]
    public async Task Imported_videos_can_be_appended_overlaid_and_moved_to_another_track()
    {
        if (MakeClip() is not { } clip) return;
        var overlay = new VideoOverlay { Page = new Core.Model.Page { Width = 320, Height = 240, Background = ColorRgba.Transparent } };
        var w = OpenEditor(clip, overlay);
        var seq = w.Sequence;
        await w.ImportVideoAsync("append", clip);
        Pump(200);
        Assert.Equal(16, seq.Duration, 1);
        Assert.Equal(2, seq.Tracks[0].Clips.Count);
        // picture-in-picture at 2 s on its own track
        for (var i = 0; i < 2; i++) w.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        await w.ImportVideoAsync("overlay", clip);
        Pump(200);
        Assert.Equal(2, seq.Tracks.Count);
        var pip = Assert.Single(seq.Tracks[1].Clips);
        Assert.Equal(2, pip.Start, 1);
        Assert.NotNull(pip.Rect);
        Assert.True(pip.Rect!.Value.Width < 160);
        // move the second main clip up onto a new track (overlay)
        var second = seq.Tracks[0].Clips[1];
        w.MoveClipToTrack(second, +1);
        Assert.Contains(second, seq.Tracks[1].Clips);
        Assert.Single(seq.Tracks[0].Clips);
        // the guide export renders all tracks
        var studio = new StudioWindow(App);
        var session = new ProjectSession(new CaptureProject(), new Core.Serialization.AssetStore());
        session.Project.Guide.Steps.Add(new GuideStep { Title = "Seq", Video = new VideoRef { Path = clip, Sequence = Core.Serialization.ProjectJson.Clone(seq), Overlay = overlay, MaxBytes = null } });
        var work = Path.Combine(Root, "seq-" + Guid.NewGuid().ToString("N"));
        var media = await Task.Run(() => VideoStepRenderer.RenderAllAsync(App, session, work, null));
        var info = await Task.Run(() => MediaProbe.ProbeAsync(App.Ffmpeg!, media.Values.Single().FilePath));
        Assert.InRange(info.Duration, 15.5, 16.5);
        w.Close(); studio.Close();
    }

    [AvaloniaFact]
    public void Properties_panel_scrolls_when_content_is_taller()
    {
        var session = new ProjectSession(new CaptureProject(), new Core.Serialization.AssetStore());
        var page = new Core.Model.Page { Width = 1124, Height = 658, Background = ColorRgba.White };
        var shape = new ShapeLayer { Shape = ShapeKind.Ellipse, Rect = new RectD(10, 10, 40, 40), Stroke = new ColorRgba(255, 200, 0), Name = "Klick 2" };
        page.Layers.Add(shape);
        session.Project.Pages.Add(page);
        var w = new EditorWindow(App, session, page, isStudioPage: true) { Width = 1590, Height = 848 };
        w.Show();
        var canvas = w.GetVisualDescendants().OfType<EditorCanvas>().Single();
        canvas.Select(new[] { shape.Id });
        Pump(200);
        w.UpdateLayout();
        var props = w.GetVisualDescendants().OfType<PropertiesPanel>().Single();
        Assert.True(props.Extent.Height > props.Viewport.Height, $"extent {props.Extent} viewport {props.Viewport}");
        var swatch = props.GetVisualDescendants().OfType<Button>().First(b => (ToolTip.GetTip(b) as string ?? "").StartsWith("#"));
        swatch.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(ToolTip.GetTip(swatch), shape.Stroke.ToHex());
        w.Close();
    }

    [AvaloniaFact]
    public async Task Video_step_with_a_slide_is_longer_by_the_slide()
    {
        if (MakeClip() is not { } clip) return;
        var session = new ProjectSession(new CaptureProject(), new Core.Serialization.AssetStore());
        var overlay = new VideoOverlay { Page = new Core.Model.Page { Width = 320, Height = 240, Background = ColorRgba.Transparent } };
        var sp = new Core.Model.Page { Width = 320, Height = 240, Background = new ColorRgba(0, 0, 200) };
        sp.Layers.Add(new TextLayer { Rect = new RectD(20, 20, 200, 40), Text = "Zusammenfassung", Foreground = ColorRgba.White, Timing = new LayerTiming { StartSeconds = 0.5, EndSeconds = 3 } });
        overlay.Slides.Add(new VideoSlide { At = 2, Duration = 3, Page = sp });
        session.Project.Guide.Steps.Add(new GuideStep { Title = "Video", Video = new VideoRef { Path = clip, Start = 1, End = 6, Overlay = overlay, MaxBytes = null } });
        var work = Path.Combine(Root, "work-" + Guid.NewGuid().ToString("N"));
        var media = await Task.Run(() => VideoStepRenderer.RenderAllAsync(App, session, work, null));
        var info = await Task.Run(() => MediaProbe.ProbeAsync(App.Ffmpeg!, media.Values.Single().FilePath));
        Assert.InRange(info.Duration, 7.5, 8.5);     // 5 s clip + 3 s slide
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

public class ScreenshotProbe
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Render_video_editor_to_png()
    {
        var outDir = Environment.GetEnvironmentVariable("CD_SHOT_DIR");
        if (string.IsNullOrEmpty(outDir)) return;
        UiTests.ShotVideoEditor(outDir);
    }
}
