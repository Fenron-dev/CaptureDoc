using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CaptureDoc.App.Services;
using CaptureDoc.App.Util;
using CaptureDoc.App.Views;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Serialization;
using CaptureDoc.Rendering;

using Page = CaptureDoc.Core.Model.Page;

namespace CaptureDoc.App.Editor;

/// <summary>Image editor: toolbar, canvas, layers + properties, export bar.</summary>
public sealed class EditorWindow : Window
{
    private readonly AppServices _app;
    private readonly ProjectSession _session;
    private readonly EditorCanvas _canvas;
    private readonly ScrollViewer _scroll;
    private readonly Canvas _overlay = new();
    private readonly TextBlock _status = Ui.Label("", 12, opacity: 0.8);
    private readonly Dictionary<Tool, ToggleButton> _toolButtons = new();
    private readonly DispatcherTimer _autosave;
    private TextBox? _textEditor;
    private static List<(Layer layer, Dictionary<string, byte[]> assets)>? _layerClipboard;
    private readonly bool _isStudioPage;

    public ProjectSession Session => _session;
    public event EventHandler? PageEdited;

    public EditorWindow(AppServices app, ProjectSession session, Page page, bool isStudioPage = false)
    {
        _app = app; _session = session; _isStudioPage = isStudioPage;
        Width = 1280; Height = 820; MinWidth = 800; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = IconFactory.WindowIcon;

        _canvas = new EditorCanvas(session, page, app.Settings.Shared.Style);
        var host = new Panel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24) };
        host.Children.Add(_canvas);
        host.Children.Add(_overlay);
        _scroll = new ScrollViewer
        {
            Content = host, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 64)),
        };
        _scroll.AddHandler(PointerWheelChangedEvent, OnWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var right = new Grid { RowDefinitions = new RowDefinitions("*,Auto,*"), Width = 290 };
        var layers = new LayersPanel(_canvas);
        var props = new PropertiesPanel(_canvas);
        Grid.SetRow(layers, 0); Grid.SetRow(props, 2);
        var splitter = new GridSplitter { Height = 5, ResizeDirection = GridResizeDirection.Rows, Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)) };
        Grid.SetRow(splitter, 1);
        right.Children.Add(layers); right.Children.Add(splitter); right.Children.Add(props);

        var root = new DockPanel();
        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        var bottom = BuildBottomBar();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        var rightBorder = new Border { Child = right, BorderThickness = new Thickness(1, 0, 0, 0), BorderBrush = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)), Padding = new Thickness(6) };
        DockPanel.SetDock(rightBorder, Dock.Right);
        root.Children.Add(rightBorder);
        root.Children.Add(_scroll);
        Content = root;

        _canvas.TextEditRequested += (_, t) => BeginTextEdit(t);
        _canvas.ViewChanged += (_, _) => UpdateStatus();
        _canvas.DocumentChanged += (_, _) => { UpdateStatus(); PageEdited?.Invoke(this, EventArgs.Empty); };
        _session.Changed += (_, _) => UpdateTitle();
        KeyDown += OnKey;
        Opened += (_, _) => { StudioWindow.FitToScreen(this); FitZoom(onlyShrink: true); _canvas.Focus(); };
        Closing += OnClosing;

        _autosave = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(15, app.Settings.Shared.AutoSaveSeconds)) };
        _autosave.Tick += (_, _) => Recovery.Write(_session);
        if (!_isStudioPage) _autosave.Start();
        SetTool(Tool.Select);
        UpdateTitle(); UpdateStatus();
    }

    private void UpdateTitle()
    {
        var name = _session.FilePath is null ? _session.Project.Title : Path.GetFileName(_session.FilePath);
        Title = $"{(_session.IsDirty ? "• " : "")}{name}{(_session.ReadOnly ? " [schreibgeschützt]" : "")} – CaptureDoc Editor";
    }

    private void UpdateStatus()
    {
        var p = _canvas.Page;
        _status.Text = $"{p.Width} × {p.Height} px  ·  Zoom {_canvas.Zoom * 100:0}%  ·  {p.AllLayers().Count()} Ebenen  ·  {(_canvas.History.UndoDescription is { } u ? "Rückgängig: " + u : "")}";
    }

    // ---------------------------------------------------------------- toolbar

    private Control BuildToolbar()
    {
        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4) };
        void ToolBtn(Tool t, string glyph, string key)
        {
            var b = new ToggleButton { Content = glyph, MinWidth = 38, Padding = new Thickness(6, 4), Margin = new Thickness(1), FontSize = 15 };
            ToolTip.SetTip(b, $"{EditorCanvas.ToolName(t)} ({key})");
            b.Click += (_, _) => SetTool(t);
            _toolButtons[t] = b;
            bar.Children.Add(b);
        }
        ToolBtn(Tool.Select, "⬚", "V");
        ToolBtn(Tool.Arrow, "➜", "A");
        ToolBtn(Tool.Line, "╱", "L");
        ToolBtn(Tool.Rectangle, "▭", "R");
        ToolBtn(Tool.Ellipse, "◯", "E");
        ToolBtn(Tool.Text, "T", "T");
        ToolBtn(Tool.InfoBox, "ⓘ", "I");
        ToolBtn(Tool.Bubble, "💬", "U");
        ToolBtn(Tool.Step, "①", "N");
        ToolBtn(Tool.Highlight, "▮", "H");
        ToolBtn(Tool.Spotlight, "◎", "S");
        ToolBtn(Tool.Magnifier, "🔍", "M");
        ToolBtn(Tool.Freehand, "✎", "F");
        bar.Children.Add(new Separator { Width = 10 });
        ToolBtn(Tool.Blackout, "■", "B");
        ToolBtn(Tool.Pixelate, "▦", "P");
        ToolBtn(Tool.Blur, "◌", "W");
        bar.Children.Add(new Separator { Width = 10 });
        ToolBtn(Tool.Crop, "⛶", "C");
        bar.Children.Add(Ui.Button("✓ Zuschneiden", () => _canvas.ApplyCrop(), "Zuschnitt anwenden (Enter)"));
        bar.Children.Add(new Separator { Width = 10 });

        // default style for new annotations (also applied to selection)
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in Ui.Palette)
        {
            var b = new Button { Width = 20, Height = 20, Margin = new Thickness(1), Padding = new Thickness(0), Background = new SolidColorBrush(c.ToAvalonia()), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
            var cc = c;
            ToolTip.SetTip(b, "Farbe für neue Elemente und Auswahl");
            b.Click += (_, _) => ApplyColor(cc);
            swatches.Children.Add(b);
        }
        bar.Children.Add(swatches);
        var stroke = new NumericUpDown { Value = (decimal)_canvas.Style.StrokeWidth, Minimum = 1, Maximum = 40, Width = 110, FormatString = "0", Margin = new Thickness(6, 0) };
        ToolTip.SetTip(stroke, "Linienstärke für neue Elemente");
        stroke.ValueChanged += (_, _) => _canvas.Style.StrokeWidth = (double)(stroke.Value ?? 4);
        bar.Children.Add(stroke);
        var font = new NumericUpDown { Value = (decimal)_canvas.Style.FontSize, Minimum = 8, Maximum = 160, Width = 110, FormatString = "0" };
        ToolTip.SetTip(font, "Schriftgröße für neue Texte");
        font.ValueChanged += (_, _) => _canvas.Style.FontSize = (double)(font.Value ?? 28);
        bar.Children.Add(font);
        bar.Children.Add(new Separator { Width = 10 });
        bar.Children.Add(Ui.Button("↶", _canvas.Undo, "Rückgängig (Strg+Z)"));
        bar.Children.Add(Ui.Button("↷", _canvas.Redo, "Wiederholen (Strg+Y)"));
        bar.Children.Add(Ui.Button("−", () => _canvas.Zoom /= 1.25, "Verkleinern (Strg+-)"));
        bar.Children.Add(Ui.Button("100%", () => _canvas.Zoom = 1, "Originalgröße (Strg+0)"));
        bar.Children.Add(Ui.Button("⤢", () => FitZoom(false), "Einpassen"));
        bar.Children.Add(Ui.Button("+", () => _canvas.Zoom *= 1.25, "Vergrößern (Strg++)"));
        return bar;
    }

    private Control BuildBottomBar()
    {
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 4 };
        right.Children.Add(Ui.Button("📋 Kopieren", CopyImage, "Bild in die Zwischenablage (Strg+C)"));
        right.Children.Add(Ui.Button("⭳ Schnell speichern", async () => await QuickSaveAsync(), "Mit Standardprofil in den letzten Ordner speichern (Strg+Umschalt+E)"));
        right.Children.Add(Ui.Button("Exportieren…", async () => await ExportAsync(), "Format, Größe und Dateigrößenlimit wählen (Strg+E)"));
        if (!_isStudioPage) right.Children.Add(Ui.Button("➕ Zur Anleitung", AddToGuide, "Bild als Schritt im Documentation Studio hinzufügen"));
        right.Children.Add(Ui.Button("💾 Projekt speichern", async () => await SaveProjectAsync(false), "Bearbeitbares Projekt (.capturedoc) speichern (Strg+S)", accent: true));
        var dock = new DockPanel { Margin = new Thickness(8, 4) };
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(_status);
        return new Border { Child = dock, BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)) };
    }

    private void SetTool(Tool t)
    {
        CommitTextEdit();
        if (_canvas.Tool == Tool.Crop && t != Tool.Crop) _canvas.CancelCrop();
        _canvas.Tool = t;
        foreach (var (tool, b) in _toolButtons) b.IsChecked = tool == t;
        _canvas.Focus();
    }

    private void ApplyColor(Core.Model.ColorRgba c)
    {
        _canvas.Style.Color = c;
        var sel = _canvas.SelectedLayers.ToList();
        if (sel.Count == 0) return;
        _canvas.Apply("Farbe", () =>
        {
            foreach (var l in sel)
                switch (l)
                {
                    case ShapeLayer s: s.Stroke = c; break;
                    case LineLayer ln: ln.Stroke = c; break;
                    case StepLayer st: st.Fill = c; break;
                    case TextLayer t when t.Style == TextStyle.Plain: t.Foreground = c; break;
                    case TextLayer t: t.Border = c; break;
                    case FreehandLayer f: f.Stroke = c; break;
                    case HighlightLayer h: h.Color = c; break;
                    case MagnifierLayer m: m.Border = c; break;
                }
        });
    }

    private void FitZoom(bool onlyShrink)
    {
        var vw = Math.Max(100, _scroll.Bounds.Width - 60); var vh = Math.Max(100, _scroll.Bounds.Height - 60);
        if (vw < 120) { vw = Width - 360; vh = Height - 200; }
        var scaling = RenderScaling;
        var fit = Math.Min(vw * scaling / _canvas.Page.Width, vh * scaling / _canvas.Page.Height);
        _canvas.Zoom = onlyShrink ? Math.Min(1, fit) : fit;
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        _canvas.Zoom *= e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        e.Handled = true;
    }

    // ---------------------------------------------------------------- keyboard

    private async void OnKey(object? sender, KeyEventArgs e)
    {
        if (_textEditor is not null && _textEditor.IsFocused)
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))) { CommitTextEdit(); e.Handled = true; }
            return;
        }
        if (FocusManager?.GetFocusedElement() is TextBox or NumericUpDown) return;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var step = shift ? 10 : 1;
        var handled = true;
        switch (e.Key)
        {
            case Key.Z when ctrl && shift: _canvas.Redo(); break;
            case Key.Z when ctrl: _canvas.Undo(); break;
            case Key.Y when ctrl: _canvas.Redo(); break;
            case Key.C when ctrl && shift: CopyLayers(); break;
            case Key.C when ctrl: CopyImage(); break;
            case Key.V when ctrl: PasteLayers(); break;
            case Key.D when ctrl: _canvas.DuplicateSelection(); break;
            case Key.G when ctrl && shift: _canvas.UngroupSelection(); break;
            case Key.G when ctrl: _canvas.GroupSelection(); break;
            case Key.A when ctrl: _canvas.Select(_canvas.Page.Layers.Where(l => l is not ImageLayer { IsBackground: true }).Select(l => l.Id)); break;
            case Key.S when ctrl && shift: await SaveProjectAsync(true); break;
            case Key.S when ctrl: await SaveProjectAsync(false); break;
            case Key.E when ctrl && shift: await QuickSaveAsync(); break;
            case Key.E when ctrl: await ExportAsync(); break;
            case Key.OemCloseBrackets when ctrl: _canvas.MoveSelection(shift ? int.MaxValue : 1); break;
            case Key.OemOpenBrackets when ctrl: _canvas.MoveSelection(shift ? int.MinValue : -1); break;
            case Key.D0 when ctrl: _canvas.Zoom = 1; break;
            case Key.OemPlus or Key.Add when ctrl: _canvas.Zoom *= 1.25; break;
            case Key.OemMinus or Key.Subtract when ctrl: _canvas.Zoom /= 1.25; break;
            case Key.Delete or Key.Back: _canvas.DeleteSelection(); break;
            case Key.Left: _canvas.Nudge(-step, 0); break;
            case Key.Right: _canvas.Nudge(step, 0); break;
            case Key.Up: _canvas.Nudge(0, -step); break;
            case Key.Down: _canvas.Nudge(0, step); break;
            case Key.Enter when _canvas.Tool == Tool.Crop: _canvas.ApplyCrop(); SetTool(Tool.Select); break;
            case Key.Escape:
                if (_canvas.Tool == Tool.Crop) { _canvas.CancelCrop(); SetTool(Tool.Select); }
                else if (_canvas.Selection.Count > 0) _canvas.Select(Array.Empty<Guid>());
                else SetTool(Tool.Select);
                break;
            case Key.F2 when _canvas.SelectedLayers.FirstOrDefault() is TextLayer tl: BeginTextEdit(tl); break;
            case Key.V when !ctrl: SetTool(Tool.Select); break;
            case Key.A when !ctrl: SetTool(Tool.Arrow); break;
            case Key.L: SetTool(Tool.Line); break;
            case Key.R: SetTool(Tool.Rectangle); break;
            case Key.E when !ctrl: SetTool(Tool.Ellipse); break;
            case Key.T: SetTool(Tool.Text); break;
            case Key.I: SetTool(Tool.InfoBox); break;
            case Key.U: SetTool(Tool.Bubble); break;
            case Key.N: SetTool(Tool.Step); break;
            case Key.H: SetTool(Tool.Highlight); break;
            case Key.S when !ctrl: SetTool(Tool.Spotlight); break;
            case Key.M: SetTool(Tool.Magnifier); break;
            case Key.F: SetTool(Tool.Freehand); break;
            case Key.B: SetTool(Tool.Blackout); break;
            case Key.P: SetTool(Tool.Pixelate); break;
            case Key.W: SetTool(Tool.Blur); break;
            case Key.C when !ctrl: SetTool(Tool.Crop); break;
            default: handled = false; break;
        }
        e.Handled = handled;
        UpdateStatus();
    }

    // ---------------------------------------------------------------- inline text editing

    private void BeginTextEdit(TextLayer t)
    {
        CommitTextEdit();
        _canvas.Select(new[] { t.Id });
        _canvas.EditingTextLayer = t.Id;
        _canvas.NotifyDocumentChanged();
        var r = _canvas.ToView(t.Rect.Normalize());
        var s = _canvas.ViewScale;
        var tb = new TextBox
        {
            Text = t.Text, AcceptsReturn = true, TextWrapping = t.Style == TextStyle.Plain ? TextWrapping.NoWrap : TextWrapping.Wrap,
            FontSize = Math.Max(8, t.FontSize * s), FontFamily = new FontFamily(t.FontFamily),
            FontWeight = t.Bold ? FontWeight.Bold : FontWeight.Normal, FontStyle = t.Italic ? FontStyle.Italic : FontStyle.Normal,
            Foreground = new SolidColorBrush(t.Foreground.ToAvalonia()),
            Background = new SolidColorBrush(t.Background.IsTransparent ? Color.FromArgb(200, 255, 255, 255) : t.Background.ToAvalonia()),
            MinWidth = Math.Max(80, r.Width), MinHeight = Math.Max(24, r.Height), Padding = new Thickness(t.Padding * s),
        };
        Canvas.SetLeft(tb, r.X); Canvas.SetTop(tb, r.Y);
        _overlay.Children.Add(tb);
        _textEditor = tb;
        _canvas.History.BeginTransaction("Text bearbeiten");
        tb.TextChanged += (_, _) => { t.Text = tb.Text ?? ""; };
        tb.LostFocus += (_, _) => CommitTextEdit();
        // focus after the canvas finished its pointer handling
        Dispatcher.UIThread.Post(() => { tb.Focus(); tb.SelectAll(); }, DispatcherPriority.Loaded);
    }

    private void CommitTextEdit()
    {
        if (_textEditor is null) return;
        var tb = _textEditor; _textEditor = null;
        _overlay.Children.Remove(tb);
        if (_canvas.EditingTextLayer is { } id && _canvas.Page.Find(id) is TextLayer t)
        {
            if (t.Style == TextStyle.Plain) t.Rect = t.Rect.Normalize() with { Width = 0 }; // plain text: auto width, no wrapping
            var fit = TextLayout.FitBox(t);
            t.Rect = t.Style == TextStyle.Plain ? fit : t.Rect.Normalize() with { Height = Math.Max(fit.Height, t.Rect.Normalize().Height) };
        }
        _canvas.EditingTextLayer = null;
        _canvas.History.Commit();
        _canvas.NotifyDocumentChanged();
        _canvas.Focus();
    }

    // ---------------------------------------------------------------- output

    private void CopyImage()
    {
        CommitTextEdit();
        if (_app.Clipboard is null) { _ = Dialogs.InfoAsync(this, "Zwischenablage", "Auf diesem System noch nicht unterstützt."); return; }
        using var img = _canvas.RenderFlattened();
        var raw = SkiaConvert.ToRaw(img);
        var png = SkiaConvert.EncodePng(img, 3);
        var ok = _app.Clipboard.SetImage(raw, png);
        _status.Text = ok ? $"In Zwischenablage kopiert ({img.Width} × {img.Height} px)" : "⚠ Zwischenablage ist von einem anderen Programm blockiert.";
    }

    private void CopyLayers()
    {
        var sel = _canvas.SelectedLayers.ToList();
        if (sel.Count == 0) return;
        _layerClipboard = sel.Select(l =>
        {
            var assets = new Dictionary<string, byte[]>();
            foreach (var im in (l is GroupLayer g ? g.Children.Prepend(l) : new[] { l }).OfType<ImageLayer>())
                if (_session.Assets.Get(im.AssetId) is { } b) assets[im.AssetId] = b;
            return (ProjectJson.Clone(l), assets);
        }).ToList();
        _status.Text = $"{sel.Count} Ebene(n) kopiert – mit Strg+V einfügen (auch in anderen Projekten).";
    }

    private void PasteLayers()
    {
        if (_layerClipboard is null) return;
        var ids = new List<Guid>();
        _canvas.Apply("Einfügen", () =>
        {
            foreach (var (layer, assets) in _layerClipboard)
            {
                foreach (var (id, bytes) in assets)
                {
                    _session.Assets.Add(bytes);
                    _session.Project.Assets.TryAdd(id, new AssetInfo { Id = id, FileName = id + ".png" });
                }
                var copy = PageOperations.CloneWithNewIds(layer);
                if (copy is ImageLayer im) im.IsBackground = false;
                copy.Translate(12, 12);
                _canvas.Page.Layers.Add(copy);
                ids.Add(copy.Id);
            }
        });
        _canvas.Select(ids);
    }

    private string SuggestedName() => SafeName(_session.Project.Title);
    private static string SafeName(string s) => Core.Obsidian.SafeFileName.ToNoteName(s);

    private async Task ExportAsync()
    {
        CommitTextEdit();
        using var img = _canvas.RenderFlattened();
        var dlg = new ImageExportDialog(_app, img, SuggestedName());
        await dlg.ShowDialog(this);
        if (dlg.WrittenPath is not null) _status.Text = "Exportiert: " + dlg.WrittenPath;
    }

    private async Task QuickSaveAsync()
    {
        CommitTextEdit();
        var profile = _app.Profiles.Get(_app.Settings.Shared.DefaultExportProfile);
        var dir = _app.Settings.Platform.LastSaveDirectory ?? _app.Env.MediaDir;
        if (!Directory.Exists(dir)) dir = _app.Env.MediaDir;
        var name = SuggestedName();
        var path = Path.Combine(dir, name + profile.FileExtension);
        path = Path.Combine(dir, Core.Obsidian.SafeFileName.MakeUnique(Path.GetFileName(path), n => File.Exists(Path.Combine(dir, n)), slugStyle: false));
        using var img = _canvas.RenderFlattened();
        var r = await Task.Run(() => ImageExporter.Export(img, profile, path));
        _status.Text = r.Success ? $"Gespeichert: {path} – {r.Message}" : "⚠ " + r.Message;
    }

    /// <returns>true if saved.</returns>
    public async Task<bool> SaveProjectAsync(bool saveAs)
    {
        CommitTextEdit();
        if (_isStudioPage && Owner is StudioWindow studio) return await studio.SaveAsync(saveAs);
        var ok = await ProjectSaver.SaveAsync(this, _app, _session, saveAs);
        if (ok) _status.Text = "Projekt gespeichert: " + _session.FilePath;
        return ok;
    }

    private void AddToGuide()
    {
        CommitTextEdit();
        var studio = App.Current.ShowStudio();
        studio.AddPageFrom(_session, _canvas.Page);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        CommitTextEdit();
        if (_isStudioPage || !_session.IsDirty || _forceClose) { Cleanup(); return; }
        e.Cancel = true;
        var a = await Dialogs.AskAsync(this, "Änderungen speichern?", "Das Bild wurde bearbeitet. Als Projekt speichern, damit alle Ebenen später bearbeitbar bleiben?", "Speichern", "Verwerfen", "Abbrechen");
        if (a == "Speichern" && !await SaveProjectAsync(false)) return;
        if (a is null or "Abbrechen") return;
        _forceClose = true;
        Close();
    }

    private bool _forceClose;

    /// <summary>Closes without asking (content was handed over to the studio).</summary>
    public void ForceCloseWithoutPrompt() { _forceClose = true; Close(); }

    private void Cleanup()
    {
        _autosave.Stop();
        if (!_isStudioPage)
        {
            Recovery.Delete(_session);
            _session.Dispose();
        }
    }
}
