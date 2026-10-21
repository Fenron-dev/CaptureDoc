using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CaptureDoc.Core.Editing;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Portable;
using CaptureDoc.Rendering;
using SkiaSharp;

using Page = CaptureDoc.Core.Model.Page;

namespace CaptureDoc.App.Editor;

public enum Tool
{
    Select, Arrow, Line, Rectangle, Ellipse, Text, InfoBox, Bubble, Step,
    Highlight, Blackout, Pixelate, Blur, Spotlight, Magnifier, Freehand, Crop, Pipette, Marker,
}

/// <summary>
/// Interactive canvas. The page is rendered by <see cref="SceneRenderer"/> (same code as export)
/// into a reused bitmap; selection handles are drawn on top in screen space.
/// </summary>
public sealed class EditorCanvas : Control
{
    private const double HandleSize = 9;
    private WriteableBitmap? _bitmap;
    private long _docRevision = 1, _renderedRevision;
    private DragState _drag = DragState.None;
    private PointD _dragStart, _dragLast;
    private RectD _resizeOrigin;
    private int _resizeHandle = -1;
    private Layer? _creating;
    private RectD? _crop;
    // drag cache: everything below the dragged layer is rendered once (4K: ~150 ms → a few ms per move)
    private SKImage? _dragCache;
    private int _dragCacheFrom = -1;

    private void BeginDragCache()
    {
        EndDragCache();
        var ids = Selection.ToHashSet();
        var view = ViewPage();
        var idx = -1;
        for (var i = 0; i < view.Layers.Count; i++)
            if (ids.Contains(view.Layers[i].Id) || (view.Layers[i] is GroupLayer g && g.AllChildren().Any(c => ids.Contains(c.Id)))) { idx = i; break; }
        if (idx <= 0 || (long)Page.Width * Page.Height < 1_000_000) return; // small images are fast anyway
        _dragCache = Session.Renderer.RenderBelow(view, idx, Underlay);
        _dragCacheFrom = idx;
    }

    private void EndDragCache()
    {
        _dragCache?.Dispose();
        _dragCache = null;
        _dragCacheFrom = -1;
    }

    private enum DragState { None, Move, Resize, Create, Crop }

    /// <summary>
    /// Only layers passing the filter are drawn and can be clicked (video editor: layers visible at the playhead).
    /// The page itself is not changed.
    /// </summary>
    public Func<Layer, bool>? LayerFilter
    {
        get => _filter;
        set { _filter = value; InvalidateRender(); }
    }
    private Func<Layer, bool>? _filter;

    /// <summary>Picture drawn below all layers (current video frame); not part of the page.</summary>
    public SKImage? Underlay
    {
        get => _underlay;
        set { _underlay = value; InvalidateRender(); }
    }
    private SKImage? _underlay;

    /// <summary>Display-only replacement of the (filtered) page, e.g. moving annotations during playback. Not used for hit tests.</summary>
    public Func<Page, Page>? DisplayOverride { get; set; }

    /// <summary>Raised when a new layer was drawn - still inside its undo step, so changes made here are part of it.</summary>
    public event EventHandler<Layer>? LayerCreated;

    /// <summary>The page as displayed (filtered); layer objects are shared with <see cref="Page"/>.</summary>
    public Page ViewPage() => _filter is null ? Page : new Page
    {
        Id = Page.Id, Name = Page.Name, Width = Page.Width, Height = Page.Height, Background = Page.Background,
        Layers = Page.Layers.Where(l => _filter(l)).ToList(),
    };

    /// <summary>Redraws without signalling a document change (time or underlay changed).</summary>
    public void InvalidateRender() { _docRevision++; InvalidateVisual(); }

    public ProjectSession Session { get; }
    public Page Page { get; private set; }
    public History History { get; private set; }
    public ToolStyle Style { get; set; }
    public HashSet<Guid> Selection { get; } = new();
    public Guid? EditingTextLayer { get; set; }

