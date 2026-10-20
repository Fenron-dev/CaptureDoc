using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaptureDoc.Core.Model;

/// <summary>Point in page coordinates (image pixels of the page canvas).</summary>
public readonly record struct PointD(double X, double Y)
{
    public PointD Offset(double dx, double dy) => new(X + dx, Y + dy);
    public static PointD operator +(PointD a, PointD b) => new(a.X + b.X, a.Y + b.Y);
    public static PointD operator -(PointD a, PointD b) => new(a.X - b.X, a.Y - b.Y);
    public double DistanceTo(PointD o) => Math.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y));
}

/// <summary>Axis-aligned rectangle. Width/Height may be negative while dragging; call <see cref="Normalize"/>.</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public PointD Center => new(X + Width / 2, Y + Height / 2);
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static RectD FromPoints(PointD a, PointD b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public RectD Normalize() => FromPoints(new PointD(X, Y), new PointD(X + Width, Y + Height));
    public RectD Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };
    public RectD Inflate(double d) => new(X - d, Y - d, Width + 2 * d, Height + 2 * d);
    public bool Contains(PointD p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
    public bool IntersectsWith(RectD o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;

    public RectD Union(RectD o)
    {
        if (IsEmpty && Width == 0 && Height == 0 && X == 0 && Y == 0) return o;
        var x = Math.Min(X, o.X); var y = Math.Min(Y, o.Y);
        return new(x, y, Math.Max(Right, o.Right) - x, Math.Max(Bottom, o.Bottom) - y);
    }

    public RectD Intersect(RectD o)
    {
        var x = Math.Max(X, o.X); var y = Math.Max(Y, o.Y);
        var r = Math.Min(Right, o.Right); var b = Math.Min(Bottom, o.Bottom);
        return r <= x || b <= y ? default : new RectD(x, y, r - x, b - y);
    }

    public RectD Scale(double f) => new(X * f, Y * f, Width * f, Height * f);
}

/// <summary>Integer rectangle in physical (device) pixels, e.g. virtual-screen coordinates.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public bool Contains(int px, int py) => px >= X && px < Right && py >= Y && py < Bottom;

    public PixelRect Intersect(PixelRect o)
    {
        var x = Math.Max(X, o.X); var y = Math.Max(Y, o.Y);
        var r = Math.Min(Right, o.Right); var b = Math.Min(Bottom, o.Bottom);
        return r <= x || b <= y ? default : new PixelRect(x, y, r - x, b - y);
    }

    public PixelRect Union(PixelRect o)
    {
        if (IsEmpty) return o;
        if (o.IsEmpty) return this;
        var x = Math.Min(X, o.X); var y = Math.Min(Y, o.Y);
        return new(x, y, Math.Max(Right, o.Right) - x, Math.Max(Bottom, o.Bottom) - y);
    }
}

/// <summary>sRGB color with alpha. Serialized as "#RRGGBBAA".</summary>
[JsonConverter(typeof(ColorRgbaJsonConverter))]
public readonly record struct ColorRgba(byte R, byte G, byte B, byte A = 255)
{
    public static readonly ColorRgba Transparent = new(0, 0, 0, 0);
    public static readonly ColorRgba Black = new(0, 0, 0);
    public static readonly ColorRgba White = new(255, 255, 255);
    public static readonly ColorRgba Red = new(231, 29, 54);
    public static readonly ColorRgba Yellow = new(255, 214, 0);
    public static readonly ColorRgba Blue = new(30, 120, 230);

    public bool IsTransparent => A == 0;
    public ColorRgba WithAlpha(byte a) => this with { A = a };

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";

    public static ColorRgba Parse(string s)
    {
        if (!TryParse(s, out var c)) throw new FormatException($"Invalid color '{s}'.");
        return c;
    }

    public static bool TryParse(string? s, out ColorRgba color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim().TrimStart('#');
        if (s.Length is not (6 or 8)) return false;
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        if (s.Length == 6) v = (v << 8) | 0xFF;
        color = new ColorRgba((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public override string ToString() => ToHex();
}

internal sealed class ColorRgbaJsonConverter : JsonConverter<ColorRgba>
{
    public override ColorRgba Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ColorRgba.TryParse(reader.GetString(), out var c) ? c : throw new JsonException("Invalid color value.");

    public override void Write(Utf8JsonWriter writer, ColorRgba value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToHex());
}
