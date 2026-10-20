using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Platform;
using SkiaSharp;

namespace CaptureDoc.App.Util;

/// <summary>Small helpers for code-built UI and bitmap conversion.</summary>
public static class Ui
{
    public static WriteableBitmap ToBitmap(RawImage raw)
    {
        var wb = new WriteableBitmap(new PixelSize(raw.Width, raw.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var fb = wb.Lock();
        unsafe
        {
            fixed (byte* src = raw.Pixels)
            {
                for (var y = 0; y < raw.Height; y++)
                    Buffer.MemoryCopy(src + y * raw.Width * 4, (byte*)fb.Address + y * fb.RowBytes, fb.RowBytes, raw.Width * 4);
            }
        }
        return wb;
    }

    public static WriteableBitmap ToBitmap(SKImage img)
    {
        var wb = new WriteableBitmap(new PixelSize(img.Width, img.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = wb.Lock();
        var info = new SKImageInfo(img.Width, img.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        img.ReadPixels(info, fb.Address, fb.RowBytes, 0, 0);
        return wb;
    }

    public static Color ToAvalonia(this ColorRgba c) => Color.FromArgb(c.A, c.R, c.G, c.B);
    public static ColorRgba ToModel(this Color c) => new(c.R, c.G, c.B, c.A);

    public static Button Button(string text, Action onClick, string? tip = null, bool accent = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 5), Margin = new Thickness(2) };
        if (tip is not null) ToolTip.SetTip(b, tip);
        if (accent) b.Classes.Add("accent");
        b.Click += (_, _) => onClick();
        return b;
    }

    public static TextBlock Label(string text, double size = 13, bool bold = false, double opacity = 1) =>
        new() { Text = text, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, Opacity = opacity, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

    public static StackPanel Row(params Control[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    public static StackPanel Column(double spacing, params Control[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Vertical, Spacing = spacing };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    /// <summary>Label above a control.</summary>
    public static Control Field(string label, Control control) => Column(2, Label(label, 12, opacity: 0.75), control);

    public static readonly ColorRgba[] Palette =
    {
        ColorRgba.Red, new(255, 120, 0), ColorRgba.Yellow, new(40, 170, 70), ColorRgba.Blue, new(140, 70, 220),
        ColorRgba.Black, new(90, 90, 90), ColorRgba.White,
    };
}
