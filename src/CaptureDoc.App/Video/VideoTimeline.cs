using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CaptureDoc.Media;

namespace CaptureDoc.App.Video;

/// <summary>One annotation track (top layer first).</summary>
public sealed record TimelineLane(Guid Id, string Name, double Start, double End, bool IsEffect, bool Selected);

/// <summary>An inserted slide, shown at its insertion point.</summary>
public sealed record TimelineSlide(Guid Id, string Name, double At, double Duration, Color Background, bool Selected);

/// <summary>
/// Multi-track timeline: ruler (drag = select a range), video track with thumbnails, trim and cuts,
/// zoom keyframes, slides and one track per annotation (drag the bar = move, drag an edge = start/end).
/// Ctrl+wheel zooms the time axis, wheel/shift+wheel scrolls when zoomed in.
/// </summary>
public sealed class VideoTimeline : Control
{
    public const double HeaderW = 124, RulerH = 22, VideoH = 54, ZoomH = 20, SlideH = 24, LaneH = 20, Gap = 2;

    public double Duration { get; set; } = 1;
    public double Position { get; set; }
    public ClipRange Range { get; set; } = new();
    public double? PendingCut { get; set; }
    /// <summary>Range selected on the ruler (seconds) or null.</summary>
    public (double A, double B)? Selection { get; set; }
    public IReadOnlyList<Bitmap> Thumbs { get; set; } = Array.Empty<Bitmap>();
    public double ThumbFps { get; set; } = 1;
    public IReadOnlyList<double> ZoomKeys { get; set; } = Array.Empty<double>();
    public int? SelectedZoomKey { get; set; }

    /// <summary>Slide editing: the video track shows the slide colour instead of thumbnails, no zoom/slide tracks.</summary>
    public Color? SlideMode { get; set; }

    private IReadOnlyList<TimelineLane> _lanes = Array.Empty<TimelineLane>();
    public IReadOnlyList<TimelineLane> Lanes { get => _lanes; set { _lanes = value; UpdateHeight(); InvalidateVisual(); } }
    private IReadOnlyList<TimelineSlide> _slides = Array.Empty<TimelineSlide>();
    public IReadOnlyList<TimelineSlide> Slides { get => _slides; set { _slides = value; InvalidateVisual(); } }

    // ---- view: 1 = whole duration fits; Offset = first visible second
    private double _zoom = 1, _offset;
    public double ZoomFactor { get => _zoom; set { _zoom = Math.Clamp(value, 1, 400); ClampOffset(); InvalidateVisual(); ViewChanged?.Invoke(this, EventArgs.Empty); } }
    public double Offset { get => _offset; set { _offset = value; ClampOffset(); InvalidateVisual(); ViewChanged?.Invoke(this, EventArgs.Empty); } }

    public event EventHandler<double>? Seek;
    public event EventHandler<(double A, double B)?>? SelectionChanged;
    public event EventHandler<(Guid Id, double Start, double End)>? LaneEdited;
    public event EventHandler<Guid>? LaneEditStarted;
    public event EventHandler<Guid>? LaneEditCompleted;
    public event EventHandler<Guid>? LaneSelected;
    public event EventHandler<int>? ZoomKeySelected;
    public event EventHandler<(int Index, double Time)>? ZoomKeyMoved;
    public event EventHandler<Guid>? SlideSelected;
    public event EventHandler<(Guid Id, double At)>? SlideMoved;
    public event EventHandler<Guid>? SlideOpened;
    public event EventHandler? ViewChanged;

    public VideoTimeline()
    {
        ClipToBounds = true; Focusable = true;
        UpdateHeight();
    }

    private bool Main => SlideMode is null;
    private double ZoomTop => RulerH + VideoH;
    private double SlideTop => ZoomTop + (Main ? ZoomH : 0);
    private double LanesTop => SlideTop + (Main ? SlideH : 0) + 4;

    private void UpdateHeight() => Height = LanesTop + Math.Max(1, _lanes.Count) * (LaneH + Gap) + 4;

    // ---------------------------------------------------------------- mapping

    private double TrackW => Math.Max(1, Bounds.Width - HeaderW);
    private double Pps => TrackW / Math.Max(0.001, Duration) * _zoom;
    public double VisibleSeconds => TrackW / Pps;
    private double X(double t) => HeaderW + (t - _offset) * Pps;
    private double T(double x) => Math.Clamp(_offset + (x - HeaderW) / Pps, 0, Duration);

    private void ClampOffset() => _offset = Math.Clamp(_offset, 0, Math.Max(0, Duration - VisibleSeconds));

