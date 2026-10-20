namespace CaptureDoc.Core.Model;

/// <summary>Root of a .capturedoc file.</summary>
public sealed class CaptureProject
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Unbenannt";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.Now;
    /// <summary>Incremented on each save; used for conflict detection.</summary>
    public long Revision { get; set; }
    public List<Page> Pages { get; set; } = new();
    public Guide Guide { get; set; } = new();
    /// <summary>Asset index (content hash → metadata). Bytes live in <see cref="AssetStore"/>.</summary>
    public Dictionary<string, AssetInfo> Assets { get; set; } = new();
    public List<Export.ExportProfile> ExportProfiles { get; set; } = new();
    public Dictionary<string, string> Settings { get; set; } = new();

    public Page? FindPage(Guid id) => Pages.FirstOrDefault(p => p.Id == id);
}

public sealed class AssetInfo
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public string MediaType { get; set; } = "image/png";
    public int Width { get; set; }
    public int Height { get; set; }
    public long Length { get; set; }
    public DateTimeOffset Captured { get; set; } = DateTimeOffset.Now;
}

/// <summary>One editable image (canvas + layers). An image project has one page; a guide has many.</summary>
public sealed class Page
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Seite";
    public int Width { get; set; }
    public int Height { get; set; }
    public ColorRgba Background { get; set; } = ColorRgba.White;
    /// <summary>Bottom → top.</summary>
    public List<Layer> Layers { get; set; } = new();

    public IEnumerable<Layer> AllLayers()
    {
        foreach (var l in Layers)
        {
            yield return l;
            if (l is GroupLayer g)
                foreach (var c in Flatten(g)) yield return c;
        }
    }

    private static IEnumerable<Layer> Flatten(GroupLayer g)
    {
        foreach (var c in g.Children)
        {
            yield return c;
            if (c is GroupLayer cg)
                foreach (var cc in Flatten(cg)) yield return cc;
        }
    }

    public Layer? Find(Guid id) => AllLayers().FirstOrDefault(l => l.Id == id);

    /// <summary>Returns the list that directly contains the layer (page or group children).</summary>
    public List<Layer>? FindContainer(Guid id) => FindContainer(Layers, id);

    private static List<Layer>? FindContainer(List<Layer> list, Guid id)
    {
        foreach (var l in list)
        {
            if (l.Id == id) return list;
            if (l is GroupLayer g && FindContainer(g.Children, id) is { } r) return r;
        }
        return null;
    }

    /// <summary>Topmost visible layer under the point (descends into groups, returns the top-level item).</summary>
    public Layer? HitTest(PointD p, double tolerance, bool includeLocked = false)
    {
        for (var i = Layers.Count - 1; i >= 0; i--)
        {
            var l = Layers[i];
            if (!l.Visible || (l.Locked && !includeLocked)) continue;
            if (l is ImageLayer { IsBackground: true }) continue;
            if (l.HitTest(p, tolerance)) return l;
        }
        return null;
    }
}

/// <summary>Step-by-step guide built from pages (Documentation Studio).</summary>
public sealed class Guide
{
    public string Title { get; set; } = "";
    public string TemplateId { get; set; } = "step-by-step";
    public List<string> Tags { get; set; } = new();
    public string Status { get; set; } = "entwurf";
    /// <summary>Introductory text (Markdown allowed).</summary>
    public string Introduction { get; set; } = "";
    public string Prerequisites { get; set; } = "";
    public List<GuideStep> Steps { get; set; } = new();
    public List<string> Checklist { get; set; } = new();
    public string Conclusion { get; set; } = "";
    /// <summary>Stable id written into exported notes (frontmatter) to recognise earlier exports.</summary>
    public Guid ExportId { get; set; } = Guid.NewGuid();
    /// <summary>Extra template sections (heading → text).</summary>
    public Dictionary<string, string> Sections { get; set; } = new();
}

public sealed class GuideStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public Guid? PageId { get; set; }
    public List<Callout> Callouts { get; set; } = new();
    /// <summary>Display width in Obsidian (independent of exported pixel size). Null = profile default.</summary>
    public int? DisplayWidth { get; set; }
}

public enum CalloutKind { Info, Note, Tip, Warning, Danger, Success, Question, Example }

public sealed class Callout
{
    public CalloutKind Kind { get; set; } = CalloutKind.Info;
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
}
