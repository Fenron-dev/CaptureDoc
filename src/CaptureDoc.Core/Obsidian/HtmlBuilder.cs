using System.Net;
using System.Text;
using CaptureDoc.Core.Model;

namespace CaptureDoc.Core.Obsidian;

/// <summary>Self-contained HTML guide (inline CSS, relative image paths) – opens in any browser, prints to PDF.</summary>
public static class HtmlBuilder
{
    public static string Build(Guide guide, GuideTemplate template, IReadOnlyDictionary<Guid, MarkdownBuilder.ImageRef> images)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        static string Para(string s) => string.Join("", s.Replace("\r\n", "\n").Split("\n\n").Select(p => $"<p>{E(p).Replace("\n", "<br>")}</p>"));
        var sb = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(guide.Title) ? "Unbenannt" : guide.Title;
        sb.Append("<!doctype html>\n<html lang=\"de\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append($"<title>{E(title)}</title><meta name=\"generator\" content=\"CaptureDoc\"><style>");
        sb.Append("body{font:16px/1.55 system-ui,-apple-system,Segoe UI,sans-serif;max-width:860px;margin:2rem auto;padding:0 1rem;color:#1b1b1f;background:#fff}");
        sb.Append("img{max-width:100%;height:auto;border:1px solid #ddd;border-radius:6px}h1{font-size:2rem}h2{margin-top:2.2rem;border-bottom:1px solid #eee;padding-bottom:.3rem}");
        sb.Append(".callout{border-left:4px solid #2f80ed;background:#f2f7ff;padding:.6rem 1rem;margin:1rem 0;border-radius:4px}.callout.warning,.callout.danger{border-color:#e5484d;background:#fff4f4}.callout.tip,.callout.success{border-color:#30a46c;background:#f1fbf5}");
        sb.Append(".callout b{display:block}.tags span{display:inline-block;background:#eef;border-radius:10px;padding:0 .6rem;margin-right:.3rem;font-size:.85rem}ul.check{list-style:none;padding-left:0}ul.check li:before{content:'☐ '}");
        sb.Append("@media print{body{margin:0}h2{break-after:avoid}figure{break-inside:avoid}}</style></head><body>\n");
        sb.Append($"<h1>{E(title)}</h1>\n");
        var tags = template.Tags.Concat(guide.Tags).Select(MarkdownBuilder.NormalizeTag).Where(t => t.Length > 0).Distinct().ToList();
        if (tags.Count > 0) sb.Append("<div class=\"tags\">").Append(string.Join("", tags.Select(t => $"<span>#{E(t)}</span>"))).Append("</div>\n");
        if (!string.IsNullOrWhiteSpace(guide.Introduction)) sb.Append(Para(guide.Introduction.Trim())).Append('\n');
        if (!string.IsNullOrWhiteSpace(guide.Prerequisites)) sb.Append(Callout(template.PrerequisitesKind, template.PrerequisitesTitle, guide.Prerequisites));
        var n = 0;
        foreach (var s in guide.Steps)
        {
            n++;
            var stepTitle = string.IsNullOrWhiteSpace(s.Title) ? $"Schritt {n}" : s.Title.Trim();
            sb.Append($"<h2>{E(template.StepHeading.Replace("{n}", n.ToString()).Replace("{title}", stepTitle))}</h2>\n");
            if (images.TryGetValue(s.Id, out var img))
            {
                var src = string.Join('/', img.LinkTarget.Split('/').Select(Uri.EscapeDataString));
                var w = img.DisplayWidth is { } dw ? $" width=\"{dw}\"" : "";
                sb.Append($"<figure><img src=\"{src}\" alt=\"{E(img.AltText)}\"{w} loading=\"lazy\"></figure>\n");
            }
            if (!string.IsNullOrWhiteSpace(s.Text)) sb.Append(Para(s.Text.Trim())).Append('\n');
            foreach (var c in s.Callouts) sb.Append(Callout(c.Kind, c.Title, c.Text));
        }
        if (guide.Checklist.Count > 0 || !string.IsNullOrWhiteSpace(guide.Conclusion))
        {
            sb.Append($"<h2>{E(template.ChecklistTitle)}</h2>\n");
            if (guide.Checklist.Count > 0) sb.Append("<ul class=\"check\">").Append(string.Join("", guide.Checklist.Select(i => $"<li>{E(i)}</li>"))).Append("</ul>\n");
            if (!string.IsNullOrWhiteSpace(guide.Conclusion)) sb.Append(Para(guide.Conclusion.Trim()));
        }
        sb.Append("</body></html>\n");
        return sb.ToString();

        static string Callout(CalloutKind k, string t, string text) =>
            $"<div class=\"callout {k.ToString().ToLowerInvariant()}\">{(string.IsNullOrWhiteSpace(t) ? "" : $"<b>{WebUtility.HtmlEncode(t)}</b>")}{WebUtility.HtmlEncode(text ?? "").Replace("\n", "<br>")}</div>\n";
    }
}
