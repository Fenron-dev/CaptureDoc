using CaptureDoc.Core.Model;
using SkiaSharp;

namespace CaptureDoc.Rendering;

/// <summary>
/// Renders a page (all layers, bottom → top) into an SKSurface. Used for the editor preview
/// and for export, so both always match. Effect layers (blur, pixelate, magnifier) operate on
/// the composite of everything drawn below them – exactly like the exported result.
/// </summary>
public sealed class SceneRenderer
{
    private readonly AssetImageCache _assets;
    private static readonly SKSamplingOptions Smooth = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public SceneRenderer(AssetImageCache assets) => _assets = assets;

    public AssetImageCache Assets => _assets;

    /// <summary>Renders the page at <paramref name="scale"/> into a new raster image.</summary>
    public SKImage RenderPage(Page page, double scale = 1.0, Guid? hideLayer = null)
    {
        var w = Math.Max(1, (int)Math.Round(page.Width * scale));
        var h = Math.Max(1, (int)Math.Round(page.Height * scale));
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul))
                            ?? throw new InvalidOperationException($"Surface {w}×{h} konnte nicht erstellt werden (zu groß?).");
        Render(surface, page, scale, hideLayer);
        return surface.Snapshot();
    }

    /// <summary>Renders only the top-level layers <c>[0, count)</c> (used as a static cache while dragging).</summary>
    public SKImage RenderBelow(Page page, int count)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Math.Max(1, page.Width), Math.Max(1, page.Height), SKColorType.Rgba8888, SKAlphaType.Premul))!;
        var c = surface.Canvas;
        c.Clear(page.Background.ToSk());
        for (var i = 0; i < Math.Min(count, page.Layers.Count); i++) DrawLayer(surface, c, page, page.Layers[i], 1.0, null, 1.0);
        return surface.Snapshot();
    }

    /// <summary>Draws <paramref name="below"/> and then the top-level layers from <paramref name="from"/> on (same result as a full render).</summary>
    public void RenderFrom(SKSurface surface, Page page, SKImage below, int from, Guid? hideLayer = null)
    {
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);
        c.DrawImage(below, 0, 0);
        for (var i = Math.Max(0, from); i < page.Layers.Count; i++) DrawLayer(surface, c, page, page.Layers[i], 1.0, hideLayer, 1.0);
    }

    public void Render(SKSurface surface, Page page, double scale, Guid? hideLayer = null)
    {
        var c = surface.Canvas;
        c.Clear(page.Background.ToSk());
        c.Save();
        c.Scale((float)scale);
        foreach (var l in page.Layers) DrawLayer(surface, c, page, l, scale, hideLayer, 1.0);
        c.Restore();
    }

    private void DrawLayer(SKSurface s, SKCanvas c, Page page, Layer layer, double scale, Guid? hide, double parentOpacity)
    {
        if (!layer.Visible || layer.Id == hide) return;
        var op = Math.Clamp(layer.Opacity * parentOpacity, 0, 1);
        // Redactions are always fully opaque: a half-transparent blackout would leak the content.
        if (layer is RedactLayer) op = 1;
        if (op <= 0.001) return;

        switch (layer)
        {
            case GroupLayer g:
                foreach (var ch in g.Children) DrawLayer(s, c, page, ch, scale, hide, op);
                break;
            case ImageLayer im: DrawImageLayer(c, im, op); break;
            case ShapeLayer sh: DrawShape(c, sh, op); break;
            case LineLayer ln: DrawLine(c, ln, op); break;
            case TextLayer tx: DrawText(c, tx, op); break;
            case StepLayer st: DrawStep(c, st, op); break;
            case FreehandLayer fh: DrawFreehand(c, fh, op); break;
            case HighlightLayer hl: DrawHighlight(c, hl, op); break;
            case SpotlightLayer sp: DrawSpotlight(c, page, sp, op); break;
            case RedactLayer rd: DrawRedact(s, c, rd, scale); break;
            case MagnifierLayer mg: DrawMagnifier(s, c, mg, scale, op); break;
        }
    }

    private void DrawImageLayer(SKCanvas c, ImageLayer im, double op)
    {
        var dst = im.Rect.Normalize().ToSk();
        var img = _assets.Get(im.AssetId);
        if (img is null)
        {
            using var p = new SKPaint { Color = new SKColor(200, 0, 0, 160), Style = SKPaintStyle.Stroke, StrokeWidth = 4, IsAntialias = true };
            c.DrawRect(dst, p); c.DrawLine(dst.Left, dst.Top, dst.Right, dst.Bottom, p); c.DrawLine(dst.Right, dst.Top, dst.Left, dst.Bottom, p);
            return;
        }
        if (im.Shadow)
        {
            using var sh = new SKPaint { Color = new SKColor(0, 0, 0, (byte)(120 * op)), ImageFilter = SKImageFilter.CreateBlur(10, 10), IsAntialias = true };
            c.DrawRoundRect(SKRect.Inflate(dst, -2, -2).Offset2(6, 8), (float)im.CornerRadius, (float)im.CornerRadius, sh);
        }
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * op)) };
        if (im.CornerRadius > 0) { c.Save(); c.ClipRoundRect(new SKRoundRect(dst, (float)im.CornerRadius), antialias: true); }
        // Pixel-exact for unscaled background images, smooth otherwise.
        var exact = Math.Abs(dst.Width - img.Width) < 0.01 && Math.Abs(dst.Height - img.Height) < 0.01 && Math.Abs(c.TotalMatrix.ScaleX - 1) < 1e-6;
        c.DrawImage(img, new SKRect(0, 0, img.Width, img.Height), dst, exact ? new SKSamplingOptions(SKFilterMode.Nearest) : Smooth, paint);
        if (im.CornerRadius > 0) c.Restore();
        if (im.BorderWidth > 0)
        {
            using var bp = new SKPaint { Color = im.BorderColor.ToSk(op), Style = SKPaintStyle.Stroke, StrokeWidth = (float)im.BorderWidth, IsAntialias = true };
            c.DrawRoundRect(dst, (float)im.CornerRadius, (float)im.CornerRadius, bp);
        }
    }

    private static SKImageFilter? Shadow(bool on, double size) =>
        on ? SKImageFilter.CreateDropShadow((float)(size * 0.35 + 1), (float)(size * 0.35 + 1), (float)(size * 0.6 + 1.5), (float)(size * 0.6 + 1.5), new SKColor(0, 0, 0, 110)) : null;

    private static void DrawShape(SKCanvas c, ShapeLayer sh, double op)
    {
        var r = sh.Rect.Normalize().ToSk();
        using var shadow = Shadow(sh.Shadow, sh.StrokeWidth);
        if (!sh.Fill.IsTransparent)
        {
            using var fill = new SKPaint { Color = sh.Fill.ToSk(op), IsAntialias = true, Style = SKPaintStyle.Fill, ImageFilter = sh.Stroke.IsTransparent ? shadow : null };
            Draw(c, sh, r, fill);
        }
        if (!sh.Stroke.IsTransparent && sh.StrokeWidth > 0)
        {
            using var stroke = new SKPaint { Color = sh.Stroke.ToSk(op), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)sh.StrokeWidth, StrokeJoin = SKStrokeJoin.Round, ImageFilter = shadow };
            Draw(c, sh, r, stroke);
        }

        static void Draw(SKCanvas c, ShapeLayer sh, SKRect r, SKPaint p)
        {
            switch (sh.Shape)
            {
                case ShapeKind.Ellipse: c.DrawOval(r, p); break;
                case ShapeKind.RoundedRectangle: c.DrawRoundRect(r, (float)sh.CornerRadius, (float)sh.CornerRadius, p); break;
                default: c.DrawRect(r, p); break;
            }
        }
    }

    private static void DrawLine(SKCanvas c, LineLayer ln, double op)
    {
        var a = ln.Start.ToSk(); var b = ln.End.ToSk();
        var d = b - a; var len = d.Length;
        if (len < 0.5f) return;
        var u = new SKPoint(d.X / len, d.Y / len);
        var n = new SKPoint(-u.Y, u.X);
        var sw = (float)ln.StrokeWidth;
        var head = Math.Min(Math.Max(sw * 3.2f, 10f), len * 0.6f);
        var half = head * 0.55f;

        using var shadow = Shadow(ln.Shadow, ln.StrokeWidth);
        using var path = new SKPath();
        var la = ln.StartCap == LineCap.Arrow ? a + new SKPoint(u.X * head * 0.8f, u.Y * head * 0.8f) : a;
        var lb = ln.EndCap == LineCap.Arrow ? b - new SKPoint(u.X * head * 0.8f, u.Y * head * 0.8f) : b;
        using var stroke = new SKPaint { Color = ln.Stroke.ToSk(op), StrokeWidth = sw, Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
        using var fill = new SKPaint { Color = ln.Stroke.ToSk(op), Style = SKPaintStyle.Fill, IsAntialias = true };

        // Draw everything into one layer so the shadow is cast once for line + heads.
        // The layer is limited to the arrow's bounds – a full-canvas layer costs ~0.6 s per arrow at 4K.
        using var layerPaint = new SKPaint { ImageFilter = shadow };
        var pad = head + sw * 2 + 12;
        var lb2 = new SKRect(Math.Min(a.X, b.X) - pad, Math.Min(a.Y, b.Y) - pad, Math.Max(a.X, b.X) + pad, Math.Max(a.Y, b.Y) + pad);
        if (shadow is not null) c.SaveLayer(lb2, layerPaint); else c.Save();
        c.DrawLine(la, lb, stroke);
        if (ln.EndCap == LineCap.Arrow) DrawHead(c, b, u, n, head, half, fill);
        if (ln.StartCap == LineCap.Arrow) DrawHead(c, a, new SKPoint(-u.X, -u.Y), n, head, half, fill);
        if (ln.EndCap == LineCap.Dot) c.DrawCircle(b, sw * 1.6f, fill);
        if (ln.StartCap == LineCap.Dot) c.DrawCircle(a, sw * 1.6f, fill);
        c.Restore();

        static void DrawHead(SKCanvas c, SKPoint tip, SKPoint u, SKPoint n, float head, float half, SKPaint fill)
        {
            var baseP = tip - new SKPoint(u.X * head, u.Y * head);
            using var p = new SKPath();
            p.MoveTo(tip);
            p.LineTo(baseP + new SKPoint(n.X * half, n.Y * half));
            p.LineTo(baseP - new SKPoint(n.X * half, n.Y * half));
            p.Close();
            c.DrawPath(p, fill);
        }
    }

    private static void DrawStep(SKCanvas c, StepLayer st, double op)
    {
        var center = st.Center.ToSk(); var r = (float)st.Radius;
        using var shadow = Shadow(st.Shadow, st.Radius / 5);
        using var fill = new SKPaint { Color = st.Fill.ToSk(op), IsAntialias = true, ImageFilter = shadow };
        c.DrawCircle(center, r, fill);
        var label = st.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var size = r * (label.Length >= 3 ? 0.75f : label.Length == 2 ? 0.95f : 1.15f);
        using var font = new SKFont(TextLayout.Typeface("Segoe UI", true, false), size) { Edging = SKFontEdging.Antialias, Subpixel = true };
        using var tp = new SKPaint { Color = st.Foreground.ToSk(op), IsAntialias = true };
        var m = font.Metrics;
        var baseline = center.Y - (m.Ascent + m.Descent) / 2;
        c.DrawText(label, center.X, baseline, SKTextAlign.Center, font, tp);
    }

    private static void DrawFreehand(SKCanvas c, FreehandLayer fh, double op)
    {
        if (fh.Points.Count == 0) return;
        using var p = new SKPaint { Color = fh.Stroke.ToSk(op), StrokeWidth = (float)fh.StrokeWidth, Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        if (fh.Points.Count == 1) { c.DrawPoint(fh.Points[0].ToSk(), p); return; }
        using var path = new SKPath();
        path.MoveTo(fh.Points[0].ToSk());
        // Quadratic smoothing through midpoints.
        for (var i = 1; i < fh.Points.Count - 1; i++)
        {
            var p0 = fh.Points[i].ToSk(); var p1 = fh.Points[i + 1].ToSk();
            path.QuadTo(p0, new SKPoint((p0.X + p1.X) / 2, (p0.Y + p1.Y) / 2));
        }
        path.LineTo(fh.Points[^1].ToSk());
        c.DrawPath(path, p);
    }

    private static void DrawHighlight(SKCanvas c, HighlightLayer hl, double op)
    {
        using var p = new SKPaint { Color = hl.Color.ToSk(op), BlendMode = SKBlendMode.Multiply, IsAntialias = true };
        c.DrawRect(hl.Rect.Normalize().ToSk(), p);
    }

    private static void DrawSpotlight(SKCanvas c, Page page, SpotlightLayer sp, double op)
    {
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        path.AddRect(new SKRect(-1, -1, page.Width + 1, page.Height + 1));
        var r = sp.Rect.Normalize().ToSk();
        if (sp.Elliptical) path.AddOval(r); else path.AddRect(r);
        using var p = new SKPaint { Color = new SKColor(0, 0, 0, (byte)Math.Clamp(255 * sp.Dim * op, 0, 255)), IsAntialias = true };
        c.DrawPath(path, p);
    }

    /// <summary>Layer rect → device pixel rect clamped to the surface.</summary>
    private static SKRectI DeviceRect(SKSurface s, RectD r, double scale, int pad = 0)
    {
        var n = r.Normalize();
        var x0 = (int)Math.Floor(n.X * scale) - pad; var y0 = (int)Math.Floor(n.Y * scale) - pad;
        var x1 = (int)Math.Ceiling(n.Right * scale) + pad; var y1 = (int)Math.Ceiling(n.Bottom * scale) + pad;
        var w = s.Canvas.DeviceClipBounds;
        var rect = new SKRectI(Math.Max(x0, 0), Math.Max(y0, 0), Math.Min(x1, w.Right), Math.Min(y1, w.Bottom));
        return rect.Width <= 0 || rect.Height <= 0 ? SKRectI.Empty : rect;
    }

    private static void DrawRedact(SKSurface s, SKCanvas c, RedactLayer rd, double scale)
    {
        var dev = DeviceRect(s, rd.Rect, scale);
        if (dev.IsEmpty) return;
        c.Save();
        c.ResetMatrix();
        try
        {
            switch (rd.Mode)
            {
                case RedactMode.Blackout:
                {
                    using var p = new SKPaint { Color = rd.Fill.WithAlpha(255).ToSk(), IsAntialias = false };
                    c.DrawRect(dev, p);
                    break;
                }
                case RedactMode.Pixelate:
                {
                    var block = Math.Max(2, (int)Math.Round(Math.Max(2, rd.Strength) * scale));
                    using var bmp = Pixelate(s, dev, block);
                    c.DrawBitmap(bmp, dev.Left, dev.Top);
                    break;
                }
                case RedactMode.Blur:
                {
                    var sigma = (float)Math.Max(1, rd.Strength * scale);
                    var pad = (int)Math.Ceiling(sigma * 3);
                    var src = DeviceRect(s, rd.Rect, scale, pad);
                    using var img = s.Snapshot(src);
                    using var blur = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp);
                    using var p = new SKPaint { ImageFilter = blur };
                    c.ClipRect(dev);
                    c.DrawImage(img, src.Left, src.Top, SKSamplingOptions.Default, p);
                    break;
                }
            }
        }
        finally { c.Restore(); }
    }

    /// <summary>Replaces each block by its average colour. The original pixels are not recoverable from the output.</summary>
    internal static SKBitmap Pixelate(SKSurface s, SKRectI dev, int block)
    {
        var info = new SKImageInfo(dev.Width, dev.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        using (var img = s.Snapshot(dev))
            img.ReadPixels(info, bmp.GetPixels(), info.RowBytes, 0, 0);
        var px = bmp.GetPixelSpan();
        var stride = info.RowBytes;
        for (var by = 0; by < dev.Height; by += block)
        for (var bx = 0; bx < dev.Width; bx += block)
        {
            var bw = Math.Min(block, dev.Width - bx); var bh = Math.Min(block, dev.Height - by);
            long r = 0, g = 0, b = 0, a = 0;
            for (var y = by; y < by + bh; y++)
            {
                var row = y * stride;
                for (var x = bx; x < bx + bw; x++)
                {
                    var i = row + x * 4;
                    r += px[i]; g += px[i + 1]; b += px[i + 2]; a += px[i + 3];
                }
            }
            var cnt = bw * bh;
            byte R = (byte)(r / cnt), G = (byte)(g / cnt), B = (byte)(b / cnt), A = (byte)(a / cnt);
            for (var y = by; y < by + bh; y++)
            {
                var row = y * stride;
                for (var x = bx; x < bx + bw; x++)
                {
                    var i = row + x * 4;
                    px[i] = R; px[i + 1] = G; px[i + 2] = B; px[i + 3] = A;
                }
            }
        }
        bmp.NotifyPixelsChanged();
        return bmp;
    }

    private static void DrawMagnifier(SKSurface s, SKCanvas c, MagnifierLayer mg, double scale, double op)
    {
        var src = DeviceRect(s, mg.Source, scale);
        var dstPage = mg.Rect.Normalize().ToSk();
        if (!src.IsEmpty)
        {
            using var img = s.Snapshot(src);
            c.Save();
            if (mg.Elliptical) c.ClipPath(OvalPath(dstPage), antialias: true); else c.ClipRect(dstPage);
            using var p = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * op)) };
            c.DrawImage(img, new SKRect(0, 0, img.Width, img.Height), dstPage, Smooth, p);
            c.Restore();
        }
        if (mg.BorderWidth > 0 && !mg.Border.IsTransparent)
        {
            using var bp = new SKPaint { Color = mg.Border.ToSk(op), Style = SKPaintStyle.Stroke, StrokeWidth = (float)mg.BorderWidth, IsAntialias = true };
            if (mg.Elliptical) c.DrawOval(dstPage, bp); else c.DrawRect(dstPage, bp);
        }

        static SKPath OvalPath(SKRect r) { var p = new SKPath(); p.AddOval(r); return p; }
    }

    private static void DrawText(SKCanvas c, TextLayer tx, double op)
    {
        var layout = TextLayout.Layout(tx);
        var box = layout.Box.ToSk();
        using var shadowFilter = tx.Style != TextStyle.Plain ? Shadow(true, 3) : null;

        if (tx.Style != TextStyle.Plain || !tx.Background.IsTransparent)
        {
            var radius = tx.Style == TextStyle.Plain ? 4f : 10f;
            using var path = new SKPath();
            path.AddRoundRect(box, radius, radius);
            if (tx.Style == TextStyle.Bubble && tx.TailTip is { } tip)
            {
                using var tail = BubbleTail(box, tip.ToSk());
                using var union = path.Op(tail, SKPathOp.Union);
                if (union is not null) { path.Reset(); path.AddPath(union); }
            }
            var bg = tx.Background.IsTransparent ? SKColors.White : tx.Background.ToSk();
            using var fill = new SKPaint { Color = bg.WithAlpha((byte)(bg.Alpha * op)), IsAntialias = true, ImageFilter = shadowFilter };
            c.DrawPath(path, fill);
            if (!tx.Border.IsTransparent && tx.BorderWidth > 0)
            {
                using var bp = new SKPaint { Color = tx.Border.ToSk(op), Style = SKPaintStyle.Stroke, StrokeWidth = (float)tx.BorderWidth, IsAntialias = true };
                c.DrawPath(path, bp);
            }
        }

        using var font = layout.CreateFont();
        using var tp = new SKPaint { Color = tx.Foreground.ToSk(op), IsAntialias = true };
        var align = tx.Alignment switch { TextAlignment.Center => SKTextAlign.Center, TextAlignment.Right => SKTextAlign.Right, _ => SKTextAlign.Left };
        var inner = layout.Box.Inflate(-tx.Padding);
        var x = align switch { SKTextAlign.Center => (float)inner.Center.X, SKTextAlign.Right => (float)inner.Right, _ => (float)inner.X };
        var y = (float)inner.Y - font.Metrics.Ascent;
        foreach (var line in layout.Lines)
        {
            c.DrawText(line, x, y, align, font, tp);
            y += layout.LineHeight;
        }
    }

    private static SKPath BubbleTail(SKRect box, SKPoint tip)
    {
        var cx = Math.Clamp(tip.X, box.Left + 20, box.Right - 20);
        var cy = Math.Clamp(tip.Y, box.Top + 20, box.Bottom - 20);
        var baseW = Math.Min(box.Width, box.Height) * 0.25f;
        var p = new SKPath();
        if (tip.Y > box.Bottom || tip.Y < box.Top)
        {
            var edgeY = tip.Y > box.Bottom ? box.Bottom - 2 : box.Top + 2;
            p.MoveTo(cx - baseW / 2, edgeY); p.LineTo(tip); p.LineTo(cx + baseW / 2, edgeY);
        }
        else
        {
            var edgeX = tip.X > box.Right ? box.Right - 2 : box.Left + 2;
            p.MoveTo(edgeX, cy - baseW / 2); p.LineTo(tip); p.LineTo(edgeX, cy + baseW / 2);
        }
        p.Close();
        return p;
    }
}
