namespace CaptureDoc.Core.Export;

public readonly record struct EncodeParameters(int Width, int Height, int Quality);

public sealed record OptimizeResult(
    bool Success,
    byte[]? Data,
    EncodeParameters Parameters,
    long Bytes,
    int Attempts,
    string Message);

/// <summary>
/// Searches encoder parameters that meet a byte limit. Encoder-agnostic (images now, GIF/video later):
/// the caller supplies <c>encode(params) → bytes</c>. The real size of every candidate is measured,
/// never estimated. Strategy: fit dimensions (no upscaling) → binary search quality (lossy) →
/// shrink dimensions stepwise until <c>MinWidth</c>.
/// </summary>
public static class SizeOptimizer
{
    public sealed record Request(
        int SourceWidth, int SourceHeight,
        int? MaxWidth, int? MaxHeight, double Scale,
        bool Lossy, int StartQuality, int MinQuality,
        long? MaxBytes, SizeLimitKind LimitKind, int MinWidth);

    public static (int W, int H) FitDimensions(int srcW, int srcH, int? maxW, int? maxH, double scale)
    {
        double w = srcW * (scale > 0 ? Math.Min(scale, 1.0) : 1.0);
        double h = srcH * (scale > 0 ? Math.Min(scale, 1.0) : 1.0);
        var f = 1.0;
        if (maxW is > 0 && w > maxW) f = Math.Min(f, maxW.Value / w);
        if (maxH is > 0 && h > maxH) f = Math.Min(f, maxH.Value / h);
        // Never upscale beyond source.
        return (Math.Max(1, (int)Math.Round(w * f)), Math.Max(1, (int)Math.Round(h * f)));
    }

    public static OptimizeResult Optimize(Request r, Func<EncodeParameters, byte[]> encode)
    {
        var (w, h) = FitDimensions(r.SourceWidth, r.SourceHeight, r.MaxWidth, r.MaxHeight, r.Scale);
        var attempts = 0;
        byte[] Enc(EncodeParameters p) { attempts++; return encode(p); }

        var first = new EncodeParameters(w, h, r.StartQuality);
        var data = Enc(first);
        if (r.LimitKind == SizeLimitKind.None || r.MaxBytes is null || data.LongLength <= r.MaxBytes)
            return new(true, data, first, data.LongLength, attempts, "OK");

        var limit = r.MaxBytes.Value;
        byte[] best = data; var bestP = first;
        var aspect = h / (double)w;
        var minW = Math.Min(r.MinWidth, w);

        for (var round = 0; round < 12; round++)
        {
            if (r.Lossy)
            {
                // Binary search: highest quality that fits at these dimensions.
                int lo = r.MinQuality, hi = Math.Min(r.StartQuality, 100);
                byte[]? fit = null; var fitQ = -1;
                while (lo <= hi)
                {
                    var q = (lo + hi) / 2;
                    var cand = Enc(new(w, h, q));
                    if (cand.LongLength <= limit) { fit = cand; fitQ = q; lo = q + 1; }
                    else { hi = q - 1; if (cand.LongLength < best.LongLength) { best = cand; bestP = new(w, h, q); } }
                }
                if (fit is not null)
                    return new(true, fit, new(w, h, fitQ), fit.LongLength, attempts, $"Ziel erreicht mit Qualität {fitQ} bei {w}×{h}.");
            }

            if (w <= minW) break;
            // Shrink by the estimated factor (area ~ bytes), at least 5 %, at most 50 %.
            var ratio = Math.Sqrt(limit / (double)Math.Max(1, best.LongLength)) * 0.95;
            ratio = Math.Clamp(ratio, 0.5, 0.95);
            w = Math.Max(minW, (int)Math.Floor(w * ratio));
            h = Math.Max(1, (int)Math.Round(w * aspect));
            var p = new EncodeParameters(w, h, r.Lossy ? r.MinQuality : r.StartQuality);
            var c = Enc(p);
            if (c.LongLength < best.LongLength) { best = c; bestP = p; }
            if (!r.Lossy && c.LongLength <= limit)
                return new(true, c, p, c.LongLength, attempts, $"Ziel erreicht durch Verkleinerung auf {w}×{h}.");
            if (r.Lossy && c.LongLength <= limit)
            {
                // fits at min quality → loop again to raise quality at this size
                best = c; bestP = p;
                continue;
            }
        }

        var msg = $"Größenlimit {FormatBytes(limit)} nicht erreichbar: kleinstes Ergebnis {FormatBytes(best.LongLength)} bei {bestP.Width}×{bestP.Height}" +
                  (r.Lossy ? $", Qualität {bestP.Quality}" : "") + $" (Mindestbreite {r.MinWidth} px" + (r.Lossy ? $", Mindestqualität {r.MinQuality}" : "") + ").";
        return r.LimitKind == SizeLimitKind.Hard
            ? new(false, null, bestP, best.LongLength, attempts, msg)
            : new(false, best, bestP, best.LongLength, attempts, msg + " Weiches Limit: kleinste Variante wird verwendet.");
    }

    public static string FormatBytes(long b) => b >= 1024 * 1024 ? $"{b / 1024.0 / 1024.0:0.##} MB" : $"{b / 1024.0:0.#} KB";
}
