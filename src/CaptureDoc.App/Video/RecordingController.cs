using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CaptureDoc.App.Editor;
using CaptureDoc.App.Services;
using CaptureDoc.App.Util;
using CaptureDoc.App.Views;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Portable;
using CaptureDoc.Media;
using AvPixelRect = Avalonia.PixelRect;

namespace CaptureDoc.App.Video;

/// <summary>
/// Screen recording workflow: select area → countdown → visible frame + control bar → stop → video editor.
/// Recording only happens while the red frame and the timer are visible (no hidden recording).
/// </summary>
public sealed class RecordingController
{
    private readonly AppServices _app;
    private bool _busy;

    public RecordingController(AppServices app) => _app = app;

    public async Task StartAsync()
    {
        if (_busy) return;
        if (_app.Ffmpeg is not { } tools) { await ShowMissingFfmpegAsync(null); return; }
        if (!_app.Capture.IsSupported) { await Dialogs.InfoAsync(null, "Nicht verfügbar", "Videoaufnahme ist auf diesem Betriebssystem noch nicht implementiert."); return; }
        _busy = true;
        var hidden = App.Current.OpenWindows.Where(w => w.IsVisible).ToList();
        try
        {
            foreach (var w in hidden) w.Hide();
            await Task.Delay(220);
            var windows = _app.Capture.GetWindows();
            var frozen = _app.Capture.CaptureVirtualScreen(false);
            var area = await RegionSelector.SelectAsync(frozen, windows, SelectorMode.RegionOrWindow, App.Current.AnyWindow);
            if (area is not { } region || region.Width < 16 || region.Height < 16)
            {
                foreach (var w in hidden) w.Show();
                return;
            }
            var opt = _app.Settings.Platform;
            if (opt.RecordingCountdown > 0) await CountdownWindow.RunAsync(opt.RecordingCountdown);

            if (OperatingSystem.IsMacOS())
            {
                // avfoundation records a whole screen; area is cropped relative to that screen (untested on real hardware)
                ScreenRecorder.MacScreenDevice = await MediaProbe.FindMacScreenDeviceAsync(tools) ?? 1;
                var primary = _app.Capture.GetMonitors().FirstOrDefault(m => m.IsPrimary);
                ScreenRecorder.MacScreenOrigin = primary is null ? (0, 0) : (primary.Bounds.X, primary.Bounds.Y);
            }
            var work = Path.Combine(_app.Env.TempDir, $"rec-{DateTime.Now:yyyyMMdd-HHmmss}");
            string? mic = null;
            if (opt.RecordMicrophone && !string.IsNullOrWhiteSpace(opt.MicrophoneDevice)) mic = opt.MicrophoneDevice;
            var recorder = new ScreenRecorder(tools, new RecordingOptions
            {
                Region = region, Fps = Math.Clamp(opt.RecordingFps, 5, 60), DrawMouse = opt.IncludeCursor || opt.RecordCursor, Microphone = mic, WorkDirectory = work,
            });
            var session = new RecordingSessionUi(_app, recorder);
            var file = await session.RunAsync();
            foreach (var w in hidden.Where(w => w is not EditorWindow)) w.Show();
            if (file is not null)
            {
                Log.Info($"Recording saved {region.Width}x{region.Height}");
                var ed = new VideoEditorWindow(_app, file, clicks: session.MappedClicks, liveLayers: session.LiveLayers);
                ed.Show();
                ed.Activate();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Recording failed", ex);
            foreach (var w in hidden) w.Show();
            await Dialogs.InfoAsync(null, "Aufnahme fehlgeschlagen", ex.Message);
        }
        finally { _busy = false; }
    }

    public static Task ShowMissingFfmpegAsync(Window? owner) => Dialogs.InfoAsync(owner, "FFmpeg nicht gefunden",
        "Für Video und GIF benötigt CaptureDoc FFmpeg (wird als externes Programm genutzt, nicht mitgeliefert).\n\n" +
        "Portabel: ffmpeg.exe und ffprobe.exe nach  Runtime\\ffmpeg\\  im CaptureDoc-Ordner kopieren.\n" +
        "Alternativ: FFmpeg installieren (im PATH) oder Umgebungsvariable CAPTUREDOC_FFMPEG setzen.\n\n" +
        "Lizenzhinweis: FFmpeg-Builds stehen unter LGPL bzw. GPL – bei Weitergabe Lizenz beilegen (siehe THIRD_PARTY_LICENSES.md).");
}

/// <summary>Red frame around the recorded area + floating control bar. Both are excluded from the recording.</summary>
internal sealed class RecordingSessionUi
{
    private readonly AppServices _app;
    private readonly ScreenRecorder _rec;
    private readonly List<Window> _frame = new();
    private Window? _bar;
    private readonly TaskCompletionSource<string?> _done = new();
    private TextBlock? _time, _state;
    private Button? _pause;
    private DispatcherTimer? _timer;
    private IDisposable? _hook;
    private LiveDrawLayer? _live;
    /// <summary>Clicks inside the area: recording clock (s) and position relative to the area.</summary>
    public List<(double T, int X, int Y)> Clicks { get; } = new();
    /// <summary>After stop: clicks in video seconds (ffmpeg start delay removed).</summary>
    public List<(double T, int X, int Y)> MappedClicks { get; private set; } = new();
    /// <summary>After stop: live drawings as timed annotations (video seconds).</summary>
    public List<Core.Model.Layer> LiveLayers { get; private set; } = new();

