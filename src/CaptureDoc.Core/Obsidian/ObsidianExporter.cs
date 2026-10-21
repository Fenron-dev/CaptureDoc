using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Serialization;
using CaptureDoc.Core.Storage;

namespace CaptureDoc.Core.Obsidian;

public sealed record RenderedImage(byte[] Data, string Extension, int Width, int Height);

/// <summary>A rendered video/GIF on disk (copied, never loaded into memory).</summary>
public sealed record RenderedMedia(string FilePath, string Extension, bool IsVideo);

/// <summary>Content of a planned file: bytes in memory or a file on disk.</summary>
public sealed class Payload
{
    public byte[]? Data { get; }
    public string? SourceFile { get; }
    private string? _hash;
    public Payload(byte[] data) => Data = data;
    public Payload(string sourceFile, bool file) => SourceFile = sourceFile;
    public string Hash => _hash ??= Data is not null ? ObsidianExporter.HashBytes(Data) : ObsidianExporter.HashFile(SourceFile!);
}

public enum ConflictPolicy
{
    /// <summary>Default: keep the modified/foreign file and write ours under a new unique name.</summary>
    NewVersion,
    /// <summary>Only after explicit user confirmation.</summary>
    Overwrite,
    /// <summary>Keep the existing file, do not write ours.</summary>
    Skip,
}

public enum PlannedAction { Create, Update, Unchanged, ConflictNewVersion, ConflictOverwrite, ConflictSkip }

public sealed class PlannedFile
{
    public required string FullPath { get; init; }
    public required string VaultRelativePath { get; init; }
    public required bool IsNote { get; init; }
    public required PlannedAction Action { get; init; }
    public byte[] Data { get; init; } = Array.Empty<byte>();
    /// <summary>Large content (videos) is copied from here instead of <see cref="Data"/>.</summary>
    public string? SourceFile { get; init; }
    public string ContentHash { get; init; } = "";
    public string? Reason { get; init; }
    /// <summary>Original target if renamed due to a conflict.</summary>
    public string? OriginalName { get; init; }
}

public sealed class ObsidianExportRequest
{
    public required string VaultRoot { get; init; }
    /// <summary>Vault-relative folder that will contain the note.</summary>
    public required string NoteFolder { get; init; }
    public required Guide Guide { get; init; }
    public required GuideTemplate Template { get; init; }
    public required IReadOnlyDictionary<Guid, RenderedImage> Images { get; init; }
    /// <summary>Optional video/GIF per step; replaces the step image in the note.</summary>
    public IReadOnlyDictionary<Guid, RenderedMedia> Videos { get; init; } = new Dictionary<Guid, RenderedMedia>();
    public LinkStyle Links { get; init; } = LinkStyle.Wikilink;
    public string AssetsFolder { get; init; } = "Assets";
    public int DefaultDisplayWidth { get; init; } = 700;
    public string? NoteName { get; init; }
    public ConflictPolicy Policy { get; init; } = ConflictPolicy.NewVersion;
    public DateTimeOffset Now { get; init; } = DateTimeOffset.Now;
}

public sealed class ExportPlan
{
    public required string VaultRoot { get; init; }
    public required string NoteDirectory { get; init; }
    public required PlannedFile Note { get; init; }
    public required List<PlannedFile> Assets { get; init; }
    public required string Markdown { get; init; }
    public List<string> Warnings { get; } = new();
    public IEnumerable<PlannedFile> AllFiles => Assets.Append(Note);
    public bool HasConflicts => AllFiles.Any(f => f.Action is PlannedAction.ConflictNewVersion or PlannedAction.ConflictOverwrite or PlannedAction.ConflictSkip);
}

