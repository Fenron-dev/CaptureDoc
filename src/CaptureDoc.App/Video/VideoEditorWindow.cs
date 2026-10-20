using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CaptureDoc.App.Editor;
using CaptureDoc.App.Services;
using CaptureDoc.App.Util;
using CaptureDoc.App.Views;
using CaptureDoc.Media;
using CaptureDoc.Rendering;

namespace CaptureDoc.App.Video;

/// <summary>
/// Lean timeline editor: preview, play, set start/end, cut out sections, export MP4/GIF, grab frames.
/// The source file is never modified; only low-resolution preview frames are held in memory.
/// </summary>
public sealed class VideoEditorWindow : Window
{
    private readonly AppServices _app;
    private readonly string _file;
    private readonly FfmpegTools _tools;
    private readonly VideoExporter _exporter;
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly TimelineControl _timeline = new();
    private readonly TextBlock _info = Ui.Label("Lade Video …", 12, opacity: 0.8);
    private readonly TextBlock _timeLabel = Ui.Label("0:00.0", 14);
    private readonly TextBlock _rangeLabel = Ui.Label("", 12);
    private readonly Button _play;
    private readonly DispatcherTimer _playTimer = new();
    private readonly ClipRange _clip = new();
    private List<Bitmap> _frames = new();
    private double _previewFps = 10, _pos;
    private double? _cutA;
    private MediaInfo? _media;
    private CancellationTokenSource? _hiResCts;

    public VideoEditorWindow(AppServices app, string file)
    {
        _app = app; _file = file; _tools = app.Ffmpeg!; _exporter = new VideoExporter(_tools);
        Title = Path.GetFileName(file) + " – CaptureDoc Video";
        Icon = IconFactory.WindowIcon;
        Width = 1100; Height = 760; MinWidth = 760; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _play = Ui.Button("▶", TogglePlay, "Abspielen / Pause (Leertaste)");
        _playTimer.Tick += (_, _) => Step();
        _timeline.Seek += (_, t) => SeekTo(t, hiRes: !_playTimer.IsEnabled);

        var top = new WrapPanel { Margin = new Thickness(8, 6) };
        top.Children.Add(Ui.Button("⇩ MP4…", async () => await ExportAsync(gif: false), "Als MP4 exportieren (Größe, Qualität, Dateigrößenlimit)", accent: true));
        top.Children.Add(Ui.Button("⇩ GIF…", async () => await ExportAsync(gif: true), "Als GIF exportieren – mit geprüftem Größenlimit", accent: true));
        top.Children.Add(new Separator { Width = 12 });
        top.Children.Add(Ui.Button("🖼 Frame → Editor", async () => await FrameToEditorAsync(), "Aktuelles Bild als Screenshot im Editor bearbeiten"));
        top.Children.Add(Ui.Button("➕ Frame → Anleitung", async () => await FrameToGuideAsync(), "Aktuelles Bild als Schritt ins Documentation Studio"));
        top.Children.Add(Ui.Button("📂 Ordner", () => OpenFolder(Path.GetDirectoryName(_file)!), "Speicherort der Aufnahme öffnen"));

        var transport = new WrapPanel { Margin = new Thickness(8, 4) };
        transport.Children.Add(_play);
        transport.Children.Add(_timeLabel);
        transport.Children.Add(new Separator { Width = 12 });
        transport.Children.Add(Ui.Button("[ Start hier", () => SetRange(start: true), "Clip beginnt an der aktuellen Position (I)"));
        transport.Children.Add(Ui.Button("Ende hier ]", () => SetRange(start: false), "Clip endet an der aktuellen Position (O)"));
        transport.Children.Add(Ui.Button("✂ Schnitt von/bis", MarkCut, "Erster Klick: Beginn, zweiter Klick: Ende des zu entfernenden Abschnitts (X)"));
        transport.Children.Add(Ui.Button("↺ Schnitte zurücksetzen", () => { _clip.Cuts.Clear(); _clip.Start = 0; _clip.End = _media?.Duration ?? 0; _cutA = null; Refresh(); }));
        transport.Children.Add(new Separator { Width = 12 });
        transport.Children.Add(_rangeLabel);

        var bottom = Ui.Column(4, _timeline, transport, _info);
        bottom.Margin = new Thickness(8);
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        root.Children.Add(new Border { Child = _preview, Background = new SolidColorBrush(Color.FromRgb(20, 20, 22)), Margin = new Thickness(8, 0) });
        Content = root;

        KeyDown += OnKey;
        Opened += async (_, _) => { StudioWindow.FitToScreen(this); await LoadAsync(); };
        Closed += (_, _) => { _playTimer.Stop(); foreach (var f in _frames) f.Dispose(); };
    }

    private async Task LoadAsync()
    {
        try
        {
            _media = await MediaProbe.ProbeAsync(_tools, _file);
            _clip.Start = 0; _clip.End = _media.Duration;
            _timeline.Duration = _media.Duration;
            // keep preview memory small: ≤ ~1500 frames
            _previewFps = Math.Clamp(1500 / Math.Max(1, _media.Duration), 1, Math.Min(15, Math.Max(1, _media.Fps)));
            var width = Math.Min(640, _media.Width);
            _info.Text = $"{_media.Width}×{_media.Height} · {_media.Fps:0.#} fps · {TimelineControl.Fmt(_media.Duration)} · {SizeText(new FileInfo(_file).Length)} · {_file}  –  Vorschau wird erstellt …";
            var jpegs = await Task.Run(() => _exporter.ExtractPreviewFramesAsync(_file, _previewFps, width));
            _frames = jpegs.Select(j => new Bitmap(new MemoryStream(j))).ToList();
            _timeline.Thumbs = _frames;
            _info.Text = $"{_media.Width}×{_media.Height} · {_media.Fps:0.#} fps · {TimelineControl.Fmt(_media.Duration)} · {SizeText(new FileInfo(_file).Length)}{(_media.HasAudio ? " · mit Ton" : "")} · {_file}";
            SeekTo(0, hiRes: true);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException)
        {
            _info.Text = "⚠ " + ex.Message;
        }
    }

