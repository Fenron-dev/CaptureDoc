using CaptureDoc.Core.Model;

namespace CaptureDoc.App.Video;

/// <summary>Recorded mouse clicks → editable ring annotations (double clicks merged).</summary>
public static class ClickMarkers
{
    /// <param name="clicks">video seconds and positions in recorded pixels</param>
    /// <param name="timeOffset">where the recording starts on the sequence</param>
    /// <param name="scale">recorded pixels → canvas pixels</param>
    public static List<Layer> Create(IReadOnlyList<(double T, int X, int Y)> clicks, int recordedWidth, double duration,
        double timeOffset = 0, double scale = 1, double ox = 0, double oy = 0, int startNumber = 1)
    {
        var groups = new List<(double T, int X, int Y, int Count)>();
        foreach (var c in clicks.OrderBy(c => c.T))
        {
            if (groups.Count > 0 && c.T - groups[^1].T < 0.5 && Math.Abs(c.X - groups[^1].X) <= 12 && Math.Abs(c.Y - groups[^1].Y) <= 12)
                groups[^1] = groups[^1] with { Count = groups[^1].Count + 1 };
            else groups.Add((c.T, c.X, c.Y, 1));
        }
        var list = new List<Layer>();
        var n = startNumber - 1;
        var r = Math.Max(12, recordedWidth / 70.0) * scale;
        foreach (var g in groups)
        {
            n++;
            double x = ox + g.X * scale, y = oy + g.Y * scale;
            list.Add(new ShapeLayer
            {
                Name = g.Count >= 2 ? $"Doppelklick {n}" : $"Klick {n}", Shape = ShapeKind.Ellipse, Rect = new RectD(x - r, y - r, 2 * r, 2 * r),
                Stroke = new ColorRgba(255, 200, 0), Fill = new ColorRgba(255, 214, 0, 60), StrokeWidth = Math.Max(3, r / 4), Shadow = false,
                Timing = new LayerTiming
                {
                    StartSeconds = Math.Round(timeOffset + Math.Max(0, g.T - 0.05), 2),
                    EndSeconds = Math.Round(timeOffset + Math.Min(duration, g.T + (g.Count >= 2 ? 0.9 : 0.6)), 2),
                },
            });
        }
        return list;
    }

    /// <summary>Live drawings of a recording placed on the sequence (time offset, scaled into the clip's rectangle).</summary>
    public static List<Layer> PlaceLive(IReadOnlyList<Layer> live, double timeOffset, double scale, double ox, double oy)
    {
        var list = new List<Layer>();
        foreach (var l in live)
        {
            var c = Core.Serialization.ProjectJson.Clone(l);
            if (Math.Abs(scale - 1) > 1e-6) c.ScaleAll(scale);
            c.Translate(ox, oy);
            if (c.Timing is { } tm)
            {
                tm.StartSeconds += timeOffset; tm.EndSeconds += timeOffset;
                foreach (var k in tm.Keyframes) { k.TimeSeconds += timeOffset; }
            }
            list.Add(c);
        }
        return list;
    }
}
