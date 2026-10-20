using CaptureDoc.Core.Model;
using CaptureDoc.Core.Platform;
using SkiaSharp;

namespace CaptureDoc.Rendering;

public static class SkiaConvert
{
    public static SKColor ToSk(this ColorRgba c) => new(c.R, c.G, c.B, c.A);
    public static SKColor ToSk(this ColorRgba c, double opacity) => new(c.R, c.G, c.B, (byte)Math.Clamp(c.A * opacity, 0, 255));
    public static SKRect ToSk(this RectD r) => new((float)r.X, (float)r.Y, (float)r.Right, (float)r.Bottom);
    public static SKPoint ToSk(this PointD p) => new((float)p.X, (float)p.Y);

    /// <summary>Opaque BGRA capture → SKImage (copy).</summary>
    public static SKImage ToSkImage(RawImage raw)
    {
        var info = new SKImageInfo(raw.Width, raw.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        return SKImage.FromPixelCopy(info, raw.Pixels, raw.Width * 4);
    }

    /// <summary>PNG for storing captures losslessly (fast zlib level – screenshots compress well anyway).</summary>
    public static byte[] EncodePng(SKImage image, int zlibLevel = 4)
    {
        using var bmp = SKBitmap.FromImage(image);
        using var pm = bmp.PeekPixels();
        using var data = pm.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, zlibLevel)) ?? throw new InvalidOperationException("PNG encoding failed.");
        return data.ToArray();
    }

    public static byte[] EncodePng(RawImage raw, int zlibLevel = 4)
    {
        var info = new SKImageInfo(raw.Width, raw.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        unsafe
        {
            fixed (byte* p = raw.Pixels)
            {
                using var pm = new SKPixmap(info, (nint)p, raw.Width * 4);
                using var data = pm.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, zlibLevel)) ?? throw new InvalidOperationException("PNG encoding failed.");
                return data.ToArray();
            }
        }
    }

    /// <summary>Converts a rendered image to opaque/straight BGRA (for the clipboard).</summary>
    public static RawImage ToRaw(SKImage image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var px = new byte[info.BytesSize];
        unsafe
        {
            fixed (byte* p = px)
            {
                if (!image.ReadPixels(info, (nint)p, info.RowBytes, 0, 0))
                    throw new InvalidOperationException("Pixel readback failed.");
            }
        }
        return new RawImage(image.Width, image.Height, px);
    }
}
