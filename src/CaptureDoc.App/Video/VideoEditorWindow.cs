using System.Text.Json;
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
/// Video studio: a non-destructive multi-track editor. Clips can be split, deleted (the gap closes, annotations
/// follow), moved onto other tracks, trimmed; further videos can be imported (insert, append, picture-in-picture);
/// slides are clips; annotations are drawn directly on the composed picture and have their own tracks.
/// Source files are never modified – everything is rendered at export.
/// </summary>
public sealed class VideoEditorWindow : Window
{
    private readonly AppServices _app;
    private readonly string _file;
    private readonly FfmpegTools _tools;
    private readonly VideoExporter _exporter;
    private MediaInfo? _media;
    private VideoSequence _seq = new();
    private double _pos, _slidePos;
    private (double A, double B)? _rangeSel;
    private CancellationTokenSource? _hiResCts;
    private readonly DispatcherTimer _playTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private SKImage? _frame;

    private readonly ProjectSession _session;
    private readonly bool _ownsSession;
    private readonly VideoOverlay _overlay;
    private GuideStep? _step;
    private readonly StudioWindow? _studio;
    private readonly string? _sidecar;
    private SequenceClip? _slideClip;
    private VideoSlide? _slide => _slideClip?.Slide;
    private int? _zoomKey;
    private Guid? _selectedClip;
    private bool _zoomPick;
    private readonly bool _freshAnnotations;

    // previews of every source file (low-res JPEG frames)
    private sealed class SourcePreview
    {
        public MediaInfo? Info;
        public double Fps = 5;
        public List<byte[]> Jpegs = new();
        public List<Bitmap> Thumbs = new();
        public int DecodedIndex = -1;
        public SKImage? Decoded;
    }
    private readonly Dictionary<string, SourcePreview> _sources = new(StringComparer.OrdinalIgnoreCase);

    // UI
    private readonly EditorCanvas _canvas;
    private readonly Canvas _textLayer = new();
    private readonly InlineTextEditor _text;
    private readonly StageOverlay _stageOverlay = new();
    private readonly Panel _stage = new() { Background = new SolidColorBrush(Color.FromRgb(18, 18, 21)), ClipToBounds = true };
    private readonly VideoTimeline _timeline = new();
    private readonly ScrollBar _hbar = new() { Orientation = Orientation.Horizontal, IsVisible = false, Height = 14 };
    private readonly TextBlock _info = Ui.Label("Lade Video …", 11, opacity: 0.75);
    private readonly TextBlock _timeLabel = new() { Text = "0:00.0", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas, monospace"), Margin = new Thickness(6, 0) };
    private readonly TextBlock _rangeLabel = Ui.Label("", 12);
    private readonly Button _play;
    private readonly PropertiesPanel _props;
    private readonly LayersPanel _layers;
    private readonly StackPanel _timeBox = new() { Spacing = 6, Margin = new Thickness(4) };
    private readonly StackPanel _videoBox = new() { Spacing = 8, Margin = new Thickness(6) };
    private readonly StackPanel _mediaBox = new() { Spacing = 6, Margin = new Thickness(6) };
    private TabControl? _tabs;
    private readonly Border _slideBanner = new() { IsVisible = false, Padding = new Thickness(8, 4), Background = new SolidColorBrush(Color.FromArgb(70, 140, 70, 220)) };
    private readonly Dictionary<Tool, ToggleButton> _toolButtons = new();
    private readonly ToggleButton _zoomTool;
    private readonly Slider _zoomFactor = new() { Minimum = 1.2, Maximum = 4, Value = 2, Width = 150 };
    private readonly NumericUpDown _stroke, _font;
    private readonly StackPanel _editTools = new() { Orientation = Orientation.Horizontal };
    private readonly CheckBox _ripple = new() { Content = "Lücken schließen", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _carry = new() { Content = "Annotationen mitnehmen", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };

    private readonly IReadOnlyList<(double T, int X, int Y)>? _clicks;
    private readonly IReadOnlyList<Layer>? _live;

    public ProjectSession Session => _session;
    public VideoSequence Sequence => _seq;

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
                catch (Exception ex) when (ex is IOException or InvalidDataException or ProjectFormatException or JsonException)
                {
                    Log.Warn("Video side-car not readable: " + ex.Message);
                }
            }
            _session ??= new ProjectSession(new CaptureProject { Title = Path.GetFileNameWithoutExtension(file) }, new AssetStore());
        }
        _overlay = _step?.Video?.Overlay ?? new VideoOverlay();
        if (_step?.Video is { } sv) sv.Overlay = _overlay;
        _overlay.Page.Layers.RemoveAll(l => l is ImageLayer { IsBackground: true });   // old "reference frame" layers
        _overlay.Page.Background = ColorRgba.Transparent;
        _freshAnnotations = fresh;

        Title = Path.GetFileName(file) + (_studio is not null && _step is not null ? $" – Schritt „{_step.Title}“" : "") + " – CaptureDoc Video";
        Icon = IconFactory.WindowIcon;
        Width = 1440; Height = 900; MinWidth = 1000; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // one undo history for annotations, zoom and the whole edit
        _session.ReplaceHistory(_overlay.Page, CaptureState, RestoreState);
        _canvas = new EditorCanvas(_session, _overlay.Page, app.Settings.Shared.Style) { Tool = Tool.Select };
        _text = new InlineTextEditor(_canvas, _textLayer);
        _canvas.LayerFilter = l => ActiveAt(l, CurrentTime);
        _canvas.DisplayOverride = MovingView;
        _canvas.LayerCreated += (_, l) => OnLayerCreated(l);
        _canvas.TextEditRequested += (_, t) => _text.Begin(t);
        _canvas.SelectionChanged += (_, _) => { if (_canvas.Selection.Count > 0) _selectedClip = null; BuildTimeBox(); RefreshTimeline(); UpdateStageOverlay(); };
        _canvas.DocumentChanged += (_, _) => { MarkChanged(); Refresh(); };
        _canvas.HistoryChanged += (_, _) => { if (_selectedClip is { } id && _seq.Find(id) is null) _selectedClip = null; BuildTimeBox(); BuildVideoBox(); BuildMediaBox(); RecomposeSoon(); };
        _stageOverlay.ZoomPicked += (_, r) => OnZoomPicked(r);
        _stageOverlay.ClipRectStarted += (_, _) => _canvas.History.BeginTransaction("Bild-im-Bild");
        _stageOverlay.ClipRectChanging += (_, r) => OnClipRectChanging(r);
        _stageOverlay.ClipRectDone += (_, _) => { _canvas.History.Commit(); BuildTimeBox(); LoadHiResSoon(); };
        var host = new Panel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(_canvas);
        host.Children.Add(_textLayer);
        host.Children.Add(_stageOverlay);
        _stage.Children.Add(host);
        _stage.SizeChanged += (_, _) => FitCanvas();

        _props = new PropertiesPanel(_canvas) { ShowPageSection = false };
        _props.Rebuild();
        _layers = new LayersPanel(_canvas);

        // ---- top bar
        var title = Ui.Label(Path.GetFileName(file), 13, bold: true);
        title.Margin = new Thickness(10, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 2 };
        var importTop = Ui.Button("🎞 Video importieren…", () => { }, "Weiteres Video einfügen, anhängen oder darüberlegen");
        importTop.Click += (_, _) => ShowImportMenu(importTop);
        var recordTop = Ui.Button("● Aufnehmen…", () => { }, "Neues Video aufnehmen und direkt in die Timeline legen", accent: true);
        recordTop.Click += (_, _) => ShowRecordMenu(recordTop);
        actions.Children.Add(recordTop);
        actions.Children.Add(importTop);
        actions.Children.Add(Ui.Button("📂 Ordner", () => OpenFolder(Path.GetDirectoryName(_file)!), "Speicherort öffnen"));
        actions.Children.Add(Ui.Button("🖼 Frame → Editor", async () => await FrameToEditorAsync(), "Aktuelles Bild mit den sichtbaren Annotationen als bearbeitbaren Screenshot öffnen"));
        actions.Children.Add(Ui.Button("➕ Frame → Anleitung", async () => await FrameToGuideAsync(), "Aktuelles Bild (mit sichtbaren Annotationen, als Ebenen) als Bild-Schritt ins Documentation Studio"));
        if (_studio is not null && _step is not null)
            actions.Children.Add(Ui.Button("✓ In Anleitung übernehmen", () => { WriteBack(); Close(); }, "Schnitt, Spuren, Annotationen, Zoom und Folien im Anleitungsschritt speichern", accent: true));
        else
            actions.Children.Add(Ui.Button("🎬 Video → Anleitung", async () => await VideoToGuideAsync(), "Das ganze geschnittene Video als Video-Schritt ins Documentation Studio"));
        actions.Children.Add(Ui.Button("⇩ GIF…", async () => await ExportAsync(gif: true), "Als GIF exportieren – mit geprüftem Größenlimit", accent: true));
        actions.Children.Add(Ui.Button("⇩ MP4…", async () => await ExportAsync(gif: false), "Als MP4 exportieren (alle Spuren, Annotationen, Zoom und Folien)", accent: true));
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
        ToolBtn(Tool.Spotlight, "◎", "Spotlight", "L");
        ToolBtn(Tool.Magnifier, "🔍", "Lupe", "M");
        ToolBtn(Tool.Blackout, "■", "Schwärzen", "B");
        ToolBtn(Tool.Pixelate, "▦", "Verpixeln", "P");
        _zoomTool = RailButton("⤢", "Zoom", "Zoom/Schwenk: Rechteck im Video aufziehen = Zoom-Keyframe an der Abspielposition (Z)");
        _zoomTool.Click += (_, _) => SetZoomPick(_zoomTool.IsChecked == true);
        rail.Children.Add(_zoomTool);
        var img = RailButton("🖼", "Bild", "Bild als Annotation einfügen (z. B. Logo)");
        img.Click += async (_, _) => { img.IsChecked = false; await InsertImageAsync(); };
        rail.Children.Add(img);
        var rec = RailButton("●", "Aufnahme", "Neues Video aufnehmen und in die Timeline legen");
        rec.Click += (_, _) => { rec.IsChecked = false; ShowRecordMenu(rec); };
        rail.Children.Add(rec);
        var vid = RailButton("🎞", "Video", "Weiteres Video importieren");
        vid.Click += (_, _) => { vid.IsChecked = false; ShowImportMenu(vid); };
        rail.Children.Add(vid);
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
        strip.Children.Add(Ui.Button("↶", () => _canvas.Undo(), "Rückgängig – auch Schnitte und Spuren (Strg+Z)"));
        strip.Children.Add(Ui.Button("↷", () => _canvas.Redo(), "Wiederholen (Strg+Y)"));
        strip.Children.Add(Ui.Button("🗑", DeleteSelected, "Ausgewählte Annotation bzw. ausgewählten Clip löschen (Entf)"));
        strip.Children.Add(new Separator { Width = 10 });
        strip.Children.Add(Ui.Button("📋 Bild kopieren", async () => await CopyFrameAsync(), "Aktuelles Bild mit allen sichtbaren Annotationen in die Zwischenablage (Strg+C) – z. B. zum Einfügen in einen Chat"));

