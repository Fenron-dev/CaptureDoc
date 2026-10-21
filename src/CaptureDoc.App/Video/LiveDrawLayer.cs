using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CaptureDoc.App.Editor;
using CaptureDoc.App.Services;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Serialization;
using CaptureDoc.Media;
using Page = CaptureDoc.Core.Model.Page;

namespace CaptureDoc.App.Video;

/// <summary>
/// Drawing layer over the recorded area while recording. It is excluded from the capture itself:
/// every stroke becomes a normal, editable annotation with its time window and is rendered at export.
/// Pointer mode = click-through (the recorded app is operated normally).
/// </summary>
internal sealed class LiveDrawLayer
{
    private readonly AppServices _app;
    private readonly ScreenRecorder _rec;
    private readonly Window _win;
    private readonly Panel _host;
    private readonly EditorCanvas _canvas;
    private readonly InlineTextEditor _text;
    private readonly Canvas _textLayer = new();
    private readonly ProjectSession _session;
    private readonly Page _page;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public Tool? ActiveTool { get; private set; }
    public bool DrawingActive => ActiveTool is not null;
    public int Count => _page.Layers.Count;

    public LiveDrawLayer(AppServices app, ScreenRecorder rec, Core.Model.PixelRect r)
    {
        _app = app; _rec = rec;
        _page = new Page { Width = r.Width, Height = r.Height, Background = ColorRgba.Transparent };
        _session = new ProjectSession(new CaptureProject(), new AssetStore());
        var style = ProjectJson.Clone(app.Settings.Shared.Style);
        style.Shadow = false;
        _canvas = new EditorCanvas(_session, _page, style) { Zoom = 1 };
        _canvas.LayerFilter = Active;
        _canvas.LayerCreated += (_, l) =>
        {
            var t = Now;
            var secs = _app.Settings.Shared.LiveAnnotationSeconds;
            l.Timing = new LayerTiming { StartSeconds = Math.Round(t, 2), EndSeconds = secs > 0 ? Math.Round(t + secs, 2) : 0 };
        };
        _text = new InlineTextEditor(_canvas, _textLayer);
        _canvas.TextEditRequested += (_, t) => _text.Begin(t);
        _host = new Panel { Background = Brushes.Transparent };
        _host.Children.Add(_canvas);
        _host.Children.Add(_textLayer);
        _win = new Window
        {
            WindowDecorations = WindowDecorations.None, Topmost = true, ShowInTaskbar = false, CanResize = false, ShowActivated = false,
            Background = Brushes.Transparent, TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(r.X, r.Y), Width = 100, Height = 100,
            Content = _host, Title = "CaptureDoc Live-Zeichnen",
        };
        _win.Opened += (_, _) =>
        {
            var s = _win.RenderScaling;
            _win.Width = r.Width / s; _win.Height = r.Height / s;
            _win.Position = new PixelPoint(r.X, r.Y);
            if (OperatingSystem.IsWindows())
            {
                var h = _win.TryGetPlatformHandle()?.Handle ?? 0;
                CaptureDoc.Platform.Windows.WindowTools.ExcludeFromCapture(h);
                CaptureDoc.Platform.Windows.WindowTools.SetClickThrough(h, true);
            }
        };
        _tick.Tick += (_, _) => _canvas.InvalidateRender();   // drawings disappear after their time
    }

    public void Show() { _win.Show(); _tick.Start(); }

    internal Window Window => _win;
    internal EditorCanvas Canvas => _canvas;

    private double Now => _rec.Elapsed.TotalSeconds;

    private bool Active(Layer l)
    {
        if (l.Timing is not { } tm) return true;
        var now = Now;
        return now >= tm.StartSeconds - 0.05 && (tm.EndSeconds <= tm.StartSeconds || now <= tm.EndSeconds);
    }

    /// <summary>null = pointer: clicks go to the recorded application.</summary>
    public void SetTool(Tool? t)
    {
        _text.Commit();
        ActiveTool = t;
        if (t is { } tool) _canvas.Tool = tool;
        // a nearly transparent background is needed so the window receives clicks on "empty" pixels
        _host.Background = t is null ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        if (OperatingSystem.IsWindows())
            CaptureDoc.Platform.Windows.WindowTools.SetClickThrough(_win.TryGetPlatformHandle()?.Handle ?? 0, t is null);
        if (t is not null) _win.Activate();
    }

    public void SetColor(ColorRgba c) => _canvas.Style.Color = c;
    public double StrokeWidth { get => _canvas.Style.StrokeWidth; set => _canvas.Style.StrokeWidth = Math.Clamp(value, 1, 40); }
    public void Undo() => _canvas.Undo();

    /// <summary>Ends all drawings that are visible now.</summary>
    public void Clear()
    {
        var now = Math.Round(Now, 2);
        foreach (var l in _page.Layers.Where(Active))
            if (l.Timing is { } tm) tm.EndSeconds = Math.Max(tm.StartSeconds + 0.2, now);
        _canvas.InvalidateRender();
    }

    public void Close()
    {
        _text.Commit();
        _tick.Stop();
        _win.Close();
    }

    /// <summary>Drawings with times converted to video seconds (<paramref name="map"/> = recording clock → video).</summary>
    public List<Layer> TakeLayers(Func<double, double> map, double videoDuration)
    {
        var list = new List<Layer>();
        var n = 0;
        foreach (var l in _page.Layers)
        {
            var c = ProjectJson.Clone(l);
            var tm = c.Timing ?? new LayerTiming();
            var s = map(tm.StartSeconds);
            var e = tm.EndSeconds > tm.StartSeconds ? map(tm.EndSeconds) : videoDuration;
            c.Timing = new LayerTiming { StartSeconds = Math.Round(s, 2), EndSeconds = Math.Round(Math.Max(s + 0.2, Math.Min(videoDuration, e)), 2) };
            c.Name = $"Live: {(string.IsNullOrWhiteSpace(c.Name) ? c.Kind : c.Name)}";
            list.Add(c);
            n++;
        }
        _session.Dispose();
        return list;
    }
}
