using CaptureDoc.Core.Serialization;

namespace CaptureDoc.Core.Model;

/// <summary>Non-destructive page-level edits. Callers wrap these in <see cref="Editing.History"/>.</summary>
public static class PageOperations
{
    /// <summary>
    /// Renumbers auto-numbered step markers in z-order (bottom → top, i.e. creation order).
    /// Manually numbered steps keep their number; auto numbers continue after the highest manual
    /// number that comes before them.
    /// </summary>
    public static void RenumberSteps(Page page)
    {
        var n = 0;
        foreach (var s in page.AllLayers().OfType<StepLayer>())
        {
            if (s.AutoNumber) s.Number = ++n;
            else n = s.Number;
        }
    }

    public static int NextStepNumber(Page page)
    {
        var steps = page.AllLayers().OfType<StepLayer>().ToList();
        return steps.Count == 0 ? 1 : steps.Max(s => s.Number) + 1;
    }

    /// <summary>Crop = shrink canvas and shift all layers. The background image is not modified.</summary>
    public static void Crop(Page page, RectD area)
    {
        var a = area.Normalize().Intersect(new RectD(0, 0, page.Width, page.Height));
        if (a.IsEmpty) return;
        var x = Math.Round(a.X); var y = Math.Round(a.Y);
        page.Width = Math.Max(1, (int)Math.Round(a.Width));
        page.Height = Math.Max(1, (int)Math.Round(a.Height));
        ShiftAll(page, -x, -y);
    }

    /// <summary>Moves all content including magnifier sources (which a normal move keeps fixed).</summary>
    private static void ShiftAll(Page page, double dx, double dy)
    {
        foreach (var l in page.Layers) l.Translate(dx, dy);
        foreach (var m in page.AllLayers().OfType<MagnifierLayer>()) m.Source = m.Source.Offset(dx, dy);
    }

    /// <summary>Scales canvas and all layers (keeps the original asset in full resolution).</summary>
    public static void Resize(Page page, int newWidth, int newHeight)
    {
        if (newWidth <= 0 || newHeight <= 0 || page.Width <= 0) return;
        var f = newWidth / (double)page.Width;
        foreach (var l in page.Layers) l.ScaleAll(f);
        page.Width = newWidth;
        page.Height = newHeight;
    }

    public enum Anchor { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }

    /// <summary>Changes the canvas size without scaling content.</summary>
    public static void ResizeCanvas(Page page, int newWidth, int newHeight, Anchor anchor = Anchor.Center)
    {
        if (newWidth <= 0 || newHeight <= 0) return;
        var dw = newWidth - page.Width; var dh = newHeight - page.Height;
        double dx = anchor switch { Anchor.TopLeft or Anchor.Left or Anchor.BottomLeft => 0, Anchor.TopRight or Anchor.Right or Anchor.BottomRight => dw, _ => dw / 2.0 };
        double dy = anchor switch { Anchor.TopLeft or Anchor.Top or Anchor.TopRight => 0, Anchor.BottomLeft or Anchor.Bottom or Anchor.BottomRight => dh, _ => dh / 2.0 };
        ShiftAll(page, Math.Round(dx), Math.Round(dy));
        page.Width = newWidth; page.Height = newHeight;
    }

    /// <summary>Moves a layer within its container. +1 = one up (towards top).</summary>
    public static bool Move(Page page, Guid id, int delta)
    {
        var list = page.FindContainer(id);
        if (list is null) return false;
        var i = list.FindIndex(l => l.Id == id);
        var j = Math.Clamp(i + delta, 0, list.Count - 1);
        // never move below a background image at index 0
        if (j == 0 && list == page.Layers && list[0] is ImageLayer { IsBackground: true } && list[i] is not ImageLayer { IsBackground: true }) j = 1;
        if (i == j) return false;
        var l = list[i];
        list.RemoveAt(i);
        list.Insert(j, l);
        return true;
    }

    public static void BringToFront(Page page, Guid id) => Move(page, id, int.MaxValue / 2);
    public static void SendToBack(Page page, Guid id) => Move(page, id, -(int.MaxValue / 2));

    public static bool Remove(Page page, Guid id)
    {
        var list = page.FindContainer(id);
        return list is not null && list.RemoveAll(l => l.Id == id) > 0;
    }

    /// <summary>Deep copy with new ids, inserted above the original.</summary>
    public static Layer? Duplicate(Page page, Guid id, double offset = 16)
    {
        var list = page.FindContainer(id);
        var src = page.Find(id);
        if (list is null || src is null) return null;
        var copy = CloneWithNewIds(src);
        copy.Name = string.IsNullOrEmpty(src.Name) ? "" : src.Name + " Kopie";
        if (copy is ImageLayer im) im.IsBackground = false;
        copy.Translate(offset, offset);
        list.Insert(list.IndexOf(src) + 1, copy);
        return copy;
    }

    public static Layer CloneWithNewIds(Layer layer)
    {
        var copy = ProjectJson.Clone(layer);
        AssignNewIds(copy);
        return copy;
    }

    private static void AssignNewIds(Layer l)
    {
        l.Id = Guid.NewGuid();
        if (l is GroupLayer g) foreach (var c in g.Children) AssignNewIds(c);
    }

    /// <summary>Groups top-level layers (same container). Group is placed at the topmost member's position.</summary>
    public static GroupLayer? Group(Page page, IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count < 2) return null;
        var list = page.FindContainer(ids.First());
        if (list is null || ids.Any(id => page.FindContainer(id) != list)) return null;
        var members = list.Where(l => ids.Contains(l.Id) && l is not ImageLayer { IsBackground: true }).ToList();
        if (members.Count < 2) return null;
        var insertAt = list.IndexOf(members[^1]) - (members.Count - 1);
        foreach (var m in members) list.Remove(m);
        var g = new GroupLayer { Name = "Gruppe", Children = members };
        list.Insert(Math.Clamp(insertAt, 0, list.Count), g);
        return g;
    }

    public static IReadOnlyList<Layer> Ungroup(Page page, Guid groupId)
    {
        var list = page.FindContainer(groupId);
        if (list?.FirstOrDefault(l => l.Id == groupId) is not GroupLayer g) return Array.Empty<Layer>();
        var i = list.IndexOf(g);
        list.RemoveAt(i);
        foreach (var c in g.Children)
        {
            c.Opacity *= g.Opacity;
            if (!g.Visible) c.Visible = false;
        }
        list.InsertRange(i, g.Children);
        return g.Children;
    }

    /// <summary>Adds a transparent margin, a drop shadow and a thin border around the screenshot (non-destructive).</summary>
    public static void AddFrameAndShadow(Page page, int margin = 32, double cornerRadius = 8)
    {
        if (page.Layers.FirstOrDefault() is not ImageLayer { IsBackground: true } bg) return;
        ResizeCanvas(page, page.Width + 2 * margin, page.Height + 2 * margin);
        page.Background = ColorRgba.Transparent;
        bg.Shadow = true; bg.BorderWidth = 1; bg.CornerRadius = cornerRadius;
    }

    /// <summary>Default display name, e.g. "Pfeil 3".</summary>
    public static string NextName(Page page, Layer layer)
    {
        var kind = layer.Kind;
        var n = page.AllLayers().Count(l => l.Kind == kind && !ReferenceEquals(l, layer)) + 1;
        return $"{kind} {n}";
    }

    /// <summary>True if any visible redaction exists (used for the "original still in project" warning).</summary>
    public static bool HasRedactions(Page page) => page.AllLayers().Any(l => l is RedactLayer);
}
