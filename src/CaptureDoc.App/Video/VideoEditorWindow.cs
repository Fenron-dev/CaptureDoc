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
using SkiaSharp;
using Page = CaptureDoc.Core.Model.Page;

namespace CaptureDoc.App.Video;

/// <summary>
/// Video studio: annotate directly on the video (same tools and layers as the image editor), every annotation
/// has a time window on its own track, zoom/pan keyframes, inserted slides, trim and cuts, export MP4/GIF and
/// hand-over to the documentation studio. The source file is never modified.
/// </summary>
public sealed class VideoEditorWindow : Window
{
    private readonly AppServices _app;
    private readonly string _file;
    private readonly FfmpegTools _tools;
    private readonly VideoExporter _exporter;
    private readonly ClipRange _clip = new();
    private MediaInfo? _media;
    private List<byte[]> _jpegs = new();
    private List<Bitmap> _thumbs = new();
    private double _previewFps = 10, _pos, _slidePos;
    private double? _cutA;
    private (double A, double B)? _rangeSel;
    private CancellationTokenSource? _hiResCts;
    private readonly DispatcherTimer _playTimer = new();
    private SKImage? _frame;

    // annotations belong to a guide step (studio), to the side-car project next to the video, or are new
    private readonly ProjectSession _session;
    private readonly bool _ownsSession;
    private readonly VideoOverlay _overlay;
    private GuideStep? _step;
    private readonly StudioWindow? _studio;
    private readonly string? _sidecar;
    private bool _skipWriteBack;
    private VideoSlide? _slide;
    private int? _zoomKey;
    private Guid? _selectedSlide;
    private bool _zoomPick;

