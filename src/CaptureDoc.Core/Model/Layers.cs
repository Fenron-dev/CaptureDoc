using System.Text.Json.Serialization;

namespace CaptureDoc.Core.Model;

/// <summary>
/// Base of every editable object on a page. Layers are stored bottom-to-top.
/// Every annotation is its own layer, so it stays editable after creation.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ImageLayer), "image")]
[JsonDerivedType(typeof(ShapeLayer), "shape")]
[JsonDerivedType(typeof(LineLayer), "line")]
[JsonDerivedType(typeof(TextLayer), "text")]
[JsonDerivedType(typeof(StepLayer), "step")]
[JsonDerivedType(typeof(FreehandLayer), "freehand")]
[JsonDerivedType(typeof(HighlightLayer), "highlight")]
[JsonDerivedType(typeof(RedactLayer), "redact")]
[JsonDerivedType(typeof(SpotlightLayer), "spotlight")]
[JsonDerivedType(typeof(MagnifierLayer), "magnifier")]
[JsonDerivedType(typeof(GroupLayer), "group")]
public abstract class Layer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    /// <summary>0..1</summary>
    public double Opacity { get; set; } = 1.0;
    /// <summary>Optional timing for video annotations (Phase 7). Null = always visible.</summary>
    public LayerTiming? Timing { get; set; }

    [JsonIgnore] public abstract string Kind { get; }
    /// <summary>Bounding box in page coordinates.</summary>
    [JsonIgnore] public abstract RectD Bounds { get; }

    public abstract void Translate(double dx, double dy);
    /// <summary>Moves/resizes the layer so that its bounds become <paramref name="target"/>.</summary>
    public abstract void SetBounds(RectD target);
    /// <summary>Uniform scale around the origin (used by page resize).</summary>
    public abstract void ScaleAll(double factor);

    public virtual bool HitTest(PointD p, double tolerance) => Bounds.Inflate(tolerance).Contains(p);
}

/// <summary>Layer whose geometry is a rectangle.</summary>
public abstract class BoxLayer : Layer
{
    public RectD Rect { get; set; }
    public override RectD Bounds => Rect.Normalize();
    public override void Translate(double dx, double dy) => Rect = Rect.Offset(dx, dy);
    public override void SetBounds(RectD target) => Rect = target.Normalize();
    public override void ScaleAll(double f) => Rect = Rect.Scale(f);
}

public sealed class ImageLayer : BoxLayer
{
    public override string Kind => IsBackground ? "Hintergrund" : "Bild";
    public string AssetId { get; set; } = "";
    public bool IsBackground { get; set; }
    public bool Shadow { get; set; }
    public double BorderWidth { get; set; }
    public ColorRgba BorderColor { get; set; } = new(160, 160, 160);
    public double CornerRadius { get; set; }
}

public enum ShapeKind { Rectangle, Ellipse, RoundedRectangle }

public sealed class ShapeLayer : BoxLayer
{
    public override string Kind => Shape switch
    {
        ShapeKind.Ellipse => "Ellipse",
        ShapeKind.RoundedRectangle => "Abgerundetes Rechteck",
        _ => "Rechteck",
    };
    public ShapeKind Shape { get; set; }
    public ColorRgba Stroke { get; set; } = ColorRgba.Red;
    public ColorRgba Fill { get; set; } = ColorRgba.Transparent;
    public double StrokeWidth { get; set; } = 4;
    public double CornerRadius { get; set; } = 12;
    public bool Shadow { get; set; } = true;
}

public enum LineCap { None, Arrow, Dot }

public sealed class LineLayer : Layer
{
    public override string Kind => EndCap == LineCap.Arrow || StartCap == LineCap.Arrow ? "Pfeil" : "Linie";
    public PointD Start { get; set; }
    public PointD End { get; set; }
    public ColorRgba Stroke { get; set; } = ColorRgba.Red;
    public double StrokeWidth { get; set; } = 5;
    public LineCap StartCap { get; set; } = LineCap.None;
    public LineCap EndCap { get; set; } = LineCap.Arrow;
    public bool Shadow { get; set; } = true;

    public override RectD Bounds => RectD.FromPoints(Start, End).Inflate(StrokeWidth * 2);
    public override void Translate(double dx, double dy) { Start = Start.Offset(dx, dy); End = End.Offset(dx, dy); }
    public override void ScaleAll(double f) { Start = new(Start.X * f, Start.Y * f); End = new(End.X * f, End.Y * f); StrokeWidth *= f; }

