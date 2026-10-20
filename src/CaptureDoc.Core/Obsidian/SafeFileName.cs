using System.Globalization;
using System.Text;

namespace CaptureDoc.Core.Obsidian;

/// <summary>File names that are valid on Windows, macOS and Linux and safe inside Obsidian links.</summary>
public static class SafeFileName
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    // Windows-invalid + characters with meaning in Obsidian links: # ^ [ ] |
    private const string Invalid = "<>:\"/\\|?*#^[]%{}`";

    /// <summary>"Drucker konfigurieren!" → "Drucker-konfigurieren" (keeps case and umlauts transliterated).</summary>
    public static string ToSlug(string? text, int maxLength = 80, bool lowercase = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return "unbenannt";
        var s = Transliterate(text.Trim());
        var sb = new StringBuilder(s.Length);
        var lastDash = false;
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) && ch < 128) { sb.Append(lowercase ? char.ToLowerInvariant(ch) : ch); lastDash = false; }
            else if (ch is '.' or '_' && !lastDash && sb.Length > 0) { sb.Append(ch); lastDash = false; }
            else if (!lastDash && sb.Length > 0) { sb.Append('-'); lastDash = true; }
        }
        var result = sb.ToString().Trim('-', '.', '_');
        if (result.Length > maxLength) result = result[..maxLength].Trim('-', '.', '_');
        if (result.Length == 0) result = "unbenannt";
        if (Reserved.Contains(result) || Reserved.Contains(result.Split('.')[0])) result = "_" + result;
        return result;
    }

    /// <summary>Keeps spaces/umlauts (nicer note titles) but removes everything unsafe.</summary>
    public static string ToNoteName(string? title, int maxLength = 100)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Unbenannt";
        var sb = new StringBuilder();
        foreach (var ch in title.Trim())
        {
            if (Invalid.Contains(ch) || char.IsControl(ch)) sb.Append(' ');
            else sb.Append(ch);
        }
        var r = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.');
        if (r.Length > maxLength) r = r[..maxLength].TrimEnd(' ', '.');
        if (r.Length == 0) r = "Unbenannt";
        if (Reserved.Contains(r)) r = "_" + r;
        return r;
    }

    private static string Transliterate(string s)
    {
        s = s.Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("Ä", "Ae").Replace("Ö", "Oe").Replace("Ü", "Ue").Replace("ß", "ss");
        var norm = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);
        foreach (var c in norm)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Returns a name not in <paramref name="taken"/> (case-insensitive – safe on NTFS/APFS and ext4)
    /// by appending " (2)", "-2" etc.
    /// </summary>
    public static string MakeUnique(string fileName, Func<string, bool> taken, bool slugStyle)
    {
        if (!taken(fileName)) return fileName;
        var ext = Path.GetExtension(fileName);
        var stem = fileName[..^ext.Length];
        for (var i = 2; i < 10000; i++)
        {
            var cand = slugStyle ? $"{stem}-{i}{ext}" : $"{stem} ({i}){ext}";
            if (!taken(cand)) return cand;
        }
        return $"{stem}-{Guid.NewGuid():N}{ext}";
    }
}