    public event EventHandler? SelectionChanged;
    public event EventHandler? DocumentChanged;
    public event EventHandler<TextLayer>? TextEditRequested;
    public event EventHandler? ViewChanged;
    /// <summary>Colour picked with the pipette tool.</summary>
    public event EventHandler<ColorRgba>? ColorPicked;
    /// <summary>Raised after every undo step, undo or redo (not during drags).</summary>
    public event EventHandler? HistoryChanged;

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, History)) return;
        _docRevision++; PruneSelection(); InvalidateVisual();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private Tool _tool;
    public Tool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            if (value != Tool.Crop) _crop = null;
            Cursor = new Cursor(value == Tool.Select ? StandardCursorType.Arrow : StandardCursorType.Cross);
            InvalidateVisual();
        }
    }

    private double _zoom = 1;
    /// <summary>1 = one image pixel per physical screen pixel.</summary>
    public double Zoom
    {
        get => _zoom;
        set { _zoom = Math.Clamp(value, 0.05, 16); InvalidateMeasure(); InvalidateVisual(); ViewChanged?.Invoke(this, EventArgs.Empty); }
    }

    private double RenderScaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
    /// <summary>DIPs per page pixel.</summary>
    public double ViewScale => Zoom / RenderScaling;

    public EditorCanvas(ProjectSession session, Page page, ToolStyle style)
    {
        Session = session; Page = page; Style = style;
        History = session.HistoryFor(page);
        History.Changed += OnHistoryChanged;
        Focusable = true;
        ClipToBounds = false;
    }

    public void SetPage(Page page)
    {
        Page = page;
        History.Changed -= OnHistoryChanged;
        History = Session.HistoryFor(page);
        History.Changed += OnHistoryChanged;
        Selection.Clear();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        NotifyDocumentChanged();
        InvalidateMeasure();
    }

    public IEnumerable<Layer> SelectedLayers => Selection.Select(id => Page.Find(id)).Where(l => l is not null)!;

    public void NotifyDocumentChanged()
    {
        _docRevision++;
        InvalidateVisual();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Select(IEnumerable<Guid> ids)
    {
        Selection.Clear();
        foreach (var id in ids) Selection.Add(id);
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PruneSelection()
    {
        var removed = Selection.RemoveWhere(id => Page.Find(id) is null);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        if (removed > 0) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Applies a change as one undo step and refreshes the view.</summary>
    public void Apply(string description, Action change, string? mergeKey = null)
    {
        if (mergeKey is null) History.Execute(description, change);
        else History.Execute(description, change, mergeKey);
        PageOperations.RenumberSteps(Page);
        NotifyDocumentChanged();
    }

    protected override Size MeasureOverride(Size availableSize) => new(Page.Width * ViewScale, Page.Height * ViewScale);

    // ---------------------------------------------------------------- rendering

    public override void Render(DrawingContext ctx)
    {
        EnsureBitmap();
        var dst = new Rect(0, 0, Page.Width * ViewScale, Page.Height * ViewScale);
        if (_bitmap is not null)
        {
            using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = Zoom >= 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality }))
                ctx.DrawImage(_bitmap, new Rect(0, 0, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height), dst);
        }
        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)), 1), dst);

        var accent = new SolidColorBrush(Color.FromRgb(0, 140, 255));
        var pen = new Pen(accent, 1.2, DashStyle.Dash);
        foreach (var l in SelectedLayers)
        {
            var b = ToView(l.Bounds);
            ctx.DrawRectangle(null, pen, b.Inflate(2));
            if (Selection.Count == 1 && !l.Locked) DrawHandles(ctx, l, accent);
        }

        if (_crop is { } c)
        {
            var r = ToView(c.Normalize());
            var geo = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(dst), new RectangleGeometry(r));
            ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), null, geo);
            ctx.DrawRectangle(null, new Pen(Brushes.White, 1.5), r);
        }
    }

    private void DrawHandles(DrawingContext ctx, Layer l, IBrush accent)
    {
        foreach (var h in HandlePoints(l))
            ctx.DrawRectangle(Brushes.White, new Pen(accent, 1.5), new Rect(h.X - HandleSize / 2, h.Y - HandleSize / 2, HandleSize, HandleSize));
    }

    /// <summary>Handle positions in view coordinates. Lines: start, end. Others: 8 box handles.</summary>
    private List<Point> HandlePoints(Layer l)
    {
        if (l is LineLayer ln) return new() { ToView(ln.Start), ToView(ln.End) };
        var b = ToView(l is BoxLayer bx ? bx.Rect.Normalize() : l.Bounds);
        double x0 = b.X, y0 = b.Y, x1 = b.Right, y1 = b.Bottom, xm = (x0 + x1) / 2, ym = (y0 + y1) / 2;
        return new() { new(x0, y0), new(xm, y0), new(x1, y0), new(x1, ym), new(x1, y1), new(xm, y1), new(x0, y1), new(x0, ym) };
    }

    private void EnsureBitmap()
    {
        if (Page.Width <= 0 || Page.Height <= 0) return;
        if (_bitmap is null || _bitmap.PixelSize.Width != Page.Width || _bitmap.PixelSize.Height != Page.Height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(Page.Width, Page.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            _renderedRevision = 0;
        }
        if (_renderedRevision == _docRevision) return;
        using (var fb = _bitmap.Lock())
        {
            var info = new SKImageInfo(Page.Width, Page.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, fb.Address, fb.RowBytes);
            if (surface is not null)
            {
                var view = ViewPage();
                if (DisplayOverride is { } display) view = display(view);
                if (_dragCache is not null && _drag != DragState.None) Session.Renderer.RenderFrom(surface, view, _dragCache, _dragCacheFrom, EditingTextLayer);
                else Session.Renderer.Render(surface, view, 1.0, EditingTextLayer, _underlay);
            }
        }
        _renderedRevision = _docRevision;
    }

    public Rect ToView(RectD r) => new(r.X * ViewScale, r.Y * ViewScale, r.Width * ViewScale, r.Height * ViewScale);
    public Point ToView(PointD p) => new(p.X * ViewScale, p.Y * ViewScale);
    public PointD ToPage(Point p) => new(p.X / ViewScale, p.Y / ViewScale);

    // ---------------------------------------------------------------- input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pt = e.GetCurrentPoint(this);
        if (!pt.Properties.IsLeftButtonPressed) return;
        var p = ToPage(pt.Position);
        _dragStart = _dragLast = p;
        e.Pointer.Capture(this);

        if (Tool == Tool.Pipette)
        {
            using var flat = RenderFlattened();
            var x = (int)Math.Clamp(p.X, 0, flat.Width - 1); var y = (int)Math.Clamp(p.Y, 0, flat.Height - 1);
            var px = new byte[4];
            var info = new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            unsafe { fixed (byte* b = px) flat.ReadPixels(info, (nint)b, 4, x, y); }
            ColorPicked?.Invoke(this, new ColorRgba(px[0], px[1], px[2], 255));
            e.Pointer.Capture(null);
            return;
        }

        if (Tool == Tool.Crop)
        {
            _crop = new RectD(p.X, p.Y, 0, 0);
            _drag = DragState.Crop;
            return;
        }

        if (Tool == Tool.Select)
        {
            var tol = 4 / ViewScale;
            if (Selection.Count == 1 && SelectedLayers.FirstOrDefault() is { Locked: false } sel)
            {
                var h = HitHandle(sel, pt.Position);
                if (h >= 0)
                {
                    _resizeHandle = h;
                    _resizeOrigin = sel is BoxLayer bx ? bx.Rect.Normalize() : sel.Bounds;
                    History.BeginTransaction("Größe ändern");
                    _drag = DragState.Resize;
                    BeginDragCache();
                    return;
                }
            }
            var hit = ViewPage().HitTest(p, tol);
            if (hit is null) { if (Selection.Count > 0) Select(Array.Empty<Guid>()); return; }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (!Selection.Remove(hit.Id)) Selection.Add(hit.Id);
                InvalidateVisual(); SelectionChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (!Selection.Contains(hit.Id)) Select(new[] { hit.Id });
            if (e.ClickCount == 2 && hit is TextLayer tl) { TextEditRequested?.Invoke(this, tl); return; }
            History.BeginTransaction("Verschieben");
            _drag = DragState.Move;
            BeginDragCache();
            return;
        }

        // creation tools
        History.BeginTransaction(ToolName(Tool) + " hinzufügen");
        _creating = CreateLayer(Tool, p);
        _creating.Name = PageOperations.NextName(Page, _creating);
        Page.Layers.Add(_creating);
        Select(new[] { _creating.Id });
        _drag = DragState.Create;
        BeginDragCache();
        PageOperations.RenumberSteps(Page);
        NotifyDocumentChanged();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var p = ToPage(pos);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (_drag)
        {
            case DragState.Move:
            {
                var dx = p.X - _dragLast.X; var dy = p.Y - _dragLast.Y;
                foreach (var l in SelectedLayers.Where(l => !l.Locked)) l.Translate(dx, dy);
                _dragLast = p;
                NotifyDocumentChanged();
                break;
            }
            case DragState.Resize:
                ResizeSelected(p, shift);
                NotifyDocumentChanged();
                break;
            case DragState.Create when _creating is not null:
                UpdateCreating(_creating, _dragStart, p, shift);
                NotifyDocumentChanged();
                break;
            case DragState.Crop:
                _crop = RectD.FromPoints(_dragStart, p).Intersect(new RectD(0, 0, Page.Width, Page.Height));
                InvalidateVisual();
                break;
            default:
                UpdateHoverCursor(pos);
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        var state = _drag; _drag = DragState.None;
        EndDragCache();
        switch (state)
        {
            case DragState.Move:
            case DragState.Resize:
                History.Commit();
                break;
            case DragState.Create when _creating is not null:
                var c = _creating; _creating = null;
                if (!FinishCreating(c)) { History.Rollback(); Select(Array.Empty<Guid>()); }
                else
                {
                    LayerCreated?.Invoke(this, c);
                    History.Commit();
                    if (c is TextLayer t) TextEditRequested?.Invoke(this, t);
                }
                break;
        }
        PageOperations.RenumberSteps(Page);
        NotifyDocumentChanged();
    }

    private void UpdateHoverCursor(Point pos)
    {
        if (Tool != Tool.Select) return;
        if (Selection.Count == 1 && SelectedLayers.FirstOrDefault() is { Locked: false } sel && HitHandle(sel, pos) >= 0)
        { Cursor = new Cursor(StandardCursorType.SizeAll); return; }
        var hit = ViewPage().HitTest(ToPage(pos), 4 / ViewScale);
        Cursor = new Cursor(hit is null ? StandardCursorType.Arrow : StandardCursorType.Hand);
    }

    private int HitHandle(Layer l, Point viewPos)
    {
        var pts = HandlePoints(l);
        for (var i = 0; i < pts.Count; i++)
            if (Math.Abs(pts[i].X - viewPos.X) <= HandleSize && Math.Abs(pts[i].Y - viewPos.Y) <= HandleSize) return i;
        return -1;
    }

    private void ResizeSelected(PointD p, bool keepAspect)
    {
        var l = SelectedLayers.FirstOrDefault();
        if (l is null) return;
        if (l is LineLayer ln)
        {
            var q = keepAspect ? SnapAngle(_resizeHandle == 0 ? ln.End : ln.Start, p) : p;
            if (_resizeHandle == 0) ln.Start = q; else ln.End = q;
            return;
        }
        var o = _resizeOrigin;
        double x0 = o.X, y0 = o.Y, x1 = o.Right, y1 = o.Bottom;
        switch (_resizeHandle)
        {
            case 0: x0 = p.X; y0 = p.Y; break;
            case 1: y0 = p.Y; break;
            case 2: x1 = p.X; y0 = p.Y; break;
            case 3: x1 = p.X; break;
            case 4: x1 = p.X; y1 = p.Y; break;
            case 5: y1 = p.Y; break;
            case 6: x0 = p.X; y1 = p.Y; break;
            case 7: x0 = p.X; break;
        }
        var r = RectD.FromPoints(new PointD(x0, y0), new PointD(x1, y1));
        if (keepAspect && o.Width > 0 && o.Height > 0)
        {
            var f = Math.Max(r.Width / o.Width, r.Height / o.Height);
            r = r with { Width = o.Width * f, Height = o.Height * f };
        }
        if (r.Width < 4) r = r with { Width = 4 };
        if (r.Height < 4) r = r with { Height = 4 };
        l.SetBounds(r);
    }

    private static PointD SnapAngle(PointD origin, PointD p)
    {
        var dx = p.X - origin.X; var dy = p.Y - origin.Y;
        var ang = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
        var len = Math.Sqrt(dx * dx + dy * dy);
        return new PointD(origin.X + Math.Cos(ang) * len, origin.Y + Math.Sin(ang) * len);
    }

    // ---------------------------------------------------------------- creation

    public static string ToolName(Tool t) => t switch
    {
        Tool.Arrow => "Pfeil", Tool.Line => "Linie", Tool.Rectangle => "Rechteck", Tool.Ellipse => "Ellipse",
        Tool.Text => "Text", Tool.InfoBox => "Infobox", Tool.Bubble => "Sprechblase", Tool.Step => "Schritt",
        Tool.Highlight => "Hervorhebung", Tool.Blackout => "Schwärzung", Tool.Pixelate => "Verpixeln", Tool.Blur => "Weichzeichnen",
        Tool.Spotlight => "Spotlight", Tool.Magnifier => "Lupe", Tool.Freehand => "Stift", Tool.Marker => "Textmarker", Tool.Crop => "Zuschneiden", Tool.Pipette => "Farbpipette", _ => "Auswahl",
    };

    private Layer CreateLayer(Tool tool, PointD p)
    {
        var s = Style;
        var r0 = new RectD(p.X, p.Y, 0, 0);
        return tool switch
        {
            Tool.Arrow => new LineLayer { Start = p, End = p, Stroke = s.Color, StrokeWidth = s.StrokeWidth + 1, EndCap = LineCap.Arrow, Shadow = s.Shadow },
            Tool.Line => new LineLayer { Start = p, End = p, Stroke = s.Color, StrokeWidth = s.StrokeWidth, EndCap = LineCap.None, Shadow = s.Shadow },
            Tool.Rectangle => new ShapeLayer { Rect = r0, Stroke = s.Color, StrokeWidth = s.StrokeWidth, Shadow = s.Shadow },
            Tool.Ellipse => new ShapeLayer { Shape = ShapeKind.Ellipse, Rect = r0, Stroke = s.Color, StrokeWidth = s.StrokeWidth, Shadow = s.Shadow },
            Tool.Text => new TextLayer { Rect = r0, Text = "Text", FontFamily = s.FontFamily, FontSize = s.FontSize, Foreground = s.Color, Padding = 4 },
            Tool.InfoBox => new TextLayer { Rect = r0, Text = "Hinweis", Style = TextStyle.InfoBox, FontFamily = s.FontFamily, FontSize = Math.Max(14, s.FontSize * 0.75), Foreground = new ColorRgba(30, 30, 30), Background = new ColorRgba(255, 250, 225), Border = s.Color, BorderWidth = 2 },
            Tool.Bubble => new TextLayer { Rect = r0, Text = "Text", Style = TextStyle.Bubble, FontFamily = s.FontFamily, FontSize = Math.Max(14, s.FontSize * 0.75), Foreground = new ColorRgba(30, 30, 30), Background = ColorRgba.White, Border = s.Color, BorderWidth = 2 },
            Tool.Step => new StepLayer { Center = p, Radius = s.StepRadius, Fill = s.Color, Shadow = s.Shadow, Number = PageOperations.NextStepNumber(Page) },
            Tool.Highlight => new HighlightLayer { Rect = r0 },
            Tool.Blackout => new RedactLayer { Rect = r0, Mode = RedactMode.Blackout },
            Tool.Pixelate => new RedactLayer { Rect = r0, Mode = RedactMode.Pixelate, Strength = 12 },
            Tool.Blur => new RedactLayer { Rect = r0, Mode = RedactMode.Blur, Strength = 10 },
            Tool.Spotlight => new SpotlightLayer { Rect = r0 },
            Tool.Magnifier => new MagnifierLayer { Rect = r0, Source = r0, Border = s.Color },
            Tool.Freehand => new FreehandLayer { Points = { p }, Stroke = s.Color, StrokeWidth = s.StrokeWidth },
            Tool.Marker => new FreehandLayer { Points = { p }, Stroke = s.Color.WithAlpha(110), StrokeWidth = Math.Max(14, s.StrokeWidth * 4), Highlighter = true },
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };
    }

    private void UpdateCreating(Layer l, PointD a, PointD b, bool shift)
    {
        switch (l)
        {
            case LineLayer ln: ln.End = shift ? SnapAngle(a, b) : b; break;
            case StepLayer st: st.Center = b; break;
            case FreehandLayer fh:
                if (fh.Points.Count == 0 || fh.Points[^1].DistanceTo(b) > 1.5 / ViewScale) fh.Points.Add(b);
                break;
            case MagnifierLayer mg: mg.Source = RectD.FromPoints(a, b); mg.Rect = mg.Source; break;
            case BoxLayer bx:
                var r = RectD.FromPoints(a, b);
                if (shift) { var m = Math.Max(r.Width, r.Height); r = new RectD(b.X < a.X ? a.X - m : a.X, b.Y < a.Y ? a.Y - m : a.Y, m, m); }
                bx.Rect = r;
                break;
        }
    }

    /// <returns>false if the new layer is too small and should be discarded.</returns>
    private bool FinishCreating(Layer l)
    {
        switch (l)
        {
            case StepLayer: return true;
            case LineLayer ln: return ln.Start.DistanceTo(ln.End) >= 4;
            case FreehandLayer fh: return fh.Points.Count >= 2;
            case TextLayer t:
                var r = t.Rect.Normalize();
                if (r.Width < 20) t.Rect = new RectD(r.X, r.Y, t.Style == TextStyle.Plain ? 0 : 260, 0);
                if (t.Style == TextStyle.Bubble) t.TailTip = new PointD(t.Rect.X + 30, t.Rect.Y + 110);
                t.Rect = TextLayout.FitBox(t);
                return true;
            case MagnifierLayer mg:
                var src = mg.Source.Normalize();
                if (src.Width < 6 || src.Height < 6) return false;
                var w = src.Width * 2; var h = src.Height * 2;
                var x = src.Right + 20; if (x + w > Page.Width) x = Math.Max(0, src.X - w - 20);
                var y = Math.Clamp(src.Y - h / 4, 0, Math.Max(0, Page.Height - h));
                mg.Source = src; mg.Rect = new RectD(x, y, w, h);
                return true;
            case BoxLayer bx:
                var b = bx.Rect.Normalize();
                return b.Width >= 3 && b.Height >= 3;
        }
        return true;
    }

    // ---------------------------------------------------------------- commands

    public bool HasCrop => _crop is { IsEmpty: false };

    public void ApplyCrop()
    {
        if (_crop is not { } c || c.IsEmpty) return;
        Apply("Zuschneiden", () => PageOperations.Crop(Page, c));
        _crop = null;
        Tool = Tool.Select;
        InvalidateMeasure();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CancelCrop() { _crop = null; InvalidateVisual(); }

    public void DeleteSelection()
    {
        var ids = Selection.Where(id => Page.Find(id) is { Locked: false } and not ImageLayer { IsBackground: true }).ToList();
        if (ids.Count == 0) return;
        Apply(ids.Count == 1 ? "Ebene löschen" : $"{ids.Count} Ebenen löschen", () => { foreach (var id in ids) PageOperations.Remove(Page, id); });
        Select(Array.Empty<Guid>());
    }

    public void Nudge(double dx, double dy)
    {
        var layers = SelectedLayers.Where(l => !l.Locked).ToList();
        if (layers.Count == 0) return;
        Apply("Verschieben", () => { foreach (var l in layers) l.Translate(dx, dy); }, "nudge");
    }

    public void DuplicateSelection()
    {
        var copies = new List<Guid>();
        Apply("Duplizieren", () =>
        {
            foreach (var id in Selection.ToList())
                if (PageOperations.Duplicate(Page, id) is { } c) copies.Add(c.Id);
        });
        Select(copies);
    }

    public void GroupSelection()
    {
        GroupLayer? g = null;
        Apply("Gruppieren", () => g = PageOperations.Group(Page, Selection.ToList()));
        if (g is not null) Select(new[] { g.Id });
    }

    public void UngroupSelection()
    {
        var kids = new List<Guid>();
        Apply("Gruppe auflösen", () =>
        {
            foreach (var id in Selection.ToList())
                kids.AddRange(PageOperations.Ungroup(Page, id).Select(l => l.Id));
        });
        if (kids.Count > 0) Select(kids);
    }

    public void MoveSelection(int delta)
    {
        var ids = Selection.ToList();
        Apply(delta > 0 ? "Nach vorne" : "Nach hinten", () =>
        {
            foreach (var id in delta > 0 ? ids.AsEnumerable().Reverse() : ids)
            {
                if (delta == int.MaxValue) PageOperations.BringToFront(Page, id);
                else if (delta == int.MinValue) PageOperations.SendToBack(Page, id);
                else PageOperations.Move(Page, id, delta);
            }
        });
    }

    public void Undo() { if (History.Undo()) { PageOperations.RenumberSteps(Page); NotifyDocumentChanged(); InvalidateMeasure(); } }
    public void Redo() { if (History.Redo()) { PageOperations.RenumberSteps(Page); NotifyDocumentChanged(); InvalidateMeasure(); } }

    /// <summary>Flattened page at full resolution (what export and clipboard get).</summary>
    public SKImage RenderFlattened() => Session.Renderer.RenderPage(Page);

    /// <summary>Main colour of a layer (marker keeps its transparency).</summary>
    public static void SetMainColor(Layer l, ColorRgba c)
    {
        switch (l)
        {
            case ShapeLayer s: s.Stroke = c; break;
            case LineLayer ln: ln.Stroke = c; break;
            case StepLayer st: st.Fill = c; break;
            case TextLayer t when t.Style == TextStyle.Plain: t.Foreground = c; break;
            case TextLayer t: t.Border = c; break;
            case FreehandLayer f: f.Stroke = f.Highlighter ? c.WithAlpha(f.Stroke.A) : c; break;
            case HighlightLayer h: h.Color = c; break;
            case MagnifierLayer m: m.Border = c; break;
        }
    }
}
