using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CaptureDoc.App.Editor;
using CaptureDoc.App.Services;
using CaptureDoc.App.Util;
using CaptureDoc.Core.Export;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Obsidian;
using CaptureDoc.Core.Serialization;
using CaptureDoc.Rendering;

using Page = CaptureDoc.Core.Model.Page;

namespace CaptureDoc.App.Views;

/// <summary>Documentation Studio: builds a step-by-step guide from pages and exports it to Obsidian/Markdown/HTML.</summary>
public sealed class StudioWindow : Window
{
    private readonly AppServices _app;
    private ProjectSession _session;
    private readonly ListBox _steps = new();
    private readonly StackPanel _stepEditor = new() { Spacing = 8 };
    private readonly TextBox _title = new(), _tags = new(), _status = new(), _prereq = Multi(70), _intro = Multi(60), _checklist = Multi(80), _conclusion = Multi(50);
    private readonly ComboBox _template = new() { MinWidth = 240 };
    private readonly ComboBox _vault = new() { MinWidth = 220 };
    private readonly TextBox _folder = new() { MinWidth = 220 };
    private readonly ComboBox _links = new() { ItemsSource = new[] { "Wikilinks ![[…]]", "Markdown-Links ![](…)" } };
    private readonly ComboBox _profile = new() { MinWidth = 180 };
    private readonly TextBlock _info = Ui.Label("", 12, opacity: 0.75);
    private List<GuideTemplate> _templates;
    private readonly List<ExportProfile> _profiles;
    private bool _loading;