    public RecordingSessionUi(AppServices app, ScreenRecorder rec) { _app = app; _rec = rec; }

    public async Task<string?> RunAsync()
    {
        ShowFrame(_rec.EffectiveRegion);
        // the drawing window must be excluded from the capture – only implemented for Windows so far
        if (_app.Settings.Shared.LiveToolbar && OperatingSystem.IsWindows())
        {
            _live = new LiveDrawLayer(_app, _rec, _rec.EffectiveRegion);
            _live.Show();
        }
        ShowBar(_rec.EffectiveRegion);
        _rec.StateChanged += (_, _) => Dispatcher.UIThread.Post(UpdateUi);
        if (OperatingSystem.IsWindows() && _app.Settings.Platform.HighlightClicks)
        {
            var hook = new CaptureDoc.Platform.Windows.WindowsMouseHook();
            var area = _rec.EffectiveRegion;
            hook.Click += (x, y, _) =>
            {
                if (_rec.State != RecorderState.Recording || !area.Contains(x, y) || _live?.DrawingActive == true) return;
                lock (Clicks) Clicks.Add((_rec.Elapsed.TotalSeconds, x - area.X, y - area.Y));
            };
            _hook = hook;
        }
        _rec.Start();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => UpdateUi();
        _timer.Start();
        try { return await _done.Task; }
        finally
        {
            _hook?.Dispose();
            _timer.Stop();
            _live?.Close();
            foreach (var w in _frame) w.Close();
            _bar?.Close();
        }
    }

    private void UpdateUi()
    {
        if (_time is null || _state is null || _pause is null) return;
        var t = _rec.Elapsed;
        _time.Text = $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
        (_state.Text, _state.Foreground) = _rec.State switch
        {
            RecorderState.Recording => ("● REC", (IBrush)new SolidColorBrush(Color.FromRgb(255, 70, 70))),
            RecorderState.Paused => ("❚❚ Pause", Brushes.Orange),
            RecorderState.Failed => ("⚠ Fehler", Brushes.Orange),
            _ => ("■", Brushes.Gray),
        };
        _pause.Content = _rec.State == RecorderState.Paused ? "▶ Weiter" : "❚❚ Pause";
        if (_frameBorder is not null) _frameBorder.BorderBrush = _rec.State == RecorderState.Paused ? Brushes.Orange : new SolidColorBrush(Color.FromRgb(230, 30, 40));
        if (_rec.State == RecorderState.Failed && !_done.Task.IsCompleted)
        {
            var err = _rec.Error;
            _ = FinishAsync(save: true, err);
        }
    }

    private static void Exclude(Window w, bool clickThrough)
    {
        if (!OperatingSystem.IsWindows()) return;
        var h = w.TryGetPlatformHandle()?.Handle ?? 0;
        CaptureDoc.Platform.Windows.WindowTools.ExcludeFromCapture(h);
        if (clickThrough) CaptureDoc.Platform.Windows.WindowTools.MakeClickThrough(h);
    }

    private Border? _frameBorder;

