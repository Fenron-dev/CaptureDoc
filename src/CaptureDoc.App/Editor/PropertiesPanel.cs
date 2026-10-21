using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CaptureDoc.App.Util;
using CaptureDoc.Core.Model;
using CaptureDoc.Rendering;

using Page = CaptureDoc.Core.Model.Page;

namespace CaptureDoc.App.Editor;

/// <summary>Edits properties of the selected layer(s) after creation. Every change is one (merged) undo step.</summary>
public sealed class PropertiesPanel : ScrollViewer
{
    private readonly EditorCanvas _canvas;
    private readonly StackPanel _stack = new() { Spacing = 8, Margin = new Thickness(4) };
    private bool _building;
    private bool _selfEdit;

    // a subclass gets no control theme of its own → without this there is no template and nothing scrolls
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    /// <summary>Page size/canvas options (image editor only; video pages keep the video size).</summary>
    public bool ShowPageSection { get; set; } = true;

    public PropertiesPanel(EditorCanvas canvas)
    {
        _canvas = canvas;
        Content = _stack;
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        _canvas.SelectionChanged += (_, _) => Rebuild();
        _canvas.HistoryChanged += (_, _) => { if (!_selfEdit) Rebuild(); };
        Rebuild();
    }

    private void Edit<T>(string desc, string key, Action<T> apply) where T : Layer
    {
        var targets = _canvas.SelectedLayers.OfType<T>().ToList();
        if (targets.Count == 0 || _building) return;
        _selfEdit = true;
        try { _canvas.Apply(desc, () => { foreach (var t in targets) apply(t); }, key); }
        finally { _selfEdit = false; }
    }

