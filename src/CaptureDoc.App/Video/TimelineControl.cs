using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CaptureDoc.Media;

namespace CaptureDoc.App.Video;

/// <summary>One annotation track below the thumbnails (top layer first).</summary>
public sealed record TimelineLane(Guid Id, string Name, double Start, double End, bool IsEffect, bool Selected);

/// <summary>
/// Thumbnail strip with in/out range, cut-outs and playhead. Click/drag = seek.
/// Below it one track per annotation: drag the bar to move it in time, drag its edges to change start/end.
/// </summary>
public sealed class TimelineControl : Control
{
    public const double StripBottom = 78, LaneH = 18, LaneGap = 2;
    private IReadOnlyList<TimelineLane> _lanes = Array.Empty<TimelineLane>();
    public IReadOnlyList<TimelineLane> Lanes
    {
        get => _lanes;
        set { _lanes = value; Height = StripBottom + (value.Count > 0 ? 4 + value.Count * (LaneH + LaneGap) : 0); InvalidateVisual(); }
    }
    /// <summary>Live while dragging a track: id, new start, new end (seconds).</summary>
    public event EventHandler<(Guid Id, double Start, double End)>? LaneEdited;
    /// <summary>Track clicked or drag finished.</summary>
    public event EventHandler<Guid>? LaneSelected;
    private enum LaneDrag { None, Move, Start, End }
    private LaneDrag _laneDrag;
    private TimelineLane? _dragLane;
    private double _dragT0;

    public double Duration { get; set; } = 1;
    public double Position { get; set; }
    public ClipRange Range { get; set; } = new();
    /// <summary>Start of a cut that is being marked (end = playhead).</summary>
    public double? PendingCut { get; set; }
    public IReadOnlyList<Bitmap> Thumbs { get; set; } = Array.Empty<Bitmap>();
    public event EventHandler<double>? Seek;
    private bool _drag;

    public TimelineControl() { Height = 78; ClipToBounds = true; Focusable = true; Cursor = new Cursor(StandardCursorType.Hand); }

