using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CaptureDoc.Media;

namespace CaptureDoc.App.Video;

/// <summary>Thumbnail strip with in/out range, cut-outs and playhead. Click/drag = seek.</summary>
public sealed class TimelineControl : Control
{
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
        var head = new Pen(Brushes.White, 2);
        ctx.DrawLine(head, new Point(X(Position), 0), new Point(X(Position), Bounds.Height));
    }

    public static string Fmt(double t) => $"{(int)(t / 60)}:{t % 60:00.0}".Replace(',', '.');

    protected override void OnPointerPressed(PointerPressedEventArgs e) { _drag = true; e.Pointer.Capture(this); Seek?.Invoke(this, T(e.GetPosition(this).X)); }
    protected override void OnPointerMoved(PointerEventArgs e) { if (_drag) Seek?.Invoke(this, T(e.GetPosition(this).X)); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { _drag = false; e.Pointer.Capture(null); }
}