    /// <summary>
    /// One transparent, click-through window slightly larger than the area; only its border is painted,
    /// which lies OUTSIDE the recorded pixels. (Thin separate windows hit the Windows minimum window height.)
    /// </summary>
    private void ShowFrame(Core.Model.PixelRect r)
    {
        const int t = 3;
        _frameBorder = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(230, 30, 40)), Background = Brushes.Transparent };
        var w = new Window
        {
            WindowDecorations = WindowDecorations.None, Topmost = true, ShowInTaskbar = false, CanResize = false, ShowActivated = false,
            Background = Brushes.Transparent, TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(r.X - t, r.Y - t), Width = 100, Height = 100,
            Content = _frameBorder, Title = "CaptureDoc Rahmen",
        };
        w.Opened += (_, _) =>
        {
            var s = w.RenderScaling;
            w.Width = (r.Width + 2 * t) / s; w.Height = (r.Height + 2 * t) / s;
            w.Position = new PixelPoint(r.X - t, r.Y - t);
            _frameBorder.BorderThickness = new Thickness(t / s);
            Exclude(w, clickThrough: true);
        };
        _frame.Add(w);
        w.Show();
    }

    private void ShowBar(Core.Model.PixelRect r)
    {
        _time = new TextBlock { Text = "00:00", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas, monospace") };
        _state = new TextBlock { Text = "● REC", FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        _pause = Ui.Button("❚❚ Pause", async () =>
        {
            if (_rec.State == RecorderState.Recording) await _rec.PauseAsync(); else _rec.Resume();
            UpdateUi();
        }, "Pausieren / Fortsetzen – während der Pause wird nichts aufgenommen");
        var stop = Ui.Button("■ Stopp", async () => await FinishAsync(true, null), "Aufnahme beenden und im Video-Editor öffnen", accent: true);
        var cancel = Ui.Button("✕", async () => await FinishAsync(false, null), "Abbrechen und verwerfen");
        var main = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _state, _time, _pause, stop, cancel } };
        var content = new StackPanel { Spacing = 6, Margin = new Thickness(10, 6), Children = { main } };
        if (_live is not null) content.Children.Add(BuildLiveTools());
        var bar = new Window
        {
            WindowDecorations = WindowDecorations.None, Topmost = true, ShowInTaskbar = false, CanResize = false, SizeToContent = SizeToContent.WidthAndHeight,
            Background = new SolidColorBrush(Color.FromArgb(235, 25, 25, 30)), WindowStartupLocation = WindowStartupLocation.Manual, Title = "CaptureDoc Aufnahme",
            Content = content, Opacity = Math.Clamp(_app.Settings.Shared.LiveToolbarOpacity, 0.3, 1),
        };
        bar.Opened += (_, _) =>
        {
            Exclude(bar, clickThrough: false);
            // size is only known after the first layout pass
            Dispatcher.UIThread.Post(() => PlaceBar(bar, r), DispatcherPriority.Background);
        };
        _bar = bar;
        bar.Show();
    }

    /// <summary>Live drawing tools (which ones: settings). Drawings are not part of the raw video but editable annotations.</summary>
    private Control BuildLiveTools()
    {
        var live = _live!;
        var enabled = _app.Settings.Shared.LiveTools.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var group = new List<(ToggleButton Button, CaptureDoc.App.Editor.Tool? Tool)>();
        void ToolBtn(string id, string glyph, string tip, CaptureDoc.App.Editor.Tool? tool)
        {
            if (id != "pointer" && !enabled.Contains(id)) return;
            var b = new ToggleButton { Content = glyph, MinWidth = 34, Padding = new Thickness(6, 2), Margin = new Thickness(1), FontSize = 15 };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => { live.SetTool(tool); foreach (var (bb, tt) in group) bb.IsChecked = tt == tool; };
            group.Add((b, tool));
            row.Children.Add(b);
        }
        ToolBtn("pointer", "🖱", "Zeiger – das aufgenommene Programm normal bedienen (Zeichnen aus)", null);
        ToolBtn("pen", "✎", "Stift", CaptureDoc.App.Editor.Tool.Freehand);
        ToolBtn("marker", "🖍", "Textmarker", CaptureDoc.App.Editor.Tool.Marker);
        ToolBtn("arrow", "➜", "Pfeil", CaptureDoc.App.Editor.Tool.Arrow);
        ToolBtn("rect", "▭", "Rahmen", CaptureDoc.App.Editor.Tool.Rectangle);
        ToolBtn("ellipse", "◯", "Kreis", CaptureDoc.App.Editor.Tool.Ellipse);
        ToolBtn("step", "①", "Nummer", CaptureDoc.App.Editor.Tool.Step);
        ToolBtn("text", "T", "Text", CaptureDoc.App.Editor.Tool.Text);
        group[0].Button.IsChecked = true;
        row.Children.Add(new Border { Width = 8 });
        foreach (var c in new[] { Core.Model.ColorRgba.Red, Core.Model.ColorRgba.Yellow, new Core.Model.ColorRgba(40, 170, 70), Core.Model.ColorRgba.Blue, Core.Model.ColorRgba.White })
        {
            var cc = c;
            var b = new Button { Width = 20, Height = 20, Margin = new Thickness(1), Padding = new Thickness(0), Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(b, "Farbe");
            b.Click += (_, _) => live.SetColor(cc);
            row.Children.Add(b);
        }
        var width = new TextBlock { Text = $"{live.StrokeWidth:0}", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0), MinWidth = 16 };
        row.Children.Add(new Border { Width = 6 });
        row.Children.Add(Ui.Button("−", () => { live.StrokeWidth -= 1; width.Text = $"{live.StrokeWidth:0}"; }, "Dünner"));
        row.Children.Add(width);
        row.Children.Add(Ui.Button("+", () => { live.StrokeWidth += 1; width.Text = $"{live.StrokeWidth:0}"; }, "Dicker"));
        if (enabled.Contains("undo")) row.Children.Add(Ui.Button("↶", live.Undo, "Letzte Zeichnung rückgängig"));
        if (enabled.Contains("clear")) row.Children.Add(Ui.Button("🧹", live.Clear, "Alle sichtbaren Zeichnungen ausblenden (ab jetzt)"));
        return row;
    }

    private static void PlaceBar(Window bar, Core.Model.PixelRect r)
    {
        {
            var s = bar.RenderScaling;
            var size = bar.ClientSize.Width > 1 ? bar.ClientSize : (bar.Content as Control)?.DesiredSize ?? new Size(420, 48);
            var bw = (int)(size.Width * s); var bh = (int)(size.Height * s);
            var screen = bar.Screens.ScreenFromPoint(new PixelPoint(r.X + r.Width / 2, r.Y + r.Height / 2)) ?? bar.Screens.Primary;
            var wa = screen?.WorkingArea ?? new AvPixelRect(0, 0, 1920, 1080);
            var x = Math.Clamp(r.X + r.Width / 2 - bw / 2, wa.X, wa.Right - bw);
            // below the frame, else above, else inside top edge (it is excluded from the recording anyway)
            var y = r.Bottom + 10 + bh <= wa.Bottom ? r.Bottom + 10 : r.Y - 10 - bh >= wa.Y ? r.Y - 10 - bh : r.Y + 10;
            bar.Position = new PixelPoint(x, y);
        }
    }

    private async Task FinishAsync(bool save, string? error)
    {
        if (_done.Task.IsCompleted) return;
        if (!save)
        {
            await _rec.CancelAsync();
            _done.TrySetResult(null);
            return;
        }
        try
        {
            var dir = Path.Combine(_app.Env.MediaDir, "Aufnahmen");
            var file = Path.Combine(dir, FileNamePattern.Format("Aufnahme_{yyyy-MM-dd_HH-mm-ss}", DateTime.Now) + ".mkv");
            var result = await _rec.StopAsync(file);
            _rec.Cleanup();
            double duration = 0;
            try { duration = (await MediaProbe.ProbeAsync(_app.Ffmpeg!, result)).Duration; } catch (InvalidDataException) { }
            lock (Clicks) MappedClicks = Clicks.Select(c => (_rec.MapToVideoTime(c.T), c.X, c.Y)).ToList();
            if (_live is not null) LiveLayers = _live.TakeLayers(_rec.MapToVideoTime, duration);
            if (error is not null) await Dialogs.InfoAsync(null, "Aufnahme unterbrochen", error + "\n\nDas bis dahin Aufgenommene wurde gespeichert.");
            _done.TrySetResult(result);
        }
        catch (Exception ex)
        {
            Log.Error("Stop recording", ex);
            _rec.Cleanup();
            await Dialogs.InfoAsync(null, "Aufnahme fehlgeschlagen", ex.Message);
            _done.TrySetResult(null);
        }
    }
}