    private double X(double t) => Bounds.Width * Math.Clamp(t / Math.Max(0.001, Duration), 0, 1);
    private double T(double x) => Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1) * Duration;

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width; var stripTop = 18.0; var stripH = Bounds.Height - stripTop;
        ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 30, 34)), null, new Rect(0, 0, w, Bounds.Height));
        if (Thumbs.Count > 0)
        {
            var tw = stripH * Thumbs[0].PixelSize.Width / Math.Max(1.0, Thumbs[0].PixelSize.Height);
            var count = Math.Max(1, (int)Math.Ceiling(w / tw));
            for (var i = 0; i < count; i++)
            {
                var idx = Math.Min(Thumbs.Count - 1, (int)(i / (double)count * Thumbs.Count));
                var b = Thumbs[idx];
                ctx.DrawImage(b, new Rect(0, 0, b.PixelSize.Width, b.PixelSize.Height), new Rect(i * tw, stripTop, tw, stripH));
            }
        }
        var dim = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0));
        ctx.DrawRectangle(dim, null, new Rect(0, stripTop, X(Range.Start), stripH));
        ctx.DrawRectangle(dim, null, new Rect(X(Range.End), stripTop, w - X(Range.End), stripH));
        var cutBrush = new SolidColorBrush(Color.FromArgb(150, 220, 40, 40));
        foreach (var (a, b) in Range.Cuts)
            ctx.DrawRectangle(cutBrush, null, new Rect(X(a), stripTop, Math.Max(2, X(b) - X(a)), stripH));
        if (PendingCut is { } pc)
        {
            var a = Math.Min(pc, Position); var b = Math.Max(pc, Position);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(90, 255, 140, 0)), new Pen(Brushes.Orange, 1, DashStyle.Dash), new Rect(X(a), stripTop, Math.Max(2, X(b) - X(a)), stripH));
        }
        foreach (var (a, b) in Range.Cuts)
        {
            var lbl = new FormattedText("✂ entfernt", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10, Brushes.White);
            if (X(b) - X(a) > lbl.Width + 4) ctx.DrawText(lbl, new Point(X(a) + 2, stripTop + 2));
        }
        var green = new Pen(new SolidColorBrush(Color.FromRgb(60, 200, 100)), 3);
        ctx.DrawLine(green, new Point(X(Range.Start), stripTop), new Point(X(Range.Start), Bounds.Height));
        ctx.DrawLine(green, new Point(X(Range.End), stripTop), new Point(X(Range.End), Bounds.Height));

        // time ruler
        var step = Duration > 600 ? 60 : Duration > 120 ? 10 : Duration > 30 ? 5 : 1;
        var tick = new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1);
        for (double t = 0; t <= Duration; t += step)
        {
            ctx.DrawLine(tick, new Point(X(t), 12), new Point(X(t), 18));
            if (t % (step * 5) == 0 || step >= 10)
            {
                var ft = new FormattedText(Fmt(t), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 10, Brushes.Gray);
                ctx.DrawText(ft, new Point(X(t) + 2, 0));
            }
        }
        // annotation tracks
        var top = StripBottom + 4;
        foreach (var l in _lanes)
        {
            var x0 = X(l.Start); var x1 = X(Math.Min(Duration, l.End));
            var bar = new Rect(x0, top, Math.Max(4, x1 - x0), LaneH);
            var fill = l.IsEffect ? Color.FromArgb(220, 70, 70, 76) : Color.FromArgb(220, 0, 110, 200);
            ctx.DrawRectangle(new SolidColorBrush(fill), l.Selected ? new Pen(Brushes.Gold, 2) : new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1), bar, 3, 3);
            // edge grips
            var grip = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
            ctx.DrawRectangle(grip, null, new Rect(bar.X, bar.Y + 4, 3, LaneH - 8));
            ctx.DrawRectangle(grip, null, new Rect(bar.Right - 3, bar.Y + 4, 3, LaneH - 8));
            var label = new FormattedText((l.IsEffect ? "■ " : "") + l.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.White);
            using (ctx.PushClip(new Rect(bar.X + 5, bar.Y, Math.Max(0, bar.Width - 10), LaneH)))
                ctx.DrawText(label, new Point(bar.X + 6, bar.Y + 2));
            top += LaneH + LaneGap;
        }

        var head = new Pen(Brushes.White, 2);
        ctx.DrawLine(head, new Point(X(Position), 0), new Point(X(Position), Bounds.Height));
    }

    public static string Fmt(double t) => $"{(int)(t / 60)}:{t % 60:00.0}".Replace(',', '.');

    /// <summary>Track index at a y position, or -1.</summary>
    public static int LaneAt(double y) => y < StripBottom + 4 ? -1 : (int)((y - StripBottom - 4) / (LaneH + LaneGap));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this);
        e.Pointer.Capture(this);
        var i = LaneAt(p.Y);
        if (i >= 0 && i < _lanes.Count)
        {
            var l = _lanes[i];
            var x0 = X(l.Start); var x1 = X(Math.Min(Duration, l.End));
            _laneDrag = Math.Abs(p.X - x0) <= 6 ? LaneDrag.Start : Math.Abs(p.X - x1) <= 6 ? LaneDrag.End : p.X > x0 && p.X < x1 ? LaneDrag.Move : LaneDrag.None;
            _dragLane = l; _dragT0 = T(p.X);
            LaneSelected?.Invoke(this, l.Id);
            if (_laneDrag == LaneDrag.None) { _drag = true; Seek?.Invoke(this, T(p.X)); }
            return;
        }
        _drag = true;
        Seek?.Invoke(this, T(p.X));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_laneDrag != LaneDrag.None && _dragLane is { } l)
        {
            var t = T(p.X);
            const double min = 0.1;
            var end = Math.Min(Duration, l.End);
            (double s, double en) r = _laneDrag switch
            {
                LaneDrag.Start => (Math.Clamp(t, 0, end - min), end),
                LaneDrag.End => (l.Start, Math.Clamp(t, l.Start + min, Duration)),
                _ => Shift(l.Start, end, t - _dragT0),
            };
            LaneEdited?.Invoke(this, (l.Id, Math.Round(r.s, 2), Math.Round(r.en, 2)));
            return;
        }
        if (_drag) { Seek?.Invoke(this, T(p.X)); return; }
        // cursor feedback over tracks
        var i = LaneAt(p.Y);
        if (i >= 0 && i < _lanes.Count)
        {
            var ln = _lanes[i]; var x0 = X(ln.Start); var x1 = X(Math.Min(Duration, ln.End));
            Cursor = new Cursor(Math.Abs(p.X - x0) <= 6 || Math.Abs(p.X - x1) <= 6 ? StandardCursorType.SizeWestEast : p.X > x0 && p.X < x1 ? StandardCursorType.SizeAll : StandardCursorType.Hand);
        }
        else Cursor = new Cursor(StandardCursorType.Hand);
    }

    private (double, double) Shift(double s, double e, double dt)
    {
        var len = e - s;
        var ns = Math.Clamp(s + dt, 0, Math.Max(0, Duration - len));
        return (ns, ns + len);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var wasLane = _laneDrag != LaneDrag.None ? _dragLane : null;
        _drag = false; _laneDrag = LaneDrag.None; _dragLane = null;
        e.Pointer.Capture(null);
        if (wasLane is not null) LaneSelected?.Invoke(this, wasLane.Id);
    }
}
