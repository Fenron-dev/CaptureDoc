using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CaptureDoc.App.Editor;
using CaptureDoc.App.Services;
using CaptureDoc.App.Util;
using CaptureDoc.App.Views;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Serialization;
using CaptureDoc.Media;
using CaptureDoc.Rendering;
using Page = CaptureDoc.Core.Model.Page;

namespace CaptureDoc.App.Video;

/// <summary>
/// Lean timeline editor: preview, play, start/end, cut out sections, time-based annotations (same layer
/// editor as screenshots), zoom/pan keyframes, export MP4/GIF, frames and clips into the guide.
/// The source file is never modified; only low-resolution preview frames are held in memory.
/// </summary>
public sealed class VideoEditorWindow : Window
{
    private readonly AppServices _app;
    private string _file;
    private readonly FfmpegTools _tools;
    private readonly VideoExporter _exporter;
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly Image _overlayImage = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    private readonly TimelineControl _timeline = new();
    private readonly TextBlock _info = Ui.Label("Lade Video …", 12, opacity: 0.8);
    private readonly TextBlock _timeLabel = Ui.Label("0:00.0", 14);
    private readonly TextBlock _rangeLabel = Ui.Label("", 12);
    private readonly Button _play;
    private readonly DispatcherTimer _playTimer = new();
    private readonly ClipRange _clip = new();
    private readonly WrapPanel _cutList = new();
    private readonly StackPanel _side = new() { Spacing = 6, Margin = new Thickness(6) };
    private readonly Slider _zoomFactor = new() { Minimum = 1, Maximum = 4, Value = 2, Width = 140 };
    private List<Bitmap> _frames = new();
    private double _previewFps = 10, _pos;
    private double? _cutA;
    private MediaInfo? _media;
    private CancellationTokenSource? _hiResCts;
    private (double X, double Y)? _zoomCenter;

    // annotations: belong to the guide step (studio session) or live in an in-memory session
    private readonly ProjectSession _overlaySession;
    private readonly bool _ownsSession;
    private readonly VideoOverlay _overlay;
    private readonly GuideStep? _step;
    private readonly StudioWindow? _studio;

    private readonly IReadOnlyList<(double T, int X, int Y)>? _clicks;