    public override void SetBounds(RectD target)
    {
        var old = RectD.FromPoints(Start, End);
        Start = Map(Start, old, target);
        End = Map(End, old, target);
    }

    internal static PointD Map(PointD p, RectD from, RectD to)
    {
        var sx = from.Width < 1e-9 ? 1 : to.Width / from.Width;
        var sy = from.Height < 1e-9 ? 1 : to.Height / from.Height;
        return new(to.X + (p.X - from.X) * sx, to.Y + (p.Y - from.Y) * sy);
    }

    public override bool HitTest(PointD p, double tolerance)
    {
        var d = DistanceToSegment(p, Start, End);
        return d <= Math.Max(tolerance, StrokeWidth);
    }

    internal static double DistanceToSegment(PointD p, PointD a, PointD b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y;
        var len2 = dx * dx + dy * dy;
        if (len2 < 1e-12) return p.DistanceTo(a);
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        return p.DistanceTo(new PointD(a.X + t * dx, a.Y + t * dy));
    }
}

public enum TextStyle { Plain, InfoBox, Bubble }
public enum TextAlignment { Left, Center, Right }

public sealed class TextLayer : BoxLayer
{
    public override string Kind => Style switch { TextStyle.InfoBox => "Infobox", TextStyle.Bubble => "Sprechblase", _ => "Text" };
    public string Text { get; set; } = "Text";
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 28;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;
    public ColorRgba Foreground { get; set; } = ColorRgba.Red;
    public ColorRgba Background { get; set; } = ColorRgba.Transparent;
    public ColorRgba Border { get; set; } = ColorRgba.Transparent;
    public double BorderWidth { get; set; } = 2;
    public double Padding { get; set; } = 10;
    public TextStyle Style { get; set; } = TextStyle.Plain;
    /// <summary>Tail tip for speech bubbles (page coordinates).</summary>
    public PointD? TailTip { get; set; }
    public override void Translate(double dx, double dy) { base.Translate(dx, dy); if (TailTip is { } t) TailTip = t.Offset(dx, dy); }
    public override void ScaleAll(double f) { base.ScaleAll(f); FontSize *= f; Padding *= f; if (TailTip is { } t) TailTip = new(t.X * f, t.Y * f); }
}

/// <summary>Numbered step marker. Number stays editable; <see cref="AutoNumber"/> lets the page renumber it.</summary>
public sealed class StepLayer : Layer
{
    public override string Kind => "Schritt";
    public PointD Center { get; set; }
    public double Radius { get; set; } = 20;
    public int Number { get; set; } = 1;
    public bool AutoNumber { get; set; } = true;
    public ColorRgba Fill { get; set; } = ColorRgba.Red;
    public ColorRgba Foreground { get; set; } = ColorRgba.White;
    public bool Shadow { get; set; } = true;

    public override RectD Bounds => new(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);
    public override void Translate(double dx, double dy) => Center = Center.Offset(dx, dy);
    public override void ScaleAll(double f) { Center = new(Center.X * f, Center.Y * f); Radius *= f; }
    public override void SetBounds(RectD target)
    {
        var t = target.Normalize();
        Center = t.Center;
        Radius = Math.Max(4, Math.Min(t.Width, t.Height) / 2);
    }
    public override bool HitTest(PointD p, double tolerance) => p.DistanceTo(Center) <= Radius + tolerance;
}

public sealed class FreehandLayer : Layer
{
    public override string Kind => Highlighter ? "Textmarker" : "Freihand";
    public List<PointD> Points { get; set; } = new();
    public ColorRgba Stroke { get; set; } = ColorRgba.Red;
    public double StrokeWidth { get; set; } = 4;
    /// <summary>Highlighter pen: wide, semi-transparent stroke (the colour's alpha is used).</summary>
    public bool Highlighter { get; set; }