public sealed class ExportResult
{
    public required string NotePath { get; init; }
    public List<string> Written { get; } = new();
    public List<string> Skipped { get; } = new();
    public List<string> MissingAssets { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool Verified => MissingAssets.Count == 0;
}

/// <summary>Record of what CaptureDoc wrote, stored as .capturedoc-export.json in the note folder.</summary>
public sealed class ExportManifest
{
    public int Version { get; set; } = 1;
    public Dictionary<Guid, ExportRecord> Exports { get; set; } = new();
}

public sealed class ExportRecord
{
    public string NoteFile { get; set; } = "";
    public DateTimeOffset ExportedAt { get; set; }
    /// <summary>Note-folder-relative path (forward slashes) → SHA-256 of what we wrote.</summary>
    public Dictionary<string, string> Files { get; set; } = new();
}

/// <summary>
/// Two-phase Obsidian export: <see cref="Plan"/> (pure, previewable) then <see cref="Execute"/>.
/// Never overwrites files that were changed outside CaptureDoc unless the policy is Overwrite.
/// </summary>
public static class ObsidianExporter
{
    public const string ManifestFileName = ".capturedoc-export.json";
    private static readonly char[] LinkUnsafe = { '#', '^', '[', ']', '|' };

    public static ExportPlan Plan(ObsidianExportRequest r)
    {
        var vault = VaultValidator.ValidateVault(r.VaultRoot);
        if (!vault.Ok) throw new InvalidOperationException(vault.Error);
        if (r.NoteFolder.IndexOfAny(LinkUnsafe) >= 0 || r.AssetsFolder.IndexOfAny(LinkUnsafe) >= 0)
            throw new ArgumentException("Ordnernamen dürfen keine der Zeichen # ^ [ ] | enthalten (sie brechen Obsidian-Links).");

        var noteDir = VaultValidator.ResolveInside(r.VaultRoot, r.NoteFolder);
        var assetsDir = Path.Combine(noteDir, SafeFolder(r.AssetsFolder));
        var manifest = ReadManifest(noteDir);
        manifest.Exports.TryGetValue(r.Guide.ExportId, out var previous);

        var warnings = new List<string>();
        if (vault.Warning is not null) warnings.Add(vault.Warning);

        var noteName = SafeFileName.ToNoteName(r.NoteName ?? r.Guide.Title) + ".md";
        // Re-export: keep the note file name we used last time (could be a "(2)" version).
        if (previous is not null && !string.IsNullOrEmpty(previous.NoteFile)) noteName = previous.NoteFile;
        var slug = SafeFileName.ToSlug(r.Guide.Title, 60, lowercase: true);

        var plannedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assets = new List<PlannedFile>();
        var imageRefs = new Dictionary<Guid, MarkdownBuilder.ImageRef>();
        var n = 0;
        foreach (var step in r.Guide.Steps)
        {
            n++;
            string Link(string full) => r.Links == LinkStyle.Wikilink
                ? VaultValidator.ToVaultRelative(r.VaultRoot, full)
                : Path.GetRelativePath(noteDir, full).Replace('\\', '/');
            var alt = string.IsNullOrWhiteSpace(step.Title) ? $"Schritt {n}" : step.Title;
            if (r.Videos.TryGetValue(step.Id, out var vid))
            {
                var vname = $"{slug}-schritt-{n:00}-video{vid.Extension}";
                var vf = PlanFile(assetsDir, vname, new Payload(vid.FilePath, true), previous, noteDir, r.Policy, plannedNames, isNote: false, r.VaultRoot);
                assets.Add(vf);
                imageRefs[step.Id] = new(Link(vf.FullPath), step.DisplayWidth ?? r.DefaultDisplayWidth, alt, vid.IsVideo);
                continue;
            }
            if (!r.Images.TryGetValue(step.Id, out var img)) continue;
            var name = $"{slug}-schritt-{n:00}{img.Extension}";
            var pf = PlanFile(assetsDir, name, new Payload(img.Data), previous, noteDir, r.Policy, plannedNames, isNote: false, r.VaultRoot);
            assets.Add(pf);
            imageRefs[step.Id] = new(Link(pf.FullPath), step.DisplayWidth ?? r.DefaultDisplayWidth, alt);
        }

        if (previous is not null)
        {
            var current = new HashSet<string>(assets.Select(a => Rel(noteDir, a.FullPath)), StringComparer.OrdinalIgnoreCase);
            foreach (var old in previous.Files.Keys.Where(k => !k.Equals(previous.NoteFile, StringComparison.OrdinalIgnoreCase) && !current.Contains(k)))
                warnings.Add($"Frühere Datei '{old}' wird nicht mehr verwendet und bleibt unverändert erhalten.");
        }

        var md = MarkdownBuilder.Build(r.Guide, r.Template, imageRefs, r.Links, r.Now);
        var mdBytes = new UTF8Encoding(false).GetBytes(md);
        var note = PlanFile(noteDir, noteName, new Payload(mdBytes), previous, noteDir, r.Policy, plannedNames, isNote: true, r.VaultRoot);

        var plan = new ExportPlan { VaultRoot = r.VaultRoot, NoteDirectory = noteDir, Note = note, Assets = assets, Markdown = md };
        plan.Warnings.AddRange(warnings);
        foreach (var f in plan.AllFiles.Where(f => f.Reason is not null && f.Action != PlannedAction.Unchanged)) plan.Warnings.Add(f.Reason!);
        return plan;
    }

