using CaptureDoc.App.Services;
using CaptureDoc.Core.Editing;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Serialization;
using CaptureDoc.Core.Storage;
using CaptureDoc.Rendering;
using SkiaSharp;

using Page = CaptureDoc.Core.Model.Page;

namespace CaptureDoc.App.Editor;

/// <summary>
/// An open project (one or more pages) with assets, history per page, file path, lock and autosave.
/// Shared by the image editor and the Documentation Studio.
/// </summary>
public sealed class ProjectSession : IDisposable
{
    private readonly Dictionary<Guid, History> _histories = new();
    private ProjectLock? _lock;

    public CaptureProject Project { get; private set; }
    public AssetStore Assets { get; private set; }
    public AssetImageCache Images { get; private set; }
    public SceneRenderer Renderer { get; private set; }
    public string? FilePath { get; private set; }
    public bool IsDirty { get; private set; }
    public bool ReadOnly { get; private set; }
    public event EventHandler? Changed;

    public ProjectSession(CaptureProject project, AssetStore assets, string? filePath = null)
    {
        Project = project; Assets = assets; FilePath = filePath;
        Images = new AssetImageCache(assets);
        Renderer = new SceneRenderer(Images);
    }

    /// <summary>New single-page project from a capture.</summary>
    public static ProjectSession FromCapture(SKImage image, byte[] png, string title)
    {
        var assets = new AssetStore();
        var id = assets.Add(png);
        var project = new CaptureProject { Title = title };
        project.Assets[id] = new AssetInfo { Id = id, FileName = id + ".png", Width = image.Width, Height = image.Height, Length = png.LongLength };
        var page = NewPageFor(id, image.Width, image.Height, title);
        project.Pages.Add(page);
        var s = new ProjectSession(project, assets);
        s.Images.Put(id, image);
        s.IsDirty = true;
        return s;
    }

    public static Page NewPageFor(string assetId, int w, int h, string name) => new()
    {
        Name = name, Width = w, Height = h,
        Layers = { new ImageLayer { AssetId = assetId, IsBackground = true, Locked = true, Name = "Hintergrund", Rect = new RectD(0, 0, w, h) } },
    };

    public static ProjectSession Open(string path, out List<string> warnings)
    {
        var loaded = ProjectFile.Load(path);
        warnings = loaded.Warnings.ToList();
        var s = new ProjectSession(loaded.Project, loaded.Assets, path);
        if (loaded.WasMigrated) warnings.Add($"Projekt wurde von Format {loaded.LoadedFormatVersion} migriert und wird beim Speichern im aktuellen Format geschrieben.");
        if (!ProjectLock.TryAcquire(path, out var l, out var heldBy))
        {
            s.ReadOnly = true;
            warnings.Add(heldBy is null
                ? "Das Projekt liegt auf einem schreibgeschützten Datenträger und wird schreibgeschützt geöffnet."
                : $"Das Projekt ist bereits geöffnet (Rechner {heldBy.Machine}, seit {heldBy.Since:g}). Es wird schreibgeschützt geöffnet.");
        }
        s._lock = l;
        return s;
    }

    /// <summary>Opens an autosave copy and re-targets it to the original file (no lock on the recovery copy).</summary>
    public static ProjectSession OpenRecovery(string recoveryFile, string? originalPath)
    {
        var loaded = ProjectFile.Load(recoveryFile);
        var s = new ProjectSession(loaded.Project, loaded.Assets, null);
        // an autosave copy must never become the "original" (old data could point there)
        if (originalPath is not null && Recovery.Dir is not null && Path.GetFullPath(originalPath).StartsWith(Path.GetFullPath(Recovery.Dir), StringComparison.OrdinalIgnoreCase)) originalPath = null;
        if (originalPath is not null && File.Exists(originalPath) && ProjectFile.TryReadManifest(originalPath) is { } m && m.ProjectId == loaded.Project.Id)
        {
            s.FilePath = originalPath;
            s.Project.Revision = m.Revision; // so saving over the original is not reported as a conflict
            if (ProjectLock.TryAcquire(originalPath, out var l, out _)) s._lock = l; else s.ReadOnly = true;
        }
        s.IsDirty = true;
        return s;
    }

    public History HistoryFor(Page page)
    {
        if (!_histories.TryGetValue(page.Id, out var h))
        {
            h = new History(() => ProjectJson.SerializePage(page), json => ProjectJson.RestorePageInto(page, json));
            h.Changed += (_, _) => MarkDirty();
            _histories[page.Id] = h;
        }
        return h;
    }

