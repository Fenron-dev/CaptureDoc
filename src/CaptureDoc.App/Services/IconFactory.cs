using Avalonia.Controls;
using SkiaSharp;

namespace CaptureDoc.App.Services;

/// <summary>Draws the app icon at runtime (no binary assets needed).</summary>
public static class IconFactory
{
    private static WindowIcon? _icon;
    public static WindowIcon WindowIcon => _icon ??= new WindowIcon(new MemoryStream(Png(64)));

    public static byte[] Png(int size)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul))!;
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);
        var s = size / 64f;
        using var bg = new SKPaint { Color = new SKColor(0, 120, 215), IsAntialias = true };
        c.DrawRoundRect(new SKRect(4 * s, 4 * s, 60 * s, 60 * s), 12 * s, 12 * s, bg);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4 * s, StrokeCap = SKStrokeCap.Round };
        // selection corners
        void Corner(float x, float y, float dx, float dy) { c.DrawLine(x, y, x + dx, y, white); c.DrawLine(x, y, x, y + dy, white); }
        Corner(14 * s, 14 * s, 10 * s, 10 * s); Corner(50 * s, 14 * s, -10 * s, 10 * s);
        Corner(14 * s, 50 * s, 10 * s, -10 * s); Corner(50 * s, 50 * s, -10 * s, -10 * s);
        using var dot = new SKPaint { Color = new SKColor(255, 80, 80), IsAntialias = true };
        c.DrawCircle(32 * s, 32 * s, 8 * s, dot);
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
