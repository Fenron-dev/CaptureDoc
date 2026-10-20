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
    public ClipRange Clip { get; set; } = new();
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
        ctx.DrawRectangle(dim, null, new Rect(0, stripTop, X(Clip.Start), stripH));
        ctx.DrawRectangle(dim, null, new Rect(X(Clip.End), stripTop, w - X(Clip.End), stripH));
        var cutBrush = new SolidColorBrush(Color.FromArgb(150, 220, 40, 40));
        foreach (var (a, b) in Clip.Cuts)
            ctx.DrawRectangle(cutBrush, null, new Rect(X(a), stripTop, Math.Max(2, X(b) - X(a)), stripH));
        var green = new Pen(new SolidColorBrush(Color.FromRgb(60, 200, 100)), 3);
        ctx.DrawLine(green, new Point(X(Clip.Start), stripTop), new Point(X(Clip.Start), Bounds.Height));
        ctx.DrawLine(green, new Point(X(Clip.End), stripTop), new Point(X(Clip.End), Bounds.Height));

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