    public void MarkDirty()
    {
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds a capture as new page (Documentation Studio) and returns it.</summary>
    public Page AddPage(SKImage image, byte[] png, string name)
    {
        var id = Assets.Add(png);
        Project.Assets[id] = new AssetInfo { Id = id, FileName = id + ".png", Width = image.Width, Height = image.Height, Length = png.LongLength };
        Images.Put(id, image);
        var page = NewPageFor(id, image.Width, image.Height, name);
        Project.Pages.Add(page);
        MarkDirty();
        return page;
    }

    /// <summary>Copies a page (with its assets) from another session.</summary>
    public Page ImportPage(ProjectSession source, Page page)
    {
        var copy = ProjectJson.Clone(page);
        copy.Id = Guid.NewGuid();
        foreach (var im in copy.AllLayers().OfType<ImageLayer>())
        {
            var bytes = source.Assets.Get(im.AssetId);
            if (bytes is null) continue;
            Assets.Add(bytes);
            if (source.Project.Assets.TryGetValue(im.AssetId, out var info)) Project.Assets[im.AssetId] = info;
        }
        Project.Pages.Add(copy);
        MarkDirty();
        return copy;
    }

    public byte[] RenderThumbnail()
    {
        var page = Project.Pages.FirstOrDefault();
        if (page is null || page.Width == 0) return Array.Empty<byte>();
        var scale = Math.Min(1.0, 512.0 / Math.Max(page.Width, page.Height));
        using var img = Renderer.RenderPage(page, scale);
        return SkiaConvert.EncodePng(img, 6);
    }

    public void Save(string path, bool force = false)
    {
        if (ReadOnly && string.Equals(Path.GetFullPath(path), FilePath is null ? null : Path.GetFullPath(FilePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Das Projekt ist schreibgeschützt geöffnet. Bitte unter neuem Namen speichern.");
        var samePath = FilePath is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(FilePath), StringComparison.OrdinalIgnoreCase);
        long? expected = force ? null : samePath ? Project.Revision : (File.Exists(path) ? -1 : null);
        if (expected == -1) expected = null; // "save as" over an existing file is confirmed by the file dialog
        RebaseMediaPaths(path);
        ProjectFile.Save(path, Project, Assets, RenderThumbnail(), expected);
        if (!samePath)
        {
            _lock?.Dispose();
            ProjectLock.TryAcquire(path, out _lock, out _);
            ReadOnly = false;
        }
        FilePath = path;
        IsDirty = false;
        Changed?.Invoke(this, EventArgs.Empty);
        Recovery.Delete(this);
    }

    /// <summary>Portable path conversion (set by the app at start).</summary>
    public static Func<string, string> ToPortable { get; set; } = p => p;
    public static Func<string, string> FromPortable { get; set; } = p => p;

    /// <summary>Video references are relative to the project file – keep them valid on "save as" into another folder.</summary>
    private void RebaseMediaPaths(string newPath)
    {
        foreach (var v in Project.Guide.Steps.Select(s => s.Video).OfType<VideoRef>())
        {
            var resolved = ProjectFile.ResolveMedia(FilePath, v.Path, FromPortable);
            if (resolved is not null) v.Path = ProjectFile.ToProjectRelative(newPath, resolved, ToPortable);
        }
    }

    public bool HasRedactions => Project.Pages.Any(PageOperations.HasRedactions);

    public void Dispose()
    {
        _lock?.Dispose();
        Images.Dispose();
    }
}

/// <summary>Autosave copies in Data/Cache/Recovery for crash recovery.</summary>
public static class Recovery
{
    public static string? Dir { get; set; }

    public static string? PathFor(ProjectSession s) => Dir is null ? null : System.IO.Path.Combine(Dir, s.Project.Id.ToString("N") + ProjectFile.Extension);

    public static void Write(ProjectSession s)
    {
        var p = PathFor(s);
        if (p is null || !s.IsDirty) return;
        try
        {
            var clone = ProjectJson.Clone(s.Project);
            ProjectFile.Save(p, clone, s.Assets);
            File.WriteAllText(p + ".origin", s.FilePath ?? "");
        }
        catch (Exception ex) { Log.Warn("Autosave failed: " + ex.Message); }
    }

    public static void Delete(ProjectSession s)
    {
        var p = PathFor(s);
        if (p is null) return;
        try { File.Delete(p); File.Delete(p + ".origin"); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public static IEnumerable<(string recoveryFile, string? originalPath, DateTime when)> Pending()
    {
        if (Dir is null || !Directory.Exists(Dir)) yield break;
        foreach (var f in Directory.EnumerateFiles(Dir, "*" + ProjectFile.Extension))
        {
            string? origin = null;
            try { origin = File.Exists(f + ".origin") ? File.ReadAllText(f + ".origin") : null; } catch (IOException) { }
            yield return (f, string.IsNullOrWhiteSpace(origin) ? null : origin, File.GetLastWriteTime(f));
        }
    }
}
