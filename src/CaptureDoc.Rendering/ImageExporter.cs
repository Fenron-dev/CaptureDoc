using CaptureDoc.Core.Export;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Storage;
using SkiaSharp;

namespace CaptureDoc.Rendering;

public sealed record ImageExportOutcome(
    bool Success,
    string? Path,
    byte[]? Data,
    int Width,
    int Height,
    long Bytes,
    int Quality,
    bool LimitMet,
    string Message);

/// <summary>
/// Flattens a page to pixels and encodes it according to an <see cref="ExportProfile"/>.
/// The flattened output contains no layers – redactions are burned in.
/// When a file is written, its real size and dimensions are re-read from disk before success is reported.
/// </summary>
public static class ImageExporter
{
    public static ImageExportOutcome Export(SceneRenderer renderer, Page page, ExportProfile profile, string? outputPath = null)
    {
        using var full = renderer.RenderPage(page);
        return Export(full, profile, outputPath);
    }

    public static ImageExportOutcome Export(SKImage flattened, ExportProfile profile, string? outputPath = null)
    {
        var lossy = profile.Format == ImageFormat.Jpeg || (profile.Format == ImageFormat.WebP && !profile.Lossless);
        var req = new SizeOptimizer.Request(
            flattened.Width, flattened.Height, profile.MaxWidth, profile.MaxHeight, profile.Scale,
            lossy, Math.Clamp(profile.Quality, 1, 100), Math.Clamp(profile.MinQuality, 1, 100),
            profile.MaxBytes, profile.MaxBytes is null ? SizeLimitKind.None : profile.LimitKind, profile.MinWidth);

        SKImage? cached = null; (int, int) cachedSize = default;
        try
        {
            var result = SizeOptimizer.Optimize(req, p =>
            {
                if (cached is null || cachedSize != (p.Width, p.Height))
                {
                    cached?.Dispose();
                    cached = Resize(flattened, p.Width, p.Height);
                    cachedSize = (p.Width, p.Height);
                }
                return Encode(cached, profile.Format, p.Quality, profile.Lossless, profile.KeepTransparency);
            });

            if (result.Data is null)
                return new(false, null, null, result.Parameters.Width, result.Parameters.Height, result.Bytes, result.Parameters.Quality, false, result.Message);

            var limitMet = profile.MaxBytes is null || result.Data.LongLength <= profile.MaxBytes;
            if (outputPath is null)
                return new(true, null, result.Data, result.Parameters.Width, result.Parameters.Height, result.Data.LongLength, result.Parameters.Quality, limitMet, result.Message);

            AtomicFile.WriteAllBytes(outputPath, result.Data);

            // Verify on disk – never claim a limit is met without checking the real file.
            var len = new FileInfo(outputPath).Length;
            using var codec = SKCodec.Create(outputPath);
            var (w, h) = codec is null ? (0, 0) : (codec.Info.Width, codec.Info.Height);
            limitMet = profile.MaxBytes is null || len <= profile.MaxBytes;
            if (profile.LimitKind == SizeLimitKind.Hard && !limitMet)
            {
                File.Delete(outputPath);
                return new(false, null, null, w, h, len, result.Parameters.Quality, false, $"Datei war nach dem Schreiben {SizeOptimizer.FormatBytes(len)} groß und wurde wegen des harten Limits verworfen.");
            }
            if (codec is null || w != result.Parameters.Width || h != result.Parameters.Height)
                return new(false, outputPath, result.Data, w, h, len, result.Parameters.Quality, limitMet, "Prüfung der geschriebenen Datei fehlgeschlagen (Abmessungen weichen ab).");

            var msg = $"{w}×{h} px, {SizeOptimizer.FormatBytes(len)}" + (lossy ? $", Qualität {result.Parameters.Quality}" : "") +
                      (profile.MaxBytes is { } mb ? (limitMet ? $" – Limit {SizeOptimizer.FormatBytes(mb)} eingehalten (geprüft)." : $" – Limit {SizeOptimizer.FormatBytes(mb)} NICHT eingehalten. {result.Message}") : ".");
            return new(true, outputPath, result.Data, w, h, len, result.Parameters.Quality, limitMet, msg);
        }
        finally
        {
            cached?.Dispose();
        }
    }

    public static SKImage Resize(SKImage src, int w, int h)
    {
        if (src.Width == w && src.Height == h) return SKImage.FromBitmap(SKBitmap.FromImage(src));
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul))!;
        surface.Canvas.Clear(SKColors.Transparent);
        // Mitchell cubic gives crisp text when downscaling screenshots.
        surface.Canvas.DrawImage(src, new SKRect(0, 0, src.Width, src.Height), new SKRect(0, 0, w, h), new SKSamplingOptions(SKCubicResampler.Mitchell));
        return surface.Snapshot();
    }

    /// <summary>Encodes without any metadata (Skia's encoders write no EXIF/XMP).</summary>
    public static byte[] Encode(SKImage image, ImageFormat format, int quality, bool lossless, bool keepAlpha)
    {
        using var bmp = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, format == ImageFormat.Jpeg || !keepAlpha ? SKAlphaType.Opaque : SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        {
            if (format == ImageFormat.Jpeg || !keepAlpha) canvas.Clear(SKColors.White); else canvas.Clear(SKColors.Transparent);
            canvas.DrawImage(image, 0, 0);
        }
        using var pm = bmp.PeekPixels();
        SKData? data = format switch
        {
            ImageFormat.Jpeg => pm.Encode(new SKJpegEncoderOptions(Math.Clamp(quality, 1, 100), SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore)),
            ImageFormat.WebP => pm.Encode(new SKWebpEncoderOptions(lossless ? SKWebpEncoderCompression.Lossless : SKWebpEncoderCompression.Lossy, lossless ? 75 : Math.Clamp(quality, 1, 100))),
            _ => pm.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 9)),
        };
        if (data is null) throw new InvalidOperationException($"Kodierung als {format} fehlgeschlagen.");
        using (data) return data.ToArray();
    }
}
