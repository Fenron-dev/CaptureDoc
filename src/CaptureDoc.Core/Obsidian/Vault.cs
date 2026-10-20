namespace CaptureDoc.Core.Obsidian;

public enum LinkStyle { Wikilink, Markdown }

/// <summary>Saved vault target. Stored in platform settings (paths are OS-specific).</summary>
public sealed class VaultProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Vault";
    /// <summary>Absolute path, or "./…" relative to the portable app root (see PortableEnvironment.ToPortablePath).</summary>
    public string VaultPath { get; set; } = "";
    public string DefaultFolder { get; set; } = "Dokumentation";
    public LinkStyle Links { get; set; } = LinkStyle.Wikilink;
    public string AssetsFolder { get; set; } = "Assets";
    public int DefaultDisplayWidth { get; set; } = 700;
    public bool SubfolderPerGuide { get; set; } = true;
    public string ExportProfileId { get; set; } = "documentation-png";
}

public static class VaultValidator
{
    public sealed record Result(bool Ok, bool LooksLikeVault, string? Error, string? Warning);

    public static Result ValidateVault(string vaultPath)
    {
        if (string.IsNullOrWhiteSpace(vaultPath)) return new(false, false, "Kein Vault-Pfad angegeben.", null);
        string full;
        try { full = Path.GetFullPath(vaultPath); } catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return new(false, false, "Ungültiger Pfad: " + e.Message, null); }
        if (!Directory.Exists(full)) return new(false, false, "Der Vault-Ordner existiert nicht (Laufwerk getrennt?): " + full, null);
        var isVault = Directory.Exists(Path.Combine(full, ".obsidian"));
        return new(true, isVault, null, isVault ? null : "Im Ordner wurde kein '.obsidian'-Verzeichnis gefunden. Ist das wirklich ein Obsidian-Vault?");
    }

    /// <summary>
    /// Resolves a vault-relative folder and guarantees it stays inside the vault and outside ".obsidian".
    /// </summary>
    public static string ResolveInside(string vaultRoot, string relativeFolder)
    {
        var root = Path.GetFullPath(vaultRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rel = (relativeFolder ?? "").Replace('\\', '/').Trim('/');
        if (Path.IsPathRooted(rel) || rel.Split('/').Any(seg => seg == ".."))
            throw new ArgumentException("Der Zielordner muss innerhalb des Vaults liegen (keine absoluten Pfade, kein '..').");
        var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
        var cmp = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!(full.Equals(root, cmp) || full.StartsWith(root + Path.DirectorySeparatorChar, cmp)))
            throw new ArgumentException("Der Zielordner liegt außerhalb des Vaults.");
        var firstSeg = rel.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstSeg is not null && (firstSeg.Equals(".obsidian", StringComparison.OrdinalIgnoreCase) || firstSeg.Equals(".trash", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("In '.obsidian' oder '.trash' darf nicht exportiert werden.");
        return full;
    }

    /// <summary>Vault-relative path with forward slashes (for wikilinks).</summary>
    public static string ToVaultRelative(string vaultRoot, string fullPath) =>
        Path.GetRelativePath(Path.GetFullPath(vaultRoot), Path.GetFullPath(fullPath)).Replace('\\', '/');
}
