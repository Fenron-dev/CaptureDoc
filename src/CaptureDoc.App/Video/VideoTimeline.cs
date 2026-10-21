using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CaptureDoc.App.Video;

/// <summary>One annotation track (top layer first).</summary>
public sealed record TimelineLane(Guid Id, string Name, double Start, double End, bool IsEffect, bool Selected);

/// <summary>A video track (index 0 = main track, drawn lowest).</summary>
public sealed record TimelineTrack(Guid Id, string Name, bool Hidden, bool Muted);

/// <summary>A clip on a video track.</summary>
public sealed record TimelineClip(Guid Id, int Track, string Name, double Start, double Duration, double SourceIn, string? Source, bool IsSlide, Color SlideColor, bool Selected, bool HasAudio);

/// <summary>
/// Multi-track timeline like a video editor: ruler (drag = select a range), video tracks with clips
/// (drag = move, also onto another track; edges = trim), zoom keyframes and one track per annotation.
/// Ctrl+wheel zooms the time axis, shift+wheel scrolls.
/// </summary>
public sealed class VideoTimeline : Control
{
    public const double HeaderW = 132, RulerH = 22, TrackH = 46, ZoomH = 20, LaneH = 20, Gap = 2;

    public double Duration { get; set; } = 1;
    public double Position { get; set; }
    public (double A, double B)? Selection { get; set; }
    public IReadOnlyList<double> ZoomKeys { get; set; } = Array.Empty<double>();
    public int? SelectedZoomKey { get; set; }
    /// <summary>Thumbnail of a source at a source time (null = none yet).</summary>
    public Func<string, double, Bitmap?>? ThumbProvider { get; set; }

    /// <summary>Slide editing: one coloured bar instead of the video tracks, no zoom track.</summary>
    public Color? SlideMode { get; set; }

    private IReadOnlyList<TimelineTrack> _tracks = new[] { new TimelineTrack(Guid.Empty, "Video 1", false, false) };
    public IReadOnlyList<TimelineTrack> Tracks { get => _tracks; set { _tracks = value.Count > 0 ? value : _tracks; UpdateHeight(); InvalidateVisual(); } }
    public IReadOnlyList<TimelineClip> Clips { get; set; } = Array.Empty<TimelineClip>();
    private IReadOnlyList<TimelineLane> _lanes = Array.Empty<TimelineLane>();
    public IReadOnlyList<TimelineLane> Lanes { get => _lanes; set { _lanes = value; UpdateHeight(); InvalidateVisual(); } }

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
    /// <summary>Clip clicked (Guid.Empty = click on an empty track area).</summary>
    public event EventHandler<Guid>? ClipSelected;
    public event EventHandler<Guid>? ClipEditStarted;
    /// <summary>Live while dragging: new start and track index.</summary>
    public event EventHandler<(Guid Id, double Start, int Track)>? ClipMoved;
    /// <summary>Live while trimming: left (true) or right edge to sequence time.</summary>
    public event EventHandler<(Guid Id, bool Left, double Time)>? ClipTrimmed;
    public event EventHandler<Guid>? ClipEditCompleted;
    public event EventHandler<Guid>? ClipOpened;
    /// <summary>Track header icon clicked: track index, "hide" or "mute".</summary>
    public event EventHandler<(int Track, string What)>? TrackToggled;
    public event EventHandler? ViewChanged;

    public VideoTimeline() { ClipToBounds = true; Focusable = true; UpdateHeight(); }

    private bool Main => SlideMode is null;
    private int VisualTracks => Main ? _tracks.Count : 1;
    private double TracksTop => RulerH;
    private double ZoomTop => TracksTop + VisualTracks * TrackH;
    private double LanesTop => ZoomTop + (Main ? ZoomH : 0) + 4;

    private void UpdateHeight() => Height = LanesTop + Math.Max(1, _lanes.Count) * (LaneH + Gap) + 4;

    /// <summary>Top y of a track (track 0 = main, drawn at the bottom of the video area).</summary>
    public double TrackTop(int track) => TracksTop + (VisualTracks - 1 - track) * TrackH;
    public double TrackCenterY(int track) => TrackTop(track) + TrackH / 2;
    private int TrackAt(double y)
    {
        if (y < TracksTop || y >= ZoomTop) return -1;
        return VisualTracks - 1 - (int)((y - TracksTop) / TrackH);
    }

    // ---------------------------------------------------------------- mapping

    private double TrackW => Math.Max(1, Bounds.Width - HeaderW);
    private double Pps => TrackW / Math.Max(0.001, Duration) * _zoom;
    public double VisibleSeconds => TrackW / Pps;
    private double X(double t) => HeaderW + (t - _offset) * Pps;
    private double T(double x) => Math.Clamp(_offset + (x - HeaderW) / Pps, 0, Duration);
    private void ClampOffset() => _offset = Math.Clamp(_offset, 0, Math.Max(0, Duration - VisibleSeconds));

