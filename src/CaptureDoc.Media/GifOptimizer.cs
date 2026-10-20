using CaptureDoc.Core.Export;

namespace CaptureDoc.Media;

public readonly record struct GifParams(int Width, int Height, double Fps, int Colors);

public sealed record GifOptimizeResult(bool Success, GifParams Parameters, long Bytes, int BestAttempt, int Attempts, string Message);

/// <summary>
/// Finds GIF parameters under a byte limit. Order of reductions favours readable tutorials:
/// colours (cheap, small savings) → frame rate (down to MinFps) → resolution (down to MinWidth).
/// Every candidate is really encoded; sizes are measured, not estimated.
/// </summary>
public static class GifOptimizer
{
    public sealed record Request(
        int SourceWidth, int SourceHeight, int? MaxWidth, int? MaxHeight,
        double Fps, double MinFps, int Colors, int MinColors, int MinWidth,
        long? MaxBytes, SizeLimitKind LimitKind, int MaxAttempts = 10);

    /// <param name="encode">Encodes candidate #attempt and returns its real size in bytes.</param>
    public static GifOptimizeResult Optimize(Request r, Func<GifParams, int, long> encode)
    {
        var (w, h) = SizeOptimizer.FitDimensions(r.SourceWidth, r.SourceHeight, r.MaxWidth, r.MaxHeight, 1.0);
        w = Even(w); h = Even(h);
        var aspect = h / (double)w;
        var p = new GifParams(w, h, Math.Max(1, r.Fps), Math.Clamp(r.Colors, 2, 256));
        long bestBytes = long.MaxValue; var bestAttempt = -1; var bestP = p;
        var minW = Math.Min(r.MinWidth, w);

        for (var attempt = 0; attempt < r.MaxAttempts; attempt++)
        {
            var size = encode(p, attempt);
            if (size < bestBytes) { bestBytes = size; bestAttempt = attempt; bestP = p; }
            if (r.LimitKind == SizeLimitKind.None || r.MaxBytes is null || size <= r.MaxBytes)
                return new(true, p, size, attempt, attempt + 1,
                    r.MaxBytes is null ? "OK" : $"Ziel erreicht: {p.Width}×{p.Height}, {p.Fps:0.#} fps, {p.Colors} Farben, {SizeOptimizer.FormatBytes(size)}.");

            var ratio = r.MaxBytes.Value / (double)size;
            if (p.Colors > r.MinColors && ratio > 0.75)
                p = p with { Colors = Math.Max(r.MinColors, p.Colors / 2) };
            else if (p.Fps > r.MinFps + 0.01)
                p = p with { Fps = Math.Max(r.MinFps, Math.Round(p.Fps * Math.Clamp(ratio * 1.05, 0.5, 0.9), 1)) };
            else if (p.Width > minW)
            {
                var nw = Even(Math.Max(minW, (int)(p.Width * Math.Clamp(Math.Sqrt(ratio) * 0.95, 0.5, 0.92))));
                if (nw >= p.Width) nw = Even(p.Width - 2);
                p = p with { Width = nw, Height = Math.Max(2, Even((int)Math.Round(nw * aspect))) };
            }
            else if (p.Colors > r.MinColors)
                p = p with { Colors = Math.Max(r.MinColors, p.Colors / 2) };
            else break;
        }

        var msg = $"Größenlimit {SizeOptimizer.FormatBytes(r.MaxBytes ?? 0)} nicht erreichbar: kleinstes Ergebnis {SizeOptimizer.FormatBytes(bestBytes)} " +
                  $"bei {bestP.Width}×{bestP.Height}, {bestP.Fps:0.#} fps, {bestP.Colors} Farben (Grenzen: ≥ {r.MinWidth} px, ≥ {r.MinFps:0.#} fps, ≥ {r.MinColors} Farben). " +
                  "Tipp: Clip kürzen oder Mindestwerte senken.";
        return new(false, bestP, bestBytes, bestAttempt, Math.Min(r.MaxAttempts, bestAttempt + 1),
            r.LimitKind == SizeLimitKind.Soft ? msg + " Weiches Limit: kleinste Variante wird verwendet." : msg);
    }

    private static int Even(int v) => Math.Max(2, v & ~1);
}
