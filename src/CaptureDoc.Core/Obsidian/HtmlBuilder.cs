using System.Net;
using System.Text;
using CaptureDoc.Core.Model;

namespace CaptureDoc.Core.Obsidian;

/// <summary>Self-contained HTML guide (inline CSS, relative image paths) – opens in any browser, prints to PDF.</summary>
public static class HtmlBuilder
{
    /// <summary>Image popup: fit to screen, click/Space = 1:1, ←/→ = previous/next, Esc/backdrop = close.</summary>
    private const string Lightbox = """
<div id="lb" role="dialog" aria-modal="true" aria-label="Bildansicht"><div class="bar"><span id="lbc"></span>
<button id="lbp" title="Vorheriges Bild (←)">‹</button><button id="lbn" title="Nächstes Bild (→)">›</button>
<button id="lbz" title="Originalgröße / Einpassen (Leertaste)">1:1</button><button id="lbx" title="Schließen (Esc)">✕</button></div><img id="lbi" alt=""></div>
<script>
(function(){var links=[].slice.call(document.querySelectorAll('a.zoom')),lb=document.getElementById('lb'),im=document.getElementById('lbi'),cap=document.getElementById('lbc'),cur=-1,last=null;
function show(i){cur=(i+links.length)%links.length;var a=links[cur],t=a.querySelector('img');im.src=a.getAttribute('href');im.alt=t.alt;cap.textContent=(cur+1)+' / '+links.length+'  ·  '+t.alt;lb.classList.remove('actual');}
function open(i){last=document.activeElement;show(i);lb.classList.add('open');document.getElementById('lbx').focus();}
function close(){lb.classList.remove('open','actual');im.removeAttribute('src');if(last)last.focus();}
function zoom(){lb.classList.toggle('actual');}
links.forEach(function(a,i){a.addEventListener('click',function(e){e.preventDefault();open(i);});});
im.addEventListener('click',function(e){e.stopPropagation();zoom();});
lb.addEventListener('click',function(e){if(e.target===lb)close();});
document.getElementById('lbx').onclick=close;document.getElementById('lbz').onclick=zoom;
document.getElementById('lbp').onclick=function(){show(cur-1);};document.getElementById('lbn').onclick=function(){show(cur+1);};
if(links.length<2){document.getElementById('lbp').hidden=true;document.getElementById('lbn').hidden=true;}
document.addEventListener('keydown',function(e){if(!lb.classList.contains('open'))return;if(e.key==='Escape')close();else if(e.key==='ArrowRight')show(cur+1);else if(e.key==='ArrowLeft')show(cur-1);else if(e.key===' '){e.preventDefault();zoom();}});})();
</script>

""";

    public static string Build(Guide guide, GuideTemplate template, IReadOnlyDictionary<Guid, MarkdownBuilder.ImageRef> images)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        static string Para(string s) => string.Join("", s.Replace("\r\n", "\n").Split("\n\n").Select(p => $"<p>{E(p).Replace("\n", "<br>")}</p>"));
        var sb = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(guide.Title) ? "Unbenannt" : guide.Title;
        sb.Append("<!doctype html>\n<html lang=\"de\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append($"<title>{E(title)}</title><meta name=\"generator\" content=\"CaptureDoc\"><style>");
        sb.Append("body{font:16px/1.55 system-ui,-apple-system,Segoe UI,sans-serif;max-width:860px;margin:2rem auto;padding:0 1rem;color:#1b1b1f;background:#fff}");
        sb.Append("img{max-width:100%;height:auto;border:1px solid #ddd;border-radius:6px}video{max-width:100%;height:auto;border-radius:6px;background:#000}h1{font-size:2rem}h2{margin-top:2.2rem;border-bottom:1px solid #eee;padding-bottom:.3rem}");
        sb.Append(".callout{border-left:4px solid #2f80ed;background:#f2f7ff;padding:.6rem 1rem;margin:1rem 0;border-radius:4px}.callout.warning,.callout.danger{border-color:#e5484d;background:#fff4f4}.callout.tip,.callout.success{border-color:#30a46c;background:#f1fbf5}");
        sb.Append(".callout b{display:block}.tags span{display:inline-block;background:#eef;border-radius:10px;padding:0 .6rem;margin-right:.3rem;font-size:.85rem}ul.check{list-style:none;padding-left:0}ul.check li:before{content:'☐ '}");
        sb.Append("figure{margin:1rem 0}a.zoom{display:inline-block;cursor:zoom-in}a.zoom:focus-visible img{outline:3px solid #2f80ed}");
        // Lightbox: click an image to view it large. Offline, no external scripts; without JS the link simply opens the image.
        sb.Append("#lb{position:fixed;inset:0;background:rgba(10,10,14,.92);display:none;align-items:center;justify-content:center;z-index:10;overflow:auto}#lb.open{display:flex}");
        sb.Append("#lb img{width:96vw;height:86vh;margin-top:2.5rem;object-fit:contain;border:0;cursor:zoom-in}#lb.actual{align-items:flex-start;justify-content:flex-start}#lb.actual img{width:auto;height:auto;object-fit:none;cursor:zoom-out;margin:3.5rem 2rem 2rem}");
        sb.Append("#lb .bar{position:fixed;top:0;left:0;right:0;display:flex;gap:.5rem;align-items:center;padding:.5rem 1rem;color:#eee;font-size:.9rem;background:rgba(0,0,0,.45)}#lb .bar span{flex:1}");
        sb.Append("#lb button{background:rgba(255,255,255,.15);color:#fff;border:0;border-radius:4px;padding:.35rem .7rem;font-size:1rem;cursor:pointer}#lb button:hover{background:rgba(255,255,255,.3)}");
        sb.Append("@media print{body{margin:0}h2{break-after:avoid}figure{break-inside:avoid}#lb{display:none!important}}</style></head><body>\n");
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
                if (img.IsVideo)
                {
                    var poster = img.PosterTarget is { } pt ? $" poster=\"{string.Join('/', pt.Split('/').Select(Uri.EscapeDataString))}\"" : "";
                    sb.Append($"<figure><video controls preload=\"metadata\" playsinline{w}{poster} src=\"{src}\" title=\"{E(img.AltText)}\">Video: <a href=\"{src}\">{E(img.AltText)}</a></video></figure>\n");
                }
                else sb.Append($"<figure><a class=\"zoom\" href=\"{src}\" title=\"Klicken zum Vergrößern\"><img src=\"{src}\" alt=\"{E(img.AltText)}\"{w} loading=\"lazy\"></a></figure>\n");
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
        if (images.Count > 0) sb.Append(Lightbox);
        sb.Append("</body></html>\n");
        return sb.ToString();

        static string Callout(CalloutKind k, string t, string text) =>
            $"<div class=\"callout {k.ToString().ToLowerInvariant()}\">{(string.IsNullOrWhiteSpace(t) ? "" : $"<b>{WebUtility.HtmlEncode(t)}</b>")}{WebUtility.HtmlEncode(text ?? "").Replace("\n", "<br>")}</div>\n";
    }
}
