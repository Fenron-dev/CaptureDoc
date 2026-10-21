using CaptureDoc.App.Editor;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Serialization;
using CaptureDoc.Media;
using CaptureDoc.Rendering;
using SkiaSharp;

namespace CaptureDoc.App.Video;

/// <summary>
/// Turns the annotation page of a video into an <see cref="OverlaySpec"/>:
/// pixel effects (redaction, magnifier) are done by ffmpeg on the video pixels,
/// all other layers are rendered with the normal <see cref="SceneRenderer"/> to transparent PNGs.
/// </summary>
public static class OverlayRenderer
{
    public const string ReferenceLayerName = "Videobild (nur Referenz, wird nicht exportiert)";

    public static (double start, double end) TimeOf(Layer l) =>
        l.Timing is { } t ? (t.StartSeconds, t.EndSeconds > t.StartSeconds ? t.EndSeconds : double.MaxValue) : (0, double.MaxValue);

    public static Task<OverlaySpec> PrepareAsync(ProjectSession session, VideoOverlay overlay, string workDir) => Task.Run(() => Prepare(session, overlay, workDir));

    public static OverlaySpec Prepare(ProjectSession session, VideoOverlay overlay, string workDir)
    {
        Directory.CreateDirectory(workDir);
        var spec = new OverlaySpec();
        var page = overlay.Page;
        var i = 0;
        for (var order = 0; order < page.Layers.Count; order++)
        {
            var layer = page.Layers[order];
            if (!layer.Visible || layer is ImageLayer { IsBackground: true }) continue;
            var (start, end) = TimeOf(layer);
            switch (layer)
            {
                case RedactLayer rd:
                {
                    var r = Clamp(rd.Rect.Normalize(), page);
                    if (r.W < 2 || r.H < 2) continue;
                    spec.Effects.Add(new RegionEffect
                    {
                        Kind = rd.Mode switch { RedactMode.Pixelate => RegionEffectKind.Pixelate, RedactMode.Blur => RegionEffectKind.Blur, _ => RegionEffectKind.Blackout },
                        X = r.X, Y = r.Y, W = r.W, H = r.H, Strength = rd.Strength, Color = "0x" + rd.Fill.ToHex()[1..7], Start = start, End = end, Order = order,
                    });
                    continue;
                }
                case MagnifierLayer mg:
                {
                    var s = Clamp(mg.Source.Normalize(), page); var d = Clamp(mg.Rect.Normalize(), page);
                    if (s.W >= 2 && s.H >= 2 && d.W >= 2 && d.H >= 2)
                        spec.Effects.Add(new RegionEffect { Kind = RegionEffectKind.Magnify, X = s.X, Y = s.Y, W = s.W, H = s.H, DX = d.X, DY = d.Y, DW = d.W & ~1, DH = d.H & ~1, Start = start, End = end, Order = order });
                    break; // the border is rendered below as a PNG
                }
            }
            var png = RenderLayerPng(session, page, layer, out var origin);
            if (png is null) continue;
            var path = Path.Combine(workDir, $"layer-{i++:000}.png");
            File.WriteAllBytes(path, png);
            var item = new ImageOverlay { PngPath = path, X = origin.X, Y = origin.Y, Start = start, End = end, Order = order };
            if (layer.Timing is { Keyframes.Count: > 0 } tm)
            {
                var b0 = layer.Bounds;
                foreach (var k in tm.Keyframes.OrderBy(k => k.TimeSeconds))
                    item.Path.Add(new PathPoint(k.TimeSeconds, origin.X + (k.Bounds.X - b0.X), origin.Y + (k.Bounds.Y - b0.Y)));
            }
            spec.Images.Add(item);
        }
        foreach (var z in overlay.Zoom.OrderBy(z => z.TimeSeconds))
            spec.Zoom.Add(new ZoomPoint(z.TimeSeconds, z.View.X, z.View.Y, Math.Max(16, z.View.Width), Math.Max(16, z.View.Height)));
        return spec;
    }

    private static (int X, int Y, int W, int H) Clamp(RectD r, Page p)
    {
        var x = (int)Math.Max(0, Math.Floor(r.X)); var y = (int)Math.Max(0, Math.Floor(r.Y));
        var right = (int)Math.Min(p.Width, Math.Ceiling(r.Right)); var bottom = (int)Math.Min(p.Height, Math.Ceiling(r.Bottom));
        return (x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    /// <summary>Renders one layer alone on a transparent page and crops to its bounds (+ room for shadows).</summary>
    public static byte[]? RenderLayerPng(ProjectSession session, Page page, Layer layer, out (int X, int Y) origin)
    {
        origin = (0, 0);
        var solo = new Page { Width = page.Width, Height = page.Height, Background = ColorRgba.Transparent, Layers = { ProjectJson.Clone(layer) } };
        solo.Layers[0].Timing = null;
        using var full = session.Renderer.RenderPage(solo);
        // spotlights darken the whole frame → keep full size; others are cropped
        var b = layer is SpotlightLayer ? new RectD(0, 0, page.Width, page.Height) : layer.Bounds.Inflate(24);
        var r = Clamp(b, page);
        if (r.W < 1 || r.H < 1) return null;
        using var sub = full.Subset(new SKRectI(r.X, r.Y, r.X + r.W, r.Y + r.H));
        if (sub is null) return null;
        origin = (r.X, r.Y);
        using var data = sub.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Preview of all annotations visible at time t (transparent, page size).</summary>
    public static SKImage RenderPreview(ProjectSession session, VideoOverlay overlay, double t)
    {
        var p = overlay.Page;
        var visible = new Page { Width = p.Width, Height = p.Height, Background = ColorRgba.Transparent };
        foreach (var l in p.Layers)
        {
            if (!l.Visible || l is ImageLayer { IsBackground: true }) continue;
            var (s, e) = TimeOf(l);
            if (t < s || t > e) continue;
            var c = ProjectJson.Clone(l);
            if (l.Timing is { Keyframes.Count: > 0 } tm)
            {
                var b0 = l.Bounds;
                var (dx, dy) = Interpolate(tm.Keyframes, t, b0);
                c.Translate(dx, dy);
            }
            // pixel effects cannot be previewed on a transparent layer – show their outline instead
            if (c is RedactLayer rd) { visible.Layers.Add(new ShapeLayer { Rect = rd.Rect, Stroke = ColorRgba.Black, Fill = rd.Mode == RedactMode.Blackout ? rd.Fill.WithAlpha(255) : new ColorRgba(0, 0, 0, 120), StrokeWidth = 2, Shadow = false }); continue; }
            visible.Layers.Add(c);
        }
        return session.Renderer.RenderPage(visible);
    }

    private static (double dx, double dy) Interpolate(List<Keyframe> keys, double t, RectD b0)
    {
        var k = keys.OrderBy(x => x.TimeSeconds).ToList();
        if (t <= k[0].TimeSeconds) return (k[0].Bounds.X - b0.X, k[0].Bounds.Y - b0.Y);
        for (var i = 0; i < k.Count - 1; i++)
        {
            if (t > k[i + 1].TimeSeconds) continue;
            var f = (t - k[i].TimeSeconds) / Math.Max(1e-6, k[i + 1].TimeSeconds - k[i].TimeSeconds);
            return (k[i].Bounds.X + (k[i + 1].Bounds.X - k[i].Bounds.X) * f - b0.X, k[i].Bounds.Y + (k[i + 1].Bounds.Y - k[i].Bounds.Y) * f - b0.Y);
        }
        return (k[^1].Bounds.X - b0.X, k[^1].Bounds.Y - b0.Y);
    }
}