    private static string SizeText(long b) => Core.Export.SizeOptimizer.FormatBytes(b);

    private void Refresh()
    {
        _timeline.Position = _pos; _timeline.Clip = _clip;
        _timeline.InvalidateVisual();
        _timeLabel.Text = TimelineControl.Fmt(_pos) + " / " + TimelineControl.Fmt(_media?.Duration ?? 0);
        _rangeLabel.Text = $"Clip {TimelineControl.Fmt(_clip.Start)} – {TimelineControl.Fmt(_clip.End)}, {_clip.Cuts.Count} Schnitt(e) → Ergebnis {_clip.KeptDuration:0.0} s" +
                           (_cutA is { } a ? $"   ✂ Schnitt ab {TimelineControl.Fmt(a)} – Ende markieren …" : "");
    }

    private void SeekTo(double t, bool hiRes)
    {
        if (_media is null) return;
        _pos = Math.Clamp(t, 0, _media.Duration);
        if (_frames.Count > 0) _preview.Source = _frames[Math.Clamp((int)(_pos * _previewFps), 0, _frames.Count - 1)];
        Refresh();
        if (hiRes) _ = LoadHiResAsync(_pos);
    }

    /// <summary>When paused/scrubbing, show the sharp original frame.</summary>
    private async Task LoadHiResAsync(double t)
    {
        _hiResCts?.Cancel();
        var cts = _hiResCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(120, cts.Token);
            var png = await Task.Run(() => _exporter.ExtractFrameAsync(_file, t, cts.Token), cts.Token);
            if (!cts.IsCancellationRequested && !_playTimer.IsEnabled) _preview.Source = new Bitmap(new MemoryStream(png));
        }
        catch (OperationCanceledException) { }
        catch (InvalidDataException) { }
    }

    private void TogglePlay()
    {
        if (_playTimer.IsEnabled) { _playTimer.Stop(); _play.Content = "▶"; SeekTo(_pos, true); return; }
        if (_pos >= _clip.End - 0.05) _pos = _clip.Start;
        _playTimer.Interval = TimeSpan.FromSeconds(1 / _previewFps);
        _playTimer.Start();
        _play.Content = "❚❚";
    }

    private void Step()
    {
        var next = _pos + 1 / _previewFps;
        foreach (var (a, b) in _clip.Cuts) if (next >= a && next < b) next = b; // preview skips removed sections
        if (next >= _clip.End) { TogglePlay(); SeekTo(_clip.End, true); return; }
        SeekTo(next, false);
    }

    private void SetRange(bool start)
    {
        if (start) _clip.Start = Math.Min(_pos, _clip.End - 0.1); else _clip.End = Math.Max(_pos, _clip.Start + 0.1);
        Refresh();
    }

    private void MarkCut()
    {
        if (_cutA is null) { _cutA = _pos; Refresh(); return; }
        var a = Math.Min(_cutA.Value, _pos); var b = Math.Max(_cutA.Value, _pos);
        _cutA = null;
        if (b - a >= 0.05) _clip.Cuts.Add((a, b));
        Refresh();
    }

    private void OnKey(object? s, KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is TextBox) return;
        var frame = 1 / Math.Max(1, _media?.Fps ?? 30);
        switch (e.Key)
        {
            case Key.Space: TogglePlay(); break;
            case Key.Left: SeekTo(_pos - (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : frame), true); break;
            case Key.Right: SeekTo(_pos + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : frame), true); break;
            case Key.Home: SeekTo(_clip.Start, true); break;
            case Key.End: SeekTo(_clip.End, true); break;
            case Key.I: SetRange(true); break;
            case Key.O: SetRange(false); break;
            case Key.X: MarkCut(); break;
            default: return;
        }
        e.Handled = true;
    }

    private async Task<(SkiaSharp.SKImage img, byte[] png)?> GrabFrameAsync()
    {
        try
        {
            var png = await Task.Run(() => _exporter.ExtractFrameAsync(_file, _pos));
            var img = SkiaSharp.SKImage.FromEncodedData(png);
            return img is null ? null : (img, png);
        }
        catch (InvalidDataException ex) { await Dialogs.InfoAsync(this, "Frame", ex.Message); return null; }
    }

    private async Task FrameToEditorAsync()
    {
        if (await GrabFrameAsync() is not { } f) return;
        var session = ProjectSession.FromCapture(f.img, f.png, $"{Path.GetFileNameWithoutExtension(_file)} {TimelineControl.Fmt(_pos).Replace(':', '-')}");
        new EditorWindow(_app, session, session.Project.Pages[0]).Show();
    }

    private async Task FrameToGuideAsync()
    {
        if (await GrabFrameAsync() is not { } f) return;
        var studio = App.Current.ShowStudio();
        studio.AddCapturedImage(f.img, f.png, $"Frame {TimelineControl.Fmt(_pos)}");
    }

    private async Task ExportAsync(bool gif)
    {
        if (_media is null) return;
        if (_playTimer.IsEnabled) TogglePlay();
        var clip = new ClipRange { Start = _clip.Start, End = _clip.End, Cuts = _clip.Cuts.ToList() };
        var dlg = new VideoExportDialog(_app, _exporter, _file, _media, clip, gif);
        await dlg.ShowDialog(this);
    }

    private static void OpenFolder(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Open folder: " + ex.Message); }
    }
}
