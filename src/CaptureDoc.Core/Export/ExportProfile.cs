using System.Text.Json;
using CaptureDoc.Core.Serialization;
using CaptureDoc.Core.Storage;

namespace CaptureDoc.Core.Export;

public enum ImageFormat { Png, Jpeg, WebP }
/// <summary>Soft = best effort, report if missed. Hard = never produce a file above the limit.</summary>
public enum SizeLimitKind { None, Soft, Hard }

public sealed class ExportProfile
{
    public string Id { get; set; } = "custom";
    public string Name { get; set; } = "Benutzerdefiniert";
    public bool BuiltIn { get; set; }
    public ImageFormat Format { get; set; } = ImageFormat.Png;
    public int? MaxWidth { get; set; }
    public int? MaxHeight { get; set; }
    /// <summary>Applied before Max* limits. 1 = original.</summary>
    public double Scale { get; set; } = 1.0;
    /// <summary>1..100 (lossy formats). WebP 100 + Lossless = lossless.</summary>
    public int Quality { get; set; } = 90;
    public bool Lossless { get; set; } = true;
    public bool KeepTransparency { get; set; } = true;
    public bool StripMetadata { get; set; } = true;
    public long? MaxBytes { get; set; }
    public SizeLimitKind LimitKind { get; set; } = SizeLimitKind.None;
    public int MinQuality { get; set; } = 40;
    public int MinWidth { get; set; } = 320;
    /// <summary>Obsidian embed display width (independent of pixel size).</summary>
    public int? DisplayWidth { get; set; }

    public string FileExtension => Format switch { ImageFormat.Jpeg => ".jpg", ImageFormat.WebP => ".webp", _ => ".png" };

    public ExportProfile Copy(string newId, string newName)
    {
        var c = ProjectJson.Clone(this);
        c.Id = newId; c.Name = newName; c.BuiltIn = false;
        return c;
    }
}

public static class BuiltInProfiles
{
    public static IReadOnlyList<ExportProfile> All { get; } = new List<ExportProfile>
    {
        new() { Id = "original", Name = "Original Quality", BuiltIn = true, Format = ImageFormat.Png, Lossless = true },
        new() { Id = "documentation-png", Name = "Documentation PNG", BuiltIn = true, Format = ImageFormat.Png, MaxWidth = 1600, DisplayWidth = 700 },
        new() { Id = "obsidian", Name = "Obsidian Optimized", BuiltIn = true, Format = ImageFormat.WebP, Lossless = false, Quality = 85, MaxWidth = 1400, DisplayWidth = 700, MaxBytes = 400 * 1024, LimitKind = SizeLimitKind.Soft },
        new() { Id = "small", Name = "Small Screenshot", BuiltIn = true, Format = ImageFormat.Jpeg, Lossless = false, Quality = 80, MaxWidth = 800, MaxBytes = 150 * 1024, LimitKind = SizeLimitKind.Soft },
        new() { Id = "email", Name = "Email Attachment", BuiltIn = true, Format = ImageFormat.Jpeg, Lossless = false, Quality = 85, MaxWidth = 1280, MaxBytes = 500 * 1024, LimitKind = SizeLimitKind.Hard },
        new() { Id = "support", Name = "Support Ticket", BuiltIn = true, Format = ImageFormat.Png, MaxWidth = 1920, MaxBytes = 2 * 1024 * 1024, LimitKind = SizeLimitKind.Hard },
        new() { Id = "custom", Name = "Custom", BuiltIn = true, Format = ImageFormat.Png },
    };

    public static ExportProfile Get(string id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];
}

/// <summary>User profiles in Data/Presets/export-profiles.json (shared across OS).</summary>
public sealed class ExportProfileStore
{
    private readonly string _path;
    public List<ExportProfile> UserProfiles { get; private set; } = new();

    public ExportProfileStore(string presetsDir) => _path = Path.Combine(presetsDir, "export-profiles.json");

    public IEnumerable<ExportProfile> All => BuiltInProfiles.All.Concat(UserProfiles);
    public ExportProfile Get(string id) => All.FirstOrDefault(p => p.Id == id) ?? BuiltInProfiles.All[0];

    public void Load()
    {
        if (!File.Exists(_path)) return;
        try { UserProfiles = JsonSerializer.Deserialize<List<ExportProfile>>(File.ReadAllText(_path), ProjectJson.Options) ?? new(); }
        catch (JsonException) { UserProfiles = new(); }
    }

    public void Save() => AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(UserProfiles, ProjectJson.Options));

    public ExportProfile Duplicate(ExportProfile source)
    {
        var id = "user-" + Guid.NewGuid().ToString("N")[..8];
        var copy = source.Copy(id, source.Name + " (Kopie)");
        UserProfiles.Add(copy);
        return copy;
    }
}
