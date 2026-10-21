using CaptureDoc.Core.Model;
using CaptureDoc.Core.Obsidian;
using CaptureDoc.Core.Storage;
using SkiaSharp;

namespace CaptureDoc.Rendering;

/// <summary>Writes a guide as A4 PDF (SkiaSharp SKDocument, no external tools). Returns the page count.</summary>
public static class PdfGuideExporter
{
    private const float W = 595, H = 842, Margin = 50, ContentW = W - 2 * Margin;

    private sealed class Writer : IDisposable
    {
        private readonly SKDocument _doc;
        public SKCanvas? Canvas;
        public float Y;
        public int Pages;
        public readonly SKTypeface Regular = TextLayout.Typeface("Segoe UI", false, false);
        public readonly SKTypeface Bold = TextLayout.Typeface("Segoe UI", true, false);
        public Writer(SKDocument doc) { _doc = doc; NewPage(); }

        public void NewPage()
        {
            if (Canvas is not null) _doc.EndPage();
            Canvas = _doc.BeginPage(W, H);
            Pages++;
            Y = Margin;
            using var f = new SKFont(Regular, 8);
            using var p = new SKPaint { Color = new SKColor(150, 150, 150), IsAntialias = true };
            Canvas.DrawText($"Seite {Pages}", W - Margin, H - 25, SKTextAlign.Right, f, p);
        }

        public void Ensure(float h) { if (Y + h > H - Margin) NewPage(); }

        public void Text(string text, float size, bool bold = false, SKColor? color = null, float indent = 0, float after = 6)
        {
            using var font = new SKFont(bold ? Bold : Regular, size) { Edging = SKFontEdging.Antialias, Subpixel = true };
            using var paint = new SKPaint { Color = color ?? new SKColor(27, 27, 31), IsAntialias = true };
            foreach (var line in TextLayout.Wrap(text.Replace("\r", ""), font, ContentW - indent))
            {
                Ensure(font.Spacing);
                Y += -font.Metrics.Ascent;
                Canvas!.DrawText(line, Margin + indent, Y, font, paint);
                Y += font.Metrics.Descent + (font.Spacing - (-font.Metrics.Ascent + font.Metrics.Descent));
            }
            Y += after;
        }

        public void Box(CalloutKind kind, string title, string text)
        {
            using var font = new SKFont(Regular, 10);
            var lines = TextLayout.Wrap(text ?? "", font, ContentW - 24).Count + (string.IsNullOrWhiteSpace(title) ? 0 : 1);
            var h = lines * font.Spacing + 16;
            Ensure(h);
            var color = kind is CalloutKind.Warning or CalloutKind.Danger ? new SKColor(229, 72, 77) : kind is CalloutKind.Tip or CalloutKind.Success ? new SKColor(48, 164, 108) : new SKColor(47, 128, 237);
            using var bg = new SKPaint { Color = color.WithAlpha(25) };
            using var bar = new SKPaint { Color = color };
            Canvas!.DrawRect(Margin, Y, ContentW, h, bg);
            Canvas.DrawRect(Margin, Y, 4, h, bar);
            Y += 8;
            if (!string.IsNullOrWhiteSpace(title)) Text(title, 10, bold: true, indent: 14, after: 0);
            Text(text ?? "", 10, indent: 14, after: 0);
            Y += 14;
        }

        public void Image(byte[] data, int? displayWidth, string? caption)
        {
            using var img = SKImage.FromEncodedData(data);
            if (img is null) return;
            var w = Math.Min(ContentW, displayWidth is { } dw ? dw * 0.75f : ContentW);
            var h = w * img.Height / img.Width;
            if (h > H - 2 * Margin - 40) { h = H - 2 * Margin - 40; w = h * img.Width / img.Height; }
            Ensure(h + 8);
            var r = new SKRect(Margin, Y, Margin + w, Y + h);
            Canvas!.DrawImage(img, r, new SKSamplingOptions(SKCubicResampler.Mitchell));
            using var border = new SKPaint { Color = new SKColor(220, 220, 220), Style = SKPaintStyle.Stroke, StrokeWidth = 0.5f };
            Canvas.DrawRect(r, border);
            Y += h + 6;
            if (caption is not null) Text(caption, 8.5f, color: new SKColor(110, 110, 110));
        }

        public void Dispose() { if (Canvas is not null) _doc.EndPage(); _doc.Close(); _doc.Dispose(); }
    }

    public static int Export(Guide guide, GuideTemplate template, IReadOnlyDictionary<Guid, byte[]> images, string path)
    {
        var pages = 0;
        AtomicFile.Write(path, stream =>
        {
            var meta = new SKDocumentPdfMetadata { Title = guide.Title, Creator = "CaptureDoc", Producer = "CaptureDoc (SkiaSharp)", Creation = DateTime.Now, Modified = DateTime.Now, RasterDpi = 150, EncodingQuality = 90 };
            using var doc = SKDocument.CreatePdf(stream, meta) ?? throw new InvalidOperationException("PDF konnte nicht erstellt werden.");
            using var w = new Writer(doc);
            w.Text(string.IsNullOrWhiteSpace(guide.Title) ? "Unbenannt" : guide.Title, 22, bold: true, after: 4);
            var tags = template.Tags.Concat(guide.Tags).Select(MarkdownBuilder.NormalizeTag).Where(t => t.Length > 0).Distinct().ToList();
            if (tags.Count > 0) w.Text(string.Join("  ", tags.Select(t => "#" + t)), 9, color: new SKColor(90, 90, 160));
            if (!string.IsNullOrWhiteSpace(guide.Introduction)) w.Text(guide.Introduction.Trim(), 11);
            if (!string.IsNullOrWhiteSpace(guide.Prerequisites)) w.Box(template.PrerequisitesKind, template.PrerequisitesTitle, guide.Prerequisites);
            var n = 0;
            foreach (var s in guide.Steps)
            {
                n++;
                var title = string.IsNullOrWhiteSpace(s.Title) ? $"Schritt {n}" : s.Title.Trim();
                w.Ensure(60);
                w.Y += 8;
                w.Text(template.StepHeading.Replace("{n}", n.ToString()).Replace("{title}", title), 15, bold: true);
                if (images.TryGetValue(s.Id, out var img))
                    w.Image(img, s.DisplayWidth, s.Video is not null ? $"▶ Video ({s.Video.KeptDuration:0} s) – abspielbar im HTML- bzw. Obsidian-Export" : null);
                if (!string.IsNullOrWhiteSpace(s.Text)) w.Text(s.Text.Trim(), 11);
                foreach (var c in s.Callouts) w.Box(c.Kind, c.Title, c.Text);
            }
            if (guide.Checklist.Count > 0 || !string.IsNullOrWhiteSpace(guide.Conclusion))
            {
                w.Y += 8;
                w.Text(template.ChecklistTitle, 15, bold: true);
                foreach (var item in guide.Checklist) w.Text("☐  " + item, 11, after: 2);
                if (!string.IsNullOrWhiteSpace(guide.Conclusion)) { w.Y += 6; w.Text(guide.Conclusion.Trim(), 11); }
            }
            pages = w.Pages;
        });
        return pages;
    }
}
