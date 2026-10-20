using System.Globalization;
using System.Text.RegularExpressions;
using CaptureDoc.Core.Obsidian;

namespace CaptureDoc.Core.Portable;

/// <summary>"Screenshot_{yyyy-MM-dd_HH-mm-ss}" → "Screenshot_2026-10-20_18-30-00" (text outside braces is literal).</summary>
public static partial class FileNamePattern
{
    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex Placeholder();

    public static string Format(string pattern, DateTime time)
    {
        var s = Placeholder().Replace(string.IsNullOrWhiteSpace(pattern) ? "Screenshot_{yyyy-MM-dd_HH-mm-ss}" : pattern, m =>
        {
            try { return time.ToString(m.Groups[1].Value, CultureInfo.InvariantCulture); }
            catch (FormatException) { return m.Value; }
        });
        return SafeFileName.ToNoteName(s);
    }
}