    public override RectD Bounds
    {
        get
        {
            if (Points.Count == 0) return default;
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var p in Points) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
            return new RectD(x0, y0, x1 - x0, y1 - y0).Inflate(StrokeWidth / 2);
        }
    }
    public override void Translate(double dx, double dy) { for (var i = 0; i < Points.Count; i++) Points[i] = Points[i].Offset(dx, dy); }
    public override void ScaleAll(double f) { for (var i = 0; i < Points.Count; i++) Points[i] = new(Points[i].X * f, Points[i].Y * f); StrokeWidth *= f; }
    public override void SetBounds(RectD target)
    {
        var old = Bounds.Inflate(-StrokeWidth / 2);
        for (var i = 0; i < Points.Count; i++) Points[i] = LineLayer.Map(Points[i], old, target);
    }
    public override bool HitTest(PointD p, double tolerance)
    {
        for (var i = 1; i < Points.Count; i++)
            if (LineLayer.DistanceToSegment(p, Points[i - 1], Points[i]) <= Math.Max(tolerance, StrokeWidth)) return true;
        return false;
    }
}

public sealed class HighlightLayer : BoxLayer
{
    public override string Kind => "Hervorhebung";
    public ColorRgba Color { get; set; } = ColorRgba.Yellow;
}

public enum RedactMode { Blackout, Pixelate, Blur }

/// <summary>
/// Hides content. In exported raster media the result is burned into the pixels.
/// The project file still contains the original image underneath (see UI warning).
/// </summary>
public sealed class RedactLayer : BoxLayer
{
    public override string Kind => Mode switch { RedactMode.Pixelate => "Verpixeln", RedactMode.Blur => "Weichzeichnen", _ => "Schwärzung" };
    public RedactMode Mode { get; set; } = RedactMode.Blackout;
    /// <summary>Pixel block size (pixelate) or blur sigma (blur).</summary>
    public double Strength { get; set; } = 12;
    public ColorRgba Fill { get; set; } = ColorRgba.Black;
}

public sealed class SpotlightLayer : BoxLayer
{
    public override string Kind => "Spotlight";
    public bool Elliptical { get; set; }
    /// <summary>Darkening of everything outside the spot, 0..1.</summary>
    public double Dim { get; set; } = 0.55;
}

/// <summary>Shows <see cref="Source"/> region of everything below, enlarged into <see cref="BoxLayer.Rect"/>.</summary>
public sealed class MagnifierLayer : BoxLayer
{
    public override string Kind => "Lupe";
    public RectD Source { get; set; }
    public ColorRgba Border { get; set; } = ColorRgba.Red;
    public double BorderWidth { get; set; } = 3;
    public bool Elliptical { get; set; }
    public override void Translate(double dx, double dy) => Rect = Rect.Offset(dx, dy); // Source stays fixed
    public override void ScaleAll(double f) { base.ScaleAll(f); Source = Source.Scale(f); BorderWidth *= f; }
}

public sealed class GroupLayer : Layer
{
    public override string Kind => "Gruppe";
    public List<Layer> Children { get; set; } = new();

    /// <summary>All nested children (depth first).</summary>
    public IEnumerable<Layer> AllChildren()
    {
        foreach (var c in Children)
        {
            yield return c;
            if (c is GroupLayer g) foreach (var cc in g.AllChildren()) yield return cc;
        }
    }

    public override RectD Bounds
    {
        get
        {
            RectD r = default; var first = true;
            foreach (var c in Children)
            {
                if (first) { r = c.Bounds; first = false; } else r = r.Union(c.Bounds);
            }
            return r;
        }
    }
    public override void Translate(double dx, double dy) { foreach (var c in Children) c.Translate(dx, dy); }
    public override void ScaleAll(double f) { foreach (var c in Children) c.ScaleAll(f); }
    public override void SetBounds(RectD target)
    {
        var old = Bounds;
        foreach (var c in Children)
        {
            var b = c.Bounds;
            var tl = LineLayer.Map(new PointD(b.X, b.Y), old, target);
            var br = LineLayer.Map(new PointD(b.Right, b.Bottom), old, target);
            c.SetBounds(RectD.FromPoints(tl, br));
        }
    }
    public override bool HitTest(PointD p, double tolerance) => Children.Any(c => c.Visible && c.HitTest(p, tolerance));
}

/// <summary>Time range and keyframes for video annotations (Phase 7, model only).</summary>
public sealed class LayerTiming
{
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public List<Keyframe> Keyframes { get; set; } = new();
}

public sealed class Keyframe
{
    public double TimeSeconds { get; set; }
    public RectD Bounds { get; set; }
    public double Opacity { get; set; } = 1;
    public string Easing { get; set; } = "linear";
}