    private static PlannedFile PlanFile(string dir, string name, Payload data, ExportRecord? previous, string noteDir, ConflictPolicy policy,
        HashSet<string> plannedNames, bool isNote, string vaultRoot)
    {
        var full = Path.Combine(dir, name);
        var rel = Rel(noteDir, full);
        PlannedFile Make(string path, PlannedAction a, string? reason = null, string? original = null)
        {
            plannedNames.Add(path);
            return new PlannedFile { FullPath = path, VaultRelativePath = VaultValidator.ToVaultRelative(vaultRoot, path), IsNote = isNote, Action = a, Data = data.Data ?? Array.Empty<byte>(), SourceFile = data.SourceFile, ContentHash = data.Hash, Reason = reason, OriginalName = original };
        }

        if (!File.Exists(full) && !plannedNames.Contains(full)) return Make(full, PlannedAction.Create);

        var existingHash = File.Exists(full) ? HashFile(full) : null;
        var newHash = data.Hash;
        if (existingHash == newHash) return Make(full, PlannedAction.Unchanged);

        var ours = previous is not null && previous.Files.TryGetValue(rel, out var recorded) && recorded == existingHash;
        if (ours) return Make(full, PlannedAction.Update);

        var what = previous is not null && previous.Files.ContainsKey(rel)
            ? $"'{rel}' wurde seit dem letzten Export außerhalb von CaptureDoc geändert"
            : $"'{rel}' existiert bereits und stammt nicht aus diesem Export";
        switch (policy)
        {
            case ConflictPolicy.Overwrite:
                return Make(full, PlannedAction.ConflictOverwrite, what + " → wird nach Bestätigung überschrieben.");
            case ConflictPolicy.Skip:
                return Make(full, PlannedAction.ConflictSkip, what + " → bleibt unverändert, neue Version wird nicht geschrieben.");
            default:
                // Reuse an earlier conflict version we wrote ourselves and that is still untouched.
                if (previous is not null)
                {
                    var stem = Path.GetFileNameWithoutExtension(name); var ext = Path.GetExtension(name);
                    foreach (var (prevRel, prevHash) in previous.Files)
                    {
                        var prevFull = Path.GetFullPath(Path.Combine(noteDir, prevRel.Replace('/', Path.DirectorySeparatorChar)));
                        var prevName = Path.GetFileName(prevFull);
                        if (!string.Equals(Path.GetDirectoryName(prevFull), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase)) continue;
                        if (!prevName.StartsWith(stem, StringComparison.OrdinalIgnoreCase) || !prevName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) || prevName.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                        if (plannedNames.Contains(prevFull) || !File.Exists(prevFull) || HashFile(prevFull) != prevHash) continue;
                        return Make(prevFull, HashFile(prevFull) == newHash ? PlannedAction.Unchanged : PlannedAction.Update);
                    }
                }
                var unique = SafeFileName.MakeUnique(name, cand => File.Exists(Path.Combine(dir, cand)) || plannedNames.Contains(Path.Combine(dir, cand)), slugStyle: !isNote);
                return Make(Path.Combine(dir, unique), PlannedAction.ConflictNewVersion, what + $" → neue Version '{unique}'.", name);
        }
    }