    // UI
    private readonly EditorCanvas _canvas;
    private readonly Canvas _textLayer = new();
    private readonly InlineTextEditor _text;
    private readonly ZoomOverlay _zoomOverlay = new();
    private readonly Panel _stage = new() { Background = new SolidColorBrush(Color.FromRgb(18, 18, 21)), ClipToBounds = true };
    private readonly VideoTimeline _timeline = new();
    private readonly ScrollBar _hbar = new() { Orientation = Orientation.Horizontal, IsVisible = false, Height = 14 };
    private readonly WrapPanel _cutList = new() { Margin = new Thickness(6, 0) };
    private readonly TextBlock _info = Ui.Label("Lade Video …", 11, opacity: 0.75);
    private readonly TextBlock _timeLabel = new() { Text = "0:00.0", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas, monospace"), Margin = new Thickness(6, 0) };
    private readonly TextBlock _rangeLabel = Ui.Label("", 12);
    private readonly Button _play;
    private readonly PropertiesPanel _props;
    private readonly LayersPanel _layers;
    private readonly StackPanel _timeBox = new() { Spacing = 6, Margin = new Thickness(4) };
    private readonly StackPanel _videoBox = new() { Spacing = 8, Margin = new Thickness(6) };
    private readonly Border _slideBanner = new() { IsVisible = false, Padding = new Thickness(8, 4), Background = new SolidColorBrush(Color.FromArgb(70, 140, 70, 220)) };
    private readonly Dictionary<Tool, ToggleButton> _toolButtons = new();
    private readonly ToggleButton _zoomTool;
    private readonly Slider _zoomFactor = new() { Minimum = 1.2, Maximum = 4, Value = 2, Width = 150 };
    private readonly NumericUpDown _stroke, _font;
    private readonly StackPanel _cutTools = new() { Orientation = Orientation.Horizontal };

    private readonly IReadOnlyList<(double T, int X, int Y)>? _clicks;
    private readonly IReadOnlyList<Layer>? _live;

    public ProjectSession Session => _session;

    public VideoEditorWindow(AppServices app, string file, GuideStep? step = null, StudioWindow? studio = null,
        IReadOnlyList<(double, int, int)>? clicks = null, IReadOnlyList<Layer>? liveLayers = null)
    {
        _app = app; _file = file; _tools = app.Ffmpeg!; _exporter = new VideoExporter(_tools);
        _step = step; _studio = studio; _clicks = clicks; _live = liveLayers;
        var fresh = step is null;
        if (studio is not null) _session = studio.Session;
        else
        {
            _ownsSession = true;
            _sidecar = SidecarPath(file);
            if (step is null && File.Exists(_sidecar))
            {
                try
                {
                    var s = ProjectSession.Open(_sidecar, out _);
                    var st = s.Project.Guide.Steps.FirstOrDefault(x => x.Video is not null);
                    if (st is not null) { _session = s; _step = st; fresh = false; }
                    else s.Dispose();
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ProjectFormatException or System.Text.Json.JsonException)
                {
                    Log.Warn("Video side-car not readable: " + ex.Message);
                }
            }
            _session ??= new ProjectSession(new CaptureProject { Title = Path.GetFileNameWithoutExtension(file) }, new AssetStore());
        }
        _overlay = _step?.Video?.Overlay ?? new VideoOverlay();
        _overlay.Page.Layers.RemoveAll(l => l is ImageLayer { IsBackground: true });   // old "reference frame" layers
        _overlay.Page.Background = ColorRgba.Transparent;
        if (_step?.Video is { } v) { _clip.Start = v.Start; _clip.End = v.End; _clip.Cuts = VideoStepRenderer.ToClip(v).Cuts; }
        _freshAnnotations = fresh;

        Title = Path.GetFileName(file) + (_studio is not null && _step is not null ? $" – Schritt „{_step.Title}“" : "") + " – CaptureDoc Video";
        Icon = IconFactory.WindowIcon;
        Width = 1440; Height = 900; MinWidth = 1000; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // ---- canvas (annotations directly on the video)
        _canvas = new EditorCanvas(_session, _overlay.Page, app.Settings.Shared.Style) { Tool = Tool.Select };
        _text = new InlineTextEditor(_canvas, _textLayer);
        _canvas.LayerFilter = l => ActiveAt(l, CurrentTime);
        _canvas.DisplayOverride = MovingView;
        _canvas.LayerCreated += (_, l) => OnLayerCreated(l);
        _canvas.TextEditRequested += (_, t) => _text.Begin(t);
        _canvas.SelectionChanged += (_, _) => { BuildTimeBox(); RefreshTimeline(); };
        _canvas.DocumentChanged += (_, _) => { MarkChanged(); RefreshTimeline(); };
        _canvas.HistoryChanged += (_, _) => BuildTimeBox();
        _zoomOverlay.Picked += (_, r) => OnZoomPicked(r);
        var host = new Panel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(_canvas);
        host.Children.Add(_textLayer);
        host.Children.Add(_zoomOverlay);
        _stage.Children.Add(host);
        _stage.SizeChanged += (_, _) => FitCanvas();

        _props = new PropertiesPanel(_canvas) { ShowPageSection = false };
        _props.Rebuild();
        _layers = new LayersPanel(_canvas);

        // ---- top bar
        var title = Ui.Label(Path.GetFileName(file), 13, bold: true);
        title.Margin = new Thickness(10, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 2 };
        actions.Children.Add(Ui.Button("📂 Ordner", () => OpenFolder(Path.GetDirectoryName(_file)!), "Speicherort öffnen"));
        actions.Children.Add(Ui.Button("🖼 Frame → Editor", async () => await FrameToEditorAsync(), "Aktuelles Bild mit den sichtbaren Annotationen als bearbeitbaren Screenshot öffnen"));
        actions.Children.Add(Ui.Button("➕ Frame → Anleitung", async () => await FrameToGuideAsync(), "Aktuelles Bild (mit sichtbaren Annotationen, als Ebenen) als Bild-Schritt ins Documentation Studio"));
        if (_studio is not null && _step is not null)
            actions.Children.Add(Ui.Button("✓ In Anleitung übernehmen", () => { WriteBack(); Close(); }, "Clip, Schnitte, Annotationen, Zoom und Folien im Anleitungsschritt speichern", accent: true));
        else
            actions.Children.Add(Ui.Button("🎬 Video → Anleitung", async () => await VideoToGuideAsync(), "Ganzes Video (mit Schnitten, Annotationen und Folien) als Video-Schritt ins Documentation Studio"));
        actions.Children.Add(Ui.Button("⇩ GIF…", async () => await ExportAsync(gif: true), "Als GIF exportieren – mit geprüftem Größenlimit", accent: true));
        actions.Children.Add(Ui.Button("⇩ MP4…", async () => await ExportAsync(gif: false), "Als MP4 exportieren (Annotationen, Zoom und Folien werden eingebrannt)", accent: true));
        var top = new DockPanel { Margin = new Thickness(4, 4) };
        DockPanel.SetDock(actions, Dock.Right);
        top.Children.Add(actions);
        top.Children.Add(title);

        // ---- left tool rail
        var rail = new StackPanel { Spacing = 2, Margin = new Thickness(4) };
        void ToolBtn(Tool t, string glyph, string caption, string key)
        {
            var b = RailButton(glyph, caption, $"{EditorCanvas.ToolName(t)} ({key})");
            b.Click += (_, _) => SetTool(t);
            _toolButtons[t] = b;
            rail.Children.Add(b);
        }
        ToolBtn(Tool.Select, "⬚", "Auswahl", "V");
        ToolBtn(Tool.Arrow, "➜", "Pfeil", "A");
        ToolBtn(Tool.Rectangle, "▭", "Rahmen", "R");
        ToolBtn(Tool.Ellipse, "◯", "Kreis", "E");
        ToolBtn(Tool.Text, "T", "Text", "T");
        ToolBtn(Tool.InfoBox, "ⓘ", "Infobox", "I");
        ToolBtn(Tool.Step, "①", "Schritt", "N");
        ToolBtn(Tool.Freehand, "✎", "Stift", "F");
        ToolBtn(Tool.Marker, "🖍", "Marker", "G");
        ToolBtn(Tool.Highlight, "▮", "Hervorh.", "H");
        ToolBtn(Tool.Spotlight, "◎", "Spotlight", "S");
        ToolBtn(Tool.Magnifier, "🔍", "Lupe", "M");
        ToolBtn(Tool.Blackout, "■", "Schwärzen", "B");
        ToolBtn(Tool.Pixelate, "▦", "Verpixeln", "P");
        _zoomTool = RailButton("⤢", "Zoom", "Zoom/Schwenk: Rechteck im Video aufziehen = Zoom-Keyframe an der Abspielposition (Z)");
        _zoomTool.Click += (_, _) => SetZoomPick(_zoomTool.IsChecked == true);
        rail.Children.Add(_zoomTool);
        var img = RailButton("🖼", "Bild", "Bild einfügen (z. B. Logo, Screenshot)");
        img.Click += async (_, _) => { img.IsChecked = false; await InsertImageAsync(); };
        rail.Children.Add(img);
        var slideBtn = RailButton("▣", "Folie", "Folie an der Abspielposition einfügen (Titel, Zusammenfassung, Checkliste …)");
        slideBtn.Click += (_, _) => { slideBtn.IsChecked = false; ShowSlideMenu(slideBtn); };
        rail.Children.Add(slideBtn);
        var railScroll = new ScrollViewer { Content = rail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        // ---- style strip above the stage
        var strip = new WrapPanel { Margin = new Thickness(4, 2) };
        foreach (var c in Ui.Palette)
        {
            var b = new Button { Width = 22, Height = 22, Margin = new Thickness(1), Padding = new Thickness(0), Background = new SolidColorBrush(c.ToAvalonia()), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
            var cc = c;
            ToolTip.SetTip(b, "Farbe für neue Elemente und Auswahl");
            b.Click += (_, _) => ApplyColor(cc);
            strip.Children.Add(b);
        }
        _stroke = new NumericUpDown { Value = (decimal)_canvas.Style.StrokeWidth, Minimum = 1, Maximum = 40, Width = 104, FormatString = "0", Margin = new Thickness(6, 0, 0, 0) };
        ToolTip.SetTip(_stroke, "Linien-/Stiftstärke für neue Elemente und die Auswahl");
        _stroke.ValueChanged += (_, _) => ApplyStroke((double)(_stroke.Value ?? 4));
        _font = new NumericUpDown { Value = (decimal)_canvas.Style.FontSize, Minimum = 8, Maximum = 160, Width = 104, FormatString = "0" };
        ToolTip.SetTip(_font, "Schriftgröße für neue Texte");
        _font.ValueChanged += (_, _) => _canvas.Style.FontSize = (double)(_font.Value ?? 28);
        strip.Children.Add(Ui.Label("  Stärke", 11, opacity: 0.7));
        strip.Children.Add(_stroke);
        strip.Children.Add(Ui.Label("  Schrift", 11, opacity: 0.7));
        strip.Children.Add(_font);
        strip.Children.Add(new Separator { Width = 10 });
        strip.Children.Add(Ui.Button("↶", () => _canvas.Undo(), "Rückgängig (Strg+Z)"));
        strip.Children.Add(Ui.Button("↷", () => _canvas.Redo(), "Wiederholen (Strg+Y)"));
        strip.Children.Add(Ui.Button("🗑", () => _canvas.DeleteSelection(), "Ausgewählte Annotation löschen (Entf)"));

        var center = new DockPanel();
        DockPanel.SetDock(strip, Dock.Top); center.Children.Add(strip);
        DockPanel.SetDock(_slideBanner, Dock.Top); center.Children.Add(_slideBanner);
        center.Children.Add(_stage);

        // ---- right panel
        var propsTab = new DockPanel();
        var timeScroll = new Border { Child = _timeBox, BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)) };
        DockPanel.SetDock(timeScroll, Dock.Top);
        propsTab.Children.Add(timeScroll);
        propsTab.Children.Add(_props);
        var tabs = new TabControl
        {
            Width = 320,
            ItemsSource = new[]
            {
                new TabItem { Header = "Eigenschaften", Content = propsTab, FontSize = 13 },
                new TabItem { Header = "Ebenen", Content = _layers, FontSize = 13 },
                new TabItem { Header = "Zoom & Folien", Content = new ScrollViewer { Content = _videoBox, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, FontSize = 13 },
            },
        };

        // ---- bottom: transport + timeline
        _play = Ui.Button("▶", TogglePlay, "Abspielen / Pause (Leertaste)");
        var transport = new WrapPanel { Margin = new Thickness(6, 4) };
        transport.Children.Add(_play);
        transport.Children.Add(_timeLabel);
        transport.Children.Add(Ui.Button("⏮", () => SeekTo(_slide is null ? _clip.Start : 0, true), "Zum Anfang (Pos1)"));
        transport.Children.Add(Ui.Button("◀", () => StepFrame(-1), "Ein Bild zurück (, oder ←)"));
        transport.Children.Add(Ui.Button("▶|", () => StepFrame(1), "Ein Bild vor (. oder →)"));
        _cutTools.Children.Add(new Separator { Width = 10 });
        _cutTools.Children.Add(Ui.Button("[ Start hier", () => SetRange(start: true), "Clip beginnt an der Abspielposition (I)"));
        _cutTools.Children.Add(Ui.Button("Ende hier ]", () => SetRange(start: false), "Clip endet an der Abspielposition (O)"));
        _cutTools.Children.Add(Ui.Button("✂ Schnitt (X)", MarkCut, "Markierten Bereich (auf dem Zeitlineal ziehen) entfernen – oder zweimal X: Beginn und Ende"));
        _cutTools.Children.Add(Ui.Button("✂ Schnitte anwenden", async () => await ApplyCutsAsync(), "Start/Ende, Schnitte (und Annotationen/Folien) in eine neue, gekürzte Fassung schreiben – das Original bleibt erhalten", accent: true));
        _cutTools.Children.Add(Ui.Button("↺", () => { _clip.Cuts.Clear(); _clip.Start = 0; _clip.End = _media?.Duration ?? 0; _cutA = null; _rangeSel = null; _timeline.Selection = null; Refresh(); }, "Start/Ende und alle Schnitte zurücksetzen"));
        transport.Children.Add(_cutTools);
        transport.Children.Add(new Separator { Width = 10 });
        transport.Children.Add(Ui.Button("－", () => _timeline.ZoomFactor /= 1.5, "Zeitachse verkleinern (Strg+Mausrad)"));
        transport.Children.Add(Ui.Button("＋", () => { _timeline.ZoomFactor *= 1.5; _timeline.EnsureVisible(CurrentTime); }, "Zeitachse vergrößern (Strg+Mausrad)"));
        transport.Children.Add(Ui.Button("⤢", () => _timeline.ZoomFactor = 1, "Ganze Zeitachse zeigen"));
        transport.Children.Add(new Separator { Width = 10 });
        transport.Children.Add(_rangeLabel);

        _timeline.Seek += (_, t) => SeekTo(t, hiRes: !_playTimer.IsEnabled);
        _timeline.SelectionChanged += (_, s) => { _rangeSel = s; Refresh(); };
        _timeline.LaneSelected += (_, id) => SelectLayer(id);
        _timeline.LaneEditStarted += (_, _) => _canvas.History.BeginTransaction("Zeitfenster");
        _timeline.LaneEdited += (_, e) => SetTiming(e.Id, e.Start, e.End);
        _timeline.LaneEditCompleted += (_, _) => { _canvas.History.Commit(); BuildTimeBox(); MarkChanged(); };
        _timeline.ZoomKeySelected += (_, i) => { _zoomKey = i; var keys = SortedZoom(); if (i < keys.Count) SeekTo(keys[i].TimeSeconds, true); BuildVideoBox(); };
        _timeline.ZoomKeyMoved += (_, e) => { var keys = SortedZoom(); if (e.Index < keys.Count) { keys[e.Index].TimeSeconds = e.Time; MarkChanged(); Refresh(); } };
        _timeline.SlideSelected += (_, id) => { _selectedSlide = id; Refresh(); BuildVideoBox(); };
        _timeline.SlideMoved += (_, e) => { if (_overlay.Slides.FirstOrDefault(s => s.Id == e.Id) is { } s) { s.At = e.At; MarkChanged(); Refresh(); } };
        _timeline.SlideOpened += (_, id) => { if (_overlay.Slides.FirstOrDefault(s => s.Id == id) is { } s) EnterSlide(s); };
        _timeline.ViewChanged += (_, _) => SyncHBar();
        _hbar.Scroll += (_, _) => _timeline.Offset = _hbar.Value;
        var timelineScroll = new ScrollViewer { Content = _timeline, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        var bottom = new DockPanel();
        DockPanel.SetDock(transport, Dock.Top); bottom.Children.Add(transport);
        DockPanel.SetDock(_cutList, Dock.Top); bottom.Children.Add(_cutList);
        DockPanel.SetDock(_info, Dock.Bottom); _info.Margin = new Thickness(8, 2); bottom.Children.Add(_info);
        DockPanel.SetDock(_hbar, Dock.Bottom); bottom.Children.Add(_hbar);
        bottom.Children.Add(timelineScroll);

        var mid = new DockPanel();
        DockPanel.SetDock(railScroll, Dock.Left); mid.Children.Add(new Border { Child = railScroll, BorderThickness = new Thickness(0, 0, 1, 0), BorderBrush = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)) });
        mid.Children[^1].SetValue(DockPanel.DockProperty, Dock.Left);
        var tabsBorder = new Border { Child = tabs, BorderThickness = new Thickness(1, 0, 0, 0), BorderBrush = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)) };
        DockPanel.SetDock(tabsBorder, Dock.Right); mid.Children.Add(tabsBorder);
        mid.Children.Add(center);

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,5,280") };
        Grid.SetRow(top, 0); grid.Children.Add(top);
        Grid.SetRow(mid, 1); grid.Children.Add(mid);
        var split = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, Background = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)) };
        Grid.SetRow(split, 2); grid.Children.Add(split);
        Grid.SetRow(bottom, 3); grid.Children.Add(bottom);
        grid.RowDefinitions[1].MinHeight = 240; grid.RowDefinitions[3].MinHeight = 150;
        Content = grid;

        _playTimer.Tick += (_, _) => PlayStep();
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Opened += async (_, _) => { StudioWindow.FitToScreen(this); SetTool(Tool.Select); await LoadAsync(); };
        Closing += (_, _) => { _text.Commit(); WriteBack(); SaveSidecar(); };
        Closed += (_, _) =>
        {
            _playTimer.Stop();
            foreach (var f in _thumbs) f.Dispose();
            if (_ownsSession) _session.Dispose();
        };
    }

    private readonly bool _freshAnnotations;

    private static ToggleButton RailButton(string glyph, string caption, string tip)
    {
        var b = new ToggleButton
        {
            Width = 66, Padding = new Thickness(2, 3), Margin = new Thickness(0, 1), HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = new StackPanel
            {
                Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center,
                Children = { new TextBlock { Text = glyph, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center }, new TextBlock { Text = caption, FontSize = 10, Opacity = 0.8, HorizontalAlignment = HorizontalAlignment.Center } },
            },
        };
        ToolTip.SetTip(b, tip);
        return b;
    }

    public static string SidecarPath(string video) => Path.ChangeExtension(video, ProjectFile.Extension);

    // ================================================================== loading

    private async Task LoadAsync()
    {
        try
        {
            _media = await MediaProbe.ProbeAsync(_tools, _file);
            if (_clip.End <= 0 || _clip.End > _media.Duration) _clip.End = _media.Duration;
            if (_overlay.Page.Width == 0) { _overlay.Page.Width = _media.Width; _overlay.Page.Height = _media.Height; }
            _canvas.InvalidateMeasure();
            if (_freshAnnotations)
            {
                AddClickMarkers();
                if (_live is { Count: > 0 }) { foreach (var l in _live) _overlay.Page.Layers.Add(l); _info.Text = $"{_live.Count} Live-Zeichnung(en) aus der Aufnahme übernommen."; }
            }
            _timeline.Duration = _media.Duration;
            _previewFps = Math.Clamp(1500 / Math.Max(1, _media.Duration), 1, Math.Min(15, Math.Max(1, _media.Fps)));
            var width = Math.Min(640, _media.Width);
            _info.Text = $"{_media.Width}×{_media.Height} · {_media.Fps:0.#} fps · {VideoTimeline.Fmt(_media.Duration)} · {SizeText(new FileInfo(_file).Length)} · {_file}  –  Vorschau wird erstellt …";
            _jpegs = await Task.Run(() => _exporter.ExtractPreviewFramesAsync(_file, _previewFps, width));
            _thumbs = _jpegs.Select(j => new Bitmap(new MemoryStream(j))).ToList();
            _timeline.Thumbs = _thumbs; _timeline.ThumbFps = _previewFps;
            _info.Text = $"{_media.Width}×{_media.Height} · {_media.Fps:0.#} fps · {VideoTimeline.Fmt(_media.Duration)} · {SizeText(new FileInfo(_file).Length)}{(_media.HasAudio ? " · mit Ton" : "")} · {_file}" +
                         (_sidecar is not null && File.Exists(_sidecar) && !_freshAnnotations ? "  ·  Annotationen aus " + Path.GetFileName(_sidecar) + " geladen" : "");
            FitCanvas();
            BuildVideoBox(); BuildTimeBox();
            SeekTo(_clip.Start, hiRes: true);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException)
        {
            _info.Text = "⚠ " + ex.Message;
        }
    }

    private static string SizeText(long b) => Core.Export.SizeOptimizer.FormatBytes(b);

    /// <summary>Recorded clicks become normal, editable ring annotations; double clicks are merged.</summary>
    private void AddClickMarkers()
    {
        if (_clicks is null || _clicks.Count == 0 || _media is null) return;
        var groups = new List<(double T, int X, int Y, int Count)>();
        foreach (var c in _clicks.OrderBy(c => c.T))
        {
            if (groups.Count > 0 && c.T - groups[^1].T < 0.5 && Math.Abs(c.X - groups[^1].X) <= 12 && Math.Abs(c.Y - groups[^1].Y) <= 12)
                groups[^1] = groups[^1] with { Count = groups[^1].Count + 1 };
            else groups.Add((c.T, c.X, c.Y, 1));
        }
        var n = 0;
        var r = Math.Max(12, _media.Width / 70.0);
        foreach (var g in groups)
        {
            n++;
            var name = g.Count >= 2 ? $"Doppelklick {n}" : $"Klick {n}";
            _overlay.Page.Layers.Add(new ShapeLayer
            {
                Name = name, Shape = ShapeKind.Ellipse, Rect = new RectD(g.X - r, g.Y - r, 2 * r, 2 * r),
                Stroke = new ColorRgba(255, 200, 0), Fill = new ColorRgba(255, 214, 0, 60), StrokeWidth = Math.Max(3, r / 4), Shadow = false,
                Timing = new LayerTiming { StartSeconds = Math.Max(0, Math.Round(g.T - 0.05, 2)), EndSeconds = Math.Min(_media.Duration, Math.Round(g.T + (g.Count >= 2 ? 0.9 : 0.6), 2)) },
            });
        }
        _info.Text = $"{n} Mausklick(s) markiert ({_clicks.Count - n} Doppelklick-Klick(s) zusammengefasst) – Spuren unten, im Tab „Zoom & Folien“ alle entfernbar.";
    }

    // ================================================================== time & mode

    private double CurrentTime => _slide is null ? _pos : _slidePos;
    private double CurrentDuration => _slide?.Duration ?? _media?.Duration ?? 0;
    private Page CurrentPage => _slide?.Page ?? _overlay.Page;

    private static bool ActiveAt(Layer l, double t)
    {
        if (l.Timing is not { } tm) return true;
        var end = tm.EndSeconds > tm.StartSeconds ? tm.EndSeconds : double.MaxValue;
        return t >= tm.StartSeconds - 1e-6 && t <= end + 1e-6;
    }

    /// <summary>Moving annotations are shown at their position for the playhead (selected ones stay editable where they are).</summary>
    private Page MovingView(Page view)
    {
        if (!view.Layers.Any(l => l.Timing is { Keyframes.Count: > 1 } && !_canvas.Selection.Contains(l.Id))) return view;
        var t = CurrentTime;
        var p = new Page { Id = view.Id, Width = view.Width, Height = view.Height, Background = view.Background };
        foreach (var l in view.Layers)
        {
            if (l.Timing is { Keyframes.Count: > 1 } && !_canvas.Selection.Contains(l.Id))
            {
                var c = ProjectJson.Clone(l);
                var (dx, dy) = OverlayRenderer.OffsetAt(l, t);
                c.Translate(dx, dy);
                p.Layers.Add(c);
            }
            else p.Layers.Add(l);
        }
        return p;
    }

    private void OnLayerCreated(Layer l)
    {
        var t = CurrentTime;
        var dur = CurrentDuration;
        var len = _slide is null ? Math.Max(0.5, _app.Settings.Shared.VideoAnnotationSeconds) : Math.Max(0.5, dur - t);
        l.Timing = new LayerTiming { StartSeconds = Math.Round(t, 2), EndSeconds = Math.Round(Math.Min(dur, t + len), 2) };
        if (l.Timing.EndSeconds - l.Timing.StartSeconds < 0.3) l.Timing.StartSeconds = Math.Max(0, l.Timing.EndSeconds - len);
    }

    private void MarkChanged()
    {
        if (_ownsSession || _studio is not null) _session.MarkDirty();
    }

    private void FitCanvas()
    {
        var p = CurrentPage;
        if (p.Width <= 0 || _stage.Bounds.Width < 20) return;
        var s = RenderScaling;
        _canvas.Zoom = Math.Max(0.05, Math.Min((_stage.Bounds.Width - 16) * s / p.Width, (_stage.Bounds.Height - 16) * s / p.Height));
        Dispatcher.UIThread.Post(UpdateZoomOverlay, DispatcherPriority.Background);
    }

    // ================================================================== refresh

    private void Refresh()
    {
        RebuildCutList();
        _cutTools.IsVisible = _slide is null;
        var dur = CurrentDuration;
        _timeline.Duration = Math.Max(0.1, dur);
        _timeline.Position = CurrentTime;
        _timeline.Range = _slide is null ? _clip : new ClipRange { Start = 0, End = dur };
        _timeline.PendingCut = _slide is null ? _cutA : null;
        _timeline.Selection = _slide is null ? _rangeSel : null;
        _timeline.ZoomKeys = SortedZoom().Select(z => z.TimeSeconds).ToList();
        _timeline.SelectedZoomKey = _zoomKey;
        _timeline.Slides = _overlay.Slides.Select(s => new TimelineSlide(s.Id, s.Name, s.At, s.Duration, s.Page.Background.WithAlpha(255).ToAvalonia(), s.Id == _selectedSlide)).ToList();
        RefreshTimeline();
        _timeLabel.Text = VideoTimeline.Fmt(CurrentTime) + " / " + VideoTimeline.Fmt(dur);
        _rangeLabel.Text = _slide is not null
            ? $"Folie „{_slide.Name}“ · {_slide.Duration:0.#} s"
            : $"Clip {VideoTimeline.Fmt(_clip.Start)} – {VideoTimeline.Fmt(_clip.End)}, {_clip.Cuts.Count} Schnitt(e) → Ergebnis {_clip.KeptDuration:0.0} s" +
              (_overlay.Slides.Count > 0 ? $" + {_overlay.Slides.Count} Folie(n) {_overlay.Slides.Sum(s => s.Duration):0.#} s" : "") +
              (_cutA is { } a ? $"   ✂ Schnitt ab {VideoTimeline.Fmt(a)} – Ende markieren (X) …" : "") +
              (_rangeSel is { } r ? $"   Bereich {VideoTimeline.Fmt(r.A)}–{VideoTimeline.Fmt(r.B)} markiert: X = herausschneiden" : "");
        UpdateZoomOverlay();
        _canvas.InvalidateRender();
    }

    private void RefreshTimeline()
    {
        var dur = CurrentDuration;
        _timeline.Lanes = CurrentPage.Layers.AsEnumerable().Reverse().Select(l =>
        {
            var (s, e) = l.Timing is { } t ? (t.StartSeconds, t.EndSeconds > t.StartSeconds ? t.EndSeconds : dur) : (0, dur);
            return new TimelineLane(l.Id, LayerTitle(l), s, e, l is RedactLayer or MagnifierLayer or SpotlightLayer, _canvas.Selection.Contains(l.Id));
        }).ToList();
        _timeline.InvalidateVisual();
    }

    private static string LayerTitle(Layer l) => (string.IsNullOrWhiteSpace(l.Name) ? l.Kind : l.Name) + (l is RedactLayer ? " ⚠" : "");

    private void SyncHBar()
    {
        var zoomed = _timeline.ZoomFactor > 1.001;
        _hbar.IsVisible = zoomed;
        if (!zoomed) return;
        _hbar.Minimum = 0; _hbar.Maximum = Math.Max(0, _timeline.Duration - _timeline.VisibleSeconds);
        _hbar.ViewportSize = _timeline.VisibleSeconds; _hbar.Value = _timeline.Offset;
    }

    private void RebuildCutList()
    {
        _cutList.Children.Clear();
        if (_slide is not null) return;
        var ordered = _clip.Cuts.OrderBy(c => c.From).ToList();
        if (ordered.Count == 0)
        {
            _cutList.Children.Add(Ui.Label("Keine Abschnitte zum Entfernen markiert. Auf dem Zeitlineal (oberste Zeile) einen Bereich ziehen und X drücken – oder zweimal X für Beginn und Ende.", 11, opacity: 0.6));
            return;
        }
        _cutList.Children.Add(Ui.Label("Wird entfernt:", 12, bold: true));
        foreach (var c in ordered)
        {
            var cut = c;
            _cutList.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(70, 220, 40, 40)), CornerRadius = new CornerRadius(4), Margin = new Thickness(3, 1), Padding = new Thickness(4, 0),
                Child = Ui.Row(
                    Ui.Button($"✂ {VideoTimeline.Fmt(cut.From)} – {VideoTimeline.Fmt(cut.To)} ({cut.To - cut.From:0.0} s)", () => SeekTo(cut.From, true), "Zum Abschnitt springen"),
                    Ui.Button("✕", () => { _clip.Cuts.Remove(cut); MarkChanged(); Refresh(); }, "Markierung entfernen (Abschnitt bleibt im Video)")),
            });
        }
    }

    // ================================================================== tools & style

    private void SetTool(Tool t)
    {
        _text.Commit();
        SetZoomPick(false);
        _canvas.Tool = t;
        foreach (var (tool, b) in _toolButtons) b.IsChecked = tool == t;
        _canvas.Focus();
    }

    private void SetZoomPick(bool on)
    {
        if (on && _slide is not null) on = false;
        _zoomPick = on;
        _zoomTool.IsChecked = on;
        _zoomOverlay.IsHitTestVisible = on;
        _zoomOverlay.Cursor = new Cursor(on ? StandardCursorType.Cross : StandardCursorType.Arrow);
        if (on) foreach (var b in _toolButtons.Values) b.IsChecked = false;
        else if (_toolButtons.TryGetValue(_canvas.Tool, out var tb)) tb.IsChecked = true;
        if (on) _info.Text = "Zoom: Rechteck im Video aufziehen (= sichtbarer Ausschnitt ab der Abspielposition) – oder klicken: Mittelpunkt mit dem Faktor aus „Zoom & Folien“.";
    }

    private void ApplyColor(ColorRgba c)
    {
        _canvas.Style.Color = c;
        var sel = _canvas.SelectedLayers.ToList();
        if (sel.Count > 0) _canvas.Apply("Farbe", () => { foreach (var l in sel) EditorCanvas.SetMainColor(l, c); });
    }

    private void ApplyStroke(double w)
    {
        _canvas.Style.StrokeWidth = w;
        var sel = _canvas.SelectedLayers.ToList();
        if (sel.Count == 0) return;
        _canvas.Apply("Linienstärke", () =>
        {
            foreach (var l in sel)
                switch (l)
                {
                    case ShapeLayer s: s.StrokeWidth = w; break;
                    case LineLayer ln: ln.StrokeWidth = w + 1; break;
                    case FreehandLayer f: f.StrokeWidth = f.Highlighter ? Math.Max(10, w * 4) : w; break;
                }
        }, "stroke");
    }

    private async Task InsertImageAsync()
    {
        var path = await Dialogs.OpenFileAsync(this, "Bild einfügen", _app.Env.MediaDir, ("Bilder", new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.bmp" }));
        if (path is null) return;
        var bytes = await File.ReadAllBytesAsync(path);
        using var img = SKImage.FromEncodedData(bytes);
        if (img is null) { await Dialogs.InfoAsync(this, "Bild einfügen", "Bildformat nicht erkannt."); return; }
        var png = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? bytes : SkiaConvert.EncodePng(img);
        var id = _session.Assets.Add(png);
        _session.Project.Assets[id] = new AssetInfo { Id = id, FileName = id + ".png", Width = img.Width, Height = img.Height, Length = png.LongLength };
        var p = CurrentPage;
        var f = Math.Min(1.0, Math.Min(p.Width * 0.5 / img.Width, p.Height * 0.5 / img.Height));
        var layer = new ImageLayer { AssetId = id, Name = Path.GetFileNameWithoutExtension(path), Rect = new RectD((p.Width - img.Width * f) / 2, (p.Height - img.Height * f) / 2, img.Width * f, img.Height * f), Shadow = true, BorderWidth = 1 };
        _canvas.Apply("Bild einfügen", () => { OnLayerCreated(layer); p.Layers.Add(layer); });
        _canvas.Select(new[] { layer.Id });
    }

    private void SelectLayer(Guid id)
    {
        if (CurrentPage.Find(id) is not { } l) return;
        // jump into the time window, otherwise the annotation is not visible/editable
        if (!ActiveAt(l, CurrentTime) && l.Timing is { } tm) SeekTo(tm.StartSeconds + 0.01, true);
        _canvas.Select(new[] { id });
    }

    private void SetTiming(Guid id, double start, double end)
    {
        if (CurrentPage.Find(id) is not { } layer) return;
        var tm = layer.Timing ??= new LayerTiming();
        var oldS = tm.StartSeconds; var oldE = tm.EndSeconds;
        tm.StartSeconds = start; tm.EndSeconds = end;
        // movement follows the time window
        if (tm.Keyframes.Count >= 2)
        {
            var k = tm.Keyframes.OrderBy(x => x.TimeSeconds).ToList();
            k[0].TimeSeconds = start; k[^1].TimeSeconds = end;
        }
        _ = oldS; _ = oldE;
        if (!ActiveAt(layer, CurrentTime)) _canvas.Select(Array.Empty<Guid>());
        RefreshTimeline();
        _canvas.InvalidateRender();
    }

    // ================================================================== time box (selected annotation)

    private void BuildTimeBox()
    {
        _timeBox.Children.Clear();
        var sel = _canvas.SelectedLayers.ToList();
        if (sel.Count != 1)
        {
            _timeBox.Children.Add(Ui.Label("Zeit & Reihenfolge", 13, bold: true));
            _timeBox.Children.Add(Ui.Label(sel.Count == 0
                ? "Werkzeug links wählen und direkt ins Video zeichnen. Neue Annotationen erscheinen ab der Abspielposition für " + $"{_app.Settings.Shared.VideoAnnotationSeconds:0.#} s – unten als Spur, dort verschieben oder an den Rändern kürzen/verlängern."
                : $"{sel.Count} Annotationen ausgewählt.", 11, opacity: 0.7));
            var def = new NumericUpDown { Value = (decimal)_app.Settings.Shared.VideoAnnotationSeconds, Minimum = 0.5m, Maximum = 600, Increment = 0.5m, FormatString = "0.#", Width = 110 };
            def.ValueChanged += (_, _) => { _app.Settings.Shared.VideoAnnotationSeconds = (double)(def.Value ?? 4); _app.SaveSettings(); };
            _timeBox.Children.Add(Ui.Row(Ui.Label("Dauer neuer Annotationen (s)", 11), def));
            return;
        }
        var layer = sel[0];
        var tm = layer.Timing ??= new LayerTiming { StartSeconds = 0, EndSeconds = CurrentDuration };
        _timeBox.Children.Add(Ui.Label("Zeit & Reihenfolge – " + LayerTitle(layer), 13, bold: true));
        var start = Num(tm.StartSeconds); var end = Num(tm.EndSeconds);
        start.ValueChanged += (_, _) => _canvas.Apply("Beginn", () => { tm.StartSeconds = (double)(start.Value ?? 0); if (tm.EndSeconds < tm.StartSeconds + 0.1) tm.EndSeconds = tm.StartSeconds + 0.1; SyncMoveTimes(tm); }, "t-start");
        end.ValueChanged += (_, _) => _canvas.Apply("Ende", () => { tm.EndSeconds = Math.Max(tm.StartSeconds + 0.1, (double)(end.Value ?? 0)); SyncMoveTimes(tm); }, "t-end");
        _timeBox.Children.Add(Ui.Row(Ui.Label("von", 11), start, Ui.Button("⇤", () => start.Value = (decimal)Math.Round(CurrentTime, 2), "Beginn = Abspielposition")));
        _timeBox.Children.Add(Ui.Row(Ui.Label("bis ", 11), end, Ui.Button("⇥", () => end.Value = (decimal)Math.Round(CurrentTime, 2), "Ende = Abspielposition")));
        var moving = tm.Keyframes.Count >= 2;
        var move = new CheckBox { Content = "Bewegt sich (von Start- zu Endlage)", IsChecked = moving };
        move.IsCheckedChanged += (_, _) => _canvas.Apply("Bewegung", () =>
        {
            tm.Keyframes.Clear();
            if (move.IsChecked == true)
            {
                var b = layer.Bounds;
                tm.Keyframes.Add(new Keyframe { TimeSeconds = tm.StartSeconds, Bounds = b });
                tm.Keyframes.Add(new Keyframe { TimeSeconds = tm.EndSeconds, Bounds = b with { X = b.X + Math.Max(60, b.Width) } });
            }
            Dispatcher.UIThread.Post(BuildTimeBox);
        });
        _timeBox.Children.Add(move);
        if (moving)
        {
            _timeBox.Children.Add(Ui.Label("Objekt an die gewünschte Stelle ziehen, dann übernehmen:", 11, opacity: 0.7));
            _timeBox.Children.Add(Ui.Row(
                Ui.Button("◉ Startlage = jetzt", () => _canvas.Apply("Startlage", () => tm.Keyframes.OrderBy(k => k.TimeSeconds).First().Bounds = layer.Bounds), "Aktuelle Lage als Position zu Beginn"),
                Ui.Button("◎ Endlage = jetzt", () => _canvas.Apply("Endlage", () => tm.Keyframes.OrderBy(k => k.TimeSeconds).Last().Bounds = layer.Bounds), "Aktuelle Lage als Position am Ende")));
        }
        _timeBox.Children.Add(Ui.Row(
            Ui.Button("▲ Nach vorne", () => _canvas.MoveSelection(+1), "Über die Ebene darüber (Strg+])"),
            Ui.Button("▼ Nach hinten", () => _canvas.MoveSelection(-1), "Unter die Ebene darunter (Strg+[)")));
        _timeBox.Children.Add(Ui.Label("Oben in der Spurliste = vorne. Eine Schwärzung verdeckt nur, was unter ihr liegt.", 10.5, opacity: 0.6));

        static NumericUpDown Num(double v) => new() { Value = (decimal)Math.Round(v, 2), Minimum = 0, Maximum = 100000, Increment = 0.1m, FormatString = "0.##", Width = 120 };
    }

    private static void SyncMoveTimes(LayerTiming tm)
    {
        if (tm.Keyframes.Count < 2) return;
        var k = tm.Keyframes.OrderBy(x => x.TimeSeconds).ToList();
        k[0].TimeSeconds = tm.StartSeconds; k[^1].TimeSeconds = tm.EndSeconds;
    }

    // ================================================================== zoom & slides panel

    private List<ZoomKeyframe> SortedZoom() => _overlay.Zoom.OrderBy(z => z.TimeSeconds).ToList();

    private void BuildVideoBox()
    {
        _videoBox.Children.Clear();
        _videoBox.Children.Add(Ui.Label("Zoom & Schwenk", 14, bold: true));
        _videoBox.Children.Add(Ui.Label("Werkzeug ⤢ links: im Video ein Rechteck aufziehen = dieser Ausschnitt ab der Abspielposition. Zwischen Keyframes wird weich übergeblendet. Schwärzungen bleiben auf ihrem Inhalt.", 11, opacity: 0.65));
        // the slider is reused across rebuilds → detach it from its old row first
        if (_zoomFactor.Parent is Panel oldRow) oldRow.Children.Remove(_zoomFactor);
        _videoBox.Children.Add(Ui.Row(Ui.Label("Faktor bei Klick", 11), _zoomFactor));
        _videoBox.Children.Add(Ui.Row(
            Ui.Button("🔍 Keyframe hier", () => AddZoomKey(null, _zoomFactor.Value), "Zoom auf die Bildmitte mit dem Faktor"),
            Ui.Button("1× hier", () => AddZoomKey(null, 1), "Hier wieder ganz herauszoomen")));
        var keys = SortedZoom();
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i]; var idx = i;
            var factor = _media is null ? 1 : _media.Width / Math.Max(1, key.View.Width);
            var row = Ui.Row(
                Ui.Button($"{(idx == _zoomKey ? "◆" : "◇")} {VideoTimeline.Fmt(key.TimeSeconds)}  {factor:0.0}×", () => { _zoomKey = idx; SeekTo(key.TimeSeconds, true); BuildVideoBox(); }, "Zum Keyframe springen"),
                Ui.Button("✕", () => { _overlay.Zoom.Remove(key); _zoomKey = null; MarkChanged(); Refresh(); BuildVideoBox(); }, "Keyframe löschen"));
            _videoBox.Children.Add(row);
        }

        _videoBox.Children.Add(new Separator());
        _videoBox.Children.Add(Ui.Label("Folien", 14, bold: true));
        _videoBox.Children.Add(Ui.Label("Eigene Bilder zwischen den Videoabschnitten – z. B. Titel, Zusammenfassung, Checkliste. Das Video pausiert für die Dauer der Folie. Elemente können nacheinander eingeblendet werden.", 11, opacity: 0.65));
        _videoBox.Children.Add(new WrapPanel
        {
            Children =
            {
                Ui.Button("＋ Leer", () => AddSlide(SlideTemplate.Blank), "Leere Folie an der Abspielposition"),
                Ui.Button("＋ Titel", () => AddSlide(SlideTemplate.Title), "Titelfolie"),
                Ui.Button("＋ Zusammenfassung", () => AddSlide(SlideTemplate.Summary), "Überschrift + Checkliste, Punkte erscheinen nacheinander"),
            },
        });
        foreach (var s in _overlay.Slides.OrderBy(s => s.At))
        {
            var slide = s;
            _videoBox.Children.Add(new Border
            {
                BorderBrush = slide.Id == _selectedSlide ? Brushes.Gold : new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(4),
                Child = Ui.Row(
                    Ui.Label($"▣ {slide.Name}\n   bei {VideoTimeline.Fmt(slide.At)} · {slide.Duration:0.#} s", 11),
                    Ui.Button("✎", () => EnterSlide(slide), "Folie bearbeiten"),
                    Ui.Button("✕", () => RemoveSlide(slide), "Folie löschen")),
            });
        }

        _videoBox.Children.Add(new Separator());
        var clickCount = _overlay.Page.Layers.Count(l => l.Name.StartsWith("Klick ", StringComparison.Ordinal) || l.Name.StartsWith("Doppelklick ", StringComparison.Ordinal));
        _videoBox.Children.Add(Ui.Label("Mausklick-Markierungen", 14, bold: true));
        _videoBox.Children.Add(Ui.Label(clickCount == 0 ? "Keine. (Einstellungen → Video: „Mausklicks markieren“)" : $"{clickCount} Markierung(en) – normale Annotationen, einzeln änderbar.", 11, opacity: 0.65));
        if (clickCount > 0)
            _videoBox.Children.Add(Ui.Button("Alle Klick-Markierungen entfernen", () =>
            {
                _canvas.Apply("Klicks entfernen", () => _overlay.Page.Layers.RemoveAll(l => l is ShapeLayer && (l.Name.StartsWith("Klick ", StringComparison.Ordinal) || l.Name.StartsWith("Doppelklick ", StringComparison.Ordinal))));
                BuildVideoBox();
            }));
    }

    private void AddZoomKey(RectD? view, double factor)
    {
        if (_media is null || _slide is not null) return;
        RectD v;
        if (view is { } r) v = r;
        else
        {
            var w = _media.Width / Math.Max(1, factor); var h = _media.Height / Math.Max(1, factor);
            var (cx, cy) = (_zoomOverlay.LastCenter is { } c ? (c.X, c.Y) : (_media.Width / 2.0, _media.Height / 2.0));
            v = new RectD(Math.Clamp(cx - w / 2, 0, _media.Width - w), Math.Clamp(cy - h / 2, 0, _media.Height - h), w, h);
        }
        // first zoom: start from the full frame shortly before, so it zooms in smoothly
        if (_overlay.Zoom.Count == 0 && v.Width < _media.Width - 1 && _pos > 0.5)
            _overlay.Zoom.Add(new ZoomKeyframe { TimeSeconds = Math.Max(0, Math.Round(_pos - 1, 2)), View = new RectD(0, 0, _media.Width, _media.Height) });
        _overlay.Zoom.RemoveAll(z => Math.Abs(z.TimeSeconds - _pos) < 0.05);
        _overlay.Zoom.Add(new ZoomKeyframe { TimeSeconds = Math.Round(_pos, 2), View = v });
        _zoomKey = SortedZoom().FindIndex(z => Math.Abs(z.TimeSeconds - _pos) < 0.06);
        MarkChanged();
        Refresh();
        BuildVideoBox();
    }

    private void OnZoomPicked(Rect viewRect)
    {
        if (_media is null) return;
        var s = _canvas.ViewScale;
        if (viewRect.Width < 6 || viewRect.Height < 6)
        {
            _zoomOverlay.LastCenter = new Point(viewRect.X / s, viewRect.Y / s);
            AddZoomKey(null, _zoomFactor.Value);
            return;
        }
        var aspect = _media.Width / (double)_media.Height;
        var w = Math.Max(viewRect.Width / s, viewRect.Height / s * aspect);
        w = Math.Clamp(w, 16, _media.Width);
        var h = w / aspect;
        var cx = (viewRect.X + viewRect.Width / 2) / s; var cy = (viewRect.Y + viewRect.Height / 2) / s;
        AddZoomKey(new RectD(Math.Clamp(cx - w / 2, 0, _media.Width - w), Math.Clamp(cy - h / 2, 0, _media.Height - h), w, h), 1);
    }

    private void UpdateZoomOverlay()
    {
        _zoomOverlay.ViewRect = _slide is null && ZoomViewAt(_pos) is { } v ? _canvas.ToView(v) : null;
        _zoomOverlay.InvalidateVisual();
    }

    /// <summary>Interpolated zoom view at time t (null = full frame / no zoom).</summary>
    private RectD? ZoomViewAt(double t)
    {
        var z = SortedZoom();
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

    // ================================================================== slides

    private enum SlideTemplate { Blank, Title, Summary }

    private void ShowSlideMenu(Control anchor)
    {
        var menu = new MenuFlyout();
        void Item(string text, SlideTemplate t) { var mi = new MenuItem { Header = text }; mi.Click += (_, _) => AddSlide(t); menu.Items.Add(mi); }
        Item("Leere Folie", SlideTemplate.Blank);
        Item("Titelfolie", SlideTemplate.Title);
        Item("Zusammenfassung / Checkliste", SlideTemplate.Summary);
        menu.ShowAt(anchor);
    }

    public VideoSlide AddSlide(int template) => AddSlide((SlideTemplate)template);

    private VideoSlide AddSlide(SlideTemplate t)
    {
        var w = Math.Max(320, _overlay.Page.Width); var h = Math.Max(180, _overlay.Page.Height);
        var dark = new ColorRgba(28, 30, 38);
        var slide = new VideoSlide
        {
            Name = t switch { SlideTemplate.Title => "Titel", SlideTemplate.Summary => "Zusammenfassung", _ => $"Folie {_overlay.Slides.Count + 1}" },
            At = Math.Round(_slide is null ? _pos : _slide.At, 2), Duration = t == SlideTemplate.Summary ? 8 : 5,
            Page = new Page { Width = w, Height = h, Background = dark, Name = "Folie" },
        };
        var fs = Math.Max(18, h / 12.0);
        TextLayer Txt(string text, double size, double y, double start, bool bold = false, Core.Model.TextAlignment align = Core.Model.TextAlignment.Left, double x = -1)
        {
            var tl = new TextLayer { Text = text, FontSize = size, Bold = bold, Foreground = ColorRgba.White, Alignment = align, Padding = 4, Name = text.Split('\n')[0], FontFamily = _canvas.Style.FontFamily };
            tl.Rect = new RectD(0, y, 0, 0);
            var fit = TextLayout.FitBox(tl);
            tl.Rect = new RectD(x >= 0 ? x : (w - fit.Width) / 2, y, fit.Width, fit.Height);
            tl.Timing = new LayerTiming { StartSeconds = start, EndSeconds = slide.Duration };
            return tl;
        }
        switch (t)
        {
            case SlideTemplate.Title:
                slide.Page.Layers.Add(Txt("Titel des Videos", fs * 1.4, h * 0.36, 0, bold: true));
                slide.Page.Layers.Add(Txt("Untertitel / Worum geht es?", fs * 0.7, h * 0.36 + fs * 2.2, 0.5));
                break;
            case SlideTemplate.Summary:
                slide.Page.Layers.Add(Txt("Zusammenfassung", fs * 1.1, h * 0.12, 0, bold: true, x: w * 0.1));
                for (var i = 0; i < 3; i++)
                    slide.Page.Layers.Add(Txt($"☑  Punkt {i + 1}", fs * 0.75, h * 0.32 + i * fs * 1.5, 1 + i * 1.5, x: w * 0.12));
                break;
        }
        _overlay.Slides.Add(slide);
        MarkChanged();
        EnterSlide(slide);
        return slide;
    }

    private void RemoveSlide(VideoSlide s)
    {
        if (_slide == s) ExitSlide();
        _overlay.Slides.Remove(s);
        if (_selectedSlide == s.Id) _selectedSlide = null;
        MarkChanged();
        Refresh(); BuildVideoBox();
    }

    public void EnterSlide(VideoSlide s)
    {
        if (_playTimer.IsEnabled) TogglePlay();
        _text.Commit();
        SetZoomPick(false);
        _slide = s; _selectedSlide = s.Id; _slidePos = 0;
        _canvas.SetPage(s.Page);
        _canvas.Underlay = null;
        _timeline.SlideMode = s.Page.Background.WithAlpha(255).ToAvalonia();
        _timeline.ZoomFactor = 1;
        BuildSlideBanner();
        _slideBanner.IsVisible = true;
        FitCanvas();
        Refresh(); BuildVideoBox(); BuildTimeBox();
        _info.Text = $"Folie „{s.Name}“: Elemente mit den Werkzeugen links hinzufügen. Jedes Element hat unten eine Spur – so erscheinen Punkte nacheinander. „✓ Zurück zum Video“ beendet die Bearbeitung.";
    }

    public void ExitSlide()
    {
        if (_slide is null) return;
        if (_playTimer.IsEnabled) TogglePlay();
        _text.Commit();
        _slide = null;
        _canvas.SetPage(_overlay.Page);
        _timeline.SlideMode = null;
        _slideBanner.IsVisible = false;
        FitCanvas();
        Refresh(); BuildVideoBox(); BuildTimeBox();
        SeekTo(_pos, true);
    }

    private void BuildSlideBanner()
    {
        if (_slide is not { } s) return;
        var name = new TextBox { Text = s.Name, Width = 170 };
        name.TextChanged += (_, _) => { s.Name = name.Text ?? "Folie"; MarkChanged(); Refresh(); };
        var dur = new NumericUpDown { Value = (decimal)s.Duration, Minimum = 0.5m, Maximum = 600, Increment = 0.5m, FormatString = "0.#", Width = 110 };
        dur.ValueChanged += (_, _) =>
        {
            var old = s.Duration; var nd = (double)(dur.Value ?? 5);
            // elements shown "until the end" keep lasting until the (new) end
            foreach (var l in s.Page.Layers) if (l.Timing is { } tm && tm.EndSeconds >= old - 0.01) tm.EndSeconds = nd;
            s.Duration = nd; MarkChanged(); Refresh();
        };
        var colors = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in new[] { new ColorRgba(28, 30, 38), ColorRgba.White, new ColorRgba(0, 84, 166), new ColorRgba(20, 110, 60), new ColorRgba(120, 40, 140), ColorRgba.Black })
        {
            var cc = c;
            var b = new Button { Width = 20, Height = 20, Margin = new Thickness(1), Padding = new Thickness(0), Background = new SolidColorBrush(c.ToAvalonia()), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
            ToolTip.SetTip(b, "Hintergrundfarbe");
            b.Click += (_, _) =>
            {
                _canvas.Apply("Folienfarbe", () => s.Page.Background = cc);
                // readable default text colour on light backgrounds
                if (cc.R + cc.G + cc.B > 600) _canvas.Apply("Textfarbe", () => { foreach (var t in s.Page.Layers.OfType<TextLayer>().Where(t => t.Foreground == ColorRgba.White)) t.Foreground = new ColorRgba(30, 30, 30); });
                else _canvas.Apply("Textfarbe", () => { foreach (var t in s.Page.Layers.OfType<TextLayer>().Where(t => t.Foreground == new ColorRgba(30, 30, 30))) t.Foreground = ColorRgba.White; });
                _timeline.SlideMode = cc.WithAlpha(255).ToAvalonia();
                Refresh();
            };
            colors.Children.Add(b);
        }
        var at = new NumericUpDown { Value = (decimal)s.At, Minimum = 0, Maximum = (decimal)Math.Max(0.1, _media?.Duration ?? 0), Increment = 0.5m, FormatString = "0.##", Width = 110 };
        at.ValueChanged += (_, _) => { s.At = (double)(at.Value ?? 0); MarkChanged(); };
        _slideBanner.Child = new WrapPanel
        {
            Children =
            {
                Ui.Label("▣ Folie ", 13, bold: true), name,
                Ui.Label("  Dauer s", 11), dur, Ui.Label("  bei Video-Sek.", 11), at,
                Ui.Label("  Hintergrund", 11), colors,
                Ui.Button("✓ Zurück zum Video", ExitSlide, "Folienbearbeitung beenden", accent: true),
                Ui.Button("🗑 Folie löschen", () => RemoveSlide(s)),
            },
        };
    }

    // ================================================================== playback

    private void SeekTo(double t, bool hiRes)
    {
        if (_slide is not null)
        {
            _slidePos = Math.Clamp(t, 0, _slide.Duration);
            PruneSelection();
            Refresh();
            return;
        }
        if (_media is null) return;
        _pos = Math.Clamp(t, 0, _media.Duration);
        if (_jpegs.Count > 0)
        {
            var idx = Math.Clamp((int)(_pos * _previewFps), 0, _jpegs.Count - 1);
            SetFrame(SKImage.FromEncodedData(_jpegs[idx]));
        }
        PruneSelection();
        Refresh();
        if (_playTimer.IsEnabled) _timeline.EnsureVisible(_pos);
        if (hiRes) _ = LoadHiResAsync(_pos);
    }

    /// <summary>Annotations outside the playhead are hidden → also deselect them.</summary>
    private void PruneSelection()
    {
        var keep = _canvas.SelectedLayers.Where(l => ActiveAt(l, CurrentTime)).Select(l => l.Id).ToList();
        if (keep.Count != _canvas.Selection.Count) _canvas.Select(keep);
    }

    private void SetFrame(SKImage? img)
    {
        var old = _frame;
        _frame = img;
        if (_slide is null) _canvas.Underlay = img;
        old?.Dispose();
    }

    private async Task LoadHiResAsync(double t)
    {
        _hiResCts?.Cancel();
        var cts = _hiResCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(120, cts.Token);
            var png = await Task.Run(() => _exporter.ExtractFrameAsync(_file, t, cts.Token), cts.Token);
            if (!cts.IsCancellationRequested && !_playTimer.IsEnabled && _slide is null && Math.Abs(t - _pos) < 1e-6) SetFrame(SKImage.FromEncodedData(png));
        }
        catch (OperationCanceledException) { }
        catch (InvalidDataException) { }
    }

    private void TogglePlay()
    {
        if (_playTimer.IsEnabled) { _playTimer.Stop(); _play.Content = "▶"; SeekTo(CurrentTime, true); return; }
        _text.Commit();
        if (_slide is not null) { if (_slidePos >= _slide.Duration - 0.05) _slidePos = 0; }
        else if (_pos >= _clip.End - 0.05) _pos = _clip.Start;
        _playTimer.Interval = TimeSpan.FromSeconds(1 / (_slide is null ? _previewFps : 15));
        _playTimer.Start();
        _play.Content = "❚❚";
    }

    private void PlayStep()
    {
        if (_slide is not null)
        {
            var ns = _slidePos + 1 / 15.0;
            if (ns >= _slide.Duration) { TogglePlay(); SeekTo(_slide.Duration, false); return; }
            SeekTo(ns, false);
            return;
        }
        var next = _pos + 1 / _previewFps;
        foreach (var (a, b) in _clip.Cuts) if (next >= a && next < b) next = b; // preview skips removed sections
        if (next >= _clip.End) { TogglePlay(); SeekTo(_clip.End, true); return; }
        SeekTo(next, false);
    }

    private void StepFrame(int dir)
    {
        var frame = _slide is not null ? 1 / 15.0 : 1 / Math.Max(1, _media?.Fps ?? 30);
        SeekTo(CurrentTime + dir * frame, true);
    }

    private void SetRange(bool start)
    {
        if (_slide is not null) return;
        if (start) _clip.Start = Math.Min(_pos, _clip.End - 0.1); else _clip.End = Math.Max(_pos, _clip.Start + 0.1);
        MarkChanged();
        Refresh();
    }

    private void MarkCut()
    {
        if (_slide is not null) return;
        if (_rangeSel is { } r && r.B - r.A >= 0.05)
        {
            _clip.Cuts.Add((Math.Round(r.A, 2), Math.Round(r.B, 2)));
            _rangeSel = null; _timeline.Selection = null;
            MarkChanged(); Refresh();
            return;
        }
        if (_cutA is null) { _cutA = _pos; Refresh(); return; }
        var a = Math.Min(_cutA.Value, _pos); var b = Math.Max(_cutA.Value, _pos);
        _cutA = null;
        if (b - a >= 0.05) { _clip.Cuts.Add((a, b)); MarkChanged(); }
        else _info.Text = "Schnitt zu kurz – Abspielkopf zwischen den beiden X verschieben (oder auf dem Zeitlineal einen Bereich ziehen).";
        Refresh();
    }

    private void OnKey(object? s, KeyEventArgs e)
    {
        if (_text.IsFocused || FocusManager?.GetFocusedElement() is TextBox) return;
        if (FocusManager?.GetFocusedElement() is NumericUpDown && e.Key is not (Key.Space or Key.Escape)) return;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var hasSel = _canvas.Selection.Count > 0;
        switch (e.Key)
        {
            case Key.Space: TogglePlay(); break;
            case Key.Z when ctrl && shift: _canvas.Redo(); break;
            case Key.Z when ctrl: _canvas.Undo(); break;
            case Key.Y when ctrl: _canvas.Redo(); break;
            case Key.D when ctrl: _canvas.DuplicateSelection(); break;
            case Key.OemCloseBrackets when ctrl: _canvas.MoveSelection(shift ? int.MaxValue : 1); break;
            case Key.OemOpenBrackets when ctrl: _canvas.MoveSelection(shift ? int.MinValue : -1); break;
            case Key.Left when hasSel && !ctrl: _canvas.Nudge(shift ? -10 : -1, 0); break;
            case Key.Right when hasSel && !ctrl: _canvas.Nudge(shift ? 10 : 1, 0); break;
            case Key.Up when hasSel: _canvas.Nudge(0, shift ? -10 : -1); break;
            case Key.Down when hasSel: _canvas.Nudge(0, shift ? 10 : 1); break;
            case Key.Left: SeekTo(CurrentTime - (shift ? 1 : 1 / Math.Max(1, _media?.Fps ?? 30)), true); break;
            case Key.Right: SeekTo(CurrentTime + (shift ? 1 : 1 / Math.Max(1, _media?.Fps ?? 30)), true); break;
            case Key.OemComma: StepFrame(-1); break;
            case Key.OemPeriod: StepFrame(1); break;
            case Key.Home: SeekTo(_slide is null ? _clip.Start : 0, true); break;
            case Key.End: SeekTo(_slide is null ? _clip.End : _slide.Duration, true); break;
            case Key.I when !ctrl: SetRange(true); break;
            case Key.O when !ctrl: SetRange(false); break;
            case Key.X when !ctrl: MarkCut(); break;
            case Key.Delete or Key.Back:
                if (hasSel) { _canvas.DeleteSelection(); break; }
                var hit = _clip.Cuts.FirstOrDefault(c => _pos >= c.From && _pos <= c.To);
                if (_slide is null && hit != default) { _clip.Cuts.Remove(hit); MarkChanged(); Refresh(); }
                else if (_zoomKey is { } zk && zk < _overlay.Zoom.Count) { _overlay.Zoom.Remove(SortedZoom()[zk]); _zoomKey = null; MarkChanged(); Refresh(); BuildVideoBox(); }
                else if (_cutA is not null) MarkCut();
                break;
            case Key.Escape:
                if (_cutA is not null || _rangeSel is not null) { _cutA = null; _rangeSel = null; _timeline.Selection = null; Refresh(); }
                else if (hasSel) _canvas.Select(Array.Empty<Guid>());
                else if (_zoomPick) SetZoomPick(false);
                else if (_slide is not null) ExitSlide();
                else SetTool(Tool.Select);
                break;
            case Key.V when !ctrl: SetTool(Tool.Select); break;
            case Key.A when !ctrl: SetTool(Tool.Arrow); break;
            case Key.R when !ctrl: SetTool(Tool.Rectangle); break;
            case Key.E when !ctrl: SetTool(Tool.Ellipse); break;
            case Key.T when !ctrl: SetTool(Tool.Text); break;
            case Key.N when !ctrl: SetTool(Tool.Step); break;
            case Key.F when !ctrl: SetTool(Tool.Freehand); break;
            case Key.G when !ctrl: SetTool(Tool.Marker); break;
            case Key.H when !ctrl: SetTool(Tool.Highlight); break;
            case Key.S when !ctrl: SetTool(Tool.Spotlight); break;
            case Key.M when !ctrl: SetTool(Tool.Magnifier); break;
            case Key.B when !ctrl: SetTool(Tool.Blackout); break;
            case Key.P when !ctrl: SetTool(Tool.Pixelate); break;
            case Key.Z when !ctrl: SetZoomPick(!_zoomPick); break;
            default: return;
        }
        e.Handled = true;
    }

    // ================================================================== cuts / export / guide

    private ClipRange CurrentClip() => new() { Start = _clip.Start, End = _clip.End, Cuts = _clip.Cuts.ToList() };

    private async Task<VideoRenderPlan.Prepared?> PrepareAsync(string workDir)
    {
        var progress = new Progress<string>(s => _info.Text = s);
        try
        {
            IsEnabled = false;
            return await VideoRenderPlan.PrepareAsync(_app, _session, _file, CurrentClip(), _overlay, workDir, progress);
        }
        catch (InvalidOperationException ex) { await Dialogs.InfoAsync(this, "Vorbereitung fehlgeschlagen", ex.Message); return null; }
        finally { IsEnabled = true; }
    }

    private async Task ApplyCutsAsync()
    {
        if (_media is null) return;
        if (_slide is not null) ExitSlide();
        var nothing = _clip.Cuts.Count == 0 && _clip.Start <= 0.01 && _clip.End >= _media.Duration - 0.01 && _overlay.Slides.Count == 0;
        if (nothing) { await Dialogs.InfoAsync(this, "Schnitte anwenden", "Es ist nichts markiert. Mit „[ Start hier“, „Ende hier ]“ oder „✂ Schnitt“ festlegen, was entfernt werden soll."); return; }
        if (_playTimer.IsEnabled) TogglePlay();
        if (OverlayRenderer.HasContent(_overlay) && await Dialogs.AskAsync(this, "Annotationen einbrennen?",
                "Beim Schneiden verschieben sich die Zeiten. Annotationen, Zoom und Folien werden deshalb fest in die neue Fassung eingebrannt und sind danach nicht mehr einzeln bearbeitbar.\n\nTipp: Ohne „Schnitte anwenden“ werden Schnitte beim Export trotzdem berücksichtigt – dann bleibt alles bearbeitbar.",
                "Einbrennen und schneiden", "Abbrechen") != "Einbrennen und schneiden") return;
        var dir = Path.GetDirectoryName(_file)!;
        var name = Core.Obsidian.SafeFileName.MakeUnique(Path.GetFileNameWithoutExtension(_file) + "_geschnitten.mkv", n => File.Exists(Path.Combine(dir, n)), slugStyle: false);
        var output = Path.Combine(dir, name);
        var work = Path.Combine(_app.Env.TempDir, $"cut-{Guid.NewGuid():N}");
        try
        {
            var plan = await PrepareAsync(work);
            if (plan is null) return;
            IsEnabled = false;
            var progress = new Progress<string>(s => _info.Text = s);
            if (plan.Composed) File.Move(plan.Source, output);
            else await Task.Run(() => _exporter.RenderEditedAsync(plan.Source, output, plan.Clip, progress, default, plan.Overlay));
            Log.Info($"Cuts applied: {_clip.Cuts.Count} cuts, kept {_clip.KeptDuration:0.0}s, slides={_overlay.Slides.Count}");
            if (_studio is not null && _step?.Video is { } v)
            {
                v.Path = VideoStepRenderer.Store(_app, _session, output);
                var info = await MediaProbe.ProbeAsync(_tools, output);
                v.Start = 0; v.End = info.Duration; v.Cuts.Clear(); v.Overlay = null;
                _session.MarkDirty();
                _skipWriteBack = true;
                new VideoEditorWindow(_app, output, _step, _studio).Show();
            }
            else new VideoEditorWindow(_app, output).Show();
            Close();
        }
        catch (InvalidOperationException ex) { await Dialogs.InfoAsync(this, "Schneiden fehlgeschlagen", ex.Message); }
        finally
        {
            IsEnabled = true;
            try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private async Task ExportAsync(bool gif)
    {
        if (_media is null) return;
        if (_playTimer.IsEnabled) TogglePlay();
        _text.Commit();
        var work = Path.Combine(_app.Env.TempDir, $"export-{Guid.NewGuid():N}");
        try
        {
            var plan = await PrepareAsync(work);
            if (plan is null) return;
            var info = plan.Composed ? await MediaProbe.ProbeAsync(_tools, plan.Source) : _media;
            var dlg = new VideoExportDialog(_app, _exporter, plan.Source, info, plan.Clip, gif, plan.Overlay, Path.GetFileNameWithoutExtension(_file));
            await dlg.ShowDialog(this);
        }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Saves clip and annotations into the guide step (studio) or the step of the side-car project.</summary>
    private void WriteBack()
    {
        if (_skipWriteBack || _step?.Video is not { } v) return;
        VideoStepRenderer.SetClip(v, _clip);
        v.Overlay = OverlayRenderer.HasContent(_overlay) ? _overlay : null;
        _session.MarkDirty();
    }

    /// <summary>Annotations of a recording are kept in "&lt;video&gt;.capturedoc" next to it (also opens in the studio).</summary>
    private void SaveSidecar()
    {
        if (!_ownsSession || _sidecar is null || _skipWriteBack || _media is null) return;
        var trimmed = _clip.Cuts.Count > 0 || _clip.Start > 0.01 || _clip.End < _media.Duration - 0.01;
        if (!OverlayRenderer.HasContent(_overlay) && !trimmed && !File.Exists(_sidecar)) return;
        if (!_session.IsDirty && File.Exists(_sidecar)) return;
        try
        {
            if (_step is null)
            {
                _step = new GuideStep { Title = Path.GetFileNameWithoutExtension(_file), Video = new VideoRef { IncludeAudio = _media.HasAudio, MaxWidth = Math.Min(1280, _media.Width) } };
                _session.Project.Guide.Steps.Add(_step);
                _session.Project.Guide.Title = Path.GetFileNameWithoutExtension(_file);
            }
            var v = _step.Video!;
            v.Path = ProjectFile.ToProjectRelative(null, _file, ProjectSession.ToPortable);
            VideoStepRenderer.SetClip(v, _clip);
            v.Overlay = _overlay;
            _session.Save(_sidecar, force: true);
            Log.Info("Video annotations saved to " + _sidecar);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ProjectFormatException)
        {
            Log.Warn("Video side-car not saved: " + ex.Message);
        }
    }

    private async Task<(SKImage img, byte[] png)?> GrabFrameAsync(double? at = null)
    {
        try
        {
            var png = await Task.Run(() => _exporter.ExtractFrameAsync(_file, at ?? _pos));
            var img = SKImage.FromEncodedData(png);
            return img is null ? null : (img, png);
        }
        catch (InvalidDataException ex) { await Dialogs.InfoAsync(this, "Frame", ex.Message); return null; }
    }

    /// <summary>Current frame (or slide) as an editable screenshot: background + clones of the visible annotations.</summary>
    private async Task<(ProjectSession Session, Page Page)?> FrameAsSessionAsync()
    {
        string title;
        ProjectSession session;
        Page page;
        if (_slide is { } s)
        {
            using var flat = _session.Renderer.RenderPage(new Page { Width = s.Page.Width, Height = s.Page.Height, Background = s.Page.Background });
            var png = SkiaConvert.EncodePng(flat);
            title = s.Name;
            session = ProjectSession.FromCapture(flat, png, title);
            page = session.Project.Pages[0];
        }
        else
        {
            if (await GrabFrameAsync() is not { } f) return null;
            title = $"{Path.GetFileNameWithoutExtension(_file)} {VideoTimeline.Fmt(_pos).Replace(':', '-')}";
            session = ProjectSession.FromCapture(f.img, f.png, title);
            page = session.Project.Pages[0];
        }
        foreach (var l in CurrentPage.Layers.Where(l => ActiveAt(l, CurrentTime)))
        {
            var c = PageOperations.CloneWithNewIds(l);
            c.Timing = null;
            var (dx, dy) = OverlayRenderer.OffsetAt(l, CurrentTime);
            c.Translate(dx, dy);
            foreach (var im in (c is GroupLayer g ? g.AllChildren().Prepend(c) : new[] { c }).OfType<ImageLayer>())
                if (_session.Assets.Get(im.AssetId) is { } bytes)
                {
                    session.Assets.Add(bytes);
                    if (_session.Project.Assets.TryGetValue(im.AssetId, out var info)) session.Project.Assets[im.AssetId] = info;
                }
            page.Layers.Add(c);
        }
        return (session, page);
    }

    private async Task FrameToEditorAsync()
    {
        if (await FrameAsSessionAsync() is not { } f) return;
        new EditorWindow(_app, f.Session, f.Page).Show();
    }

    private async Task FrameToGuideAsync()
    {
        if (await FrameAsSessionAsync() is not { } f) return;
        var studio = App.Current.ShowStudio();
        studio.AddPageFrom(f.Session, f.Page);
        f.Session.Dispose();
        _info.Text = "Bild mit den sichtbaren Annotationen (als Ebenen) zur Anleitung hinzugefügt.";
    }

    private async Task VideoToGuideAsync()
    {
        if (_media is null) return;
        if (await GrabFrameAsync(_clip.Start + 0.05) is not { } poster) return;
        var studio = App.Current.ShowStudio();
        var v = new VideoRef { IncludeAudio = _media.HasAudio, MaxWidth = Math.Min(1280, _media.Width) };
        VideoStepRenderer.SetClip(v, _clip);
        studio.AddVideoStep(_file, v, poster.img, poster.png, Path.GetFileNameWithoutExtension(_file), _session, _overlay);
        _info.Text = "Als Video-Schritt zur Anleitung hinzugefügt (mit Schnitten, Annotationen, Zoom und Folien).";
    }

    private static void OpenFolder(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Open folder: " + ex.Message); }
    }

    // ================================================================== zoom overlay control

    /// <summary>Draws the zoom view of the playhead and lets the user drag a new view (zoom tool).</summary>
    private sealed class ZoomOverlay : Control
    {
        public Rect? ViewRect { get; set; }
        public Point? LastCenter { get; set; }
        private Point? _start;
        private Rect? _drag;
        public event EventHandler<Rect>? Picked;

        public ZoomOverlay() { IsHitTestVisible = false; }

        public override void Render(DrawingContext ctx)
        {
            if (IsHitTestVisible) ctx.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));   // receive clicks everywhere
            if (ViewRect is { } v)
            {
                ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0, 160, 255)), 2, new DashStyle(new double[] { 5, 3 }, 0)), v);
                ctx.DrawText(new FormattedText("Zoom-Ausschnitt", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, new SolidColorBrush(Color.FromRgb(0, 160, 255))), new Point(v.X + 4, v.Y + 2));
            }
            if (_drag is { } d) ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 160, 0)), new Pen(Brushes.Orange, 2), d);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e) { _start = e.GetPosition(this); _drag = new Rect(_start.Value, new Size(0, 0)); e.Pointer.Capture(this); e.Handled = true; }
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (_start is not { } s) return;
            var p = e.GetPosition(this);
            _drag = new Rect(Math.Min(s.X, p.X), Math.Min(s.Y, p.Y), Math.Abs(p.X - s.X), Math.Abs(p.Y - s.Y));
            InvalidateVisual();
        }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_start is not { } s) return;
            var r = _drag ?? new Rect(s, new Size(0, 0));
            _start = null; _drag = null;
            e.Pointer.Capture(null);
            InvalidateVisual();
            Picked?.Invoke(this, r.Width < 6 && r.Height < 6 ? new Rect(s.X, s.Y, 0, 0) : r);
        }
    }
}
