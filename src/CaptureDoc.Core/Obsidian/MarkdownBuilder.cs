using System.Globalization;
using System.Text;
using CaptureDoc.Core.Model;

namespace CaptureDoc.Core.Obsidian;

/// <summary>Builds Obsidian-flavoured Markdown (frontmatter, callouts, embeds, checklists).</summary>
public static class MarkdownBuilder
{
    /// <param name="IsVideo">MP4/WebM embed (Obsidian plays it inline); GIFs count as images.</param>
    public sealed record ImageRef(string LinkTarget, int? DisplayWidth, string AltText, bool IsVideo = false, string? PosterTarget = null);

    public static string Build(Guide guide, GuideTemplate template, IReadOnlyDictionary<Guid, ImageRef> images, LinkStyle links, DateTimeOffset now)
    {
        var sb = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(guide.Title) ? "Unbenannt" : guide.Title.Trim();
        var date = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // --- frontmatter (YAML properties)
        sb.Append("---\n");
        sb.Append("title: ").Append(YamlString(title)).Append('\n');
        var tags = template.Tags.Concat(guide.Tags).Select(NormalizeTag).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (tags.Count > 0)
        {
            sb.Append("tags:\n");
            foreach (var t in tags) sb.Append("  - ").Append(t).Append('\n');
        }
        sb.Append("created: ").Append(date).Append('\n');
        sb.Append("status: ").Append(YamlString(string.IsNullOrWhiteSpace(guide.Status) ? template.DefaultStatus : guide.Status)).Append('\n');
        foreach (var (k, v) in template.Properties)
        {
            var key = NormalizeKey(k);
            if (key.Length == 0 || key is "title" or "tags" or "created" or "status") continue;
            sb.Append(key).Append(": ").Append(YamlString(v.Replace("{date}", date).Replace("{title}", title))).Append('\n');
        }
        sb.Append("capturedoc-id: ").Append(guide.ExportId.ToString("D")).Append('\n');
        sb.Append("---\n\n");

        sb.Append("# ").Append(SingleLine(title)).Append("\n\n");
        if (!string.IsNullOrWhiteSpace(guide.Introduction)) sb.Append(guide.Introduction.Trim()).Append("\n\n");
        if (!string.IsNullOrWhiteSpace(guide.Prerequisites))
            AppendCallout(sb, template.PrerequisitesKind, template.PrerequisitesTitle, guide.Prerequisites);

        foreach (var s in template.SectionsBefore) AppendSection(sb, s, guide);

        var stepLevel = "## ";
        if (!string.IsNullOrWhiteSpace(template.StepsHeading))
        {
            sb.Append("## ").Append(SingleLine(template.StepsHeading)).Append("\n\n");
            stepLevel = "### ";
        }

        var n = 0;
        foreach (var step in guide.Steps)
        {
            n++;
            var stepTitle = string.IsNullOrWhiteSpace(step.Title) ? $"Schritt {n}" : step.Title.Trim();
            var heading = template.StepHeading.Replace("{n}", n.ToString(CultureInfo.InvariantCulture)).Replace("{title}", stepTitle);
            sb.Append(stepLevel).Append(SingleLine(heading)).Append("\n\n");
            if (images.TryGetValue(step.Id, out var img)) sb.Append(Embed(img, links)).Append("\n\n");
            if (!string.IsNullOrWhiteSpace(step.Text)) sb.Append(step.Text.Trim()).Append("\n\n");
            foreach (var c in step.Callouts) AppendCallout(sb, c.Kind, c.Title, c.Text);
        }

        foreach (var s in template.SectionsAfter) AppendSection(sb, s, guide);

        if (template.IncludeChecklist && (guide.Checklist.Count > 0 || !string.IsNullOrWhiteSpace(guide.Conclusion)))
        {
            sb.Append("## ").Append(SingleLine(template.ChecklistTitle)).Append("\n\n");
            foreach (var item in guide.Checklist.Where(i => !string.IsNullOrWhiteSpace(i)))
                sb.Append("- [ ] ").Append(SingleLine(item)).Append('\n');
            if (guide.Checklist.Count > 0) sb.Append('\n');
            if (!string.IsNullOrWhiteSpace(guide.Conclusion)) sb.Append(guide.Conclusion.Trim()).Append("\n\n");
        }
        else if (!string.IsNullOrWhiteSpace(guide.Conclusion))
        {
            sb.Append(guide.Conclusion.Trim()).Append("\n\n");
        }

        return sb.ToString().TrimEnd('\n') + "\n";
    }

    private static void AppendSection(StringBuilder sb, string heading, Guide guide)
    {
        sb.Append("## ").Append(SingleLine(heading)).Append("\n\n");
        if (guide.Sections.TryGetValue(heading, out var text) && !string.IsNullOrWhiteSpace(text))
            sb.Append(text.Trim()).Append("\n\n");
    }

    public static string Embed(ImageRef img, LinkStyle links)
    {
        if (links == LinkStyle.Wikilink)
            return img.DisplayWidth is { } w ? $"![[{img.LinkTarget}|{w}]]" : $"![[{img.LinkTarget}]]";
        var alt = SingleLine(img.AltText).Replace("[", "").Replace("]", "").Replace("|", "");
        var encoded = string.Join('/', img.LinkTarget.Split('/').Select(Uri.EscapeDataString));
        return img.DisplayWidth is { } mw ? $"![{alt}|{mw}]({encoded})" : $"![{alt}]({encoded})";
    }

    public static void AppendCallout(StringBuilder sb, CalloutKind kind, string title, string text)
    {
        sb.Append("> [!").Append(kind.ToString().ToLowerInvariant()).Append(']');
        if (!string.IsNullOrWhiteSpace(title)) sb.Append(' ').Append(SingleLine(title));
        sb.Append('\n');
        foreach (var line in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            sb.Append('>').Append(line.Length > 0 ? " " + line : "").Append('\n');
        sb.Append('\n');
    }

    private static string SingleLine(string s) => s.Replace("\r", " ").Replace("\n", " ").Trim();

    /// <summary>Obsidian tags: no spaces, letters/digits/-/_/ (nested).</summary>
    public static string NormalizeTag(string tag)
    {
        var t = tag.Trim().TrimStart('#');
        var sb = new StringBuilder();
        foreach (var ch in t)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '/') sb.Append(char.ToLowerInvariant(ch));
            else if (char.IsWhiteSpace(ch)) sb.Append('-');
        }
        var r = sb.ToString().Trim('-', '/');
        return r.All(char.IsDigit) && r.Length > 0 ? "t" + r : r; // purely numeric tags are invalid in Obsidian
    }

    private static string NormalizeKey(string k) => new string(k.Trim().Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());

    /// <summary>YAML scalar, double-quoted when needed.</summary>
    public static string YamlString(string? value)
    {
        var v = value ?? "";
        if (v.Length == 0) return "\"\"";
        var needsQuote = v.Any(c => ":#{}[],&*!|>'\"%@`\\".Contains(c) || char.IsControl(c)) || char.IsWhiteSpace(v[0]) || char.IsWhiteSpace(v[^1])
                         || v is "true" or "false" or "yes" or "no" or "null" or "~" || v.StartsWith('-') || v.StartsWith('?')
                         || double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        if (!needsQuote) return v;
        return "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t") + "\"";
    }
}