    public static ExportResult Execute(ExportPlan plan, Guid exportId)
    {
        var result = new ExportResult { NotePath = plan.Note.FullPath };
        result.Warnings.AddRange(plan.Warnings);
        Directory.CreateDirectory(plan.NoteDirectory);

        var manifest = ReadManifest(plan.NoteDirectory);
        if (!manifest.Exports.TryGetValue(exportId, out var record)) record = new ExportRecord();
        var newRecord = new ExportRecord { NoteFile = Rel(plan.NoteDirectory, plan.Note.FullPath), ExportedAt = DateTimeOffset.Now };

        foreach (var f in plan.Assets.Append(plan.Note))
        {
            var rel = Rel(plan.NoteDirectory, f.FullPath);
            switch (f.Action)
            {
                case PlannedAction.Unchanged:
                    newRecord.Files[rel] = f.ContentHash;
                    break;
                case PlannedAction.ConflictSkip:
                    result.Skipped.Add(f.VaultRelativePath);
                    if (record.Files.TryGetValue(rel, out var h)) newRecord.Files[rel] = h;
                    break;
                default:
                    Directory.CreateDirectory(Path.GetDirectoryName(f.FullPath)!);
                    if (f.SourceFile is not null)
                        AtomicFile.Write(f.FullPath, dst => { using var src = File.OpenRead(f.SourceFile); src.CopyTo(dst); });
                    else AtomicFile.WriteAllBytes(f.FullPath, f.Data);
                    newRecord.Files[rel] = f.ContentHash;
                    result.Written.Add(f.VaultRelativePath);
                    break;
            }
        }
        if (plan.Note.Action == PlannedAction.ConflictSkip)
            newRecord.NoteFile = record.NoteFile;

        manifest.Exports[exportId] = newRecord;
        AtomicFile.WriteAllText(Path.Combine(plan.NoteDirectory, ManifestFileName), JsonSerializer.Serialize(manifest, ProjectJson.Options));

        var notePath = plan.Note.Action == PlannedAction.ConflictSkip ? null : plan.Note.FullPath;
        if (notePath is not null) result.MissingAssets.AddRange(VerifyLinks(notePath, plan.VaultRoot));
        return result;
    }

    private static readonly Regex WikiEmbed = new(@"!\[\[([^\]\|#\^]+)(?:[\|#\^][^\]]*)?\]\]", RegexOptions.Compiled);
    private static readonly Regex MdEmbed = new(@"!\[[^\]]*\]\(([^)\s]+)\)", RegexOptions.Compiled);

    /// <summary>Checks that every embedded file of the note exists. Returns missing link targets.</summary>
    public static List<string> VerifyLinks(string notePath, string vaultRoot)
    {
        var missing = new List<string>();
        var md = File.ReadAllText(notePath);
        var noteDir = Path.GetDirectoryName(Path.GetFullPath(notePath))!;
        foreach (Match m in WikiEmbed.Matches(md))
        {
            var target = m.Groups[1].Value.Trim();
            var fromVault = Path.Combine(vaultRoot, target.Replace('/', Path.DirectorySeparatorChar));
            var fromNote = Path.Combine(noteDir, target.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fromVault) && !File.Exists(fromNote)) missing.Add(target);
        }
        foreach (Match m in MdEmbed.Matches(md))
        {
            var target = Uri.UnescapeDataString(m.Groups[1].Value);
            if (target.Contains("://", StringComparison.Ordinal)) continue;
            if (!File.Exists(Path.Combine(noteDir, target.Replace('/', Path.DirectorySeparatorChar)))) missing.Add(target);
        }
        return missing;
    }

    public static ExportManifest ReadManifest(string noteDir)
    {
        var p = Path.Combine(noteDir, ManifestFileName);
        if (!File.Exists(p)) return new ExportManifest();
        try { return JsonSerializer.Deserialize<ExportManifest>(File.ReadAllText(p), ProjectJson.Options) ?? new(); }
        catch (JsonException) { return new ExportManifest(); }
    }

    private static string SafeFolder(string name)
    {
        var n = string.Join("/", (name ?? "Assets").Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Select(s => SafeFileName.ToNoteName(s)));
        return n.Length == 0 ? "Assets" : n.Replace('/', Path.DirectorySeparatorChar);
    }

    private static string Rel(string baseDir, string full) => Path.GetRelativePath(baseDir, full).Replace('\\', '/');
    public static string HashBytes(byte[] d) => Convert.ToHexStringLower(SHA256.HashData(d));
    public static string HashFile(string path) { using var fs = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(fs)); }
}