    /// <summary>Scrolls so that t is visible (used while playing).</summary>
    public void EnsureVisible(double t)
    {
        if (_zoom <= 1.001) return;
        if (t < _offset || t > _offset + VisibleSeconds * 0.95) Offset = t - VisibleSeconds * 0.1;
    }

    public static string Fmt(double t) => $"{(int)(t / 60)}:{t % 60:00.0}".Replace(',', '.');

    // ---------------------------------------------------------------- render

    private static readonly IBrush HeaderBg = new SolidColorBrush(Color.FromRgb(24, 24, 28));
    private static readonly IBrush TrackBg = new SolidColorBrush(Color.FromRgb(32, 32, 37));
    private static readonly IBrush TrackBgAlt = new SolidColorBrush(Color.FromRgb(36, 36, 42));
    private static readonly IPen RowLine = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);

    private static FormattedText Text(string s, double size, IBrush brush, bool bold = false) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal), size, brush);

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width; var h = Bounds.Height;
        ctx.DrawRectangle(TrackBg, null, new Rect(0, 0, w, h));

        using (ctx.PushClip(new Rect(HeaderW, 0, Math.Max(0, w - HeaderW), h)))
        {
            DrawRuler(ctx, w);
            DrawVideoTrack(ctx, w);
            if (Main) { DrawZoomTrack(ctx, w); DrawSlideTrack(ctx, w); }
            DrawLanes(ctx, w);
            if (Selection is { } sel)
            {
                var a = Math.Min(sel.A, sel.B); var b = Math.Max(sel.A, sel.B);
                ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 255, 160, 0)), new Pen(Brushes.Orange, 1), new Rect(X(a), 0, Math.Max(1, X(b) - X(a)), RulerH + VideoH));
            }
            var head = new Pen(Brushes.White, 2);
            ctx.DrawLine(head, new Point(X(Position), 0), new Point(X(Position), h));
            ctx.DrawGeometry(Brushes.White, null, new PolylineGeometry(new[] { new Point(X(Position) - 6, 0), new Point(X(Position) + 6, 0), new Point(X(Position), 8) }, true));
        }

        // headers
        ctx.DrawRectangle(HeaderBg, null, new Rect(0, 0, HeaderW, h));
        void Header(double y, double rowH, string name, bool selected = false)
        {
            if (selected) ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(60, 255, 200, 0)), null, new Rect(0, y, HeaderW, rowH));
            var ft = Text(name, 11, selected ? Brushes.Gold : Brushes.Gainsboro);
            ft.MaxTextWidth = HeaderW - 10; ft.MaxLineCount = 1; ft.Trimming = TextTrimming.CharacterEllipsis;
            ctx.DrawText(ft, new Point(6, y + (rowH - ft.Height) / 2));
            ctx.DrawLine(RowLine, new Point(0, y + rowH), new Point(w, y + rowH));
        }
        Header(0, RulerH, Fmt(_offset) + (_zoom > 1.001 ? $"  ({_zoom:0.#}×)" : ""));
        Header(RulerH, VideoH, Main ? "🎬 Video" : "▣ Folie");
        if (Main) { Header(ZoomTop, ZoomH, "🔍 Zoom"); Header(SlideTop, SlideH, "▣ Folien"); }
        var y0 = LanesTop;
        if (_lanes.Count == 0) Header(y0, LaneH, "(keine Annotationen)");
        foreach (var l in _lanes) { Header(y0, LaneH, (l.IsEffect ? "■ " : "") + l.Name, l.Selected); y0 += LaneH + Gap; }
        ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1), new Point(HeaderW, 0), new Point(HeaderW, h));
    }

    private void DrawRuler(DrawingContext ctx, double w)
    {
        ctx.DrawRectangle(HeaderBg, null, new Rect(HeaderW, 0, w, RulerH));
        var steps = new[] { 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600 };
        var major = steps.FirstOrDefault(s => s * Pps >= 70, 600);
        var minor = steps.LastOrDefault(s => s < major && s * Pps >= 8, major);
        var tick = new Pen(new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), 1);
        var t0 = Math.Floor(_offset / minor) * minor;
        for (var t = t0; t <= Math.Min(Duration, _offset + VisibleSeconds + minor); t += minor)
        {
            var isMajor = Math.Abs(t / major - Math.Round(t / major)) < 1e-6;
            ctx.DrawLine(tick, new Point(X(t), isMajor ? 10 : 16), new Point(X(t), RulerH));
            if (isMajor) ctx.DrawText(Text(Fmt(t), 10, Brushes.Gray), new Point(X(t) + 3, 0));
        }
    }

    private void DrawVideoTrack(DrawingContext ctx, double w)
    {
        var top = RulerH; var hgt = VideoH;
        if (SlideMode is { } bg)
        {
            ctx.DrawRectangle(new SolidColorBrush(bg), null, new Rect(X(0), top + 2, X(Duration) - X(0), hgt - 4), 4, 4);
            ctx.DrawText(Text("Folie – Elemente unten zeitlich einblenden", 11, bg.R + bg.G + bg.B > 380 ? Brushes.Black : Brushes.White), new Point(X(0) + 8, top + 6));
            return;
        }
        if (Thumbs.Count > 0)
        {
            var b0 = Thumbs[0];
            var tw = hgt * b0.PixelSize.Width / Math.Max(1.0, b0.PixelSize.Height);
            var first = (int)Math.Floor((0 - HeaderW) / tw);
            for (var x = HeaderW + Math.Floor((X(0) - HeaderW) / tw) * tw; x < w; x += tw)
            {
                if (x + tw < X(0) || x > X(Duration)) continue;
                var t = T(x + tw / 2);
                var idx = Math.Clamp((int)(t * ThumbFps), 0, Thumbs.Count - 1);
                var b = Thumbs[idx];
                ctx.DrawImage(b, new Rect(0, 0, b.PixelSize.Width, b.PixelSize.Height), new Rect(x, top, tw, hgt));
            }
            _ = first;
        }
        var dim = new SolidColorBrush(Color.FromArgb(175, 0, 0, 0));
        ctx.DrawRectangle(dim, null, new Rect(X(0), top, Math.Max(0, X(Range.Start) - X(0)), hgt));
        ctx.DrawRectangle(dim, null, new Rect(X(Range.End), top, Math.Max(0, X(Duration) - X(Range.End)), hgt));
        var cutBrush = new SolidColorBrush(Color.FromArgb(160, 220, 40, 40));
        foreach (var (a, b) in Range.Cuts)
        {
            var r = new Rect(X(a), top, Math.Max(2, X(b) - X(a)), hgt);
            ctx.DrawRectangle(cutBrush, new Pen(Brushes.Red, 1), r);
            var lbl = Text("✂ entfernt", 10, Brushes.White, bold: true);
            if (r.Width > lbl.Width + 4) ctx.DrawText(lbl, new Point(r.X + 3, top + 3));
        }
        if (PendingCut is { } pc)
        {
            var a = Math.Min(pc, Position); var b = Math.Max(pc, Position);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(90, 255, 140, 0)), new Pen(Brushes.Orange, 1, DashStyle.Dash), new Rect(X(a), top, Math.Max(2, X(b) - X(a)), hgt));
        }
        var green = new Pen(new SolidColorBrush(Color.FromRgb(60, 200, 100)), 3);
        ctx.DrawLine(green, new Point(X(Range.Start), top), new Point(X(Range.Start), top + hgt));
        ctx.DrawLine(green, new Point(X(Range.End), top), new Point(X(Range.End), top + hgt));
        // slide insertion marks on the video
        foreach (var s in _slides)
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(180, 120, 255)), 2, DashStyle.Dash), new Point(X(s.At), top), new Point(X(s.At), top + hgt));
    }

    private void DrawZoomTrack(DrawingContext ctx, double w)
    {
        var top = ZoomTop;
        ctx.DrawRectangle(TrackBgAlt, null, new Rect(HeaderW, top, w, ZoomH));
        var cy = top + ZoomH / 2;
        for (var i = 0; i < ZoomKeys.Count; i++)
        {
            var x = X(ZoomKeys[i]);
            if (i > 0) ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 160, 255)), 2), new Point(X(ZoomKeys[i - 1]), cy), new Point(x, cy));
        }
        for (var i = 0; i < ZoomKeys.Count; i++)
        {
            var x = X(ZoomKeys[i]); var r = SelectedZoomKey == i ? 7 : 5;
            var d = new PolylineGeometry(new[] { new Point(x, cy - r), new Point(x + r, cy), new Point(x, cy + r), new Point(x - r, cy) }, true);
            ctx.DrawGeometry(SelectedZoomKey == i ? Brushes.Gold : new SolidColorBrush(Color.FromRgb(0, 160, 255)), new Pen(Brushes.White, 1), d);
        }
    }

    private double SlideW(TimelineSlide s) => Math.Max(26, Math.Min(160, s.Duration * Pps));

    private void DrawSlideTrack(DrawingContext ctx, double w)
    {
        var top = SlideTop;
        ctx.DrawRectangle(TrackBg, null, new Rect(HeaderW, top, w, SlideH));
        foreach (var s in _slides)
        {
            var r = new Rect(X(s.At), top + 2, SlideW(s), SlideH - 4);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(120, 70, 200)), s.Selected ? new Pen(Brushes.Gold, 2) : new Pen(Brushes.White, 1), r, 4, 4);
            ctx.DrawRectangle(new SolidColorBrush(s.Background), new Pen(Brushes.White, 1), new Rect(r.X + 4, r.Y + 4, 12, r.Height - 8));
            var ft = Text($"{s.Name} · {s.Duration:0.#} s", 10, Brushes.White);
            ft.MaxTextWidth = Math.Max(1, r.Width - 22); ft.MaxLineCount = 1; ft.Trimming = TextTrimming.CharacterEllipsis;
            ctx.DrawText(ft, new Point(r.X + 20, r.Y + (r.Height - ft.Height) / 2));
        }
    }

    private void DrawLanes(DrawingContext ctx, double w)
    {
        var top = LanesTop;
        var i = 0;
        foreach (var l in _lanes)
        {
            ctx.DrawRectangle(i++ % 2 == 0 ? TrackBg : TrackBgAlt, null, new Rect(HeaderW, top, w, LaneH + Gap));
            var x0 = X(l.Start); var x1 = X(Math.Min(Duration, l.End));
            var bar = new Rect(x0, top + 1, Math.Max(6, x1 - x0), LaneH - 2);
            var fill = l.IsEffect ? Color.FromArgb(230, 85, 85, 92) : Color.FromArgb(230, 0, 110, 200);
            ctx.DrawRectangle(new SolidColorBrush(fill), l.Selected ? new Pen(Brushes.Gold, 2) : new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1), bar, 3, 3);
            var grip = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255));
            ctx.DrawRectangle(grip, null, new Rect(bar.X + 1, bar.Y + 4, 3, LaneH - 10));
            ctx.DrawRectangle(grip, null, new Rect(bar.Right - 4, bar.Y + 4, 3, LaneH - 10));
            var ft = Text(l.Name, 10.5, Brushes.White);
            ft.MaxTextWidth = Math.Max(1, bar.Width - 12); ft.MaxLineCount = 1; ft.Trimming = TextTrimming.CharacterEllipsis;
            ctx.DrawText(ft, new Point(bar.X + 6, bar.Y + (bar.Height - ft.Height) / 2));
            top += LaneH + Gap;
        }
    }

    // ---------------------------------------------------------------- input

    private enum DragKind { None, Seek, Range, LaneMove, LaneStart, LaneEnd, ZoomKey, Slide }
    private DragKind _drag;
    private TimelineLane? _dragLane;
    private TimelineSlide? _dragSlide;
    private int _dragKey = -1;
    private double _dragT0, _rangeA;

    /// <summary>Track index under y (lanes) or -1.</summary>
    public int LaneAt(double y) => y < LanesTop ? -1 : (int)((y - LanesTop) / (LaneH + Gap));

    /// <summary>Vertical centre of lane i (tests, hit helpers).</summary>
    public double LaneCenterY(int i) => LanesTop + i * (LaneH + Gap) + LaneH / 2;
    public double XOf(double t) => X(t);
    public double SlideTrackCenterY => SlideTop + SlideH / 2;
    public double VideoTrackCenterY => RulerH + VideoH / 2;

    private double Snap(double t)
    {
        // snap to the playhead within 6 px
        return Math.Abs(X(t) - X(Position)) <= 6 ? Position : t;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this);
        if (p.X < HeaderW)
        {
            var hi = LaneAt(p.Y);
            if (hi >= 0 && hi < _lanes.Count) LaneSelected?.Invoke(this, _lanes[hi].Id);
            return;
        }
        e.Pointer.Capture(this);
        var t = T(p.X);
        _dragT0 = t;
        if (p.Y < RulerH)
        {
            _drag = DragKind.Range; _rangeA = t;
            Selection = null; InvalidateVisual();
            return;
        }
        if (Main && p.Y >= ZoomTop && p.Y < ZoomTop + ZoomH)
        {
            for (var i = 0; i < ZoomKeys.Count; i++)
                if (Math.Abs(X(ZoomKeys[i]) - p.X) <= 7) { _drag = DragKind.ZoomKey; _dragKey = i; ZoomKeySelected?.Invoke(this, i); return; }
        }
        if (Main && p.Y >= SlideTop && p.Y < SlideTop + SlideH)
        {
            foreach (var s in _slides.Reverse())
                if (p.X >= X(s.At) && p.X <= X(s.At) + SlideW(s))
                {
                    if (e.ClickCount >= 2) { SlideOpened?.Invoke(this, s.Id); return; }
                    _drag = DragKind.Slide; _dragSlide = s;
                    SlideSelected?.Invoke(this, s.Id);
                    return;
                }
        }
        var li = LaneAt(p.Y);
        if (li >= 0 && li < _lanes.Count)
        {
            var l = _lanes[li];
            var x0 = X(l.Start); var x1 = X(Math.Min(Duration, l.End));
            _drag = Math.Abs(p.X - x0) <= 6 ? DragKind.LaneStart : Math.Abs(p.X - x1) <= 6 ? DragKind.LaneEnd : p.X > x0 && p.X < x1 ? DragKind.LaneMove : DragKind.Seek;
            LaneSelected?.Invoke(this, l.Id);
            if (_drag != DragKind.Seek) { _dragLane = l; LaneEditStarted?.Invoke(this, l.Id); return; }
        }
        _drag = DragKind.Seek;
        Seek?.Invoke(this, t);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        var t = T(p.X);
        switch (_drag)
        {
            case DragKind.Seek: Seek?.Invoke(this, t); return;
            case DragKind.Range:
                Selection = (_rangeA, t); InvalidateVisual(); return;
            case DragKind.ZoomKey: ZoomKeyMoved?.Invoke(this, (_dragKey, Math.Round(Snap(t), 2))); return;
            case DragKind.Slide when _dragSlide is { } s:
                SlideMoved?.Invoke(this, (s.Id, Math.Round(Math.Clamp(Snap(s.At + t - _dragT0), 0, Duration), 2))); return;
            case DragKind.LaneMove or DragKind.LaneStart or DragKind.LaneEnd when _dragLane is { } l:
            {
                const double min = 0.1;
                var end = Math.Min(Duration, l.End);
                (double s, double en) r = _drag switch
                {
                    DragKind.LaneStart => (Math.Clamp(Snap(t), 0, end - min), end),
                    DragKind.LaneEnd => (l.Start, Math.Clamp(Snap(t), l.Start + min, Duration)),
                    _ => Shift(l.Start, end, t - _dragT0),
                };
                LaneEdited?.Invoke(this, (l.Id, Math.Round(r.s, 2), Math.Round(r.en, 2)));
                return;
            }
        }
        // cursor feedback
        var li = LaneAt(p.Y);
        if (p.X > HeaderW && li >= 0 && li < _lanes.Count)
        {
            var ln = _lanes[li]; var x0 = X(ln.Start); var x1 = X(Math.Min(Duration, ln.End));
            Cursor = new Cursor(Math.Abs(p.X - x0) <= 6 || Math.Abs(p.X - x1) <= 6 ? StandardCursorType.SizeWestEast : p.X > x0 && p.X < x1 ? StandardCursorType.SizeAll : StandardCursorType.Hand);
        }
        else Cursor = new Cursor(p.Y < RulerH ? StandardCursorType.Ibeam : StandardCursorType.Hand);
    }

    private (double, double) Shift(double s, double e, double dt)
    {
        var len = e - s;
        var ns = Math.Clamp(s + dt, 0, Math.Max(0, Duration - len));
        // snap either edge to the playhead
        if (Math.Abs(X(ns) - X(Position)) <= 6) ns = Position;
        else if (Math.Abs(X(ns + len) - X(Position)) <= 6) ns = Position - len;
        ns = Math.Clamp(ns, 0, Math.Max(0, Duration - len));
        return (ns, ns + len);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var kind = _drag; var lane = _dragLane;
        _drag = DragKind.None; _dragLane = null; _dragSlide = null; _dragKey = -1;
        e.Pointer.Capture(null);
        if (kind == DragKind.Range)
        {
            if (Selection is { } s && Math.Abs(X(s.B) - X(s.A)) >= 4) SelectionChanged?.Invoke(this, (Math.Min(s.A, s.B), Math.Max(s.A, s.B)));
            else { Selection = null; SelectionChanged?.Invoke(this, null); Seek?.Invoke(this, T(e.GetPosition(this).X)); }
            InvalidateVisual();
        }
        if (lane is not null) LaneEditCompleted?.Invoke(this, lane.Id);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var anchor = T(p.X);
            var frac = (p.X - HeaderW) / TrackW;
            _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.25 : 0.8), 1, 400);
            _offset = anchor - frac * VisibleSeconds;
            ClampOffset();
            InvalidateVisual(); ViewChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        if (_zoom > 1.001 && (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || Math.Abs(e.Delta.X) > 0 || p.Y < LanesTop))
        {
            var d = Math.Abs(e.Delta.X) > 0 ? e.Delta.X : e.Delta.Y;
            Offset = _offset - d * VisibleSeconds * 0.1;
            e.Handled = true;
        }
    }
}