    private static TextBox Multi(double h) => new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = h };

    public ProjectSession Session => _session;

    public StudioWindow(AppServices app)
    {
        _app = app;
        Icon = IconFactory.WindowIcon;
        Width = 1240; Height = 860; MinWidth = 900; MinHeight = 560;
        Opened += (_, _) => FitToScreen(this);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _templates = app.Templates.LoadAll();
        _profiles = app.Profiles.All.ToList();
        _session = NewGuideSession();

        _template.ItemsSource = _templates.Select(t => t.Name).ToList();
        _profile.ItemsSource = _profiles.Select(p => p.Name).ToList();
        _steps.SelectionChanged += (_, _) => BuildStepEditor();

        foreach (var tb in new[] { _title, _tags, _status, _prereq, _intro, _checklist, _conclusion })
            tb.TextChanged += (_, _) => PullMeta();
        _template.SelectionChanged += (_, _) => PullMeta();

        Content = BuildLayout();
        LoadVaults();
        LoadFromSession();
        Closing += async (_, e) =>
        {
            if (!_session.IsDirty || _session.Project.Guide.Steps.Count == 0 || _forceClose) { return; }
            e.Cancel = true;
            var a = await Dialogs.AskAsync(this, "Anleitung speichern?", "Die Anleitung hat ungespeicherte Änderungen.", "Speichern", "Verwerfen", "Abbrechen");
            if (a == "Speichern" && !await SaveAsync(false)) return;
            if (a is null or "Abbrechen") return;
            _forceClose = true; Close();
        };
        Closed += (_, _) => { Recovery.Delete(_session); _session.Dispose(); };
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(15, app.Settings.Shared.AutoSaveSeconds)) };
        timer.Tick += (_, _) => { if (_session.Project.Guide.Steps.Count > 0) Recovery.Write(_session); };
        timer.Start();
    }

    private bool _forceClose;

    /// <summary>Keeps large windows (and their title bar) inside the working area of small screens.</summary>
    public static void FitToScreen(Window w)
    {
        var screen = w.Screens.ScreenFromWindow(w) ?? w.Screens.Primary;
        if (screen is null) return;
        var wa = screen.WorkingArea; var s = screen.Scaling;
        var maxW = wa.Width / s - 20; var maxH = wa.Height / s - 20;
        if (w.Width > maxW) w.Width = maxW;
        if (w.Height > maxH) w.Height = maxH;
        w.Position = new PixelPoint(wa.X + (int)((wa.Width - w.Width * s) / 2), wa.Y + (int)((wa.Height - w.Height * s) / 2));
    }

    private static ProjectSession NewGuideSession()
    {
        var p = new CaptureProject { Title = "Neue Anleitung" };
        p.Guide.Title = "Neue Anleitung";
        return new ProjectSession(p, new AssetStore());
    }

    // ---------------------------------------------------------------- layout

    private Control BuildLayout()
    {
        var top = new WrapPanel { Margin = new Thickness(8, 6) };
        top.Children.Add(Ui.Button("Neu", async () => await NewAsync()));
        top.Children.Add(Ui.Button("Öffnen…", async () => await OpenAsync()));
        top.Children.Add(Ui.Button("💾 Speichern", async () => await SaveAsync(false), accent: true));
        top.Children.Add(Ui.Button("Speichern unter…", async () => await SaveAsync(true)));
        top.Children.Add(new Separator { Width = 16 });
        top.Children.Add(Ui.Button("📷 Bereich aufnehmen", async () => await CaptureStepAsync(), "Neuer Schritt aus einer Bereichsaufnahme"));
        top.Children.Add(Ui.Button("📷×N Serie", async () => await CaptureSeriesAsync(), "Gleichen Bereich mehrmals im Abstand aufnehmen – jedes Bild wird ein Schritt"));
        top.Children.Add(Ui.Button("🖼 Bild hinzufügen…", async () => await AddImageFileAsync(), "PNG/JPEG oder .capturedoc als Schritt hinzufügen"));
        top.Children.Add(Ui.Button("🎬 Video hinzufügen…", async () => await AddVideoFileAsync(), "Ganzes Video (MP4/MKV/WebM/MOV) als Schritt – wird beim Export eingebettet"));
        top.Children.Add(Ui.Button("● Video aufnehmen", async () => await RecordVideoStepAsync(), "Neues Video aufnehmen – wird ein Video-Schritt und öffnet sich im Video-Editor"));
        top.Children.Add(Ui.Button("📁 Dateien", ShowProjectFiles, "Übersicht aller Videos/Dateien dieses Projekts – fehlende neu verknüpfen, alles in den Projektordner sammeln"));
        top.Children.Add(new Separator { Width = 16 });
        top.Children.Add(Ui.Button("⇪ Obsidian-Export…", async () => await ExportObsidianAsync(), "Vorschau, Konfliktprüfung und Export in den gewählten Vault", accent: true));
        top.Children.Add(Ui.Button("Markdown…", async () => await ExportMarkdownFolderAsync(), "Als Markdown in beliebigen Ordner"));
        top.Children.Add(Ui.Button("HTML…", async () => await ExportHtmlAsync(), "Eigenständige HTML-Seite mit Bild-Popup und eingebetteten Videos"));
        top.Children.Add(Ui.Button("PDF…", async () => await ExportPdfAsync(), "PDF-Dokument (Videos erscheinen als Standbild mit Hinweis)"));

        // left: meta + steps
        var stepButtons = new WrapPanel
        {
            Children =
            {
                Ui.Button("▲", () => MoveStep(-1), "Nach oben"), Ui.Button("▼", () => MoveStep(1), "Nach unten"),
                Ui.Button("✎ Bild bearbeiten", OpenStepEditor, "Bild mit allen Ebenen im Editor bearbeiten"),
                Ui.Button("📋 Kopieren", CopyStepImage, "Bild des Schritts mit allen Annotationen in die Zwischenablage (z. B. zum Einfügen in einen Chat)"),
                Ui.Button("🗑", RemoveStep, "Schritt entfernen"),
                Ui.Button("+ Textschritt", () => AddStep(null, "Neuer Schritt"), "Schritt ohne Bild"),
            },
        };
        var left = new DockPanel { Width = 360 };
        var meta = Ui.Column(6,
            Ui.Field("Titel", _title),
            Ui.Field("Vorlage", _template),
            Ui.Row(Ui.Field("Status", _status), Ui.Field("Tags (Komma)", _tags)),
            Ui.Label("Schritte", 14, bold: true));
        DockPanel.SetDock(meta, Dock.Top); left.Children.Add(meta);
        DockPanel.SetDock(stepButtons, Dock.Bottom); left.Children.Add(stepButtons);
        left.Children.Add(_steps);

        // centre: step editor + guide texts
        var centre = new ScrollViewer
        {
            Content = Ui.Column(10,
                _stepEditor,
                new Separator(),
                Ui.Label("Anleitung", 14, bold: true),
                Ui.Field("Einleitung", _intro),
                Ui.Field("Voraussetzungen (als Callout)", _prereq),
                Ui.Field("Checkliste (eine Zeile pro Punkt)", _checklist),
                Ui.Field("Abschluss", _conclusion)),
            Margin = new Thickness(12, 0),
        };

        // export target
        _links.SelectionChanged += (_, _) => SaveVaultSelection();
        _profile.SelectionChanged += (_, _) => SaveVaultSelection();
        _folder.LostFocus += (_, _) => SaveVaultSelection();
        _vault.SelectionChanged += (_, _) => ShowVault();
        var target = new WrapPanel
        {
            Margin = new Thickness(8, 4),
            Children =
            {
                Ui.Field("Obsidian-Vault", _vault),
                Ui.Button("+ Vault…", async () => await AddVaultAsync(), "Vorhandenen Vault-Ordner wählen"),
                Ui.Field("Zielordner im Vault", _folder),
                Ui.Field("Links", _links),
                Ui.Field("Bildprofil", _profile),
            },
        };

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        DockPanel.SetDock(target, Dock.Top); root.Children.Add(new Border { Child = target, BorderBrush = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)), BorderThickness = new Thickness(0, 1, 0, 1) });
        root.Children[^1].SetValue(DockPanel.DockProperty, Dock.Top);
        DockPanel.SetDock(_info, Dock.Bottom); _info.Margin = new Thickness(8, 4); root.Children.Add(_info);
        var body = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(left, Dock.Left); body.Children.Add(left);
        body.Children.Add(centre);
        root.Children.Add(body);
        return root;
    }

    // ---------------------------------------------------------------- data binding (manual)

    private void LoadFromSession()
    {
        _loading = true;
        var g = _session.Project.Guide;
        _title.Text = g.Title; _tags.Text = string.Join(", ", g.Tags); _status.Text = g.Status;
        _prereq.Text = g.Prerequisites; _intro.Text = g.Introduction;
        _checklist.Text = string.Join("\n", g.Checklist); _conclusion.Text = g.Conclusion;
        _template.SelectedIndex = Math.Max(0, _templates.FindIndex(t => t.Id == g.TemplateId));
        _loading = false;
        RefreshSteps();
        UpdateTitle();
    }

    private void PullMeta()
    {
        if (_loading) return;
        var g = _session.Project.Guide;
        g.Title = _title.Text ?? ""; _session.Project.Title = string.IsNullOrWhiteSpace(g.Title) ? "Anleitung" : g.Title;
        g.Tags = (_tags.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        g.Status = _status.Text ?? "";
        g.Prerequisites = _prereq.Text ?? ""; g.Introduction = _intro.Text ?? "";
        g.Checklist = (_checklist.Text ?? "").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        g.Conclusion = _conclusion.Text ?? "";
        if (_template.SelectedIndex >= 0) g.TemplateId = _templates[_template.SelectedIndex].Id;
        _session.MarkDirty();
        UpdateTitle();
    }

    private void UpdateTitle() => Title = $"{(_session.IsDirty ? "• " : "")}{_session.Project.Guide.Title} – CaptureDoc Documentation Studio";

    private readonly Dictionary<Guid, Bitmap> _thumbs = new();

    private Bitmap? Thumb(Page? page, int max = 120)
    {
        if (page is null) return null;
        using var img = _session.Renderer.RenderPage(page, Math.Min(1.0, max / (double)Math.Max(page.Width, page.Height)));
        return Ui.ToBitmap(img);
    }

    private void RefreshSteps(int? select = null)
    {
        var keep = select ?? _steps.SelectedIndex;
        foreach (var t in _thumbs.Values) t.Dispose();
        _thumbs.Clear();
        var items = new List<Control>();
        var n = 0;
        foreach (var s in _session.Project.Guide.Steps)
        {
            n++;
            var page = s.PageId is { } pid ? _session.Project.FindPage(pid) : null;
            var thumb = Thumb(page);
            if (thumb is not null) _thumbs[s.Id] = thumb;
            var img = new Image { Source = thumb, Width = 96, Height = 64, Stretch = Stretch.Uniform };
            items.Add(new Border
            {
                Padding = new Thickness(2), Tag = s,
                Child = Ui.Row(img, Ui.Column(2, Ui.Label($"{n}. {(string.IsNullOrWhiteSpace(s.Title) ? "(ohne Titel)" : s.Title)}", 13, bold: true), Ui.Label(s.Video is { } vr ? $"🎬 Video {vr.KeptDuration:0.0} s · {vr.Format.ToString().ToUpperInvariant()}{(vr.Overlay is not null ? " · Annotationen" : "")}" : page is null ? "Textschritt" : $"{page.Width}×{page.Height}", 11, opacity: 0.6))),
            });
        }
        _steps.ItemsSource = items;
        if (items.Count > 0) _steps.SelectedIndex = Math.Clamp(keep, 0, items.Count - 1);
        _info.Text = $"{items.Count} Schritte · {_session.Project.Pages.Count} Bilder · {(_session.FilePath ?? "nicht gespeichert")}";
    }

    private GuideStep? SelectedStep => (_steps.SelectedItem as Control)?.Tag as GuideStep;

    private void BuildStepEditor()
    {
        _stepEditor.Children.Clear();
        var s = SelectedStep;
        if (s is null)
        {
            _stepEditor.Children.Add(Ui.Label("Noch keine Schritte. Über „Bereich aufnehmen“, „Bild hinzufügen“ oder im Editor „Zur Anleitung“ hinzufügen.", 13, opacity: 0.7));
            return;
        }
        var idx = _session.Project.Guide.Steps.IndexOf(s) + 1;
        _stepEditor.Children.Add(Ui.Label($"Schritt {idx}", 16, bold: true));
        var title = new TextBox { Text = s.Title };
        title.TextChanged += (_, _) => { s.Title = title.Text ?? ""; _session.MarkDirty(); };
        title.LostFocus += (_, _) => RefreshSteps();
        var text = Multi(90); text.Text = s.Text;
        text.TextChanged += (_, _) => { s.Text = text.Text ?? ""; _session.MarkDirty(); };
        var width = new NumericUpDown { Value = s.DisplayWidth, Minimum = 50, Maximum = 4000, FormatString = "0", Width = 160 };
        width.ValueChanged += (_, _) => { s.DisplayWidth = width.Value is { } v ? (int)v : null; _session.MarkDirty(); };
        _stepEditor.Children.Add(Ui.Field("Überschrift", title));
        _stepEditor.Children.Add(Ui.Field("Erklärung (Markdown erlaubt)", text));
        _stepEditor.Children.Add(Ui.Field("Anzeigebreite in Obsidian (px, leer = Vault-Standard) – unabhängig von der Bildauflösung", width));

        var page = s.PageId is { } pid ? _session.Project.FindPage(pid) : null;
        if (page is not null)
        {
            var preview = Thumb(page, 640);
            _stepEditor.Children.Add(new Border { Child = new Image { Source = preview, MaxHeight = 360, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left }, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) });
        }

        if (s.Video is { } video) BuildVideoPanel(s, video);

        _stepEditor.Children.Add(Ui.Label("Hinweise / Callouts", 13, bold: true));
        foreach (var c in s.Callouts.ToList())
        {
            var kind = new ComboBox { ItemsSource = Enum.GetNames<CalloutKind>(), SelectedIndex = (int)c.Kind, Width = 120 };
            kind.SelectionChanged += (_, _) => { c.Kind = (CalloutKind)Math.Max(0, kind.SelectedIndex); _session.MarkDirty(); };
            var ct = new TextBox { Text = c.Title, Width = 160 };
            ct.TextChanged += (_, _) => { c.Title = ct.Text ?? ""; _session.MarkDirty(); };
            var cx = new TextBox { Text = c.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinWidth = 300 };
            cx.TextChanged += (_, _) => { c.Text = cx.Text ?? ""; _session.MarkDirty(); };
            var del = Ui.Button("✕", () => { s.Callouts.Remove(c); _session.MarkDirty(); BuildStepEditor(); });
            _stepEditor.Children.Add(Ui.Row(kind, ct, cx, del));
        }
        _stepEditor.Children.Add(Ui.Row(
            Ui.Button("+ Info", () => AddCallout(s, CalloutKind.Info, "Info")),
            Ui.Button("+ Hinweis", () => AddCallout(s, CalloutKind.Warning, "Hinweis")),
            Ui.Button("+ Tipp", () => AddCallout(s, CalloutKind.Tip, "Tipp"))));
    }

    private void AddCallout(GuideStep s, CalloutKind k, string title)
    {
        s.Callouts.Add(new Callout { Kind = k, Title = title, Text = "" });
        _session.MarkDirty();
        BuildStepEditor();
    }

    // ---------------------------------------------------------------- steps

    private void BuildVideoPanel(GuideStep s, VideoRef v)
    {
        var resolved = Video.VideoStepRenderer.Resolve(_app, _session, v);
        var box = Ui.Column(6,
            Ui.Label("🎬 Video-Schritt", 13, bold: true),
            Ui.Label(resolved is null ? $"⚠ Videodatei nicht gefunden: {v.Path}" : $"{Path.GetFileName(resolved)} · {(v.Sequence is { } sq ? $"{sq.AllClips.Count()} Clip(s) auf {sq.Tracks.Count} Spur(en)" : $"Clip {Video.VideoTimeline.Fmt(v.Start)}–{Video.VideoTimeline.Fmt(v.End)} · {v.Cuts.Count} Schnitt(e)")} · Ergebnis {v.KeptDuration:0.0} s{(v.Overlay is not null ? $" · {v.Overlay.Page.Layers.Count} Annotation(en)" + (v.Overlay.Zoom.Count > 0 ? ", Zoom" : "") + (v.Overlay.Slides.Count > 0 ? $", {v.Overlay.Slides.Count} Folie(n)" : "") : "")}", 12),
            Ui.Label("Das Bild oben ist das Vorschaubild (Poster). Beim Export wird der Clip als Video eingebettet: Obsidian ![[…mp4]], HTML <video>.", 11, opacity: 0.65));
        var fmt = new ComboBox { ItemsSource = new[] { "MP4 (mit Ton, klein)", "GIF (ohne Ton)" }, SelectedIndex = (int)v.Format, Width = 180 };
        fmt.SelectionChanged += (_, _) => { v.Format = (VideoOutputFormat)Math.Max(0, fmt.SelectedIndex); _session.MarkDirty(); RefreshSteps(); };
        var maxW = new NumericUpDown { Value = v.MaxWidth ?? 0, Minimum = 0, Maximum = 8000, FormatString = "0", Width = 130 };
        maxW.ValueChanged += (_, _) => { v.MaxWidth = maxW.Value is > 0 ? (int)maxW.Value : null; _session.MarkDirty(); };
        var maxMb = new NumericUpDown { Value = v.MaxBytes is { } b ? (decimal)(b / 1024.0 / 1024.0) : 0, Minimum = 0, Maximum = 2000, Increment = 1, FormatString = "0.#", Width = 130 };
        maxMb.ValueChanged += (_, _) => { v.MaxBytes = maxMb.Value is > 0 ? (long)(maxMb.Value.Value * 1024 * 1024) : null; _session.MarkDirty(); };
        var audio = new CheckBox { Content = "Ton übernehmen", IsChecked = v.IncludeAudio };
        audio.IsCheckedChanged += (_, _) => { v.IncludeAudio = audio.IsChecked == true; _session.MarkDirty(); };
        box.Children.Add(Ui.Row(Ui.Field("Format", fmt), Ui.Field("Max. Breite px", maxW), Ui.Field("Ziel MB (weich)", maxMb)));
        box.Children.Add(audio);
        box.Children.Add(Ui.Row(
            Ui.Button("✂ Schneiden / Annotieren…", () => OpenStepVideo(s), "Clip, Schnitte, zeitgesteuerte Annotationen und Zoom im Video-Editor bearbeiten", accent: true),
            Ui.Button("Video entfernen", () => { s.Video = null; _session.MarkDirty(); RefreshSteps(); BuildStepEditor(); }, "Schritt behält nur das Bild")));
        _stepEditor.Children.Add(new Border { Child = box, Padding = new Thickness(8), CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(30, 0, 120, 215)) });
    }

    private void OpenStepVideo(GuideStep s)
    {
        if (s.Video is not { } v) return;
        if (_app.Ffmpeg is null) { _ = Video.RecordingController.ShowMissingFfmpegAsync(this); return; }
        var file = Video.VideoStepRenderer.Resolve(_app, _session, v);
        if (file is null) { _ = Dialogs.InfoAsync(this, "Video fehlt", "Die Videodatei wurde nicht gefunden: " + v.Path); return; }
        var ed = new Video.VideoEditorWindow(_app, file, s, this);
        ed.Closed += (_, _) => { RefreshSteps(); BuildStepEditor(); UpdateTitle(); };
        ed.Show(this);
    }

    /// <summary>Adds a video step: poster frame becomes the (annotatable) page, the clip is referenced.</summary>
    public GuideStep AddVideoStep(string file, VideoRef video, SkiaSharp.SKImage poster, byte[] posterPng, string title, ProjectSession? overlaySource = null, VideoOverlay? overlay = null, VideoSequence? sequence = null)
    {
        var page = _session.AddPage(poster, posterPng, title);
        video.Path = Video.VideoStepRenderer.Store(_app, _session, file);
        if (sequence is not null)
        {
            // own copy for the guide; clip paths relative to this project, slide images copied
            var seq = Core.Serialization.ProjectJson.Clone(sequence);
            foreach (var c in seq.AllClips)
            {
                if (c.Kind == ClipKind.Video && overlaySource is not null && Video.VideoRenderPlan.ResolveClip(_app, overlaySource, c) is { } full)
                    c.Path = Video.VideoStepRenderer.Store(_app, _session, full);
                foreach (var im in c.Slide?.Page.AllLayers().OfType<ImageLayer>() ?? Enumerable.Empty<ImageLayer>())
                    if (overlaySource is not null && !ReferenceEquals(overlaySource, _session) && overlaySource.Assets.Get(im.AssetId) is { } bytes)
                    {
                        _session.Assets.Add(bytes);
                        if (overlaySource.Project.Assets.TryGetValue(im.AssetId, out var info)) _session.Project.Assets[im.AssetId] = info;
                    }
            }
            foreach (var m in seq.Media)
                if (overlaySource is not null && ProjectFile.ResolveMedia(overlaySource.FilePath, m.Path, _app.Env.FromPortablePath) is { } fm)
                    m.Path = Video.VideoStepRenderer.Store(_app, _session, fm);
            video.Sequence = seq;
            video.Start = 0; video.End = seq.Duration; video.Cuts.Clear();
        }
        if (overlay is not null && overlaySource is not null && Video.OverlayRenderer.HasContent(overlay))
        {
            var copy = Core.Serialization.ProjectJson.Clone(overlay);
            foreach (var im in copy.Page.AllLayers().Concat(copy.Slides.SelectMany(sl => sl.Page.AllLayers())).OfType<ImageLayer>())
                if (overlaySource.Assets.Get(im.AssetId) is { } bytes && !ReferenceEquals(overlaySource, _session))
                {
                    _session.Assets.Add(bytes);
                    if (overlaySource.Project.Assets.TryGetValue(im.AssetId, out var info)) _session.Project.Assets[im.AssetId] = info;
                }
            video.Overlay = copy;
        }
        var step = new GuideStep { Title = title, PageId = page.Id, Video = video };
        _session.Project.Guide.Steps.Add(step);
        _session.MarkDirty();
        RefreshSteps(_session.Project.Guide.Steps.Count - 1);
        UpdateTitle();
        Activate();
        return step;
    }

    /// <summary>Records a video and adds it as a step (with click markers / live drawings), then opens the video editor for it.</summary>
    private async Task RecordVideoStepAsync()
    {
        if (_app.Ffmpeg is not { } tools) { await Video.RecordingController.ShowMissingFfmpegAsync(this); return; }
        var r = await App.Current.Recording.RecordAsync(this);
        if (r is null) return;
        try
        {
            var info = await Media.MediaProbe.ProbeAsync(tools, r.File);
            var png = await new Media.VideoExporter(tools).ExtractFrameAsync(r.File, Math.Min(0.1, info.Duration / 2));
            var img = SkiaSharp.SKImage.FromEncodedData(png) ?? throw new InvalidDataException("Vorschaubild konnte nicht gelesen werden.");
            var title = Path.GetFileNameWithoutExtension(r.File);
            var overlay = new VideoOverlay { Page = new Page { Width = info.Width, Height = info.Height, Background = ColorRgba.Transparent } };
            overlay.Page.Layers.AddRange(Video.ClickMarkers.Create(r.Clicks, info.Width, info.Duration));
            overlay.Page.Layers.AddRange(r.LiveLayers);
            var seq = SequenceOps.ForVideo(Video.VideoStepRenderer.Store(_app, _session, r.File), info.Duration, info.Width, info.Height, info.Fps, info.HasAudio, title);
            seq.Media[0].Recorded = true;
            var step = AddVideoStep(r.File, new VideoRef { Start = 0, End = info.Duration, IncludeAudio = info.HasAudio, MaxWidth = Math.Min(1280, info.Width) }, img, png, title, _session, overlay, seq);
            if (step.Video is { } v && v.Overlay is null) v.Overlay = overlay;
            SelectStep(step);
            OpenStepVideo(step);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            await Dialogs.InfoAsync(this, "Video aufnehmen", ex.Message);
        }
    }

    private void SelectStep(GuideStep step)
    {
        var i = _session.Project.Guide.Steps.IndexOf(step);
        if (i >= 0) RefreshSteps(i);
    }

    private void ShowProjectFiles()
    {
        var w = new ProjectMediaWindow(_app, _session);
        w.Changed += (_, _) => { RefreshSteps(); BuildStepEditor(); UpdateTitle(); };
        _ = w.ShowDialog(this);
    }

    private void CopyStepImage()
    {
        var s = SelectedStep;
        var page = s?.PageId is { } pid ? _session.Project.FindPage(pid) : null;
        if (page is null) { _info.Text = "Dieser Schritt hat kein Bild."; return; }
        if (_app.Clipboard is null) { _ = Dialogs.InfoAsync(this, "Zwischenablage", "Auf diesem System noch nicht unterstützt."); return; }
        using var img = _session.Renderer.RenderPage(page);
        var ok = _app.Clipboard.SetImage(SkiaConvert.ToRaw(img), SkiaConvert.EncodePng(img, 3));
        _info.Text = ok ? $"Bild von „{s!.Title}“ mit Annotationen kopiert ({img.Width} × {img.Height} px) – mit Strg+V einfügen." : "⚠ Zwischenablage ist von einem anderen Programm blockiert.";
    }

    private async Task AddVideoFileAsync()
    {
        if (_app.Ffmpeg is not { } tools) { await Video.RecordingController.ShowMissingFfmpegAsync(this); return; }
        var path = await Dialogs.OpenFileAsync(this, "Video hinzufügen", Path.Combine(_app.Env.MediaDir, "Aufnahmen"),
            ("Videos", new[] { "*.mp4", "*.mkv", "*.webm", "*.mov", "*.avi" }));
        if (path is null) return;
        try
        {
            var info = await Media.MediaProbe.ProbeAsync(tools, path);
            var png = await new Media.VideoExporter(tools).ExtractFrameAsync(path, Math.Min(0.1, info.Duration / 2));
            var img = SkiaSharp.SKImage.FromEncodedData(png) ?? throw new InvalidDataException("Vorschaubild konnte nicht gelesen werden.");
            AddVideoStep(path, new VideoRef { Start = 0, End = info.Duration, IncludeAudio = info.HasAudio, MaxWidth = Math.Min(1280, info.Width) }, img, png, Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            await Dialogs.InfoAsync(this, "Video hinzufügen", ex.Message);
        }
    }

    private string ExportWorkDir() => Path.Combine(_app.Env.TempDir, $"export-{Guid.NewGuid():N}");

    private async Task<Dictionary<Guid, RenderedMedia>?> RenderVideosAsync(string work)
    {
        if (_session.Project.Guide.Steps.All(s => s.Video is null)) return new();
        var progress = new Progress<string>(t => _info.Text = t);
        try { return await Task.Run(() => Video.VideoStepRenderer.RenderAllAsync(_app, _session, work, progress)); }
        catch (InvalidOperationException ex) { await Dialogs.InfoAsync(this, "Video-Export", ex.Message); return null; }
    }

    private void AddStep(Page? page, string title)
    {
        var step = new GuideStep { Title = title, PageId = page?.Id };
        _session.Project.Guide.Steps.Add(step);
        _session.MarkDirty();
        RefreshSteps(_session.Project.Guide.Steps.Count - 1);
        UpdateTitle();
    }

    public void AddCapturedImage(SkiaSharp.SKImage image, byte[] png, string name)
    {
        var page = _session.AddPage(image, png, name);
        AddStep(page, name);
        Activate();
    }

    public void AddPageFrom(ProjectSession source, Page page)
    {
        var copy = _session.ImportPage(source, page);
        AddStep(copy, page.Name);
        Activate();
    }

    private void MoveStep(int d)
    {
        var list = _session.Project.Guide.Steps;
        var s = SelectedStep; if (s is null) return;
        var i = list.IndexOf(s); var j = Math.Clamp(i + d, 0, list.Count - 1);
        if (i == j) return;
        list.RemoveAt(i); list.Insert(j, s);
        _session.MarkDirty();
        RefreshSteps(j);
    }

    private async void RemoveStep()
    {
        var s = SelectedStep; if (s is null) return;
        if (await Dialogs.AskAsync(this, "Schritt entfernen", $"Schritt „{s.Title}“ entfernen?", "Entfernen", "Abbrechen") != "Entfernen") return;
        _session.Project.Guide.Steps.Remove(s);
        if (s.PageId is { } pid && _session.Project.Guide.Steps.All(x => x.PageId != pid))
            _session.Project.Pages.RemoveAll(p => p.Id == pid);
        _session.MarkDirty();
        RefreshSteps();
    }

    private void OpenStepEditor()
    {
        var s = SelectedStep;
        var page = s?.PageId is { } pid ? _session.Project.FindPage(pid) : null;
        if (page is null) return;
        var ed = new EditorWindow(_app, _session, page, isStudioPage: true);
        ed.Closed += (_, _) => RefreshSteps();
        ed.Show(this);
    }

    private async Task CaptureStepAsync()
    {
        var before = App.Current.OpenWindows.OfType<EditorWindow>().ToHashSet();
        await App.Current.Capture.CaptureAsync(CaptureMode.Region);
        // the capture opened a standalone editor – move its page into the guide and edit it there
        var ed = App.Current.OpenWindows.OfType<EditorWindow>().FirstOrDefault(w => !before.Contains(w));
        if (ed is null) return;
        var src = ed.Session;
        var page = src.Project.Pages[0];
        ed.Session.MarkDirty();
        AddPageFrom(src, page);
        ed.ForceCloseWithoutPrompt();
        OpenStepEditor();
    }

    public async Task CaptureSeriesAsync()
    {
        var s = _app.Settings.Shared;
        var shots = await App.Current.Capture.CaptureSeriesAsync(Math.Clamp(s.SeriesCount, 1, 200), s.SeriesIntervalSeconds);
        var n = _session.Project.Guide.Steps.Count;
        foreach (var raw in shots)
        {
            n++;
            var img = SkiaConvert.ToSkImage(raw);
            var png = await Task.Run(() => SkiaConvert.EncodePng(raw));
            var page = _session.AddPage(img, png, $"Serie {n}");
            AddStep(page, $"Schritt {n}");
        }
        if (shots.Count > 0) _info.Text = $"{shots.Count} Bilder der Serie als Schritte hinzugefügt.";
        Activate();
    }

    private async Task AddImageFileAsync()
    {
        var path = await Dialogs.OpenFileAsync(this, "Bild oder Projekt hinzufügen", _app.Env.MediaDir,
            ("Bilder und Projekte", new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*" + ProjectFile.Extension }));
        if (path is null) return;
        try
        {
            if (path.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase))
            {
                using var src = ProjectSession.Open(path, out _);
                foreach (var p in src.Project.Pages) AddPageFrom(src, p);
                return;
            }
            var bytes = await File.ReadAllBytesAsync(path);
            var img = SkiaSharp.SKImage.FromEncodedData(bytes) ?? throw new InvalidDataException("Bildformat nicht erkannt.");
            var png = SkiaConvert.EncodePng(img);
            var page = _session.AddPage(img, png, Path.GetFileNameWithoutExtension(path));
            AddStep(page, Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ProjectFormatException)
        {
            await Dialogs.InfoAsync(this, "Hinzufügen fehlgeschlagen", ex.Message);
        }
    }

    // ---------------------------------------------------------------- files

    private async Task NewAsync()
    {
        if (_session.IsDirty && _session.Project.Guide.Steps.Count > 0)
        {
            var a = await Dialogs.AskAsync(this, "Neue Anleitung", "Ungespeicherte Änderungen verwerfen?", "Speichern", "Verwerfen", "Abbrechen");
            if (a == "Speichern" && !await SaveAsync(false)) return;
            if (a is null or "Abbrechen") return;
        }
        Recovery.Delete(_session);
        _session.Dispose();
        _session = NewGuideSession();
        LoadFromSession();
    }

    private async Task OpenAsync()
    {
        var path = await Dialogs.OpenFileAsync(this, "Anleitung öffnen", _app.Env.ProjectsDir, ("CaptureDoc-Projekt", new[] { "*" + ProjectFile.Extension }));
        if (path is null) return;
        OpenFile(path);
    }

    public async void OpenFile(string path)
    {
        try
        {
            var s = ProjectSession.Open(path, out var warnings);
            _session.Dispose();
            _session = s;
            LoadFromSession();
            if (warnings.Count > 0) await Dialogs.InfoAsync(this, "Hinweise beim Öffnen", string.Join("\n", warnings));
        }
        catch (ProjectFormatException ex) { await Dialogs.InfoAsync(this, "Öffnen fehlgeschlagen", ex.Message); }
    }

    public void UseSession(ProjectSession s)
    {
        _session.Dispose();
        _session = s;
        LoadFromSession();
    }

    public async Task<bool> SaveAsync(bool saveAs)
    {
        PullMeta();
        var ok = await ProjectSaver.SaveAsync(this, _app, _session, saveAs);
        UpdateTitle();
        RefreshSteps();
        return ok;
    }

    // ---------------------------------------------------------------- export

    private void LoadVaults()
    {
        var vaults = _app.Settings.Platform.Vaults;
        _vault.ItemsSource = vaults.Select(v => $"{v.Name}  ({_app.Env.FromPortablePath(v.VaultPath)})").ToList();
        var idx = vaults.FindIndex(v => v.Id == _app.Settings.Platform.ActiveVault);
        _vault.SelectedIndex = vaults.Count == 0 ? -1 : Math.Max(0, idx);
        ShowVault();
    }

    private VaultProfile? ActiveVault => _vault.SelectedIndex >= 0 && _vault.SelectedIndex < _app.Settings.Platform.Vaults.Count ? _app.Settings.Platform.Vaults[_vault.SelectedIndex] : null;

    private void ShowVault()
    {
        var v = ActiveVault;
        _loading = true;
        _folder.Text = v?.DefaultFolder ?? "Dokumentation";
        _links.SelectedIndex = v is null ? 0 : (int)v.Links;
        _profile.SelectedIndex = Math.Max(0, _profiles.FindIndex(p => p.Id == (v?.ExportProfileId ?? "documentation-png")));
        _loading = false;
        if (v is not null) { _app.Settings.Platform.ActiveVault = v.Id; _app.SaveSettings(); }
    }

    private void SaveVaultSelection()
    {
        if (_loading || ActiveVault is not { } v) return;
        v.DefaultFolder = _folder.Text ?? "";
        v.Links = (LinkStyle)Math.Max(0, _links.SelectedIndex);
        if (_profile.SelectedIndex >= 0) v.ExportProfileId = _profiles[_profile.SelectedIndex].Id;
        _app.SaveSettings();
    }

    private async Task AddVaultAsync()
    {
        var path = await Dialogs.PickFolderAsync(this, "Obsidian-Vault wählen", null);
        if (path is null) return;
        var check = VaultValidator.ValidateVault(path);
        if (!check.Ok) { await Dialogs.InfoAsync(this, "Kein gültiger Ordner", check.Error!); return; }
        if (!check.LooksLikeVault && await Dialogs.AskAsync(this, "Kein Obsidian-Vault?", check.Warning! + "\n\nTrotzdem verwenden?", "Verwenden", "Abbrechen") != "Verwenden") return;
        var profile = new VaultProfile { Name = Path.GetFileName(path.TrimEnd('\\', '/')), VaultPath = _app.Env.ToPortablePath(path) };
        _app.Settings.Platform.Vaults.Add(profile);
        _app.Settings.Platform.ActiveVault = profile.Id;
        _app.SaveSettings();
        LoadVaults();
    }

    private GuideTemplate SelectedTemplate => _templates[Math.Max(0, _template.SelectedIndex)];
    private ExportProfile ImageProfile => _profiles[Math.Max(0, _profile.SelectedIndex)];

    private Dictionary<Guid, RenderedImage> RenderStepImages(ExportProfile profile)
    {
        var result = new Dictionary<Guid, RenderedImage>();
        foreach (var s in _session.Project.Guide.Steps)
        {
            var page = s.PageId is { } pid ? _session.Project.FindPage(pid) : null;
            if (page is null) continue;
            using var flat = _session.Renderer.RenderPage(page);
            var r = ImageExporter.Export(flat, profile);
            if (r.Data is null) throw new InvalidOperationException($"Bild für Schritt „{s.Title}“: {r.Message}");
            result[s.Id] = new RenderedImage(r.Data, profile.FileExtension, r.Width, r.Height);
        }
        return result;
    }

    private async Task ExportObsidianAsync()
    {
        PullMeta();
        if (ActiveVault is not { } v) { await Dialogs.InfoAsync(this, "Kein Vault", "Bitte zuerst mit „+ Vault…“ einen Obsidian-Vault auswählen."); return; }
        SaveVaultSelection();
        var vaultPath = _app.Env.FromPortablePath(v.VaultPath);
        var check = VaultValidator.ValidateVault(vaultPath);
        if (!check.Ok) { await Dialogs.InfoAsync(this, "Vault nicht erreichbar", check.Error!); return; }
        if (_session.Project.Guide.Steps.Count == 0) { await Dialogs.InfoAsync(this, "Leer", "Die Anleitung hat noch keine Schritte."); return; }

        _info.Text = "Rendere Bilder…";
        Dictionary<Guid, RenderedImage> images;
        try { images = await Task.Run(() => RenderStepImages(ImageProfile)); }
        catch (InvalidOperationException ex) { await Dialogs.InfoAsync(this, "Bildexport", ex.Message); return; }
        var work = ExportWorkDir();
        var videos = await RenderVideosAsync(work);
        if (videos is null) return;
        var folder = (v.DefaultFolder ?? "").Trim('/', '\\');
        var guideFolder = SafeFileName.ToSlug(_session.Project.Guide.Title);
        var noteFolder = v.SubfolderPerGuide ? (folder.Length > 0 ? folder + "/" : "") + guideFolder : folder;
        var guide = _session.Project.Guide; var template = SelectedTemplate;
        ExportPlan Plan(ConflictPolicy policy) => ObsidianExporter.Plan(new ObsidianExportRequest
        {
            VaultRoot = vaultPath, NoteFolder = noteFolder, Guide = guide, Template = template, Images = images, Videos = videos,
            Links = v.Links, AssetsFolder = v.AssetsFolder, DefaultDisplayWidth = ImageProfile.DisplayWidth ?? v.DefaultDisplayWidth,
            NoteName = guide.Title, Policy = policy,
        });
        var dlg = new ObsidianExportDialog(Plan, guide.ExportId);
        await dlg.ShowDialog(this);
        _info.Text = dlg.Result is { } r ? $"Exportiert nach {r.NotePath}" : "Export abgebrochen.";
        if (dlg.Result is not null) { _session.MarkDirty(); } // ExportId is part of the project – keep it saved
        TryDelete(work);
    }

    private async Task ExportMarkdownFolderAsync()
    {
        PullMeta();
        var dir = await Dialogs.PickFolderAsync(this, "Zielordner für Markdown", _app.Env.MediaDir);
        if (dir is null) return;
        var images = await Task.Run(() => RenderStepImages(ImageProfile));
        var work = ExportWorkDir();
        var videos = await RenderVideosAsync(work);
        if (videos is null) return;
        var guide = _session.Project.Guide; var template = SelectedTemplate;
        var dlg = new ObsidianExportDialog(policy => ObsidianExporter.Plan(new ObsidianExportRequest
        {
            VaultRoot = dir, NoteFolder = SafeFileName.ToSlug(guide.Title), Guide = guide, Template = template, Images = images, Videos = videos,
            Links = LinkStyle.Markdown, NoteName = guide.Title, Policy = policy, DefaultDisplayWidth = ImageProfile.DisplayWidth ?? 700,
        }), guide.ExportId) { Title = "Markdown-Export – Vorschau" };
        await dlg.ShowDialog(this);
        TryDelete(work);
    }

    private async Task ExportHtmlAsync()
    {
        PullMeta();
        var dir = await Dialogs.PickFolderAsync(this, "Zielordner für HTML", _app.Env.MediaDir);
        if (dir is null) return;
        var guide = _session.Project.Guide;
        var profile = ImageProfile;
        try
        {
            var images = await Task.Run(() => RenderStepImages(profile));
            var work = ExportWorkDir();
            var videos = await RenderVideosAsync(work);
            if (videos is null) return;
            var target = Path.Combine(dir, SafeFileName.ToSlug(guide.Title));
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                target = Path.Combine(dir, SafeFileName.MakeUnique(SafeFileName.ToSlug(guide.Title), n => Directory.Exists(Path.Combine(dir, n)), true));
            Directory.CreateDirectory(Path.Combine(target, "assets"));
            var refs = new Dictionary<Guid, MarkdownBuilder.ImageRef>();
            var n = 0;
            foreach (var s in guide.Steps)
            {
                n++;
                string? posterName = null;
                if (images.TryGetValue(s.Id, out var img))
                {
                    posterName = $"schritt-{n:00}{img.Extension}";
                    Core.Storage.AtomicFile.WriteAllBytes(Path.Combine(target, "assets", posterName), img.Data);
                    refs[s.Id] = new($"assets/{posterName}", s.DisplayWidth ?? profile.DisplayWidth, s.Title);
                }
                if (videos.TryGetValue(s.Id, out var vid))
                {
                    var vname = $"schritt-{n:00}-video{vid.Extension}";
                    File.Copy(vid.FilePath, Path.Combine(target, "assets", vname), overwrite: true);
                    refs[s.Id] = new($"assets/{vname}", s.DisplayWidth ?? profile.DisplayWidth, s.Title, vid.IsVideo, posterName is null ? null : $"assets/{posterName}");
                }
            }
            var html = HtmlBuilder.Build(guide, SelectedTemplate, refs);
            var file = Path.Combine(target, "index.html");
            Core.Storage.AtomicFile.WriteAllText(file, html);
            TryDelete(work);
            await Dialogs.InfoAsync(this, "HTML exportiert", $"{file}\n\nBilder öffnen sich per Klick groß, Videos sind direkt abspielbar.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await Dialogs.InfoAsync(this, "HTML-Export fehlgeschlagen", ex.Message);
        }
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private async Task ExportPdfAsync()
    {
        PullMeta();
        var guide = _session.Project.Guide;
        var dir = _app.Settings.Platform.LastSaveDirectory ?? _app.Env.MediaDir;
        var path = await Dialogs.SaveFileAsync(this, "PDF exportieren", SafeFileName.ToNoteName(guide.Title) + ".pdf", dir, ("PDF", "*.pdf"));
        if (path is null) return;
        try
        {
            var profile = BuiltInProfiles.Get("documentation-png");
            var images = await Task.Run(() => RenderStepImages(profile));
            var template = SelectedTemplate;
            var pages = await Task.Run(() => PdfGuideExporter.Export(guide, template, images.ToDictionary(k => k.Key, k => k.Value.Data), path));
            _info.Text = $"PDF exportiert: {path} ({pages} Seiten)";
            await Dialogs.InfoAsync(this, "PDF exportiert", $"{path}\n{pages} Seite(n). Videos erscheinen als Standbild mit Hinweis.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await Dialogs.InfoAsync(this, "PDF-Export fehlgeschlagen", ex.Message);
        }
    }
}
