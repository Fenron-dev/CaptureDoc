using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Storage;

namespace CaptureDoc.Core.Serialization;

public sealed class ProjectFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Another writer saved the file after we loaded it.</summary>
public sealed class ProjectConflictException(string message, long expectedRevision, long foundRevision) : Exception(message)
{
    public long ExpectedRevision { get; } = expectedRevision;
    public long FoundRevision { get; } = foundRevision;
}

public sealed class LoadedProject
{
    public required CaptureProject Project { get; init; }
    public required AssetStore Assets { get; init; }
    /// <summary>Non-fatal problems (e.g. a damaged asset).</summary>
    public List<string> Warnings { get; } = new();
    public int LoadedFormatVersion { get; init; }
    public bool WasMigrated => LoadedFormatVersion != CaptureProject.CurrentFormatVersion;
}

public sealed class ProjectManifest
{
    public string Format { get; set; } = ProjectFile.FormatName;
    public int FormatVersion { get; set; } = CaptureProject.CurrentFormatVersion;
    public string AppVersion { get; set; } = "";
    public Guid ProjectId { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset SavedAt { get; set; }
    /// <summary>Entry path → SHA-256 hex.</summary>
    public Dictionary<string, string> Entries { get; set; } = new();
}

/// <summary>
/// Reads/writes the .capturedoc ZIP container. See DevVault ADR-003.
/// </summary>
public static class ProjectFile
{
    public const string Extension = ".capturedoc";
    public const string FormatName = "capturedoc";
    public const string MimeType = "application/x-capturedoc+zip";
    private const string ManifestEntry = "manifest.json";
    private const string ProjectEntry = "project.json";
    private const string ThumbnailEntry = "thumbnail.png";

    public static string AppVersion { get; set; } = typeof(ProjectFile).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Registered migrations, keyed by source version.</summary>
    public static readonly List<IProjectMigration> Migrations = new() { new V1ToV2() };

    /// <summary>v2 only adds optional properties (video references); v1 files stay valid.</summary>
    private sealed class V1ToV2 : IProjectMigration
    {
        public int FromVersion => 1;
        public JsonNode Migrate(JsonNode project) => project;
    }