    public void Rebuild()
    {
        _building = true;
        try
        {
            _stack.Children.Clear();
            var sel = _canvas.SelectedLayers.ToList();
            _stack.Children.Add(Ui.Label("Eigenschaften", 14, bold: true));
            if (sel.Count == 0)
            {
                _stack.Children.Add(Ui.Label("Keine Ebene ausgewählt. Werkzeug wählen und im Bild ziehen, oder eine Ebene anklicken.", 12, opacity: 0.7));
                if (ShowPageSection) PageSection();
                return;
            }
            if (sel.Count > 1)
            {
                _stack.Children.Add(Ui.Label($"{sel.Count} Ebenen ausgewählt", 12));
                ColorRow("Farbe", sel[0] switch { ShapeLayer s => s.Stroke, LineLayer l => l.Stroke, StepLayer st => st.Fill, TextLayer t => t.Foreground, _ => ColorRgba.Red },
                    c => Edit<Layer>("Farbe", "color", l => EditorCanvas.SetMainColor(l, c)));
                return;
            }
            var layer = sel[0];
            var name = new TextBox { Text = layer.Name };
            name.TextChanged += (_, _) => Edit<Layer>("Umbenennen", "name", l => l.Name = name.Text ?? "");
            _stack.Children.Add(Ui.Field($"Name ({layer.Kind})", name));
            if (layer.Locked) _stack.Children.Add(Ui.Label("🔒 Gesperrt – im Ebenenpanel entsperren, um im Bild zu bearbeiten.", 12, opacity: 0.7));

            switch (layer)
            {
                case ShapeLayer s:
                    Combo("Form", new[] { "Rechteck", "Ellipse", "Abgerundet" }, (int)s.Shape, i => Edit<ShapeLayer>("Form", "shape", x => x.Shape = (ShapeKind)i));
                    ColorRow("Linienfarbe", s.Stroke, c => Edit<ShapeLayer>("Linienfarbe", "stroke", x => x.Stroke = c), allowTransparent: true);
                    ColorRow("Füllung", s.Fill, c => Edit<ShapeLayer>("Füllung", "fill", x => x.Fill = c), allowTransparent: true);
                    Number("Linienstärke", s.StrokeWidth, 0, 60, v => Edit<ShapeLayer>("Linienstärke", "sw", x => x.StrokeWidth = v));
                    Check("Schatten", s.Shadow, v => Edit<ShapeLayer>("Schatten", "shadow", x => x.Shadow = v));
                    BoxGeometry(s);
                    break;
                case LineLayer l:
                    ColorRow("Farbe", l.Stroke, c => Edit<LineLayer>("Farbe", "stroke", x => x.Stroke = c));
                    Number("Linienstärke", l.StrokeWidth, 1, 60, v => Edit<LineLayer>("Linienstärke", "sw", x => x.StrokeWidth = v));
                    Combo("Anfang", new[] { "Keine", "Pfeil", "Punkt" }, (int)l.StartCap, i => Edit<LineLayer>("Linienende", "cap0", x => x.StartCap = (LineCap)i));
                    Combo("Ende", new[] { "Keine", "Pfeil", "Punkt" }, (int)l.EndCap, i => Edit<LineLayer>("Linienende", "cap1", x => x.EndCap = (LineCap)i));
                    Check("Schatten", l.Shadow, v => Edit<LineLayer>("Schatten", "shadow", x => x.Shadow = v));
                    break;
                case TextLayer t:
                    var tb = new TextBox { Text = t.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
                    tb.TextChanged += (_, _) => Edit<TextLayer>("Text", "text", x => { x.Text = tb.Text ?? ""; FitText(x); });
                    _stack.Children.Add(Ui.Field("Text", tb));
                    Combo("Stil", new[] { "Text", "Infobox", "Sprechblase" }, (int)t.Style, i => Edit<TextLayer>("Stil", "style", x =>
                    {
                        x.Style = (TextStyle)i;
                        if (x.Style == TextStyle.Bubble && x.TailTip is null) x.TailTip = new PointD(x.Rect.X + 30, x.Rect.Bottom + 60);
                        if (x.Style != TextStyle.Plain && x.Background.IsTransparent) x.Background = ColorRgba.White;
                        FitText(x);
                    }));
                    Number("Schriftgröße", t.FontSize, 6, 200, v => Edit<TextLayer>("Schriftgröße", "fs", x => { x.FontSize = v; FitText(x); }));
                    var font = new TextBox { Text = t.FontFamily };
                    font.TextChanged += (_, _) => Edit<TextLayer>("Schriftart", "font", x => { x.FontFamily = font.Text ?? "Segoe UI"; FitText(x); });
                    _stack.Children.Add(Ui.Field("Schriftart", font));
                    Check("Fett", t.Bold, v => Edit<TextLayer>("Fett", "bold", x => { x.Bold = v; FitText(x); }));
                    Check("Kursiv", t.Italic, v => Edit<TextLayer>("Kursiv", "italic", x => { x.Italic = v; FitText(x); }));
                    Combo("Ausrichtung", new[] { "Links", "Mitte", "Rechts" }, (int)t.Alignment, i => Edit<TextLayer>("Ausrichtung", "align", x => x.Alignment = (Core.Model.TextAlignment)i));
                    ColorRow("Textfarbe", t.Foreground, c => Edit<TextLayer>("Textfarbe", "fg", x => x.Foreground = c));
                    ColorRow("Hintergrund", t.Background, c => Edit<TextLayer>("Hintergrund", "bg", x => x.Background = c), allowTransparent: true);
                    ColorRow("Rahmen", t.Border, c => Edit<TextLayer>("Rahmen", "border", x => x.Border = c), allowTransparent: true);
                    if (t.Style == TextStyle.Bubble && t.TailTip is { } tip)
                    {
                        Number("Spitze X", tip.X, -10000, 100000, v => Edit<TextLayer>("Spitze", "tipx", x => x.TailTip = new PointD(v, x.TailTip?.Y ?? 0)));
                        Number("Spitze Y", tip.Y, -10000, 100000, v => Edit<TextLayer>("Spitze", "tipy", x => x.TailTip = new PointD(x.TailTip?.X ?? 0, v)));
                    }
                    BoxGeometry(t);
                    break;
                case StepLayer st:
                    Number("Nummer", st.Number, 0, 9999, v => Edit<StepLayer>("Nummer", "num", x => { x.Number = (int)v; x.AutoNumber = false; }));
                    Check("Automatisch nummerieren", st.AutoNumber, v => Edit<StepLayer>("Auto-Nummer", "auto", x => x.AutoNumber = v));
                    ColorRow("Farbe", st.Fill, c => Edit<StepLayer>("Farbe", "fill", x => x.Fill = c));
                    ColorRow("Ziffernfarbe", st.Foreground, c => Edit<StepLayer>("Ziffernfarbe", "fg", x => x.Foreground = c));
                    Number("Radius", st.Radius, 6, 200, v => Edit<StepLayer>("Größe", "r", x => x.Radius = v));
                    Number("X", st.Center.X, -10000, 100000, v => Edit<StepLayer>("Position", "x", x => x.Center = x.Center with { X = v }));
                    Number("Y", st.Center.Y, -10000, 100000, v => Edit<StepLayer>("Position", "y", x => x.Center = x.Center with { Y = v }));
                    Check("Schatten", st.Shadow, v => Edit<StepLayer>("Schatten", "shadow", x => x.Shadow = v));
                    break;
                case RedactLayer rd:
                    Combo("Art", new[] { "Schwärzen", "Verpixeln", "Weichzeichnen" }, (int)rd.Mode, i => Edit<RedactLayer>("Art", "mode", x => x.Mode = (RedactMode)i));
                    if (rd.Mode != RedactMode.Blackout)
                        Number(rd.Mode == RedactMode.Pixelate ? "Blockgröße" : "Stärke", rd.Strength, 2, 100, v => Edit<RedactLayer>("Stärke", "str", x => x.Strength = v));
                    else ColorRow("Farbe", rd.Fill, c => Edit<RedactLayer>("Farbe", "fill", x => x.Fill = c.WithAlpha(255)));
                    _stack.Children.Add(Ui.Label(rd.Mode == RedactMode.Blur
                        ? "⚠ Weichzeichnen kann bei schwacher Stärke teilweise lesbar bleiben. Für sensible Daten „Schwärzen“ verwenden."
                        : "Im exportierten Bild wird der Bereich eingebrannt und ist nicht wiederherstellbar. Die Projektdatei (.capturedoc) enthält das Original weiterhin.", 12, opacity: 0.75));
                    BoxGeometry(rd);
                    break;
                case HighlightLayer hl:
                    ColorRow("Farbe", hl.Color, c => Edit<HighlightLayer>("Farbe", "c", x => x.Color = c));
                    BoxGeometry(hl);
                    break;
                case SpotlightLayer sp:
                    Number("Abdunkeln %", sp.Dim * 100, 0, 100, v => Edit<SpotlightLayer>("Abdunkeln", "dim", x => x.Dim = v / 100));
                    Check("Elliptisch", sp.Elliptical, v => Edit<SpotlightLayer>("Form", "ell", x => x.Elliptical = v));
                    BoxGeometry(sp);
                    break;
                case MagnifierLayer mg:
                    Check("Rund", mg.Elliptical, v => Edit<MagnifierLayer>("Form", "ell", x => x.Elliptical = v));
                    ColorRow("Rahmen", mg.Border, c => Edit<MagnifierLayer>("Rahmen", "b", x => x.Border = c), allowTransparent: true);
                    Number("Quelle X", mg.Source.X, -10000, 100000, v => Edit<MagnifierLayer>("Quelle", "sx", x => x.Source = x.Source with { X = v }));
                    Number("Quelle Y", mg.Source.Y, -10000, 100000, v => Edit<MagnifierLayer>("Quelle", "sy", x => x.Source = x.Source with { Y = v }));
                    Number("Quelle Breite", mg.Source.Width, 2, 100000, v => Edit<MagnifierLayer>("Quelle", "sw", x => x.Source = x.Source with { Width = v }));
                    Number("Quelle Höhe", mg.Source.Height, 2, 100000, v => Edit<MagnifierLayer>("Quelle", "sh", x => x.Source = x.Source with { Height = v }));
                    BoxGeometry(mg);
                    break;
                case FreehandLayer fh:
                    ColorRow("Farbe", fh.Stroke, c => Edit<FreehandLayer>("Farbe", "c", x => x.Stroke = x.Highlighter && c.A == 255 ? c.WithAlpha(x.Stroke.A) : c));
                    Number("Linienstärke", fh.StrokeWidth, 1, 120, v => Edit<FreehandLayer>("Linienstärke", "sw", x => x.StrokeWidth = v));
                    Check("Textmarker (halbtransparent)", fh.Highlighter, v => Edit<FreehandLayer>("Textmarker", "hl", x =>
                    {
                        x.Highlighter = v;
                        x.Stroke = x.Stroke.WithAlpha(v ? (byte)110 : (byte)255);
                        if (v && x.StrokeWidth < 12) x.StrokeWidth = 16;
                    }));
                    if (fh.Highlighter)
                        Number("Deckung %", Math.Round(fh.Stroke.A / 2.55), 5, 100, v => Edit<FreehandLayer>("Deckung", "hla", x => x.Stroke = x.Stroke.WithAlpha((byte)Math.Clamp(v * 2.55, 10, 255))));
                    break;
                case ImageLayer im:
                    _stack.Children.Add(Ui.Label(im.IsBackground ? "Originalaufnahme (nichtdestruktiv, wird nie verändert)." : "Bildebene", 12, opacity: 0.75));
                    Check("Schatten", im.Shadow, v => Edit<ImageLayer>("Schatten", "shadow", x => x.Shadow = v));
                    Number("Rahmenstärke", im.BorderWidth, 0, 40, v => Edit<ImageLayer>("Rahmen", "bw", x => x.BorderWidth = v));
                    ColorRow("Rahmenfarbe", im.BorderColor, c => Edit<ImageLayer>("Rahmenfarbe", "bc", x => x.BorderColor = c));
                    Number("Eckenradius", im.CornerRadius, 0, 200, v => Edit<ImageLayer>("Ecken", "cr", x => x.CornerRadius = v));
                    if (!im.IsBackground) BoxGeometry(im);
                    break;
                case GroupLayer g:
                    _stack.Children.Add(Ui.Label($"Gruppe mit {g.Children.Count} Ebenen", 12));
                    break;
            }
        }
        finally { _building = false; }
    }

    private void PageSection()
    {
        var p = _canvas.Page;
        _stack.Children.Add(new Separator());
        _stack.Children.Add(Ui.Label("Seite", 13, bold: true));
        _stack.Children.Add(Ui.Label($"{p.Width} × {p.Height} px", 12));
        var w = new NumericUpDown { Value = p.Width, Minimum = 1, Maximum = 30000, Increment = 10, FormatString = "0" };
        var h = new NumericUpDown { Value = p.Height, Minimum = 1, Maximum = 30000, Increment = 10, FormatString = "0" };
        var keep = new CheckBox { Content = "Seitenverhältnis", IsChecked = true };
        var syncing = false;
        w.ValueChanged += (_, _) => { if (syncing || keep.IsChecked != true || w.Value is null) return; syncing = true; h.Value = Math.Round(w.Value.Value * p.Height / p.Width); syncing = false; };
        h.ValueChanged += (_, _) => { if (syncing || keep.IsChecked != true || h.Value is null) return; syncing = true; w.Value = Math.Round(h.Value.Value * p.Width / p.Height); syncing = false; };
        _stack.Children.Add(Ui.Row(Ui.Field("Breite", w), Ui.Field("Höhe", h)));
        _stack.Children.Add(keep);
        _stack.Children.Add(Ui.Row(
            Ui.Button("Skalieren", () => _canvas.Apply("Bildgröße", () => PageOperations.Resize(p, (int)(w.Value ?? p.Width), (int)(h.Value ?? p.Height))), "Bild und alle Ebenen skalieren"),
            Ui.Button("Leinwand", () => _canvas.Apply("Leinwandgröße", () => PageOperations.ResizeCanvas(p, (int)(w.Value ?? p.Width), (int)(h.Value ?? p.Height))), "Leinwand vergrößern/verkleinern (zentriert, ohne Skalierung)")));
        _stack.Children.Add(Ui.Button("▢ Rahmen & Schatten", () => _canvas.Apply("Rahmen & Schatten", () => PageOperations.AddFrameAndShadow(p)), "Transparenter Rand, weicher Schatten, feiner Rahmen und runde Ecken um den Screenshot (rückgängig machbar)"));
        ColorRow("Leinwandfarbe", p.Background, c => _canvas.Apply("Leinwandfarbe", () => p.Background = c, "pagebg"), allowTransparent: true);
    }

    private static void FitText(TextLayer t)
    {
        if (t.Style == TextStyle.Plain) t.Rect = t.Rect.Normalize() with { Width = 0 };
        var fit = TextLayout.FitBox(t);
        var r = t.Rect.Normalize();
        t.Rect = t.Style == TextStyle.Plain ? fit : new RectD(r.X, r.Y, r.Width > 0 ? r.Width : fit.Width, Math.Max(fit.Height, Math.Min(r.Height, fit.Height + 400)));
    }

    private void BoxGeometry(BoxLayer b)
    {
        var r = b.Rect.Normalize();
        var x = Num(r.X); var y = Num(r.Y); var w = Num(r.Width); var h = Num(r.Height);
        void Changed() => Edit<BoxLayer>("Position/Größe", "geom", l => l.SetBounds(new RectD((double)(x.Value ?? 0), (double)(y.Value ?? 0), Math.Max(1, (double)(w.Value ?? 1)), Math.Max(1, (double)(h.Value ?? 1)))));
        foreach (var n in new[] { x, y, w, h }) n.ValueChanged += (_, _) => Changed();
        _stack.Children.Add(Ui.Row(Ui.Field("X", x), Ui.Field("Y", y)));
        _stack.Children.Add(Ui.Row(Ui.Field("Breite", w), Ui.Field("Höhe", h)));

        static NumericUpDown Num(double v) => new() { Value = (decimal)Math.Round(v), Minimum = -100000, Maximum = 100000, Increment = 1, FormatString = "0", Width = 110 };
    }

    private void Number(string label, double value, double min, double max, Action<double> set)
    {
        var n = new NumericUpDown { Value = (decimal)Math.Round(value, 1), Minimum = (decimal)min, Maximum = (decimal)max, Increment = 1, FormatString = "0.#" };
        n.ValueChanged += (_, _) => { if (n.Value is { } v) set((double)v); };
        _stack.Children.Add(Ui.Field(label, n));
    }

    private void Check(string label, bool value, Action<bool> set)
    {
        var c = new CheckBox { Content = label, IsChecked = value };
        c.IsCheckedChanged += (_, _) => set(c.IsChecked == true);
        _stack.Children.Add(c);
    }

    private void Combo(string label, string[] items, int selected, Action<int> set)
    {
        var c = new ComboBox { ItemsSource = items, SelectedIndex = selected, MinWidth = 160 };
        c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) set(c.SelectedIndex); };
        _stack.Children.Add(Ui.Field(label, c));
    }

    private void ColorRow(string label, ColorRgba value, Action<ColorRgba> set, bool allowTransparent = false)
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        var hex = new TextBox { Text = value.ToHex(), Width = 100, FontFamily = new FontFamily("Consolas, monospace") };
        var swatches = Ui.Palette.AsEnumerable();
        if (allowTransparent) swatches = swatches.Append(ColorRgba.Transparent);
        foreach (var c in swatches)
        {
            var b = new Button
            {
                Width = 22, Height = 22, Margin = new Thickness(1), Padding = new Thickness(0),
                Background = c.IsTransparent ? Brushes.Transparent : new SolidColorBrush(c.ToAvalonia()),
                Content = c.IsTransparent ? "∅" : null, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray,
            };
            ToolTip.SetTip(b, c.IsTransparent ? "Transparent" : c.ToHex());
            var cc = c;
            b.Click += (_, _) => { set(cc); hex.Text = cc.ToHex(); };
            wrap.Children.Add(b);
        }
        hex.LostFocus += (_, _) => { if (ColorRgba.TryParse(hex.Text, out var parsed)) set(parsed); };
        hex.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter && ColorRgba.TryParse(hex.Text, out var parsed)) set(parsed); };
        _stack.Children.Add(Ui.Field(label, Ui.Column(2, wrap, hex)));
    }
}