    public VideoEditorWindow(AppServices app, string file, GuideStep? step = null, StudioWindow? studio = null, IReadOnlyList<(double, int, int)>? clicks = null)
    {
        _clicks = clicks;
        _app = app; _file = file; _tools = app.Ffmpeg!; _exporter = new VideoExporter(_tools);
        _step = step; _studio = studio;
        if (studio is not null) _overlaySession = studio.Session;
        else { _overlaySession = new ProjectSession(new CaptureProject { Title = "Video-Annotationen" }, new AssetStore()); _ownsSession = true; }
        _overlay = step?.Video?.Overlay is { } existing ? existing : new VideoOverlay();
        if (step?.Video is { } v) { _clip.Start = v.Start; _clip.End = v.End; _clip.Cuts = VideoStepRenderer.ToClip(v).Cuts; }

        Title = Path.GetFileName(file) + (step is not null ? $" – Schritt „{step.Title}“" : "") + " – CaptureDoc Video";
        Icon = IconFactory.WindowIcon;
        Width = 1320; Height = 820; MinWidth = 900; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _play = Ui.Button("▶", TogglePlay, "Abspielen / Pause (Leertaste)");
        _playTimer.Tick += (_, _) => Step();
        _timeline.Seek += (_, t) => SeekTo(t, hiRes: !_playTimer.IsEnabled);

        var top = new WrapPanel { Margin = new Thickness(8, 6) };
        top.Children.Add(Ui.Button("⇩ MP4…", async () => await ExportAsync(gif: false), "Als MP4 exportieren (inkl. Annotationen und Zoom)", accent: true));
        top.Children.Add(Ui.Button("⇩ GIF…", async () => await ExportAsync(gif: true), "Als GIF exportieren – mit geprüftem Größenlimit", accent: true));
        top.Children.Add(new Separator { Width = 12 });
        if (step is not null)
            top.Children.Add(Ui.Button("✓ In Anleitung übernehmen", () => { WriteBackToStep(); Close(); }, "Clip, Schnitte, Annotationen und Zoom im Anleitungsschritt speichern", accent: true));
        else
            top.Children.Add(Ui.Button("🎬 Video → Anleitung", async () => await VideoToGuideAsync(), "Diesen Clip (mit Schnitten und Annotationen) als Video-Schritt ins Documentation Studio"));
        top.Children.Add(Ui.Button("🖼 Frame → Editor", async () => await FrameToEditorAsync(), "Aktuelles Bild als Screenshot im Editor bearbeiten"));
        top.Children.Add(Ui.Button("➕ Frame → Anleitung", async () => await FrameToGuideAsync(), "Aktuelles Bild als Bild-Schritt ins Documentation Studio"));
        top.Children.Add(Ui.Button("📂 Ordner", () => OpenFolder(Path.GetDirectoryName(_file)!), "Speicherort öffnen"));

        var transport = new WrapPanel { Margin = new Thickness(8, 4) };
        transport.Children.Add(_play);
        transport.Children.Add(_timeLabel);
        transport.Children.Add(new Separator { Width = 12 });
        transport.Children.Add(Ui.Button("[ Start hier", () => SetRange(start: true), "Clip beginnt an der aktuellen Position (I)"));
        transport.Children.Add(Ui.Button("Ende hier ]", () => SetRange(start: false), "Clip endet an der aktuellen Position (O)"));
        transport.Children.Add(Ui.Button("✂ Schnitt von/bis", MarkCut, "Erster Klick: Beginn, zweiter Klick: Ende des zu entfernenden Abschnitts (X)"));
        transport.Children.Add(Ui.Button("↺ Alles zurücksetzen", () => { _clip.Cuts.Clear(); _clip.Start = 0; _clip.End = _media?.Duration ?? 0; _cutA = null; Refresh(); }, "Start/Ende und alle Schnitte zurücksetzen"));
        transport.Children.Add(Ui.Button("✂ Schnitte anwenden", async () => await ApplyCutsAsync(), "Start/Ende, Schnitte (und Annotationen) jetzt in eine neue, gekürzte Fassung schreiben – das Original bleibt erhalten", accent: true));
        transport.Children.Add(new Separator { Width = 12 });
        transport.Children.Add(_rangeLabel);

        _cutList.Margin = new Thickness(8, 0);
        var bottom = Ui.Column(4, _timeline, transport, _cutList, _info);
        bottom.Margin = new Thickness(8);

        var stage = new Panel { Background = new SolidColorBrush(Color.FromRgb(20, 20, 22)), Margin = new Thickness(8, 0) };
        stage.Children.Add(_preview);
        stage.Children.Add(_overlayImage);
        stage.PointerPressed += OnPreviewPressed;

        var sideScroll = new ScrollViewer { Content = _side, Width = 340, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        DockPanel.SetDock(sideScroll, Dock.Right); root.Children.Add(new Border { Child = sideScroll, BorderThickness = new Thickness(1, 0, 0, 0), BorderBrush = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)) });
        root.Children[^1].SetValue(DockPanel.DockProperty, Dock.Right);
        root.Children.Add(stage);
        Content = root;