        var center = new DockPanel();
        DockPanel.SetDock(strip, Dock.Top); center.Children.Add(strip);
        DockPanel.SetDock(_slideBanner, Dock.Top); center.Children.Add(_slideBanner);
        center.Children.Add(_stage);

        // ---- right panel
        var propsTab = new DockPanel();
        var timeBorder = new Border { Child = _timeBox, BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)) };
        DockPanel.SetDock(timeBorder, Dock.Top);
        propsTab.Children.Add(timeBorder);
        propsTab.Children.Add(_props);
        var tabs = new TabControl
        {
            Width = 330,
            ItemsSource = new[]
            {
                new TabItem { Header = "Eigenschaften", Content = propsTab, FontSize = 13 },
                new TabItem { Header = "Ebenen", Content = _layers, FontSize = 13 },
                new TabItem { Header = "Medien", Content = new ScrollViewer { Content = _mediaBox, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, FontSize = 13 },
                new TabItem { Header = "Zoom & Folien", Content = new ScrollViewer { Content = _videoBox, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, FontSize = 13 },
            },
        };
        _tabs = tabs;

        // ---- bottom: transport + edit + timeline
        _play = Ui.Button("▶", TogglePlay, "Abspielen / Pause (Leertaste)");
        var transport = new WrapPanel { Margin = new Thickness(6, 4) };
        transport.Children.Add(_play);
        transport.Children.Add(_timeLabel);
        transport.Children.Add(Ui.Button("⏮", () => SeekTo(0, true), "Zum Anfang (Pos1)"));
        transport.Children.Add(Ui.Button("◀", () => StepFrame(-1), "Ein Bild zurück (,)"));
        transport.Children.Add(Ui.Button("▶|", () => StepFrame(1), "Ein Bild vor (.)"));
        _editTools.Children.Add(new Separator { Width = 10 });
        _editTools.Children.Add(Ui.Button("✂ Teilen (S)", SplitAtPlayhead, "Ausgewählten Clip – sonst alle Clips – an der Abspielposition in zwei Teile schneiden", accent: true));
        _editTools.Children.Add(Ui.Button("🗑 Entfernen (Entf)", DeleteSelected, "Ausgewählten Clip entfernen; auf der Hauptspur schließt sich die Lücke und alles danach rückt nach (auch Annotationen)"));
        _editTools.Children.Add(Ui.Button("✂ Bereich (X)", CutRange, "Auf dem Zeitlineal gezogenen Bereich aus allen Spuren entfernen"));
        _editTools.Children.Add(Ui.Button("⇤ Anfang", () => RippleRange(0, _pos, "Anfang entfernen"), "Alles vor der Abspielposition entfernen (I)"));
        _editTools.Children.Add(Ui.Button("Ende ⇥", () => RippleRange(_pos, _seq.Duration, "Ende entfernen"), "Alles ab der Abspielposition entfernen (O)"));
        _editTools.Children.Add(Ui.Button("＋ Spur", AddTrack, "Neue Video-Spur oben (für Bild-im-Bild/Überlagerungen)"));
        _editTools.Children.Add(_ripple);
        _editTools.Children.Add(_carry);
        ToolTip.SetTip(_ripple, "Beim Entfernen eines Clips auf der Hauptspur rückt alles Folgende nach (Ripple)");
        ToolTip.SetTip(_carry, "Annotationen, die ganz in einem Clip liegen, wandern beim Verschieben mit");
        transport.Children.Add(_editTools);
        transport.Children.Add(new Separator { Width = 10 });
        transport.Children.Add(Ui.Button("－", () => _timeline.ZoomFactor /= 1.5, "Zeitachse verkleinern (Strg+Mausrad)"));
        transport.Children.Add(Ui.Button("＋", () => { _timeline.ZoomFactor *= 1.5; _timeline.EnsureVisible(CurrentTime); }, "Zeitachse vergrößern (Strg+Mausrad)"));
        transport.Children.Add(Ui.Button("⤢", () => _timeline.ZoomFactor = 1, "Ganze Zeitachse zeigen"));
        transport.Children.Add(new Separator { Width = 10 });
        transport.Children.Add(_rangeLabel);

        _timeline.ThumbProvider = Thumb;
        _timeline.Seek += (_, t) => SeekTo(t, hiRes: !_playTimer.IsEnabled);
        _timeline.SelectionChanged += (_, s) => { _rangeSel = s; Refresh(); };
        _timeline.LaneSelected += (_, id) => SelectLayer(id);
        _timeline.LaneEditStarted += (_, _) => _canvas.History.BeginTransaction("Zeitfenster");
        _timeline.LaneEdited += (_, e) => SetTiming(e.Id, e.Start, e.End);
        _timeline.LaneEditCompleted += (_, _) => { _canvas.History.Commit(); BuildTimeBox(); MarkChanged(); };
        _timeline.ZoomKeySelected += (_, i) => { _zoomKey = i; var keys = SortedZoom(); if (i < keys.Count) SeekTo(keys[i].TimeSeconds, true); BuildVideoBox(); };
        _timeline.ZoomKeyMoved += (_, e) => { var keys = SortedZoom(); if (e.Index < keys.Count) { keys[e.Index].TimeSeconds = e.Time; MarkChanged(); Refresh(); } };
        _timeline.ClipSelected += (_, id) => SelectClip(id == Guid.Empty ? null : id);
        _timeline.ClipEditStarted += (_, id) => BeginClipDrag(id);
        _timeline.ClipMoved += (_, e) => OnClipMoved(e.Id, e.Start, e.Track);
        _timeline.ClipTrimmed += (_, e) => OnClipTrimmed(e.Id, e.Left, e.Time);
        _timeline.ClipEditCompleted += (_, _) => EndClipDrag();
        _timeline.ClipOpened += (_, id) => { if (_seq.Find(id) is { Kind: ClipKind.Slide } c) EnterSlide(c); };
        _timeline.TrackToggled += (_, e) => ToggleTrack(e.Track, e.What);
        _timeline.ViewChanged += (_, _) => SyncHBar();
        _hbar.Scroll += (_, _) => _timeline.Offset = _hbar.Value;
        var timelineScroll = new ScrollViewer { Content = _timeline, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        var bottom = new DockPanel();
        DockPanel.SetDock(transport, Dock.Top); bottom.Children.Add(transport);
        DockPanel.SetDock(_info, Dock.Bottom); _info.Margin = new Thickness(8, 2); bottom.Children.Add(_info);
        DockPanel.SetDock(_hbar, Dock.Bottom); bottom.Children.Add(_hbar);
        bottom.Children.Add(timelineScroll);

        var mid = new DockPanel();
        var railBorder = new Border { Child = railScroll, BorderThickness = new Thickness(0, 0, 1, 0), BorderBrush = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)) };
        DockPanel.SetDock(railBorder, Dock.Left); mid.Children.Add(railBorder);
        var tabsBorder = new Border { Child = tabs, BorderThickness = new Thickness(1, 0, 0, 0), BorderBrush = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)) };
        DockPanel.SetDock(tabsBorder, Dock.Right); mid.Children.Add(tabsBorder);
        mid.Children.Add(center);

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,5,300") };
        Grid.SetRow(top, 0); grid.Children.Add(top);
        Grid.SetRow(mid, 1); grid.Children.Add(mid);
        var split = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, Background = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)) };
        Grid.SetRow(split, 2); grid.Children.Add(split);
        Grid.SetRow(bottom, 3); grid.Children.Add(bottom);
        grid.RowDefinitions[1].MinHeight = 240; grid.RowDefinitions[3].MinHeight = 160;
        Content = grid;

        _playTimer.Tick += (_, _) => PlayStep();
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Opened += async (_, _) => { StudioWindow.FitToScreen(this); SetTool(Tool.Select); await LoadAsync(); };
        Closing += (_, _) => { _text.Commit(); WriteBack(); SaveSidecar(); };
        Closed += (_, _) =>
        {
            _playTimer.Stop();
            foreach (var s in _sources.Values) { foreach (var b in s.Thumbs) b.Dispose(); s.Decoded?.Dispose(); }
            if (_ownsSession) _session.Dispose();
        };
    }

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

    // ================================================================== undo state (annotations + zoom + edit)

    private sealed class EditState
    {
        public Page Page { get; set; } = new();
        public List<ZoomKeyframe> Zoom { get; set; } = new();
        public VideoSequence Sequence { get; set; } = new();
    }

    private string CaptureState() => JsonSerializer.Serialize(new EditState { Page = _overlay.Page, Zoom = _overlay.Zoom, Sequence = _seq }, ProjectJson.Compact);

    private void RestoreState(string json)
    {
        var st = JsonSerializer.Deserialize<EditState>(json, ProjectJson.Compact)!;
        var p = _overlay.Page;
        p.Layers = st.Page.Layers; p.Width = st.Page.Width; p.Height = st.Page.Height;
        _overlay.Zoom.Clear(); _overlay.Zoom.AddRange(st.Zoom);
        _seq.Tracks = st.Sequence.Tracks; _seq.Width = st.Sequence.Width; _seq.Height = st.Sequence.Height; _seq.Background = st.Sequence.Background;
    }

    /// <summary>Edit of the sequence as one undo step (also covers annotations that move along).</summary>
    private void Edit(string description, Action change)
    {
        if (_slideClip is not null) { change(); MarkChanged(); Refresh(); RecomposeSoon(); return; }
        _canvas.Apply(description, change);
        RecomposeSoon();
    }

    // ================================================================== loading

    private async Task LoadAsync()
    {
        try
        {
            _media = await MediaProbe.ProbeAsync(_tools, _file);
            var name = Path.GetFileNameWithoutExtension(_file);
            if (_step?.Video?.Sequence is { } existing) _seq = existing;
            else if (_step?.Video is { } v)
            {
                // project from 0.2–0.4: trim/cuts/slides on source time → sequence (annotations are moved along)
                _seq = SequenceOps.FromLegacy(v, _media.Duration, _media.Width, _media.Height, _media.Fps, _media.HasAudio, name);
                v.Sequence = _seq;
                MarkChanged();
            }
            else _seq = SequenceOps.ForVideo(VideoStepRenderer.Store(_app, _session, _file), _media.Duration, _media.Width, _media.Height, _media.Fps, _media.HasAudio, name);
            _seq.SyncMedia();
            _overlay.Page.Width = _seq.Width; _overlay.Page.Height = _seq.Height;
            _canvas.InvalidateMeasure();
            if (_freshAnnotations)
            {
                AddClickMarkers();
                if (_live is { Count: > 0 }) { foreach (var l in _live) _overlay.Page.Layers.Add(l); _info.Text = $"{_live.Count} Live-Zeichnung(en) aus der Aufnahme übernommen."; }
            }
            _info.Text = $"{_seq.Width}×{_seq.Height} · {_media.Fps:0.#} fps · {VideoTimeline.Fmt(_media.Duration)} · {SizeText(new FileInfo(_file).Length)}{(_media.HasAudio ? " · mit Ton" : "")} · {_file}  –  Vorschau wird erstellt …";
            foreach (var path in _seq.AllClips.Where(c => c.Kind == ClipKind.Video).Select(ResolveClip).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList())
                await EnsureSourceAsync(path);
            _info.Text = $"{_seq.Width}×{_seq.Height} · {_media.Fps:0.#} fps · {VideoTimeline.Fmt(_media.Duration)} · {SizeText(new FileInfo(_file).Length)}{(_media.HasAudio ? " · mit Ton" : "")} · {_file}" +
                         (_sidecar is not null && File.Exists(_sidecar) && !_freshAnnotations ? "  ·  Annotationen aus " + Path.GetFileName(_sidecar) + " geladen" : "");
            FitCanvas();
            BuildVideoBox(); BuildTimeBox(); BuildMediaBox();
            SeekTo(0, hiRes: true);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException)
        {
            _info.Text = "⚠ " + ex.Message;
        }
    }

    private string? ResolveClip(SequenceClip c) => c.Kind == ClipKind.Video ? VideoRenderPlan.ResolveClip(_app, _session, c) : null;

    private async Task<SourcePreview?> EnsureSourceAsync(string path)
    {
        if (_sources.TryGetValue(path, out var existing)) return existing;
        var sp = new SourcePreview();
        _sources[path] = sp;
        try
        {
            sp.Info = await MediaProbe.ProbeAsync(_tools, path);
            sp.Fps = Math.Clamp(1500 / Math.Max(1, sp.Info.Duration), 1, Math.Min(10, Math.Max(1, sp.Info.Fps)));
            var width = Math.Min(480, sp.Info.Width);
            sp.Jpegs = await Task.Run(() => _exporter.ExtractPreviewFramesAsync(path, sp.Fps, width));
            sp.Thumbs = sp.Jpegs.Select(j => new Bitmap(new MemoryStream(j))).ToList();
            RefreshTimeline();
            return sp;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException)
        {
            Log.Warn("Preview failed for " + path + ": " + ex.Message);
            return sp;
        }
    }

    private Bitmap? Thumb(string path, double sourceTime) =>
        _sources.TryGetValue(path, out var s) && s.Thumbs.Count > 0 ? s.Thumbs[Math.Clamp((int)(sourceTime * s.Fps), 0, s.Thumbs.Count - 1)] : null;

    private static string SizeText(long b) => Core.Export.SizeOptimizer.FormatBytes(b);

    /// <summary>Recorded clicks become normal, editable ring annotations; double clicks are merged.</summary>
    private void AddClickMarkers()
    {
        if (_clicks is null || _clicks.Count == 0 || _media is null) return;
        var layers = ClickMarkers.Create(_clicks, _media.Width, _media.Duration);
        _overlay.Page.Layers.AddRange(layers);
        _info.Text = $"{layers.Count} Mausklick(s) markiert ({_clicks.Count - layers.Count} Doppelklick-Klick(s) zusammengefasst).";
    }

    // ================================================================== time & mode

    private double CurrentTime => _slideClip is null ? _pos : _slidePos;
    private double CurrentDuration => _slide?.Duration ?? Math.Max(0.1, _seq.Duration);
    private Page CurrentPage => _slide?.Page ?? _overlay.Page;
    /// <summary>Visible length of the time axis (a little room behind the end for moving clips).</summary>
    private double AxisDuration => _slideClip is not null ? CurrentDuration : Math.Max(1, _seq.Duration) * 1.12 + 1;

    private static bool ActiveAt(Layer l, double t)
    {
        if (l.Timing is not { } tm) return true;
        var end = tm.EndSeconds > tm.StartSeconds ? tm.EndSeconds : double.MaxValue;
        return t >= tm.StartSeconds - 1e-6 && t <= end + 1e-6;
    }

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
        var len = _slideClip is null ? Math.Max(0.5, _app.Settings.Shared.VideoAnnotationSeconds) : Math.Max(0.5, dur - t);
        l.Timing = new LayerTiming { StartSeconds = Math.Round(t, 2), EndSeconds = Math.Round(Math.Min(Math.Max(dur, t + 0.5), t + len), 2) };
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
        Dispatcher.UIThread.Post(UpdateStageOverlay, DispatcherPriority.Background);
    }

    // ================================================================== refresh

    private void Refresh()
    {
        _editTools.IsVisible = _slideClip is null;
        _timeline.Duration = AxisDuration;
        _timeline.Position = CurrentTime;
        _timeline.Selection = _slideClip is null ? _rangeSel : null;
        _timeline.ZoomKeys = SortedZoom().Select(z => z.TimeSeconds).ToList();
        _timeline.SelectedZoomKey = _zoomKey;
        RefreshTimeline();
        _timeLabel.Text = VideoTimeline.Fmt(CurrentTime) + " / " + VideoTimeline.Fmt(CurrentDuration);
        _rangeLabel.Text = _slideClip is not null
            ? $"Folie „{_slide!.Name}“ · {_slide.Duration:0.#} s"
            : $"{_seq.AllClips.Count()} Clip(s) auf {_seq.Tracks.Count} Spur(en) · Länge {_seq.Duration:0.0} s" +
              (_rangeSel is { } r ? $"   Bereich {VideoTimeline.Fmt(r.A)}–{VideoTimeline.Fmt(r.B)} markiert: X = entfernen" : "");
        UpdateStageOverlay();
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
        _timeline.Tracks = _seq.Tracks.Select(t => new TimelineTrack(t.Id, t.Name, t.Hidden, t.Muted)).ToList();
        _timeline.Clips = _seq.Tracks.SelectMany((t, i) => t.Clips.Select(c => new TimelineClip(c.Id, i, c.Name, c.Start, c.Duration, c.SourceIn, ResolveClip(c),
            c.Kind == ClipKind.Slide, (c.Slide?.Page.Background ?? ColorRgba.Black).WithAlpha(255).ToAvalonia(), c.Id == _selectedClip, c.HasAudio && !c.Muted))).ToList();
        _timeline.SlideMode = _slide?.Page.Background.WithAlpha(255).ToAvalonia();
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

    // ================================================================== composed preview

    private SKImage? PreviewFrame(string path, double sourceTime)
    {
        if (!_sources.TryGetValue(path, out var s) || s.Jpegs.Count == 0) return null;
        var idx = Math.Clamp((int)(sourceTime * s.Fps), 0, s.Jpegs.Count - 1);
        if (idx != s.DecodedIndex || s.Decoded is null)
        {
            s.Decoded?.Dispose();
            s.Decoded = SKImage.FromEncodedData(s.Jpegs[idx]);
            s.DecodedIndex = idx;
        }
        return s.Decoded;
    }

    /// <summary>The picture of all tracks at t (without annotations – those are drawn by the canvas on top).</summary>
    private SKImage Compose(double t, IReadOnlyDictionary<Guid, SKImage>? exact = null)
    {
        var w = Math.Max(2, _seq.Width); var h = Math.Max(2, _seq.Height);
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul))!;
        var c = surface.Canvas;
        c.Clear(_seq.Background.ToSk());
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
        foreach (var (_, clip) in SequenceOps.ClipsAt(_seq, t))
        {
            var r = SequenceOps.Placement(_seq, clip).ToSk();
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Clamp(255 * clip.Opacity, 0, 255)) };
            if (clip.Kind == ClipKind.Slide && clip.Slide is { } sl)
            {
                var local = t - clip.Start;
                var page = new Page { Width = sl.Page.Width, Height = sl.Page.Height, Background = sl.Page.Background, Layers = sl.Page.Layers.Where(l => ActiveAt(l, local)).ToList() };
                using var img = _session.Renderer.RenderPage(page);
                c.DrawImage(img, new SKRect(0, 0, img.Width, img.Height), r, sampling, paint);
                continue;
            }
            SKImage? frame = exact is not null && exact.TryGetValue(clip.Id, out var ex) ? ex : null;
            if (frame is null && ResolveClip(clip) is { } path) frame = PreviewFrame(path, clip.SourceTime(t));
            if (frame is not null) c.DrawImage(frame, new SKRect(0, 0, frame.Width, frame.Height), r, sampling, paint);
        }
        return surface.Snapshot();
    }

    private void SetFrame(SKImage? img)
    {
        var old = _frame;
        _frame = img;
        if (_slideClip is null) _canvas.Underlay = img;
        old?.Dispose();
    }

    private bool _recomposePending;
    private void RecomposeSoon()
    {
        if (_recomposePending) return;
        _recomposePending = true;
        Dispatcher.UIThread.Post(() => { _recomposePending = false; if (_slideClip is null) { SetFrame(Compose(_pos)); LoadHiResSoon(); } }, DispatcherPriority.Background);
    }

    private void LoadHiResSoon() { if (!_playTimer.IsEnabled) _ = LoadHiResAsync(_pos); }

    private async Task LoadHiResAsync(double t)
    {
        _hiResCts?.Cancel();
        var cts = _hiResCts = new CancellationTokenSource();
        var exact = new Dictionary<Guid, SKImage>();
        try
        {
            await Task.Delay(140, cts.Token);
            foreach (var (_, clip) in SequenceOps.ClipsAt(_seq, t))
            {
                if (clip.Kind != ClipKind.Video || ResolveClip(clip) is not { } path) continue;
                var png = await Task.Run(() => _exporter.ExtractFrameAsync(path, clip.SourceTime(t), cts.Token), cts.Token);
                if (SKImage.FromEncodedData(png) is { } im) exact[clip.Id] = im;
            }
            if (!cts.IsCancellationRequested && !_playTimer.IsEnabled && _slideClip is null && Math.Abs(t - _pos) < 1e-6) SetFrame(Compose(t, exact));
        }
        catch (OperationCanceledException) { }
        catch (InvalidDataException) { }
        finally { foreach (var im in exact.Values) im.Dispose(); }
    }

    /// <summary>Full-resolution picture of all tracks at t.</summary>
    private async Task<(SKImage img, byte[] png)?> ComposeExactAsync(double t)
    {
        var exact = new Dictionary<Guid, SKImage>();
        try
        {
            foreach (var (_, clip) in SequenceOps.ClipsAt(_seq, t))
                if (clip.Kind == ClipKind.Video && ResolveClip(clip) is { } path)
                {
                    var png = await Task.Run(() => _exporter.ExtractFrameAsync(path, clip.SourceTime(t)));
                    if (SKImage.FromEncodedData(png) is { } im) exact[clip.Id] = im;
                }
            var img = Compose(t, exact);
            return (img, SkiaConvert.EncodePng(img));
        }
        catch (InvalidDataException ex) { await Dialogs.InfoAsync(this, "Frame", ex.Message); return null; }
        finally { foreach (var im in exact.Values) im.Dispose(); }
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
        if (on && _slideClip is not null) on = false;
        _zoomPick = on;
        _zoomTool.IsChecked = on;
        _stageOverlay.ZoomPick = on;
        if (on) foreach (var b in _toolButtons.Values) b.IsChecked = false;
        else if (_toolButtons.TryGetValue(_canvas.Tool, out var tb)) tb.IsChecked = true;
        if (on) _info.Text = "Zoom: Rechteck im Video aufziehen (= sichtbarer Ausschnitt ab der Abspielposition) – oder klicken: Mittelpunkt mit dem Faktor aus „Zoom & Folien“.";
        _stageOverlay.InvalidateVisual();
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
        if (!ActiveAt(l, CurrentTime) && l.Timing is { } tm) SeekTo(tm.StartSeconds + 0.01, true);
        _canvas.Select(new[] { id });
    }

    private void SetTiming(Guid id, double start, double end)
    {
        if (CurrentPage.Find(id) is not { } layer) return;
        var tm = layer.Timing ??= new LayerTiming();
        tm.StartSeconds = start; tm.EndSeconds = end;
        if (tm.Keyframes.Count >= 2)
        {
            var k = tm.Keyframes.OrderBy(x => x.TimeSeconds).ToList();
            k[0].TimeSeconds = start; k[^1].TimeSeconds = end;
        }
        if (!ActiveAt(layer, CurrentTime)) _canvas.Select(Array.Empty<Guid>());
        RefreshTimeline();
        _canvas.InvalidateRender();
    }

    // ================================================================== clips: select, split, delete, move, trim

    private SequenceClip? SelectedClip => _selectedClip is { } id ? _seq.Find(id) : null;

    public void SelectClip(Guid? id)
    {
        _selectedClip = id;
        if (id is not null && _canvas.Selection.Count > 0) _canvas.Select(Array.Empty<Guid>());
        BuildTimeBox(); RefreshTimeline(); UpdateStageOverlay();
    }

    public void SplitAtPlayhead()
    {
        if (_slideClip is not null) return;
        var t = _pos;
        var sel = SelectedClip;
        var n = 0;
        Edit("Teilen", () =>
        {
            if (sel is not null && sel.Contains(t)) n = SequenceOps.Split(_seq, sel, t) is null ? 0 : 1;
            else n = SequenceOps.SplitAll(_seq, t).Count;
        });
        _info.Text = n == 0 ? "Hier gibt es nichts zu teilen (Abspielposition auf einen Clip stellen, nicht genau an seinen Rand)." : $"An {VideoTimeline.Fmt(t)} geteilt – den gewünschten Teil anklicken und mit Entf entfernen oder auf eine andere Spur ziehen.";
    }

    private void DeleteSelected()
    {
        if (_canvas.Selection.Count > 0) { _canvas.DeleteSelection(); return; }
        if (SelectedClip is { } c) { DeleteClip(c); return; }
        if (_rangeSel is not null) CutRange();
    }

    public void DeleteClip(SequenceClip c)
    {
        var track = _seq.TrackOf(c.Id);
        if (track is null) return;
        var main = _seq.Tracks.IndexOf(track) == 0;
        Edit("Clip entfernen", () =>
        {
            if (main && _ripple.IsChecked == true) SequenceOps.RippleDelete(_seq, _overlay, c.Start, c.End);
            else track.Clips.Remove(c);
        });
        _selectedClip = null;
        if (_pos > _seq.Duration) _pos = _seq.Duration;
        BuildTimeBox(); Refresh();
        _info.Text = main && _ripple.IsChecked == true ? "Clip entfernt – alles danach ist nachgerückt (auch Annotationen und Zoom). Strg+Z macht es rückgängig." : "Clip entfernt.";
    }

    private void CutRange()
    {
        if (_rangeSel is not { } r || r.B - r.A < 0.05) { _info.Text = "Erst auf dem Zeitlineal (oberste Zeile) einen Bereich ziehen."; return; }
        RippleRange(r.A, r.B, "Bereich entfernen");
        _rangeSel = null; _timeline.Selection = null;
        Refresh();
    }

    private void RippleRange(double a, double b, string what)
    {
        if (_slideClip is not null || b - a < 0.02) return;
        Edit(what, () => SequenceOps.RippleDelete(_seq, _overlay, a, b));
        _pos = Math.Min(a, _seq.Duration);
        SeekTo(_pos, true);
    }

    private List<Layer> _dragLinked = new();
    private List<ZoomKeyframe> _dragZoom = new();

    private void BeginClipDrag(Guid id)
    {
        if (_seq.Find(id) is not { } c) return;
        _canvas.History.BeginTransaction("Clip bearbeiten");
        _dragLinked = _carry.IsChecked == true ? SequenceOps.LinkedAnnotations(_overlay, c) : new();
        _dragZoom = _carry.IsChecked == true ? _overlay.Zoom.Where(z => z.TimeSeconds >= c.Start - 0.02 && z.TimeSeconds <= c.End + 0.02).ToList() : new();
    }

    private void OnClipMoved(Guid id, double start, int track)
    {
        if (_seq.Find(id) is not { } c) return;
        var delta = start - c.Start;
        c.Start = start;
        SequenceOps.ShiftAnnotations(_dragLinked, delta);
        foreach (var z in _dragZoom) z.TimeSeconds += delta;
        var from = _seq.TrackOf(id)!;
        track = Math.Clamp(track, 0, _seq.Tracks.Count - 1);
        if (_seq.Tracks[track] != from) { from.Clips.Remove(c); _seq.Tracks[track].Clips.Add(c); }
        RefreshTimeline();
        RecomposeSoon();
    }

    private void OnClipTrimmed(Guid id, bool left, double t)
    {
        if (_seq.Find(id) is not { } c) return;
        if (left) SequenceOps.TrimStart(c, t); else SequenceOps.TrimEnd(c, t);
        RefreshTimeline();
        RecomposeSoon();
    }

    private void EndClipDrag()
    {
        foreach (var tr in _seq.Tracks) tr.Clips.Sort((a, b) => a.Start.CompareTo(b.Start));
        _canvas.History.Commit();
        _dragLinked = new(); _dragZoom = new();
        BuildTimeBox(); Refresh(); MarkChanged();
    }

    private void AddTrack() => Edit("Spur hinzufügen", () => _seq.Tracks.Add(new VideoTrack { Name = $"Video {_seq.Tracks.Count + 1}" }));

    private void ToggleTrack(int index, string what)
    {
        if (index < 0 || index >= _seq.Tracks.Count) return;
        var t = _seq.Tracks[index];
        Edit(what == "hide" ? "Spur ein/aus" : "Ton ein/aus", () => { if (what == "hide") t.Hidden = !t.Hidden; else t.Muted = !t.Muted; });
        Refresh();
    }

    public void MoveClipToTrack(SequenceClip c, int dir)
    {
        var from = _seq.TrackOf(c.Id);
        if (from is null) return;
        var i = _seq.Tracks.IndexOf(from) + dir;
        if (i < 0) return;
        Edit(dir > 0 ? "Auf Spur darüber" : "Auf Spur darunter", () =>
        {
            if (i >= _seq.Tracks.Count) _seq.Tracks.Add(new VideoTrack { Name = $"Video {_seq.Tracks.Count + 1}" });
            from.Clips.Remove(c);
            _seq.Tracks[i].Clips.Add(c);
            _seq.Tracks[i].Clips.Sort((a, b) => a.Start.CompareTo(b.Start));
        });
        BuildTimeBox(); Refresh();
    }

    private void SetPlacement(SequenceClip c, string preset)
    {
        var W = _seq.Width; var H = _seq.Height;
        var full = SequenceOps.Placement(_seq, new SequenceClip { SourceWidth = c.SourceWidth, SourceHeight = c.SourceHeight });
        var f = 0.32; var m = Math.Round(Math.Min(W, H) * 0.03);
        var w = full.Width * f; var h = full.Height * f;
        RectD? r = preset switch
        {
            "full" => null,
            "br" => new RectD(W - w - m, H - h - m, w, h),
            "bl" => new RectD(m, H - h - m, w, h),
            "tr" => new RectD(W - w - m, m, w, h),
            "tl" => new RectD(m, m, w, h),
            "center" => new RectD((W - full.Width * 0.6) / 2, (H - full.Height * 0.6) / 2, full.Width * 0.6, full.Height * 0.6),
            _ => c.Rect,
        };
        Edit("Position", () => c.Rect = r);
        BuildTimeBox();
    }

    private void OnClipRectChanging(Rect viewRect)
    {
        if (SelectedClip is not { } c) return;
        var s = _canvas.ViewScale;
        c.Rect = new RectD(Math.Round(viewRect.X / s), Math.Round(viewRect.Y / s), Math.Max(16, Math.Round(viewRect.Width / s)), Math.Max(16, Math.Round(viewRect.Height / s)));
        RecomposeSoon();
        UpdateStageOverlay();
    }

    // ================================================================== import

    private void ShowImportMenu(Control anchor)
    {
        var menu = new MenuFlyout();
        void Item(string text, string mode) { var mi = new MenuItem { Header = text }; mi.Click += async (_, _) => await ImportVideoAsync(mode); menu.Items.Add(mi); }
        Item("An der Abspielposition einfügen (Hauptspur – der Rest rückt nach hinten)", "insert");
        Item("Ans Ende anhängen (Hauptspur)", "append");
        Item("Darüberlegen ab Abspielposition (eigene Spur, Bild-im-Bild)", "overlay");
        menu.ShowAt(anchor);
    }

    public async Task ImportVideoAsync(string mode, string? path = null)
    {
        if (_slideClip is not null) ExitSlide();
        path ??= await Dialogs.OpenFileAsync(this, "Video importieren", Path.Combine(_app.Env.MediaDir, "Aufnahmen"), ("Videos", new[] { "*.mp4", "*.mkv", "*.webm", "*.mov", "*.avi" }));
        if (path is null) return;
        await PlaceFileAsync(path, mode);
    }

    private void ShowRecordMenu(Control anchor)
    {
        var menu = new MenuFlyout();
        void Item(string text, string mode) { var mi = new MenuItem { Header = text }; mi.Click += async (_, _) => await RecordIntoAsync(mode); menu.Items.Add(mi); }
        Item("Aufnehmen und an der Abspielposition einfügen (Rest rückt nach hinten)", "insert");
        Item("Aufnehmen und ans Ende anhängen", "append");
        Item("Aufnehmen und darüberlegen (Bild-im-Bild ab Abspielposition)", "overlay");
        Item("Nur aufnehmen (in die Medienliste)", "bin");
        menu.ShowAt(anchor);
    }

    /// <summary>Records a new video (this window is hidden meanwhile) and puts it into the edit.</summary>
    public async Task RecordIntoAsync(string mode)
    {
        if (_slideClip is not null) ExitSlide();
        if (_playTimer.IsEnabled) TogglePlay();
        var r = await App.Current.Recording.RecordAsync(this);
        if (r is null) return;
        await PlaceFileAsync(r.File, mode, r);
    }

    /// <summary>
    /// Adds a file to the media list and – unless <paramref name="mode"/> is "bin" – places it: insert at the playhead (ripple),
    /// append to the main track or overlay as picture-in-picture. Clicks/live drawings of a recording come along.
    /// </summary>
    public async Task<SequenceClip?> PlaceFileAsync(string path, string mode, RecordingResult? recording = null)
    {
        MediaInfo info;
        try { info = await MediaProbe.ProbeAsync(_tools, path); }
        catch (InvalidDataException ex) { await Dialogs.InfoAsync(this, "Video", ex.Message); return null; }
        // same file already in the media list (maybe stored relative vs. absolute) → reuse its path
        var full = Path.GetFullPath(path);
        var known = _seq.Media.FirstOrDefault(m => ProjectFile.ResolveMedia(_session.FilePath, m.Path, _app.Env.FromPortablePath) is { } r && string.Equals(Path.GetFullPath(r), full, StringComparison.OrdinalIgnoreCase));
        var stored = known?.Path ?? VideoStepRenderer.Store(_app, _session, path);
        var name = Path.GetFileNameWithoutExtension(path);
        var clip = new SequenceClip
        {
            Name = name, Path = stored, SourceIn = 0, SourceOut = info.Duration, SourceDuration = info.Duration,
            SourceWidth = info.Width, SourceHeight = info.Height, HasAudio = info.HasAudio,
        };
        await EnsureSourceAsync(path);
        var placed = mode != "bin";
        Edit(recording is null ? "Video importieren" : "Aufnahme einfügen", () =>
        {
            if (_seq.Media.FirstOrDefault(m => string.Equals(m.Path, stored, StringComparison.OrdinalIgnoreCase)) is { } existing) existing.Recorded |= recording is not null;
            else _seq.Media.Add(new MediaItem { Path = stored, Name = name, Duration = info.Duration, Width = info.Width, Height = info.Height, HasAudio = info.HasAudio, Recorded = recording is not null });
            if (!placed) return;
            switch (mode)
            {
                case "append":
                    clip.Start = _seq.Tracks[0].Clips.Select(c => c.End).DefaultIfEmpty(0).Max();
                    _seq.Tracks[0].Clips.Add(clip);
                    break;
                case "overlay":
                    clip.Start = _pos;
                    var free = _seq.Tracks.Skip(1).FirstOrDefault(t => !t.Clips.Any(c => c.Start < clip.End && c.End > clip.Start));
                    if (free is null) { free = new VideoTrack { Name = $"Video {_seq.Tracks.Count + 1}" }; _seq.Tracks.Add(free); }
                    free.Clips.Add(clip);
                    var full = SequenceOps.Placement(_seq, clip);
                    var m = Math.Round(Math.Min(_seq.Width, _seq.Height) * 0.03);
                    clip.Rect = new RectD(_seq.Width - full.Width * 0.32 - m, _seq.Height - full.Height * 0.32 - m, full.Width * 0.32, full.Height * 0.32);
                    break;
                default:
                    SequenceOps.RippleInsert(_seq, _overlay, _pos, clip.Duration);
                    clip.Start = _pos;
                    _seq.Tracks[0].Clips.Add(clip);
                    break;
            }
            foreach (var t in _seq.Tracks) t.Clips.Sort((a, b) => a.Start.CompareTo(b.Start));
            if (recording is not null)
            {
                // clicks and live drawings belong to the new clip: its time and its place on the canvas
                var rect = SequenceOps.Placement(_seq, clip);
                var f = rect.Width / Math.Max(1, recording.Width);
                var number = _overlay.Page.Layers.Count(l => l.Name.StartsWith("Klick ", StringComparison.Ordinal) || l.Name.StartsWith("Doppelklick ", StringComparison.Ordinal)) + 1;
                _overlay.Page.Layers.AddRange(ClickMarkers.Create(recording.Clicks, recording.Width, info.Duration, clip.Start, f, rect.X, rect.Y, number));
                _overlay.Page.Layers.AddRange(ClickMarkers.PlaceLive(recording.LiveLayers, clip.Start, f, rect.X, rect.Y));
            }
        });
        if (placed) SelectClip(clip.Id);
        BuildMediaBox();
        Refresh();
        _info.Text = $"„{name}“ {(recording is null ? "importiert" : "aufgenommen")} ({info.Duration:0.0} s{(info.Width != _seq.Width || info.Height != _seq.Height ? $", {info.Width}×{info.Height} wird eingepasst" : "")})" +
                     (placed ? "." : " – liegt in der Medienliste (Tab „Medien“).");
        return placed ? clip : null;
    }

    // ================================================================== media list

    private void BuildMediaBox()
    {
        _mediaBox.Children.Clear();
        _mediaBox.Children.Add(Ui.Label("Medien dieses Projekts", 14, bold: true));
        _mediaBox.Children.Add(Ui.Label("Alle Videos, die zu diesem Schnitt gehören – auch noch nicht platzierte. Die Dateien werden verknüpft, nicht verändert.", 11, opacity: 0.65));
        var rec = Ui.Button("● Aufnehmen…", () => { }, "Neues Video aufnehmen", accent: true);
        rec.Click += (_, _) => ShowRecordMenu(rec);
        var imp = Ui.Button("🎞 Importieren…", () => { }, "Vorhandenes Video hinzufügen");
        imp.Click += (_, _) => ShowImportMenu(imp);
        _mediaBox.Children.Add(new WrapPanel { Children = { rec, imp, Ui.Button("📦 Sammeln…", async () => await CollectMediaAsync(), "Alle Videos in einen Ordner neben dem Projekt kopieren (zum Weitergeben)") } });
        long total = 0;
        foreach (var m in _seq.Media.ToList())
        {
            var item = m;
            var resolved = ProjectFile.ResolveMedia(_session.FilePath, m.Path, _app.Env.FromPortablePath);
            var used = _seq.UseCount(m);
            if (resolved is not null) total += new FileInfo(resolved).Length;
            var thumb = resolved is not null ? Thumb(resolved, Math.Min(1, m.Duration / 2)) : null;
            var img = new Image { Width = 96, Height = 54, Stretch = Stretch.Uniform, Source = thumb };
            var text = Ui.Column(1,
                Ui.Label((resolved is null ? "⚠ " : m.Recorded ? "● " : "🎞 ") + m.Name, 12, bold: true),
                Ui.Label(resolved is null ? "Datei nicht gefunden" : $"{VideoTimeline.Fmt(m.Duration)} · {m.Width}×{m.Height}{(m.HasAudio ? " · ♪" : "")} · {Core.Export.SizeOptimizer.FormatBytes(new FileInfo(resolved).Length)}", 10.5, opacity: 0.75),
                Ui.Label(used == 0 ? "nicht in der Timeline" : $"{used}× in der Timeline", 10.5, opacity: 0.6));
            var buttons = new WrapPanel();
            if (resolved is not null)
            {
                buttons.Children.Add(Ui.Button("⤓", async () => await PlaceFileAsync(resolved, "insert"), "An der Abspielposition einfügen (Rest rückt nach)"));
                buttons.Children.Add(Ui.Button("⇥", async () => await PlaceFileAsync(resolved, "append"), "Ans Ende anhängen"));
                buttons.Children.Add(Ui.Button("⧉", async () => await PlaceFileAsync(resolved, "overlay"), "Darüberlegen (Bild-im-Bild ab Abspielposition)"));
                buttons.Children.Add(Ui.Button("📂", () => OpenFolder(Path.GetDirectoryName(resolved)!), "Im Ordner zeigen"));
            }
            buttons.Children.Add(Ui.Button(resolved is null ? "Neu verknüpfen…" : "↺", async () => await RelinkAsync(item), "Andere Datei an diese Stelle setzen (z. B. verschobene Aufnahme)"));
            if (used == 0) buttons.Children.Add(Ui.Button("✕", () => { Edit("Aus Medienliste entfernen", () => _seq.Media.Remove(item)); BuildMediaBox(); }, "Aus der Medienliste entfernen (die Datei bleibt erhalten)"));
            var row = new DockPanel();
            DockPanel.SetDock(img, Dock.Left); img.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(img);
            row.Children.Add(Ui.Column(2, text, buttons));
            _mediaBox.Children.Add(new Border
            {
                Child = row, Padding = new Thickness(5), CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(resolved is null ? Color.FromArgb(40, 220, 60, 40) : Color.FromArgb(22, 128, 128, 128)),
            });
        }
        _mediaBox.Children.Add(Ui.Label($"{_seq.Media.Count} Video(s), {Core.Export.SizeOptimizer.FormatBytes(total)}", 11, opacity: 0.6));
    }

    private async Task RelinkAsync(MediaItem m)
    {
        var path = await Dialogs.OpenFileAsync(this, "Datei auswählen", _app.Env.MediaDir, ("Videos", new[] { "*.mp4", "*.mkv", "*.webm", "*.mov", "*.avi" }));
        if (path is null) return;
        var stored = VideoStepRenderer.Store(_app, _session, path);
        var old = m.Path;
        Edit("Neu verknüpfen", () =>
        {
            foreach (var c in _seq.AllClips.Where(c => string.Equals(c.Path, old, StringComparison.OrdinalIgnoreCase))) c.Path = stored;
            m.Path = stored;
        });
        await EnsureSourceAsync(path);
        BuildMediaBox(); Refresh(); RecomposeSoon();
    }

    private async Task CollectMediaAsync()
    {
        WriteBack();
        if (_studio is null) SaveSidecar();
        if (_session.FilePath is null) { await Dialogs.InfoAsync(this, "Sammeln", "Das Projekt hat noch keinen Speicherort. Im Studio zuerst speichern (oder eine Annotation/einen Schnitt machen und das Fenster schließen – dann wird es neben dem Video gespeichert)."); return; }
        try
        {
            var r = await ProjectMediaService.CollectAsync(_app, _session, new Progress<string>(t => _info.Text = t));
            foreach (var sp in _sources.Values) { foreach (var b in sp.Thumbs) b.Dispose(); sp.Decoded?.Dispose(); }
            _sources.Clear();
            foreach (var p in _seq.AllClips.Select(ResolveClip).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)) await EnsureSourceAsync(p);
            BuildMediaBox(); Refresh(); RecomposeSoon();
            _info.Text = $"{r.Copied} Video(s) nach {ProjectMediaService.CollectFolder(_session)} kopiert, {r.AlreadyInside} lagen schon dort." + (r.Missing > 0 ? $" ⚠ {r.Missing} fehlen." : "");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            await Dialogs.InfoAsync(this, "Sammeln nicht möglich", ex.Message);
        }
    }

    // ================================================================== clipboard

    /// <summary>Current picture with all visible annotations (as in the export) into the clipboard.</summary>
    public async Task CopyFrameAsync()
    {
        _text.Commit();
        if (_app.Clipboard is null) { await Dialogs.InfoAsync(this, "Zwischenablage", "Auf diesem System noch nicht unterstützt."); return; }
        if (await FrameAsSessionAsync() is not { } f) return;
        using (f.Session)
        {
            using var img = f.Session.Renderer.RenderPage(f.Page);
            var ok = _app.Clipboard.SetImage(SkiaConvert.ToRaw(img), SkiaConvert.EncodePng(img, 3));
            _info.Text = ok ? $"Bild mit Annotationen kopiert ({img.Width} × {img.Height} px) – z. B. mit Strg+V in einen Chat einfügen." : "⚠ Zwischenablage ist von einem anderen Programm blockiert.";
        }
    }

    // ================================================================== time box (selected annotation or clip)

    private void BuildTimeBox()
    {
        _timeBox.Children.Clear();
        if (SelectedClip is { } clip && _slideClip is null) { BuildClipBox(clip); return; }
        var sel = _canvas.SelectedLayers.ToList();
        if (sel.Count != 1)
        {
            _timeBox.Children.Add(Ui.Label("Zeit & Reihenfolge", 13, bold: true));
            _timeBox.Children.Add(Ui.Label(sel.Count == 0
                ? "Werkzeug links wählen und direkt ins Video zeichnen. Neue Annotationen erscheinen ab der Abspielposition für " + $"{_app.Settings.Shared.VideoAnnotationSeconds:0.#} s. Einen Clip in der Timeline anklicken, um ihn zu teilen (S), zu entfernen (Entf) oder auf eine andere Spur zu ziehen."
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

    private void BuildClipBox(SequenceClip c)
    {
        var track = _seq.TrackOf(c.Id)!;
        var ti = _seq.Tracks.IndexOf(track);
        _timeBox.Children.Add(Ui.Label((c.Kind == ClipKind.Slide ? "▣ Folie – " : "🎞 Clip – ") + c.Name, 13, bold: true));
        if (c.Kind == ClipKind.Video)
            _timeBox.Children.Add(Ui.Label($"{Path.GetFileName(ResolveClip(c) ?? c.Path)} · Quelle {VideoTimeline.Fmt(c.SourceIn)}–{VideoTimeline.Fmt(c.SourceOut)} · Spur „{track.Name}“", 11, opacity: 0.7));
        var start = new NumericUpDown { Value = (decimal)Math.Round(c.Start, 2), Minimum = 0, Maximum = 100000, Increment = 0.1m, FormatString = "0.##", Width = 120 };
        start.ValueChanged += (_, _) =>
        {
            var ns = (double)(start.Value ?? 0); var d = ns - c.Start;
            var linked = _carry.IsChecked == true ? SequenceOps.LinkedAnnotations(_overlay, c) : new();
            Edit("Clip-Start", () => { c.Start = ns; SequenceOps.ShiftAnnotations(linked, d); });
        };
        _timeBox.Children.Add(Ui.Row(Ui.Label("Beginnt bei", 11), start, Ui.Label($"Länge {c.Duration:0.0} s", 11, opacity: 0.7)));
        _timeBox.Children.Add(new WrapPanel
        {
            Children =
            {
                Ui.Button("✂ Hier teilen", SplitAtPlayhead, "An der Abspielposition teilen (S)"),
                Ui.Button("🗑 Entfernen", () => DeleteClip(c), "Clip entfernen (Entf)"),
                Ui.Button("▲ Spur", () => MoveClipToTrack(c, +1), "Auf die Spur darüber (legt sich über das Video)"),
                Ui.Button("▼ Spur", () => MoveClipToTrack(c, -1), "Auf die Spur darunter"),
            },
        });
        if (c.Kind == ClipKind.Slide)
        {
            _timeBox.Children.Add(Ui.Button("✎ Folie bearbeiten", () => EnterSlide(c), "Inhalt und Hintergrund der Folie bearbeiten (Doppelklick auf den Clip)", accent: true));
            return;
        }
        var vol = new Slider { Minimum = 0, Maximum = 200, Value = c.Volume * 100, Width = 150 };
        vol.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) _canvas.Apply("Lautstärke", () => c.Volume = vol.Value / 100, "vol"); };
        var mute = new CheckBox { Content = "Ton aus", IsChecked = c.Muted || !c.HasAudio, IsEnabled = c.HasAudio };
        mute.IsCheckedChanged += (_, _) => Edit("Ton", () => c.Muted = mute.IsChecked == true);
        _timeBox.Children.Add(Ui.Row(Ui.Label("Lautstärke", 11), vol, mute));
        var op = new Slider { Minimum = 5, Maximum = 100, Value = c.Opacity * 100, Width = 150 };
        op.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { _canvas.Apply("Deckkraft", () => c.Opacity = op.Value / 100, "op"); RecomposeSoon(); } };
        _timeBox.Children.Add(Ui.Row(Ui.Label("Deckkraft", 11), op));
        _timeBox.Children.Add(Ui.Label(ti == 0 ? "Position (Hauptspur meist Vollbild):" : "Position im Bild – auch direkt im Video am Rahmen ziehen, Ecke unten rechts = Größe:", 11, opacity: 0.7));
        _timeBox.Children.Add(new WrapPanel
        {
            Children =
            {
                Ui.Button("Vollbild", () => SetPlacement(c, "full")), Ui.Button("↖", () => SetPlacement(c, "tl"), "Oben links klein"), Ui.Button("↗", () => SetPlacement(c, "tr"), "Oben rechts klein"),
                Ui.Button("↙", () => SetPlacement(c, "bl"), "Unten links klein"), Ui.Button("↘", () => SetPlacement(c, "br"), "Unten rechts klein"), Ui.Button("◻ Mitte", () => SetPlacement(c, "center"), "Mittig, 60 %"),
            },
        });
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
        _videoBox.Children.Add(Ui.Label("Werkzeug ⤢ links: im Video ein Rechteck aufziehen = dieser Ausschnitt ab der Abspielposition. Zwischen Keyframes wird weich übergeblendet.", 11, opacity: 0.65));
        if (_zoomFactor.Parent is Panel oldRow) oldRow.Children.Remove(_zoomFactor);
        _videoBox.Children.Add(Ui.Row(Ui.Label("Faktor bei Klick", 11), _zoomFactor));
        _videoBox.Children.Add(Ui.Row(
            Ui.Button("🔍 Keyframe hier", () => AddZoomKey(null, _zoomFactor.Value), "Zoom auf die Bildmitte mit dem Faktor"),
            Ui.Button("1× hier", () => AddZoomKey(null, 1), "Hier wieder ganz herauszoomen")));
        var keys = SortedZoom();
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i]; var idx = i;
            var factor = _seq.Width / Math.Max(1, key.View.Width);
            _videoBox.Children.Add(Ui.Row(
                Ui.Button($"{(idx == _zoomKey ? "◆" : "◇")} {VideoTimeline.Fmt(key.TimeSeconds)}  {factor:0.0}×", () => { _zoomKey = idx; SeekTo(key.TimeSeconds, true); BuildVideoBox(); }, "Zum Keyframe springen"),
                Ui.Button("✕", () => { Edit("Zoom löschen", () => _overlay.Zoom.Remove(key)); _zoomKey = null; BuildVideoBox(); }, "Keyframe löschen")));
        }

        _videoBox.Children.Add(new Separator());
        _videoBox.Children.Add(Ui.Label("Folien", 14, bold: true));
        _videoBox.Children.Add(Ui.Label("Folien sind Clips auf der Hauptspur: an der Abspielposition eingefügt, alles danach rückt nach hinten. Doppelklick auf den Folien-Clip = bearbeiten.", 11, opacity: 0.65));
        _videoBox.Children.Add(new WrapPanel
        {
            Children =
            {
                Ui.Button("＋ Leer", () => AddSlide(SlideTemplate.Blank), "Leere Folie an der Abspielposition"),
                Ui.Button("＋ Titel", () => AddSlide(SlideTemplate.Title), "Titelfolie"),
                Ui.Button("＋ Zusammenfassung", () => AddSlide(SlideTemplate.Summary), "Überschrift + Checkliste, Punkte erscheinen nacheinander"),
            },
        });
        foreach (var c in _seq.AllClips.Where(c => c.Kind == ClipKind.Slide).OrderBy(c => c.Start).ToList())
        {
            var clip = c;
            _videoBox.Children.Add(new Border
            {
                BorderBrush = clip.Id == _selectedClip ? Brushes.Gold : new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(4),
                Child = Ui.Row(
                    Ui.Label($"▣ {clip.Name}\n   bei {VideoTimeline.Fmt(clip.Start)} · {clip.Duration:0.#} s", 11),
                    Ui.Button("✎", () => EnterSlide(clip), "Folie bearbeiten"),
                    Ui.Button("✕", () => DeleteClip(clip), "Folie entfernen")),
            });
        }

        _videoBox.Children.Add(new Separator());
        var clickCount = _overlay.Page.Layers.Count(IsClickMarker);
        _videoBox.Children.Add(Ui.Label("Mausklick-Markierungen", 14, bold: true));
        _videoBox.Children.Add(Ui.Label(clickCount == 0 ? "Keine. (Einstellungen → Video: „Mausklicks markieren“)" : $"{clickCount} Markierung(en) – normale Annotationen, einzeln änderbar.", 11, opacity: 0.65));
        if (clickCount > 0)
            _videoBox.Children.Add(Ui.Button("Alle Klick-Markierungen entfernen", () => { _canvas.Apply("Klicks entfernen", () => _overlay.Page.Layers.RemoveAll(IsClickMarker)); BuildVideoBox(); }));

        static bool IsClickMarker(Layer l) => l is ShapeLayer && (l.Name.StartsWith("Klick ", StringComparison.Ordinal) || l.Name.StartsWith("Doppelklick ", StringComparison.Ordinal));
    }

    private void AddZoomKey(RectD? view, double factor)
    {
        if (_slideClip is not null) return;
        var W = _seq.Width; var H = _seq.Height;
        RectD v;
        if (view is { } r) v = r;
        else
        {
            var w = W / Math.Max(1, factor); var h = H / Math.Max(1, factor);
            var (cx, cy) = _stageOverlay.LastCenter is { } c ? (c.X, c.Y) : (W / 2.0, H / 2.0);
            v = new RectD(Math.Clamp(cx - w / 2, 0, W - w), Math.Clamp(cy - h / 2, 0, H - h), w, h);
        }
        Edit("Zoom", () =>
        {
            if (_overlay.Zoom.Count == 0 && v.Width < W - 1 && _pos > 0.5)
                _overlay.Zoom.Add(new ZoomKeyframe { TimeSeconds = Math.Max(0, Math.Round(_pos - 1, 2)), View = new RectD(0, 0, W, H) });
            _overlay.Zoom.RemoveAll(z => Math.Abs(z.TimeSeconds - _pos) < 0.05);
            _overlay.Zoom.Add(new ZoomKeyframe { TimeSeconds = Math.Round(_pos, 2), View = v });
        });
        _zoomKey = SortedZoom().FindIndex(z => Math.Abs(z.TimeSeconds - _pos) < 0.06);
        Refresh(); BuildVideoBox();
    }

    private void OnZoomPicked(Rect viewRect)
    {
        var s = _canvas.ViewScale;
        if (viewRect.Width < 6 || viewRect.Height < 6)
        {
            _stageOverlay.LastCenter = new Point(viewRect.X / s, viewRect.Y / s);
            AddZoomKey(null, _zoomFactor.Value);
            return;
        }
        var aspect = _seq.Width / (double)Math.Max(1, _seq.Height);
        var w = Math.Clamp(Math.Max(viewRect.Width / s, viewRect.Height / s * aspect), 16, _seq.Width);
        var h = w / aspect;
        var cx = (viewRect.X + viewRect.Width / 2) / s; var cy = (viewRect.Y + viewRect.Height / 2) / s;
        AddZoomKey(new RectD(Math.Clamp(cx - w / 2, 0, _seq.Width - w), Math.Clamp(cy - h / 2, 0, _seq.Height - h), w, h), 1);
    }

    private void UpdateStageOverlay()
    {
        _stageOverlay.ViewRect = _slideClip is null && ZoomViewAt(_pos) is { } v ? _canvas.ToView(v) : null;
        _stageOverlay.ClipRect = _slideClip is null && SelectedClip is { Kind: ClipKind.Video } c && c.Contains(_pos) ? _canvas.ToView(SequenceOps.Placement(_seq, c)) : null;
        _stageOverlay.Aspect = SelectedClip is { SourceWidth: > 0, SourceHeight: > 0 } sc ? sc.SourceWidth / (double)sc.SourceHeight : 16 / 9.0;
        _stageOverlay.InvalidateVisual();
    }

    private RectD? ZoomViewAt(double t)
    {
        var z = SortedZoom();
        if (z.Count == 0) return null;
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
        return v.Width >= _seq.Width - 1 ? null : v;
    }

    // ================================================================== slides (clips on the main track)

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
        if (_slideClip is not null) ExitSlide();
        var w = Math.Max(320, _seq.Width); var h = Math.Max(180, _seq.Height);
        var slide = new VideoSlide
        {
            Name = t switch { SlideTemplate.Title => "Titel", SlideTemplate.Summary => "Zusammenfassung", _ => "Folie" },
            Duration = t == SlideTemplate.Summary ? 8 : 5,
            Page = new Page { Width = w, Height = h, Background = new ColorRgba(28, 30, 38), Name = "Folie" },
        };
        var fs = Math.Max(18, h / 12.0);
        TextLayer Txt(string text, double size, double y, double start, bool bold = false, double x = -1)
        {
            var tl = new TextLayer { Text = text, FontSize = size, Bold = bold, Foreground = ColorRgba.White, Padding = 4, Name = text.Split('\n')[0], FontFamily = _canvas.Style.FontFamily };
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
        var at = Math.Round(_pos, 2);
        var clip = new SequenceClip { Name = slide.Name, Kind = ClipKind.Slide, Slide = slide, Start = at };
        Edit("Folie einfügen", () =>
        {
            SequenceOps.RippleInsert(_seq, _overlay, at, slide.Duration);
            _seq.Tracks[0].Clips.Add(clip);
            _seq.Tracks[0].Clips.Sort((a, b) => a.Start.CompareTo(b.Start));
        });
        EnterSlide(clip);
        return slide;
    }

    public void EnterSlide(SequenceClip clip)
    {
        if (clip.Slide is null) return;
        if (_playTimer.IsEnabled) TogglePlay();
        _text.Commit();
        SetZoomPick(false);
        _slideClip = clip; _selectedClip = clip.Id; _slidePos = 0;
        _session.ResetHistory(clip.Slide.Page);       // the page object may have been replaced by undo
        _canvas.SetPage(clip.Slide.Page);
        _canvas.Underlay = null;
        _timeline.ZoomFactor = 1;
        BuildSlideBanner();
        _slideBanner.IsVisible = true;
        FitCanvas();
        Refresh(); BuildVideoBox(); BuildTimeBox();
        _info.Text = $"Folie „{clip.Slide.Name}“: Elemente mit den Werkzeugen links hinzufügen. Jedes Element hat unten eine Spur – so erscheinen Punkte nacheinander. „✓ Zurück zum Video“ beendet die Bearbeitung.";
    }

    public void ExitSlide()
    {
        if (_slideClip is null) return;
        if (_playTimer.IsEnabled) TogglePlay();
        _text.Commit();
        _pos = Math.Max(_pos, _slideClip.Start);
        _slideClip = null;
        _canvas.SetPage(_overlay.Page);
        _slideBanner.IsVisible = false;
        FitCanvas();
        Refresh(); BuildVideoBox(); BuildTimeBox();
        SeekTo(_pos, true);
    }

    private void BuildSlideBanner()
    {
        if (_slideClip is not { Slide: { } s } clip) return;
        var name = new TextBox { Text = s.Name, Width = 170 };
        name.TextChanged += (_, _) => { s.Name = clip.Name = name.Text ?? "Folie"; MarkChanged(); Refresh(); };
        var dur = new NumericUpDown { Value = (decimal)s.Duration, Minimum = 0.5m, Maximum = 600, Increment = 0.5m, FormatString = "0.#", Width = 110 };
        dur.ValueChanged += (_, _) =>
        {
            var old = s.Duration; var nd = (double)(dur.Value ?? 5);
            // the rest of the edit moves with the end of the slide (main track)
            var main = _seq.Tracks.IndexOf(_seq.TrackOf(clip.Id)!) == 0;
            if (main && nd > old) SequenceOps.RippleInsert(_seq, _overlay, clip.End, nd - old);
            else if (main && nd < old) SequenceOps.RippleDelete(_seq, _overlay, clip.Start + nd, clip.End);
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
                if (cc.R + cc.G + cc.B > 600) _canvas.Apply("Textfarbe", () => { foreach (var t in s.Page.Layers.OfType<TextLayer>().Where(t => t.Foreground == ColorRgba.White)) t.Foreground = new ColorRgba(30, 30, 30); });
                else _canvas.Apply("Textfarbe", () => { foreach (var t in s.Page.Layers.OfType<TextLayer>().Where(t => t.Foreground == new ColorRgba(30, 30, 30))) t.Foreground = ColorRgba.White; });
                Refresh();
            };
            colors.Children.Add(b);
        }
        _slideBanner.Child = new WrapPanel
        {
            Children =
            {
                Ui.Label("▣ Folie ", 13, bold: true), name,
                Ui.Label("  Dauer s", 11), dur,
                Ui.Label("  Hintergrund", 11), colors,
                Ui.Button("✓ Zurück zum Video", ExitSlide, "Folienbearbeitung beenden", accent: true),
                Ui.Button("🗑 Folie entfernen", () => { var c = clip; ExitSlide(); DeleteClip(c); }),
            },
        };
    }

    // ================================================================== playback

    private void SeekTo(double t, bool hiRes)
    {
        if (_slideClip is not null)
        {
            _slidePos = Math.Clamp(t, 0, _slide!.Duration);
            PruneSelection();
            Refresh();
            return;
        }
        _pos = Math.Clamp(t, 0, Math.Max(0, _seq.Duration));
        SetFrame(Compose(_pos));
        PruneSelection();
        Refresh();
        if (_playTimer.IsEnabled) _timeline.EnsureVisible(_pos);
        if (hiRes) _ = LoadHiResAsync(_pos);
    }

    private void PruneSelection()
    {
        var keep = _canvas.SelectedLayers.Where(l => ActiveAt(l, CurrentTime)).Select(l => l.Id).ToList();
        if (keep.Count != _canvas.Selection.Count) _canvas.Select(keep);
    }

    private void TogglePlay()
    {
        if (_playTimer.IsEnabled) { _playTimer.Stop(); _play.Content = "▶"; SeekTo(CurrentTime, true); return; }
        _text.Commit();
        if (CurrentTime >= CurrentDuration - 0.05) { if (_slideClip is null) _pos = 0; else _slidePos = 0; }
        _playTimer.Start();
        _play.Content = "❚❚";
    }

    private void PlayStep()
    {
        var next = CurrentTime + _playTimer.Interval.TotalSeconds;
        if (next >= CurrentDuration) { TogglePlay(); SeekTo(CurrentDuration, true); return; }
        SeekTo(next, false);
    }

    private void StepFrame(int dir) => SeekTo(CurrentTime + dir / Math.Max(1, _seq.Fps), true);

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
            case Key.C when ctrl: _ = CopyFrameAsync(); break;
            case Key.OemCloseBrackets when ctrl: _canvas.MoveSelection(shift ? int.MaxValue : 1); break;
            case Key.OemOpenBrackets when ctrl: _canvas.MoveSelection(shift ? int.MinValue : -1); break;
            case Key.Left when hasSel && !ctrl: _canvas.Nudge(shift ? -10 : -1, 0); break;
            case Key.Right when hasSel && !ctrl: _canvas.Nudge(shift ? 10 : 1, 0); break;
            case Key.Up when hasSel: _canvas.Nudge(0, shift ? -10 : -1); break;
            case Key.Down when hasSel: _canvas.Nudge(0, shift ? 10 : 1); break;
            case Key.Left: SeekTo(CurrentTime - (shift ? 1 : 1 / Math.Max(1, _seq.Fps)), true); break;
            case Key.Right: SeekTo(CurrentTime + (shift ? 1 : 1 / Math.Max(1, _seq.Fps)), true); break;
            case Key.OemComma: StepFrame(-1); break;
            case Key.OemPeriod: StepFrame(1); break;
            case Key.Home: SeekTo(0, true); break;
            case Key.End: SeekTo(CurrentDuration, true); break;
            case Key.S when !ctrl: SplitAtPlayhead(); break;
            case Key.X when !ctrl: CutRange(); break;
            case Key.I when !ctrl && _slideClip is null: RippleRange(0, _pos, "Anfang entfernen"); break;
            case Key.O when !ctrl && _slideClip is null: RippleRange(_pos, _seq.Duration, "Ende entfernen"); break;
            case Key.Delete or Key.Back:
                if (hasSel) { _canvas.DeleteSelection(); break; }
                if (SelectedClip is { } clip) { DeleteClip(clip); break; }
                if (_zoomKey is { } zk && zk < _overlay.Zoom.Count) { var key = SortedZoom()[zk]; Edit("Zoom löschen", () => _overlay.Zoom.Remove(key)); _zoomKey = null; BuildVideoBox(); break; }
                if (_rangeSel is not null) CutRange();
                break;
            case Key.Escape:
                if (_rangeSel is not null) { _rangeSel = null; _timeline.Selection = null; Refresh(); }
                else if (hasSel) _canvas.Select(Array.Empty<Guid>());
                else if (_selectedClip is not null) SelectClip(null);
                else if (_zoomPick) SetZoomPick(false);
                else if (_slideClip is not null) ExitSlide();
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
            case Key.L when !ctrl: SetTool(Tool.Spotlight); break;
            case Key.M when !ctrl: SetTool(Tool.Magnifier); break;
            case Key.B when !ctrl: SetTool(Tool.Blackout); break;
            case Key.P when !ctrl: SetTool(Tool.Pixelate); break;
            case Key.Z when !ctrl: SetZoomPick(!_zoomPick); break;
            default: return;
        }
        e.Handled = true;
    }

    // ================================================================== export / guide / frames

    private async Task<VideoRenderPlan.Prepared?> PrepareAsync(string workDir)
    {
        var progress = new Progress<string>(s => _info.Text = s);
        try
        {
            IsEnabled = false;
            return await VideoRenderPlan.PrepareSequenceAsync(_app, _session, _seq, _overlay, workDir, progress);
        }
        catch (InvalidOperationException ex) { await Dialogs.InfoAsync(this, "Vorbereitung fehlgeschlagen", ex.Message); return null; }
        finally { IsEnabled = true; }
    }

    private async Task ExportAsync(bool gif)
    {
        if (_media is null) return;
        if (_playTimer.IsEnabled) TogglePlay();
        if (_slideClip is not null) ExitSlide();
        _text.Commit();
        var work = Path.Combine(_app.Env.TempDir, $"export-{Guid.NewGuid():N}");
        try
        {
            var plan = await PrepareAsync(work);
            if (plan is null) return;
            var info = await MediaProbe.ProbeAsync(_tools, plan.Source);
            var dlg = new VideoExportDialog(_app, _exporter, plan.Source, info, plan.Clip, gif, plan.Overlay, Path.GetFileNameWithoutExtension(_file));
            await dlg.ShowDialog(this);
        }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private void WriteBack()
    {
        if (_step?.Video is not { } v || _seq.Tracks.Count == 0) return;
        v.Sequence = _seq;
        v.Overlay = _overlay;
        v.Start = 0; v.End = _seq.Duration; v.Cuts.Clear();
        _session.MarkDirty();
    }

    /// <summary>The edit of a recording is kept in "&lt;video&gt;.capturedoc" next to it (also opens in the studio).</summary>
    private void SaveSidecar()
    {
        if (!_ownsSession || _sidecar is null || _media is null || _seq.Tracks.Count == 0) return;
        var edited = _seq.AllClips.Count() != 1 || _seq.Tracks.Count > 1 || _seq.AllClips.First() is var c && (c.SourceIn > 0.01 || c.SourceOut < _media.Duration - 0.01);
        if (!OverlayRenderer.HasContent(_overlay) && !edited && !File.Exists(_sidecar)) return;
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
            // clip paths become absolute/portable first, Save() makes them relative to the side-car
            foreach (var clip in _seq.AllClips.Where(x => x.Kind == ClipKind.Video))
                if (ResolveClip(clip) is { } full) clip.Path = ProjectFile.ToProjectRelative(null, full, ProjectSession.ToPortable);
            WriteBack();
            _session.Save(_sidecar, force: true);
            Log.Info("Video edit saved to " + _sidecar);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ProjectFormatException)
        {
            Log.Warn("Video side-car not saved: " + ex.Message);
        }
    }

    /// <summary>Current picture (or slide) as an editable screenshot: background + clones of the visible annotations.</summary>
    private async Task<(ProjectSession Session, Page Page)?> FrameAsSessionAsync()
    {
        ProjectSession session;
        if (_slide is { } s)
        {
            using var flat = _session.Renderer.RenderPage(new Page { Width = s.Page.Width, Height = s.Page.Height, Background = s.Page.Background });
            session = ProjectSession.FromCapture(flat, SkiaConvert.EncodePng(flat), s.Name);
        }
        else
        {
            if (await ComposeExactAsync(_pos) is not { } f) return null;
            using (f.img) session = ProjectSession.FromCapture(f.img, f.png, $"{Path.GetFileNameWithoutExtension(_file)} {VideoTimeline.Fmt(_pos).Replace(':', '-')}");
        }
        var page = session.Project.Pages[0];
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
        if (_slideClip is not null) ExitSlide();
        if (await ComposeExactAsync(0.05) is not { } poster) return;
        var studio = App.Current.ShowStudio();
        var v = new VideoRef { IncludeAudio = _media.HasAudio, MaxWidth = Math.Min(1280, _seq.Width), Start = 0, End = _seq.Duration };
        studio.AddVideoStep(_file, v, poster.img, poster.png, Path.GetFileNameWithoutExtension(_file), _session, _overlay, _seq);
        _info.Text = "Als Video-Schritt zur Anleitung hinzugefügt (alle Spuren, Annotationen, Zoom und Folien bleiben bearbeitbar).";
    }

    private static void OpenFolder(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Open folder: " + ex.Message); }
    }

    // ================================================================== stage overlay (zoom pick + picture-in-picture frame)

    /// <summary>
    /// Draws the zoom view and the frame of the selected clip. Zoom tool: drag a rectangle anywhere.
    /// Selected clip: drag its border to move it, the corner handle to resize (aspect kept). Elsewhere clicks go to the canvas.
    /// </summary>
    private sealed class StageOverlay : Control, Avalonia.Rendering.ICustomHitTest
    {
        public Rect? ViewRect { get; set; }
        public Rect? ClipRect { get; set; }
        public double Aspect { get; set; } = 16 / 9.0;
        public bool ZoomPick { get; set; }
        public Point? LastCenter { get; set; }
        private Point? _start;
        private Rect? _drag;
        private enum Mode { None, Zoom, Move, Resize }
        private Mode _mode;
        private Rect _clip0;
        public event EventHandler<Rect>? ZoomPicked;
        public event EventHandler? ClipRectStarted;
        public event EventHandler<Rect>? ClipRectChanging;
        public event EventHandler? ClipRectDone;

        public bool HitTest(Point p)
        {
            if (ZoomPick) return true;
            if (ClipRect is not { } r) return false;
            var outer = r.Inflate(7); var inner = r.Deflate(7);
            return outer.Contains(p) && (!inner.Contains(p) || HandleRect(r).Contains(p));
        }

        private static Rect HandleRect(Rect r) => new(r.Right - 12, r.Bottom - 12, 16, 16);

        public override void Render(DrawingContext ctx)
        {
            if (ZoomPick) ctx.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
            if (ViewRect is { } v)
            {
                ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0, 160, 255)), 2, new DashStyle(new double[] { 5, 3 }, 0)), v);
                ctx.DrawText(new FormattedText("Zoom-Ausschnitt", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, new SolidColorBrush(Color.FromRgb(0, 160, 255))), new Point(v.X + 4, v.Y + 2));
            }
            if (ClipRect is { } c && !ZoomPick)
            {
                ctx.DrawRectangle(null, new Pen(Brushes.Gold, 2), c);
                ctx.DrawRectangle(Brushes.Gold, new Pen(Brushes.Black, 1), HandleRect(c));
            }
            if (_drag is { } d) ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 160, 0)), new Pen(Brushes.Orange, 2), d);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var p = e.GetPosition(this);
            _start = p;
            e.Pointer.Capture(this);
            e.Handled = true;
            if (ZoomPick) { _mode = Mode.Zoom; _drag = new Rect(p, new Size(0, 0)); return; }
            if (ClipRect is { } r)
            {
                _clip0 = r;
                _mode = HandleRect(r).Contains(p) ? Mode.Resize : Mode.Move;
                ClipRectStarted?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (_start is not { } s) return;
            var p = e.GetPosition(this);
            switch (_mode)
            {
                case Mode.Zoom:
                    _drag = new Rect(Math.Min(s.X, p.X), Math.Min(s.Y, p.Y), Math.Abs(p.X - s.X), Math.Abs(p.Y - s.Y));
                    InvalidateVisual();
                    break;
                case Mode.Move:
                    ClipRectChanging?.Invoke(this, _clip0.Translate(new Vector(p.X - s.X, p.Y - s.Y)));
                    break;
                case Mode.Resize:
                    var w = Math.Max(20, _clip0.Width + (p.X - s.X));
                    ClipRectChanging?.Invoke(this, new Rect(_clip0.X, _clip0.Y, w, w / Math.Max(0.1, Aspect)));
                    break;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_start is not { } s) return;
            var mode = _mode;
            var r = _drag ?? new Rect(s, new Size(0, 0));
            _start = null; _drag = null; _mode = Mode.None;
            e.Pointer.Capture(null);
            InvalidateVisual();
            if (mode == Mode.Zoom) ZoomPicked?.Invoke(this, r.Width < 6 && r.Height < 6 ? new Rect(s.X, s.Y, 0, 0) : r);
            else if (mode is Mode.Move or Mode.Resize) ClipRectDone?.Invoke(this, EventArgs.Empty);
        }
    }
}