    /// <summary>Stores a video path relative to the project folder when possible, else portable/absolute.</summary>
    public static string ToProjectRelative(string? projectFile, string videoPath, Func<string, string> portable)
    {
        var full = Path.GetFullPath(videoPath);
        if (projectFile is not null)
        {
            var rel = Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(projectFile))!, full);
            if (!rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel)) return rel.Replace('\\', '/');
        }
        return portable(full);
    }

    /// <summary>Resolves a stored video path; tries project folder, portable root, then the literal path.</summary>
    public static string? ResolveMedia(string? projectFile, string stored, Func<string, string> fromPortable)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        var candidates = new List<string>();
        if (stored.StartsWith("./", StringComparison.Ordinal)) candidates.Add(fromPortable(stored));
        else if (!Path.IsPathRooted(stored) && projectFile is not null)
            candidates.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectFile))!, stored.Replace('/', Path.DirectorySeparatorChar))));
        candidates.Add(stored);
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <param name="expectedRevision">Revision the caller loaded; null = skip conflict check (new file / force).</param>
    public static void Save(string path, CaptureProject project, AssetStore assets, byte[]? thumbnailPng = null, long? expectedRevision = null)
    {
        if (expectedRevision is { } exp && File.Exists(path))
        {
            var current = TryReadManifest(path);
            if (current is not null && (current.ProjectId != project.Id || current.Revision != exp))
                throw new ProjectConflictException(
                    current.ProjectId != project.Id
                        ? "Die Zieldatei enthält ein anderes Projekt."
                        : $"Die Datei wurde zwischenzeitlich geändert (Revision {current.Revision}, erwartet {exp}).",
                    exp, current.Revision);
        }

        var used = CollectUsedAssets(project);
        var missing = used.Where(id => !assets.Contains(id)).ToList();
        if (missing.Count > 0)
            throw new ProjectFormatException($"Fehlende Bilddaten für {missing.Count} Asset(s); Speichern abgebrochen, um Datenverlust zu vermeiden.");

        var newRevision = project.Revision + 1;
        var modified = DateTimeOffset.Now;

        // Snapshot to save (do not mutate the live project until the file is safely written)
        var snapshot = ProjectJson.Clone(project);
        snapshot.Revision = newRevision;
        snapshot.Modified = modified;
        snapshot.FormatVersion = CaptureProject.CurrentFormatVersion;
        snapshot.Assets = snapshot.Assets.Where(kv => used.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

        AtomicFile.Write(path, stream =>
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);
            var manifest = new ProjectManifest
            {
                AppVersion = AppVersion, ProjectId = project.Id, Revision = newRevision, SavedAt = modified,
            };

            var mt = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var s = mt.Open()) s.Write(Encoding.ASCII.GetBytes(MimeType));

            var projectBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, ProjectJson.Options);
            WriteEntry(zip, ProjectEntry, projectBytes, CompressionLevel.Optimal, manifest);

            foreach (var id in used.OrderBy(x => x, StringComparer.Ordinal))
            {
                var info = snapshot.Assets.TryGetValue(id, out var ai) ? ai : null;
                var ext = Path.GetExtension(info?.FileName ?? ".png");
                if (string.IsNullOrEmpty(ext)) ext = ".png";
                // PNG/JPEG are already compressed
                WriteEntry(zip, $"assets/{id}{ext}", assets.Get(id)!, CompressionLevel.NoCompression, manifest);
            }

            if (thumbnailPng is not null) WriteEntry(zip, ThumbnailEntry, thumbnailPng, CompressionLevel.NoCompression, manifest);

            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ProjectJson.Options);
            var me = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
            using (var s = me.Open()) s.Write(manifestBytes);
        },
        validateTemp: tmp =>
        {
            // Never replace a good file with a broken one: re-open and verify everything we wrote.
            var check = Load(tmp, verifyAssets: true);
            if (check.Warnings.Count > 0)
                throw new ProjectFormatException("Validierung nach dem Schreiben fehlgeschlagen: " + string.Join("; ", check.Warnings));
        });

        project.Revision = newRevision;
        project.Modified = modified;
        foreach (var k in project.Assets.Keys.Where(k => !used.Contains(k)).ToList()) project.Assets.Remove(k);
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] data, CompressionLevel level, ProjectManifest manifest)
    {
        var e = zip.CreateEntry(name, level);
        using (var s = e.Open()) s.Write(data);
        manifest.Entries[name] = Convert.ToHexStringLower(SHA256.HashData(data));
    }

    public static HashSet<string> CollectUsedAssets(CaptureProject project)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pages = project.Pages.Concat(project.Guide.Steps.Select(s => s.Video?.Overlay?.Page).OfType<Page>());
        foreach (var p in pages)
            foreach (var l in p.AllLayers().OfType<ImageLayer>())
                if (!string.IsNullOrEmpty(l.AssetId)) set.Add(l.AssetId);
        return set;
    }

    public static ProjectManifest? TryReadManifest(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var e = zip.GetEntry(ManifestEntry);
            if (e is null) return null;
            using var s = e.Open();
            return JsonSerializer.Deserialize<ProjectManifest>(s, ProjectJson.Options);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public static LoadedProject Load(string path, bool verifyAssets = true)
    {
        ZipArchive zip;
        try { zip = ZipFile.OpenRead(path); }
        catch (InvalidDataException ex) { throw new ProjectFormatException("Die Datei ist kein gültiges CaptureDoc-Projekt (ZIP beschädigt).", ex); }

        using (zip)
        {
            var mimeEntry = zip.GetEntry("mimetype");
            if (mimeEntry is null || ReadAll(mimeEntry) is var mb && Encoding.ASCII.GetString(mb) != MimeType)
                throw new ProjectFormatException("Die Datei ist kein CaptureDoc-Projekt (mimetype fehlt).");

            var manifestEntry = zip.GetEntry(ManifestEntry) ?? throw new ProjectFormatException("manifest.json fehlt – Datei unvollständig.");
            ProjectManifest manifest;
            try { manifest = JsonSerializer.Deserialize<ProjectManifest>(ReadAll(manifestEntry), ProjectJson.Options)!; }
            catch (JsonException ex) { throw new ProjectFormatException("manifest.json ist beschädigt.", ex); }

            if (manifest.FormatVersion > CaptureProject.CurrentFormatVersion)
                throw new ProjectFormatException($"Das Projekt wurde mit einer neueren CaptureDoc-Version gespeichert (Format {manifest.FormatVersion}, unterstützt bis {CaptureProject.CurrentFormatVersion}).");

            var projEntry = zip.GetEntry(ProjectEntry) ?? throw new ProjectFormatException("project.json fehlt.");
            var projBytes = ReadAll(projEntry);
            if (manifest.Entries.TryGetValue(ProjectEntry, out var expected) && !HashEquals(projBytes, expected))
                throw new ProjectFormatException("project.json ist beschädigt (Prüfsumme stimmt nicht).");

            JsonNode node;
            try { node = JsonNode.Parse(projBytes) ?? throw new ProjectFormatException("project.json ist leer."); }
            catch (JsonException ex) { throw new ProjectFormatException("project.json ist kein gültiges JSON.", ex); }

            var version = node["formatVersion"]?.GetValue<int>() ?? manifest.FormatVersion;
            var loadedVersion = version;
            node = Migrate(node, ref version);

            CaptureProject project;
            try { project = node.Deserialize<CaptureProject>(ProjectJson.Options) ?? throw new ProjectFormatException("project.json ist leer."); }
            catch (JsonException ex) { throw new ProjectFormatException("project.json passt nicht zum Datenmodell: " + ex.Message, ex); }

            project.Revision = manifest.Revision;
            var result = new LoadedProject { Project = project, Assets = new AssetStore(), LoadedFormatVersion = loadedVersion };

            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("assets/", StringComparison.Ordinal)))
            {
                var id = Path.GetFileNameWithoutExtension(entry.Name);
                var data = ReadAll(entry);
                if (verifyAssets)
                {
                    var actual = AssetStore.ComputeId(data);
                    if (!string.Equals(actual, id, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Warnings.Add($"Asset {entry.FullName} ist beschädigt und wurde übersprungen.");
                        continue;
                    }
                }
                result.Assets.AddVerified(id, data);
            }

            foreach (var id in CollectUsedAssets(project).Where(id => !result.Assets.Contains(id)))
                result.Warnings.Add($"Bild {id[..Math.Min(12, id.Length)]}… fehlt im Projekt.");
            return result;
        }
    }

    public static byte[]? ReadThumbnail(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var e = zip.GetEntry(ThumbnailEntry);
            return e is null ? null : ReadAll(e);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return null; }
    }

    internal static JsonNode Migrate(JsonNode node, ref int version)
    {
        while (version < CaptureProject.CurrentFormatVersion)
        {
            var from = version;
            var m = Migrations.FirstOrDefault(x => x.FromVersion == from)
                    ?? throw new ProjectFormatException($"Keine Migration von Format {from} verfügbar.");
            node = m.Migrate(node);
            version = m.FromVersion + 1;
            node["formatVersion"] = version;
        }
        return node;
    }

    private static byte[] ReadAll(ZipArchiveEntry e)
    {
        using var s = e.Open();
        using var ms = new MemoryStream(e.Length > 0 && e.Length < int.MaxValue ? (int)e.Length : 0);
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static bool HashEquals(byte[] data, string hex) =>
        string.Equals(Convert.ToHexStringLower(SHA256.HashData(data)), hex, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Upgrades project.json from <see cref="FromVersion"/> to FromVersion + 1.</summary>
public interface IProjectMigration
{
    int FromVersion { get; }
    JsonNode Migrate(JsonNode project);
}