    public void EnsureVisible(double t)
    {
        if (_zoom <= 1.001) return;
        if (t < _offset || t > _offset + VisibleSeconds * 0.95) Offset = t - VisibleSeconds * 0.1;
    }

    public static string Fmt(double t) => $"{(int)(t / 60)}:{t % 60:00.0}".Replace(',', '.');

    public int LaneAt(double y) => y < LanesTop ? -1 : (int)((y - LanesTop) / (LaneH + Gap));
    public double LaneCenterY(int i) => LanesTop + i * (LaneH + Gap) + LaneH / 2;
    public double XOf(double t) => X(t);

    // ---------------------------------------------------------------- render

    private static readonly IBrush HeaderBg = new SolidColorBrush(Color.FromRgb(24, 24, 28));
    private static readonly IBrush TrackBg = new SolidColorBrush(Color.FromRgb(32, 32, 37));
    private static readonly IBrush TrackBgAlt = new SolidColorBrush(Color.FromRgb(37, 37, 43));
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
            if (Main) { DrawTracks(ctx, w); DrawZoomTrack(ctx, w); }
            else DrawSlideBar(ctx);
            DrawLanes(ctx, w);
            if (Selection is { } sel)
            {
                var a = Math.Min(sel.A, sel.B); var b = Math.Max(sel.A, sel.B);
                ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 255, 160, 0)), new Pen(Brushes.Orange, 1), new Rect(X(a), 0, Math.Max(1, X(b) - X(a)), ZoomTop));
            }
            if (_snapX is { } sx) ctx.DrawLine(new Pen(Brushes.Gold, 1, DashStyle.Dash), new Point(sx, 0), new Point(sx, h));
            var head = new Pen(Brushes.White, 2);
            ctx.DrawLine(head, new Point(X(Position), 0), new Point(X(Position), h));
            ctx.DrawGeometry(Brushes.White, null, new PolylineGeometry(new[] { new Point(X(Position) - 6, 0), new Point(X(Position) + 6, 0), new Point(X(Position), 8) }, true));
        }

        // headers
        ctx.DrawRectangle(HeaderBg, null, new Rect(0, 0, HeaderW, h));
        void Header(double y, double rowH, string name, bool selected = false, string? icons = null)
        {
            if (selected) ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(60, 255, 200, 0)), null, new Rect(0, y, HeaderW, rowH));
            var ft = Text(name, 11, selected ? Brushes.Gold : Brushes.Gainsboro);
            ft.MaxTextWidth = HeaderW - (icons is null ? 10 : 50); ft.MaxLineCount = 1; ft.Trimming = TextTrimming.CharacterEllipsis;
            ctx.DrawText(ft, new Point(6, y + (rowH - ft.Height) / 2));
            if (icons is not null)
            {
                var it = Text(icons, 13, Brushes.Gainsboro);
                ctx.DrawText(it, new Point(HeaderW - it.Width - 6, y + (rowH - it.Height) / 2));
            }
            ctx.DrawLine(RowLine, new Point(0, y + rowH), new Point(w, y + rowH));
        }
        Header(0, RulerH, Fmt(_offset) + (_zoom > 1.001 ? $"  ({_zoom:0.#}×)" : ""));
        if (Main)
        {
            for (var i = 0; i < _tracks.Count; i++)
            {
                var tr = _tracks[i];
                Header(TrackTop(i), TrackH, (i == 0 ? "🎬 " : "▤ ") + tr.Name, icons: (tr.Hidden ? "◌" : "👁") + " " + (tr.Muted ? "🔇" : "🔊"));
            }
            Header(ZoomTop, ZoomH, "🔍 Zoom");
        }
        else Header(TracksTop, TrackH, "▣ Folie");
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

    private void DrawSlideBar(DrawingContext ctx)
    {
        var bg = SlideMode!.Value;
        ctx.DrawRectangle(new SolidColorBrush(bg), null, new Rect(X(0), TracksTop + 2, X(Duration) - X(0), TrackH - 4), 4, 4);
        ctx.DrawText(Text("Folie – Elemente unten zeitlich einblenden", 11, bg.R + bg.G + bg.B > 380 ? Brushes.Black : Brushes.White), new Point(X(0) + 8, TracksTop + 6));
    }

    private void DrawTracks(DrawingContext ctx, double w)
    {
        for (var i = 0; i < _tracks.Count; i++)
        {
            var top = TrackTop(i);
            ctx.DrawRectangle(i % 2 == 0 ? TrackBg : TrackBgAlt, null, new Rect(HeaderW, top, w, TrackH));
            if (_tracks[i].Hidden) ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), null, new Rect(HeaderW, top, w, TrackH));
        }
        // end of the edit
        ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1, DashStyle.Dash), new Point(X(ContentEnd), TracksTop), new Point(X(ContentEnd), ZoomTop));
        foreach (var c in Clips)
        {
            if (c.Track < 0 || c.Track >= _tracks.Count) continue;
            var top = TrackTop(c.Track) + 2;
            var r = new Rect(X(c.Start), top, Math.Max(4, c.Duration * Pps), TrackH - 4);
            if (r.Right < HeaderW || r.X > w) continue;
            using (ctx.PushClip(r))
            {
                if (c.IsSlide) ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(110, 60, 190)), null, r);
                else
                {
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(20, 70, 120)), null, r);
                    if (ThumbProvider is { } tp && c.Source is { } src)
                    {
                        var thumb0 = tp(src, c.SourceIn);
                        var tw = thumb0 is null ? 60 : r.Height * thumb0.PixelSize.Width / Math.Max(1.0, thumb0.PixelSize.Height);
                        var x0 = Math.Max(r.X, HeaderW - tw);
                        for (var x = r.X; x < Math.Min(r.Right, w); x += tw)
                        {
                            if (x + tw < HeaderW) continue;
                            var st = c.SourceIn + (x + tw / 2 - r.X) / Pps;
                            if (tp(src, st) is { } b) ctx.DrawImage(b, new Rect(0, 0, b.PixelSize.Width, b.PixelSize.Height), new Rect(x, r.Y, tw, r.Height));
                        }
                        _ = x0;
                    }
                }
                ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, new Rect(r.X, r.Y, r.Width, 15));
                var name = Text((c.IsSlide ? "▣ " : "") + c.Name + $"  {c.Duration:0.0} s" + (c.HasAudio ? "  ♪" : ""), 10, Brushes.White);
                name.MaxTextWidth = Math.Max(1, r.Width - 8); name.MaxLineCount = 1; name.Trimming = TextTrimming.CharacterEllipsis;
                ctx.DrawText(name, new Point(r.X + 4, r.Y + 1));
                if (c.IsSlide) ctx.DrawRectangle(new SolidColorBrush(c.SlideColor), new Pen(Brushes.White, 1), new Rect(r.X + 4, r.Y + 18, 14, r.Height - 22));
            }
            ctx.DrawRectangle(null, c.Selected ? new Pen(Brushes.Gold, 2) : new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1), r, 3, 3);
            var grip = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
            if (c.Selected)
            {
                ctx.DrawRectangle(grip, null, new Rect(r.X + 1, r.Y + 16, 3, r.Height - 20));
                ctx.DrawRectangle(grip, null, new Rect(r.Right - 4, r.Y + 16, 3, r.Height - 20));
            }
        }
    }

    private double ContentEnd => Clips.Select(c => c.Start + c.Duration).DefaultIfEmpty(0).Max();

    private void DrawZoomTrack(DrawingContext ctx, double w)
    {
        var top = ZoomTop;
        ctx.DrawRectangle(TrackBgAlt, null, new Rect(HeaderW, top, w, ZoomH));
        var cy = top + ZoomH / 2;
        for (var i = 1; i < ZoomKeys.Count; i++)
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 160, 255)), 2), new Point(X(ZoomKeys[i - 1]), cy), new Point(X(ZoomKeys[i]), cy));
        for (var i = 0; i < ZoomKeys.Count; i++)
        {
            var x = X(ZoomKeys[i]); var r = SelectedZoomKey == i ? 7 : 5;
            var d = new PolylineGeometry(new[] { new Point(x, cy - r), new Point(x + r, cy), new Point(x, cy + r), new Point(x - r, cy) }, true);
            ctx.DrawGeometry(SelectedZoomKey == i ? Brushes.Gold : new SolidColorBrush(Color.FromRgb(0, 160, 255)), new Pen(Brushes.White, 1), d);
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

    private enum DragKind { None, Seek, Range, LaneMove, LaneStart, LaneEnd, ZoomKey, ClipMove, ClipLeft, ClipRight }
    private DragKind _drag;
    private TimelineLane? _dragLane;
    private TimelineClip? _dragClip;
    private int _dragKey = -1;
    private double _dragT0, _rangeA;
    private double? _snapX;

    /// <summary>Snaps a time to the playhead and clip edges within 7 px.</summary>
    private double Snap(double t, Guid? ignore = null)
    {
        _snapX = null;
        var candidates = Clips.Where(c => c.Id != ignore).SelectMany(c => new[] { c.Start, c.Start + c.Duration }).Append(Position).Append(0.0);
        var best = t; var bestD = 7.0;
        foreach (var c in candidates)
        {
            var d = Math.Abs(X(c) - X(t));
            if (d < bestD) { bestD = d; best = c; _snapX = X(c); }
        }
        return best;
    }

    private TimelineClip? ClipAt(Point p)
    {
        var tr = TrackAt(p.Y);
        if (tr < 0) return null;
        return Clips.LastOrDefault(c => c.Track == tr && p.X >= X(c.Start) && p.X <= X(c.Start + c.Duration));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this);
        if (p.X < HeaderW)
        {
            var hi = LaneAt(p.Y);
            if (hi >= 0 && hi < _lanes.Count) { LaneSelected?.Invoke(this, _lanes[hi].Id); return; }
            var tr = Main ? TrackAt(p.Y) : -1;
            if (tr >= 0 && p.X > HeaderW - 46) TrackToggled?.Invoke(this, (tr, p.X < HeaderW - 24 ? "hide" : "mute"));
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
        if (Main && p.Y >= TracksTop && p.Y < ZoomTop)
        {
            if (ClipAt(p) is { } c)
            {
                ClipSelected?.Invoke(this, c.Id);
                if (e.ClickCount >= 2) { ClipOpened?.Invoke(this, c.Id); _drag = DragKind.None; return; }
                var x0 = X(c.Start); var x1 = X(c.Start + c.Duration);
                _drag = Math.Abs(p.X - x0) <= 6 ? DragKind.ClipLeft : Math.Abs(p.X - x1) <= 6 ? DragKind.ClipRight : DragKind.ClipMove;
                _dragClip = c;
                ClipEditStarted?.Invoke(this, c.Id);
                return;
            }
            ClipSelected?.Invoke(this, Guid.Empty);
        }
        if (Main && p.Y >= ZoomTop && p.Y < ZoomTop + ZoomH)
        {
            for (var i = 0; i < ZoomKeys.Count; i++)
                if (Math.Abs(X(ZoomKeys[i]) - p.X) <= 7) { _drag = DragKind.ZoomKey; _dragKey = i; ZoomKeySelected?.Invoke(this, i); return; }
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
            case DragKind.Range: Selection = (_rangeA, t); InvalidateVisual(); return;
            case DragKind.ZoomKey: ZoomKeyMoved?.Invoke(this, (_dragKey, Math.Round(Snap(t), 2))); return;
            case DragKind.ClipMove when _dragClip is { } c:
            {
                var ns = Math.Max(0, c.Start + (t - _dragT0));
                // snap the start or the end of the clip
                var snappedStart = Snap(ns, c.Id);
                if (_snapX is null) { var se = Snap(ns + c.Duration, c.Id); if (_snapX is not null) snappedStart = se - c.Duration; }
                var tr = TrackAt(p.Y);
                if (tr < 0) tr = p.Y < TracksTop ? _tracks.Count - 1 : 0;
                ClipMoved?.Invoke(this, (c.Id, Math.Round(Math.Max(0, snappedStart), 3), tr));
                InvalidateVisual();
                return;
            }
            case DragKind.ClipLeft when _dragClip is { } c: ClipTrimmed?.Invoke(this, (c.Id, true, Math.Round(Snap(t, c.Id), 3))); InvalidateVisual(); return;
            case DragKind.ClipRight when _dragClip is { } c: ClipTrimmed?.Invoke(this, (c.Id, false, Math.Round(Snap(t, c.Id), 3))); InvalidateVisual(); return;
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
        if (Main && p.X > HeaderW && ClipAt(p) is { } hc)
        {
            var x0 = X(hc.Start); var x1 = X(hc.Start + hc.Duration);
            Cursor = new Cursor(Math.Abs(p.X - x0) <= 6 || Math.Abs(p.X - x1) <= 6 ? StandardCursorType.SizeWestEast : StandardCursorType.SizeAll);
            return;
        }
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
        var a = Snap(ns);
        if (_snapX is not null) ns = a;
        else { var b = Snap(ns + len); if (_snapX is not null) ns = b - len; }
        ns = Math.Clamp(ns, 0, Math.Max(0, Duration - len));
        return (ns, ns + len);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var kind = _drag; var lane = _dragLane; var clip = _dragClip;
        _drag = DragKind.None; _dragLane = null; _dragClip = null; _dragKey = -1; _snapX = null;
        e.Pointer.Capture(null);
        if (kind == DragKind.Range)
        {
            if (Selection is { } s && Math.Abs(X(s.B) - X(s.A)) >= 4) SelectionChanged?.Invoke(this, (Math.Min(s.A, s.B), Math.Max(s.A, s.B)));
            else { Selection = null; SelectionChanged?.Invoke(this, null); Seek?.Invoke(this, T(e.GetPosition(this).X)); }
        }
        if (lane is not null) LaneEditCompleted?.Invoke(this, lane.Id);
        if (clip is not null) ClipEditCompleted?.Invoke(this, clip.Id);
        InvalidateVisual();
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
