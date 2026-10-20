using System.Collections.Concurrent;
using CaptureDoc.Core.Model;
using SkiaSharp;

namespace CaptureDoc.Rendering;

/// <summary>Word-wrapping text layout shared by renderer and editor (auto-sizing text boxes).</summary>
public sealed class TextLayout
{
    private static readonly ConcurrentDictionary<(string, bool, bool), SKTypeface> Typefaces = new();

    public required IReadOnlyList<string> Lines { get; init; }
    public required float LineHeight { get; init; }
    public required RectD Box { get; init; }
    public required SKTypeface Face { get; init; }
    public required float Size { get; init; }

    public SKFont CreateFont() => new(Face, Size) { Edging = SKFontEdging.Antialias, Subpixel = true, Hinting = SKFontHinting.Slight };

    public static SKTypeface Typeface(string family, bool bold, bool italic) =>
        Typefaces.GetOrAdd((family, bold, italic), k =>
            SKTypeface.FromFamilyName(k.Item1, new SKFontStyle(k.Item2 ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, k.Item3 ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright))
            ?? SKTypeface.Default);

    /// <summary>
    /// Wraps text to the box width (if the box has a width) and grows the box height to fit.
    /// With width 0 the box grows to the longest line.
    /// </summary>
    public static TextLayout Layout(TextLayer t)
    {
        var face = Typeface(t.FontFamily, t.Bold, t.Italic);
        var size = (float)Math.Max(4, t.FontSize);
        using var font = new SKFont(face, size);
        var r = t.Rect.Normalize();
        var pad = t.Padding;
        var maxW = r.Width > pad * 2 + size ? (float)(r.Width - pad * 2) : float.PositiveInfinity;
        var lines = Wrap(t.Text ?? "", font, maxW);
        var lineHeight = font.Spacing;
        var widest = lines.Count == 0 ? 0 : lines.Max(l => font.MeasureText(l));
        var w = float.IsPositiveInfinity(maxW) ? widest + pad * 2 : r.Width;
        var h = Math.Max(lines.Count, 1) * lineHeight + pad * 2;
        return new TextLayout { Lines = lines, LineHeight = lineHeight, Box = new RectD(r.X, r.Y, w, Math.Max(h, r.Height)), Face = face, Size = size };
    }

    /// <summary>Size the box needs for its text (height only grows; used after editing).</summary>
    public static RectD FitBox(TextLayer t)
    {
        var l = Layout(t);
        var r = t.Rect.Normalize();
        var face = l.Face; using var font = l.CreateFont();
        var needed = Math.Max(l.Lines.Count, 1) * l.LineHeight + t.Padding * 2;
        return new RectD(r.X, r.Y, l.Box.Width, needed);
    }

    public static List<string> Wrap(string text, SKFont font, float maxWidth)
    {
        var result = new List<string>();
        foreach (var para in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (float.IsPositiveInfinity(maxWidth) || font.MeasureText(para) <= maxWidth) { result.Add(para); continue; }
            var words = para.Split(' ');
            var line = "";
            foreach (var w in words)
            {
                var cand = line.Length == 0 ? w : line + " " + w;
                if (font.MeasureText(cand) <= maxWidth) { line = cand; continue; }
                if (line.Length > 0) result.Add(line);
                // Very long single word: hard break by characters
                var word = w;
                while (font.MeasureText(word) > maxWidth && word.Length > 1)
                {
                    var n = word.Length;
                    while (n > 1 && font.MeasureText(word[..n]) > maxWidth) n--;
                    result.Add(word[..n]);
                    word = word[n..];
                }
                line = word;
            }
            result.Add(line);
        }
        return result;
    }
}