        KeyDown += OnKey;
        Opened += async (_, _) => { StudioWindow.FitToScreen(this); await LoadAsync(); };
        Closing += (_, _) => { if (_step is not null) WriteBackToStep(); };
        Closed += (_, _) =>
        {
            _playTimer.Stop();
            foreach (var f in _frames) f.Dispose();
            if (_ownsSession) _overlaySession.Dispose();
        };
    }

    private async Task LoadAsync()
    {
        try
        {
            _media = await MediaProbe.ProbeAsync(_tools, _file);
            if (_clip.End <= 0 || _clip.End > _media.Duration) { _clip.End = _media.Duration; }
            _timeline.Duration = _media.Duration;
            if (_overlay.Page.Width == 0) { _overlay.Page.Width = _media.Width; _overlay.Page.Height = _media.Height; _overlay.Page.Background = ColorRgba.Transparent; }
            AddClickMarkers();
            _previewFps = Math.Clamp(1500 / Math.Max(1, _media.Duration), 1, Math.Min(15, Math.Max(1, _media.Fps)));
            var width = Math.Min(640, _media.Width);
            _info.Text = $"{_media.Width}×{_media.Height} · {_media.Fps:0.#} fps · {TimelineControl.Fmt(_media.Duration)} · {SizeText(new FileInfo(_file).Length)} · {_file}  –  Vorschau wird erstellt …";
            var jpegs = await Task.Run(() => _exporter.ExtractPreviewFramesAsync(_file, _previewFps, width));
            _frames = jpegs.Select(j => new Bitmap(new MemoryStream(j))).ToList();
            _timeline.Thumbs = _frames;
            _info.Text = $"{_media.Width}×{_media.Height} · {_media.Fps:0.#} fps · {TimelineControl.Fmt(_media.Duration)} · {SizeText(new FileInfo(_file).Length)}{(_media.HasAudio ? " · mit Ton" : "")} · {_file}";
            BuildSide();
            SeekTo(_clip.Start, hiRes: true);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException)
        {
            _info.Text = "⚠ " + ex.Message;
        }
    }

    private static string SizeText(long b) => Core.Export.SizeOptimizer.FormatBytes(b);

    /// <summary>Recorded clicks become normal, editable ring annotations (0.6 s each).</summary>
    private void AddClickMarkers()
    {
        if (_clicks is null || _clicks.Count == 0 || _media is null) return;
        var n = 0;
        foreach (var (t, x, y) in _clicks.OrderBy(c => c.T))
        {
            n++;
            var r = Math.Max(14, _media.Width / 60.0);
            _overlay.Page.Layers.Add(new ShapeLayer
            {
                Name = $"Klick {n}", Shape = ShapeKind.Ellipse, Rect = new RectD(x - r, y - r, 2 * r, 2 * r),
                Stroke = new ColorRgba(255, 200, 0), Fill = new ColorRgba(255, 214, 0, 70), StrokeWidth = Math.Max(3, r / 5), Shadow = false,
                Timing = new LayerTiming { StartSeconds = Math.Max(0, Math.Round(t - 0.05, 2)), EndSeconds = Math.Min(_media.Duration, Math.Round(t + 0.6, 2)) },
            });
        }
        _info.Text = $"{n} Mausklick(s) als Annotation markiert – im Bereich „Annotationen“ änderbar oder löschbar.";
    }

    // ------------------------------------------------------------------ cuts

    private void RebuildCutList()
    {
        _cutList.Children.Clear();
        var ordered = _clip.Cuts.OrderBy(c => c.From).ToList();
        if (ordered.Count == 0)
        {
            _cutList.Children.Add(Ui.Label("Keine Abschnitte markiert. „✂ Schnitt von/bis“ (X) zweimal drücken: Beginn und Ende des Abschnitts, der entfernt werden soll.", 11, opacity: 0.6));
            return;
        }
        _cutList.Children.Add(Ui.Label("Wird entfernt:", 12, bold: true));
        foreach (var c in ordered)
        {
            var cut = c;
            _cutList.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(70, 220, 40, 40)), CornerRadius = new CornerRadius(4), Margin = new Thickness(3), Padding = new Thickness(6, 1),
                Child = Ui.Row(
                    Ui.Button($"✂ {TimelineControl.Fmt(cut.From)} – {TimelineControl.Fmt(cut.To)} ({cut.To - cut.From:0.0} s)", () => SeekTo(cut.From, true), "Zum Abschnitt springen"),
                    Ui.Button("✕", () => { _clip.Cuts.Remove(cut); Refresh(); }, "Markierung entfernen (Abschnitt bleibt im Video)")),
            });
        }
    }

    private ClipRange CurrentClip() => new() { Start = _clip.Start, End = _clip.End, Cuts = _clip.Cuts.ToList() };

    private async Task<OverlaySpec?> PrepareOverlayAsync()
    {
        if (_overlay.Page.Layers.All(l => l is ImageLayer { IsBackground: true }) && _overlay.Zoom.Count == 0) return null;
        var dir = Path.Combine(_app.Env.TempDir, $"overlay-{Guid.NewGuid():N}");
        return await OverlayRenderer.PrepareAsync(_overlaySession, _overlay, dir);
    }

    private async Task ApplyCutsAsync()
    {
        if (_media is null) return;
        var nothing = _clip.Cuts.Count == 0 && _clip.Start <= 0.01 && _clip.End >= _media.Duration - 0.01;
        if (nothing) { await Dialogs.InfoAsync(this, "Schnitte anwenden", "Es ist nichts markiert. Mit „[ Start hier“, „Ende hier ]“ oder „✂ Schnitt von/bis“ festlegen, was entfernt werden soll."); return; }
        if (_playTimer.IsEnabled) TogglePlay();
        var overlay = await PrepareOverlayAsync();
        if (overlay is not null && await Dialogs.AskAsync(this, "Annotationen einbrennen?",
                "Beim Schneiden verschieben sich die Zeiten. Die vorhandenen Annotationen und Zoom-Keyframes werden deshalb fest in die neue Fassung eingebrannt und sind danach nicht mehr einzeln bearbeitbar.",
                "Einbrennen und schneiden", "Abbrechen") != "Einbrennen und schneiden") return;
        var dir = Path.GetDirectoryName(_file)!;
        var name = Core.Obsidian.SafeFileName.MakeUnique(Path.GetFileNameWithoutExtension(_file) + "_geschnitten.mkv", n => File.Exists(Path.Combine(dir, n)), slugStyle: false);
        var output = Path.Combine(dir, name);
        var clip = CurrentClip();
        var progress = new Progress<string>(s => _info.Text = s);
        try
        {
            IsEnabled = false;
            await Task.Run(() => _exporter.RenderEditedAsync(_file, output, clip, progress, default, overlay));
            Log.Info($"Cuts applied: {clip.Cuts.Count} cuts, kept {clip.KeptDuration:0.0}s, overlay={(overlay is not null)}");
            if (_step?.Video is { } v)
            {
                v.Path = VideoStepRenderer.Store(_app, _overlaySession, output);
                v.Start = 0; v.End = clip.KeptDuration; v.Cuts.Clear(); v.Overlay = null;
                _overlaySession.MarkDirty();
                var ed = new VideoEditorWindow(_app, output, _step, _studio);
                _skipWriteBack = true;
                ed.Show();
            }
            else new VideoEditorWindow(_app, output).Show();
            Close();
        }
        catch (InvalidOperationException ex)
        {
            IsEnabled = true;
            await Dialogs.InfoAsync(this, "Schneiden fehlgeschlagen", ex.Message);
        }
    }

    private bool _skipWriteBack;

    private void WriteBackToStep()
    {
        if (_skipWriteBack || _step?.Video is not { } v) return;
        VideoStepRenderer.SetClip(v, _clip);
        v.Overlay = _overlay.Page.Layers.Any(l => l is not ImageLayer { IsBackground: true }) || _overlay.Zoom.Count > 0 ? _overlay : null;
        _overlaySession.MarkDirty();
        _skipWriteBack = true;
    }

    // ------------------------------------------------------------------ side panel: annotations + zoom

    private void BuildSide()
    {
        _side.Children.Clear();
        _side.Children.Add(Ui.Label("Annotationen", 14, bold: true));
        _side.Children.Add(Ui.Label("Pfeile, Texte, Schritte, Infoboxen, Schwärzungen … mit demselben Editor wie bei Screenshots. Jede Ebene hat ein Zeitfenster.", 11, opacity: 0.65));
        _side.Children.Add(Ui.Button("✎ Annotationen bearbeiten…", async () => await EditAnnotationsAsync(), "Öffnet den Ebenen-Editor mit dem aktuellen Videobild als Vorlage", accent: true));

        var layers = _overlay.Page.Layers.Where(l => l is not ImageLayer { IsBackground: true }).Reverse().ToList();
        if (layers.Count == 0) _side.Children.Add(Ui.Label("Noch keine Annotationen.", 12, opacity: 0.6));
        foreach (var l in layers)
        {
            var layer = l;
            layer.Timing ??= new LayerTiming { StartSeconds = _clip.Start, EndSeconds = Math.Min(_media?.Duration ?? 0, _clip.Start + 4) };
            var tm = layer.Timing;
            var start = Num(tm.StartSeconds); var end = Num(tm.EndSeconds);
            start.ValueChanged += (_, _) => { tm.StartSeconds = (double)(start.Value ?? 0); Changed(); };
            end.ValueChanged += (_, _) => { tm.EndSeconds = (double)(end.Value ?? 0); Changed(); };
            var moving = tm.Keyframes.Count >= 2;
            var move = new CheckBox { Content = "bewegt sich bis Ende", IsChecked = moving };
            var tx = Num(moving ? tm.Keyframes[^1].Bounds.X : layer.Bounds.X, 0, 20000); var ty = Num(moving ? tm.Keyframes[^1].Bounds.Y : layer.Bounds.Y, 0, 20000);
            void UpdateMove()
            {
                tm.Keyframes.Clear();
                if (move.IsChecked != true) return;
                var b = layer.Bounds;
                tm.Keyframes.Add(new Keyframe { TimeSeconds = tm.StartSeconds, Bounds = b });
                tm.Keyframes.Add(new Keyframe { TimeSeconds = tm.EndSeconds, Bounds = b with { X = (double)(tx.Value ?? 0), Y = (double)(ty.Value ?? 0) } });
                Changed();
            }
            move.IsCheckedChanged += (_, _) => UpdateMove();
            tx.ValueChanged += (_, _) => UpdateMove(); ty.ValueChanged += (_, _) => UpdateMove();
            var card = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6),
                Child = Ui.Column(4,
                    Ui.Row(Ui.Label((string.IsNullOrWhiteSpace(layer.Name) ? layer.Kind : layer.Name) + (layer is RedactLayer ? " ⚠" : ""), 12, bold: true),
                        Ui.Button("🗑", () => { _overlay.Page.Layers.Remove(layer); Changed(); BuildSide(); }, "Annotation löschen")),
                    Ui.Row(Ui.Label("von", 11), start, Ui.Button("⇤", () => { start.Value = (decimal)Math.Round(_pos, 2); }, "Beginn = Abspielposition")),
                    Ui.Row(Ui.Label("bis", 11), end, Ui.Button("⇥", () => { end.Value = (decimal)Math.Round(_pos, 2); }, "Ende = Abspielposition")),
                    move,
                    Ui.Row(Ui.Label("Ziel X", 11), tx, Ui.Label("Y", 11), ty)),
            };
            _side.Children.Add(card);
        }

        _side.Children.Add(new Separator());
        _side.Children.Add(Ui.Label("Zoom & Schwenk", 14, bold: true));
        _side.Children.Add(Ui.Label("Ins Vorschaubild klicken = Zoom-Mittelpunkt. Zwischen Keyframes wird weich übergeblendet.", 11, opacity: 0.65));
        _side.Children.Add(Ui.Row(Ui.Label("Faktor", 11), _zoomFactor, Ui.Label($"{_zoomFactor.Value:0.0}×", 11)));
        _side.Children.Add(Ui.Row(
            Ui.Button("🔍 Keyframe hier", () => AddZoomKey(_zoomFactor.Value), "Zoom an der aktuellen Abspielposition"),
            Ui.Button("1× hier", () => AddZoomKey(1), "Hier wieder ganz herauszoomen")));
        foreach (var z in _overlay.Zoom.OrderBy(z => z.TimeSeconds).ToList())
        {
            var key = z;
            var factor = _media is null ? 1 : _media.Width / Math.Max(1, key.View.Width);
            _side.Children.Add(Ui.Row(
                Ui.Button($"{TimelineControl.Fmt(key.TimeSeconds)}  {factor:0.0}×", () => SeekTo(key.TimeSeconds, true), "Zum Keyframe springen"),
                Ui.Button("✕", () => { _overlay.Zoom.Remove(key); Changed(); BuildSide(); }, "Keyframe löschen")));
        }
        _zoomFactor.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty && _side.Children.Count > 0) { } };
    }

    private static NumericUpDown Num(double v, double min = 0, double max = 100000) =>
        new() { Value = (decimal)Math.Round(v, 2), Minimum = (decimal)min, Maximum = (decimal)max, Increment = 0.5m, FormatString = "0.##", Width = 120 };

    private void Changed()
    {
        if (_step is null && _ownsSession) _overlaySession.MarkDirty();
        UpdateOverlayPreview();
    }

    private void AddZoomKey(double factor)
    {
        if (_media is null) return;
        var w = _media.Width / Math.Max(1, factor); var h = _media.Height / Math.Max(1, factor);
        var (cx, cy) = _zoomCenter ?? (_media.Width / 2.0, _media.Height / 2.0);
        var x = Math.Clamp(cx - w / 2, 0, _media.Width - w); var y = Math.Clamp(cy - h / 2, 0, _media.Height - h);
        if (_overlay.Zoom.Count == 0 && factor > 1 && _pos > 0.5)
            _overlay.Zoom.Add(new ZoomKeyframe { TimeSeconds = Math.Max(0, _pos - 1), View = new RectD(0, 0, _media.Width, _media.Height) });
        _overlay.Zoom.RemoveAll(z => Math.Abs(z.TimeSeconds - _pos) < 0.05);
        _overlay.Zoom.Add(new ZoomKeyframe { TimeSeconds = _pos, View = new RectD(x, y, w, h) });
        Changed();
        BuildSide();
    }

    private void OnPreviewPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_media is null || sender is not Control stage) return;
        // map click through the Uniform layout of the preview image
        var b = stage.Bounds; var s = Math.Min(b.Width / _media.Width, b.Height / _media.Height);
        var ox = (b.Width - _media.Width * s) / 2; var oy = (b.Height - _media.Height * s) / 2;
        var p = e.GetPosition(stage);
        var x = (p.X - ox) / s; var y = (p.Y - oy) / s;
        if (x < 0 || y < 0 || x > _media.Width || y > _media.Height) return;
        _zoomCenter = (x, y);
        _info.Text = $"Zoom-Mittelpunkt: {x:0} / {y:0} px – jetzt „🔍 Keyframe hier“.";
        UpdateOverlayPreview();
    }

    private async Task EditAnnotationsAsync()
    {
        if (_media is null) return;
        if (_playTimer.IsEnabled) TogglePlay();
        // reference frame as locked background (not exported)
        var png = await Task.Run(() => _exporter.ExtractFrameAsync(_file, _pos));
        using var img = SkiaSharp.SKImage.FromEncodedData(png);
        if (img is null) return;
        var id = _overlaySession.Assets.Add(png);
        _overlaySession.Project.Assets[id] = new AssetInfo { Id = id, FileName = id + ".png", Width = img.Width, Height = img.Height, Length = png.Length };
        var page = _overlay.Page;
        page.Layers.RemoveAll(l => l is ImageLayer { IsBackground: true });
        page.Layers.Insert(0, new ImageLayer { AssetId = id, IsBackground = true, Locked = true, Name = OverlayRenderer.ReferenceLayerName, Rect = new RectD(0, 0, page.Width, page.Height) });
        var before = page.Layers.Select(l => l.Id).ToHashSet();
        var ed = new EditorWindow(_app, _overlaySession, page, isStudioPage: true) { Title = "Annotationen bei " + TimelineControl.Fmt(_pos) };
        var startAt = _pos;
        ed.Closed += (_, _) =>
        {
            foreach (var l in page.Layers.Where(l => !before.Contains(l.Id) && l is not ImageLayer { IsBackground: true }))
                l.Timing ??= new LayerTiming { StartSeconds = Math.Round(startAt, 2), EndSeconds = Math.Round(Math.Min(_media.Duration, startAt + 4), 2) };
            Changed();
            BuildSide();
        };
        await ed.ShowDialog(this);
    }

    private void UpdateOverlayPreview()
    {
        if (_media is null) return;
        var hasLayers = _overlay.Page.Layers.Any(l => l is not ImageLayer { IsBackground: true });
        if (!hasLayers && _overlay.Zoom.Count == 0 && _zoomCenter is null) { _overlayImage.Source = null; return; }
        using var img = OverlayRenderer.RenderPreview(_overlaySession, _overlay, _pos);
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(img.Width, img.Height));
        var c = surface.Canvas;
        c.Clear(SkiaSharp.SKColors.Transparent);
        c.DrawImage(img, 0, 0);
        if (ZoomViewAt(_pos) is { } v)
        {
            using var pen = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(0, 160, 255), Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = Math.Max(2, img.Width / 300f), PathEffect = SkiaSharp.SKPathEffect.CreateDash(new[] { 12f, 8f }, 0), IsAntialias = true };
            c.DrawRect((float)v.X, (float)v.Y, (float)v.Width, (float)v.Height, pen);
        }
        if (_zoomCenter is { } zc)
        {
            using var dot = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(0, 160, 255), IsAntialias = true };
            c.DrawCircle((float)zc.X, (float)zc.Y, Math.Max(5, img.Width / 150f), dot);
        }
        using var snap = surface.Snapshot();
        _overlayImage.Source = Ui.ToBitmap(snap);
    }

    /// <summary>Interpolated zoom view at time t (null = full frame / no zoom).</summary>
    private RectD? ZoomViewAt(double t)
    {
        var z = _overlay.Zoom.OrderBy(k => k.TimeSeconds).ToList();
        if (z.Count == 0 || _media is null) return null;
        RectD v;
        if (t <= z[0].TimeSeconds) v = z[0].View;
        else if (t >= z[^1].TimeSeconds) v = z[^1].View;
        else
        {
            var i = z.FindLastIndex(k => k.TimeSeconds <= t);
            var a = z[i]; var b = z[i + 1];
            var f = (t - a.TimeSeconds) / Math.Max(1e-6, b.TimeSeconds - a.TimeSeconds);
            v = new RectD(a.View.X + (b.View.X - a.View.X) * f, a.View.Y + (b.View.Y - a.View.Y) * f, a.View.Width + (b.View.Width - a.View.Width) * f, a.View.Height + (b.View.Height - a.View.Height) * f);
        }
        return v.Width >= _media.Width - 1 ? null : v;
    }

    // ------------------------------------------------------------------ playback

    private void Refresh()
    {
        RebuildCutList();
        _timeline.Position = _pos; _timeline.Range = _clip; _timeline.PendingCut = _cutA;
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
        UpdateOverlayPreview();
        if (hiRes) _ = LoadHiResAsync(_pos);
    }

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
        else _info.Text = "Schnitt zu kurz – Abspielkopf zwischen den beiden Klicks verschieben.";
        Refresh();
    }

    private void OnKey(object? s, KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is TextBox or NumericUpDown) return;
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
            case Key.Delete or Key.Back:
                var hit = _clip.Cuts.FirstOrDefault(c => _pos >= c.From && _pos <= c.To);
                if (hit != default) { _clip.Cuts.Remove(hit); Refresh(); }
                else if (_cutA is not null) MarkCut();
                break;
            case Key.Escape when _cutA is not null: _cutA = null; Refresh(); break;
            default: return;
        }
        e.Handled = true;
    }

    // ------------------------------------------------------------------ frames / guide / export

    private async Task<(SkiaSharp.SKImage img, byte[] png)?> GrabFrameAsync(double? at = null)
    {
        try
        {
            var png = await Task.Run(() => _exporter.ExtractFrameAsync(_file, at ?? _pos));
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
        App.Current.ShowStudio().AddCapturedImage(f.img, f.png, $"Frame {TimelineControl.Fmt(_pos)}");
    }

    private async Task VideoToGuideAsync()
    {
        if (_media is null) return;
        if (await GrabFrameAsync(_clip.Start + 0.05) is not { } poster) return;
        var studio = App.Current.ShowStudio();
        var v = new VideoRef { IncludeAudio = _media.HasAudio, MaxWidth = Math.Min(1280, _media.Width) };
        VideoStepRenderer.SetClip(v, _clip);
        studio.AddVideoStep(_file, v, poster.img, poster.png, Path.GetFileNameWithoutExtension(_file), _overlaySession, _overlay);
        _info.Text = "Als Video-Schritt zur Anleitung hinzugefügt.";
    }

    private async Task ExportAsync(bool gif)
    {
        if (_media is null) return;
        if (_playTimer.IsEnabled) TogglePlay();
        var overlay = await PrepareOverlayAsync();
        var dlg = new VideoExportDialog(_app, _exporter, _file, _media, CurrentClip(), gif, overlay);
        await dlg.ShowDialog(this);
    }

    private static void OpenFolder(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Open folder: " + ex.Message); }
    }
}
