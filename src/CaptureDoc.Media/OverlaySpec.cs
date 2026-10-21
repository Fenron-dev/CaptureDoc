using System.Globalization;
using System.Text;

namespace CaptureDoc.Media;

/// <summary>Position of an overlay image at a time (for movement).</summary>
public readonly record struct PathPoint(double Time, double X, double Y);

/// <summary>A pre-rendered transparent PNG shown from Start to End (source seconds).</summary>
public sealed class ImageOverlay
{
    public required string PngPath { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Start { get; init; }
    public double End { get; init; } = double.MaxValue;
    /// <summary>Optional movement; interpolated linearly, X/Y used if empty.</summary>
    public List<PathPoint> Path { get; init; } = new();
    /// <summary>Stacking position (layer index, bottom = 0). Equal values: effects before images.</summary>
    public int Order { get; init; }
}

public enum RegionEffectKind { Blackout, Pixelate, Blur, Magnify }

/// <summary>Effect that needs the video pixels (redaction, magnifier). Rect in source pixels.</summary>
public sealed class RegionEffect
{
    public RegionEffectKind Kind { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int W { get; init; }
    public int H { get; init; }
    /// <summary>Magnifier target rect.</summary>
    public int DX { get; init; }
    public int DY { get; init; }
    public int DW { get; init; }
    public int DH { get; init; }
    public double Strength { get; init; } = 12;
    public string Color { get; init; } = "black";
    public double Start { get; init; }
    public double End { get; init; } = double.MaxValue;
    /// <summary>Stacking position (layer index). An effect only changes what lies below it.</summary>
    public int Order { get; init; }
}

/// <summary>Visible part of the frame at a time (zoom/pan).</summary>
public readonly record struct ZoomPoint(double Time, double X, double Y, double W, double H);

/// <summary>Everything that is drawn on top of / applied to the video (Phase 7).</summary>
public sealed class OverlaySpec
{
    public List<RegionEffect> Effects { get; } = new();
    public List<ImageOverlay> Images { get; } = new();
    public List<ZoomPoint> Zoom { get; } = new();
    public bool IsEmpty => Effects.Count == 0 && Images.Count == 0 && Zoom.Count == 0;

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Piecewise-linear expression of the time variable; holds first/last value outside the range.</summary>
    internal static string Lerp(IReadOnlyList<(double t, double v)> pts, string tv = "t")
    {
        if (pts.Count == 0) return "0";
        var p = pts.OrderBy(x => x.t).ToList();
        if (p.Count == 1) return F(p[0].v);
        var expr = F(p[^1].v);
        for (var i = p.Count - 2; i >= 0; i--)
        {
            var (t0, v0) = p[i]; var (t1, v1) = p[i + 1];
            var seg = Math.Abs(t1 - t0) < 1e-6 ? F(v1) : $"({F(v0)}+({F(v1 - v0)})*({tv}-{F(t0)})/{F(t1 - t0)})";
            expr = $"if(lt({tv},{F(t1)}),{seg},{expr})";
        }
        return $"if(lt({tv},{F(p[0].t)}),{F(p[0].v)},{expr})";
    }

    private static string Enable(double start, double end, double offset) =>
        end >= double.MaxValue / 2 ? $"gte(t,{F(start - offset)})" : $"between(t,{F(start - offset)},{F(end - offset)})";

    /// <summary>
    /// Builds a filter_complex chain starting at <paramref name="input"/> and ending in label <c>[ov]</c>.
    /// Times are source seconds; <paramref name="offset"/> is the input seek (-ss) so that t is relative.
    /// Image overlays use inputs 1..n in the order of <see cref="Images"/>.
    /// </summary>
    public string BuildFilter(string input, int width, int height, double fps, double offset)
    {
        var sb = new StringBuilder();
        var cur = input; var n = 0;
        string Next() => $"[o{n++}]";

        // layer order like the screenshot editor: an effect (redaction) only changes what lies below it.
        // Stable sort: equal Order → effects first, then images, each in list order.
        var ops = Effects.Select((e, i) => (order: e.Order, kind: 0, idx: i))
            .Concat(Images.Select((im, i) => (order: im.Order, kind: 1, idx: i)))
            .OrderBy(o => o.order).ThenBy(o => o.kind).ThenBy(o => o.idx).ToList();

        foreach (var op in ops)
        {
            if (op.kind == 1)
            {
                var im = Images[op.idx];
                var ien = Enable(im.Start, im.End, offset);
                string xs, ys;
                if (im.Path.Count > 0)
                {
                    xs = Lerp(im.Path.Select(p => (p.Time - offset, p.X)).ToList());
                    ys = Lerp(im.Path.Select(p => (p.Time - offset, p.Y)).ToList());
                }
                else { xs = F(im.X); ys = F(im.Y); }
                var io = Next();
                sb.Append($"{cur}[{op.idx + 1}:v]overlay=x='{xs}':y='{ys}':enable='{ien}':eof_action=repeat{io};");
                cur = io;
                continue;
            }
            var e = Effects[op.idx];
            var en = Enable(e.Start, e.End, offset);
            if (e.Kind == RegionEffectKind.Blackout)
            {
                var o = Next();
                sb.Append($"{cur}drawbox=x={e.X}:y={e.Y}:w={e.W}:h={e.H}:color={e.Color}@1:t=fill:enable='{en}'{o};");
                cur = o;
                continue;
            }
            var b = Next(); var s = Next(); var fx = Next(); var o2 = Next();
            var effect = e.Kind switch
            {
                RegionEffectKind.Pixelate => $"scale=max(1\\,iw/{F(Math.Max(2, e.Strength))}):max(1\\,ih/{F(Math.Max(2, e.Strength))}):flags=area,scale={e.W}:{e.H}:flags=neighbor",
                RegionEffectKind.Blur => $"boxblur={F(Math.Clamp(e.Strength, 1, 50))}:2",
                _ => $"scale={e.DW}:{e.DH}:flags=lanczos",
            };
            var (ox, oy) = e.Kind == RegionEffectKind.Magnify ? (e.DX, e.DY) : (e.X, e.Y);
            sb.Append($"{cur}split{b}{s};{s}crop={e.W}:{e.H}:{e.X}:{e.Y},{effect}{fx};{b}{fx}overlay={ox}:{oy}:enable='{en}'{o2};");
            cur = o2;
        }

        // zoom last: effects and annotations are placed in source pixels and must move with the content
        if (Zoom.Count > 0)
        {
            // zoompan works per input frame; 'it' = input timestamp in seconds
            var z = Zoom.Select(p => (p.Time - offset, width / Math.Max(1, p.W))).ToList();
            var x = Zoom.Select(p => (p.Time - offset, p.X)).ToList();
            var y = Zoom.Select(p => (p.Time - offset, p.Y)).ToList();
            var o = Next();
            sb.Append($"{cur}zoompan=z='{Lerp(z, "it")}':x='{Lerp(x, "it")}':y='{Lerp(y, "it")}':d=1:s={width}x{height}:fps={F(fps)},setpts=N/({F(fps)})/TB{o};");
            cur = o;
        }
        sb.Append($"{cur}null[ov]");
        return sb.ToString();
    }

    /// <summary>Extra "-i png" inputs for the image overlays.</summary>
    public IEnumerable<string> InputArgs()
    {
        foreach (var im in Images)
        {
            // single frame; overlay keeps showing it (eof_action=repeat) – a looped input would never end
            yield return "-i"; yield return im.PngPath;
        }
    }
}
