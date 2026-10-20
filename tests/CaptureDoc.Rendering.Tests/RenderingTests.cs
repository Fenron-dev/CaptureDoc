using CaptureDoc.Core.Export;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Serialization;
using SkiaSharp;

namespace CaptureDoc.Rendering.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "capturedoc-rtests", Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string n) => System.IO.Path.Combine(Path, n);
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}

public class RenderingTests
{
    /// <summary>Noisy test image with a "secret" pattern – noise defeats compression so size tests are meaningful.</summary>
    internal static byte[] NoisePng(int w, int h, int seed = 42)
    {
        var rnd = new Random(seed);
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var px = bmp.GetPixelSpan();
        for (var i = 0; i < px.Length; i += 4) { px[i] = (byte)rnd.Next(256); px[i + 1] = (byte)rnd.Next(256); px[i + 2] = (byte)rnd.Next(256); px[i + 3] = 255; }
        bmp.NotifyPixelsChanged();
        using var img = SKImage.FromBitmap(bmp);
        return SkiaConvert.EncodePng(img);
    }

    internal static (Page page, SceneRenderer renderer, AssetStore store) Scene(int w = 400, int h = 300)
    {
        var store = new AssetStore();
        var id = store.Add(NoisePng(w, h));
        var page = new Page { Width = w, Height = h };
        page.Layers.Add(new ImageLayer { AssetId = id, IsBackground = true, Rect = new RectD(0, 0, w, h) });
        return (page, new SceneRenderer(new AssetImageCache(store)), store);
    }

    private static SKColor Px(SKBitmap b, int x, int y) => b.GetPixel(x, y);

    [Fact]
    public void Blackout_is_burned_in_and_original_is_not_recoverable()
    {
        var (page, r, _) = Scene();
        using var original = SKBitmap.FromImage(r.RenderPage(page));
        page.Layers.Add(new RedactLayer { Mode = RedactMode.Blackout, Rect = new RectD(50, 60, 100, 40), Opacity = 0.2 /* must be ignored */ });

        var outcome = ImageExporter.Export(r, page, BuiltInProfiles.Get("original"));
        using var exported = SKBitmap.Decode(outcome.Data);
        for (var y = 60; y < 100; y++)
        for (var x = 50; x < 150; x++)
            Assert.Equal(new SKColor(0, 0, 0, 255), Px(exported, x, y));
        // outside untouched
        Assert.Equal(Px(original, 10, 10), Px(exported, 10, 10));
        // Exported file has no layers: decoding gives a single flat image of the page size
        Assert.Equal(400, exported.Width);
    }

    [Fact]
    public void Pixelate_blocks_are_uniform_averages()
    {
        var (page, r, _) = Scene();
        page.Layers.Add(new RedactLayer { Mode = RedactMode.Pixelate, Strength = 10, Rect = new RectD(100, 100, 50, 50) });
        using var bmp = SKBitmap.FromImage(r.RenderPage(page));
        for (var by = 100; by < 150; by += 10)
        for (var bx = 100; bx < 150; bx += 10)
        {
            var c = Px(bmp, bx, by);
            for (var y = by; y < by + 10; y++)
            for (var x = bx; x < bx + 10; x++)
                Assert.Equal(c, Px(bmp, x, y));
        }
        // averaged noise is close to mid-grey, i.e. not an original pixel value pattern
        var mid = Px(bmp, 120, 120);
        Assert.InRange(mid.Red, 70, 185);
    }

    [Fact]
    public void Blur_changes_region_only()
    {
        var (page, r, _) = Scene();
        using var before = SKBitmap.FromImage(r.RenderPage(page));
        page.Layers.Add(new RedactLayer { Mode = RedactMode.Blur, Strength = 8, Rect = new RectD(100, 100, 80, 60) });
        using var after = SKBitmap.FromImage(r.RenderPage(page));
        Assert.Equal(Px(before, 50, 50), Px(after, 50, 50));
        var changed = 0;
        for (var x = 110; x < 170; x++) if (Px(before, x, 130) != Px(after, x, 130)) changed++;
        Assert.True(changed > 50);
    }

    [Fact]
    public void All_layer_types_render_without_errors_and_change_pixels()
    {
        var (page, r, _) = Scene(600, 400);
        using var before = SKBitmap.FromImage(r.RenderPage(page));
        var layers = new Layer[]
        {
            new ShapeLayer { Rect = new RectD(10, 10, 80, 50) },
            new ShapeLayer { Shape = ShapeKind.Ellipse, Rect = new RectD(100, 10, 80, 50), Fill = ColorRgba.Blue },
            new ShapeLayer { Shape = ShapeKind.RoundedRectangle, Rect = new RectD(190, 10, 80, 50) },
            new LineLayer { Start = new(10, 100), End = new(200, 150), StartCap = LineCap.Dot },
            new TextLayer { Rect = new RectD(220, 100, 150, 0), Text = "Ein längerer Text der umbrechen muss", Style = TextStyle.InfoBox, Border = ColorRgba.Red },
            new TextLayer { Rect = new RectD(400, 100, 150, 60), Text = "Blase", Style = TextStyle.Bubble, TailTip = new PointD(380, 250) },
            new StepLayer { Center = new(50, 250), Number = 12 },
            new FreehandLayer { Points = { new(100, 250), new(120, 270), new(150, 240), new(170, 290) } },
            new HighlightLayer { Rect = new RectD(200, 220, 100, 30) },
            new SpotlightLayer { Rect = new RectD(320, 220, 100, 80), Elliptical = true },
            new MagnifierLayer { Rect = new RectD(450, 250, 120, 120), Source = new RectD(10, 10, 40, 40), Elliptical = true },
            new GroupLayer { Opacity = 0.5, Children = { new StepLayer { Center = new(560, 30), Number = 3 } } },
        };
        foreach (var l in layers)
        {
            page.Layers.Add(l);
            using var img = r.RenderPage(page);
            Assert.Equal(600, img.Width);
        }
        using var after = SKBitmap.FromImage(r.RenderPage(page));
        Assert.NotEqual(Px(before, 50, 250), Px(after, 50, 250)); // step drawn
    }

    [Fact]
    public void Hidden_layers_are_not_rendered()
    {
        var (page, r, _) = Scene();
        using var before = SKBitmap.FromImage(r.RenderPage(page));
        page.Layers.Add(new ShapeLayer { Rect = new RectD(0, 0, 400, 300), Fill = ColorRgba.Red, Visible = false });
        using var after = SKBitmap.FromImage(r.RenderPage(page));
        Assert.Equal(Px(before, 200, 150), Px(after, 200, 150));
    }

    [Fact]
    public void Export_respects_max_width_and_aspect_and_writes_verified_file()
    {
        using var t = new TempDir();
        var (page, r, _) = Scene(1600, 900);
        var profile = new ExportProfile { Format = ImageFormat.Png, MaxWidth = 800 };
        var path = t.File("out.png");
        var o = ImageExporter.Export(r, page, profile, path);
        Assert.True(o.Success, o.Message);
        using var codec = SKCodec.Create(path);
        Assert.Equal(800, codec.Info.Width);
        Assert.Equal(450, codec.Info.Height);
        Assert.Equal(new FileInfo(path).Length, o.Bytes);
    }

    [Fact]
    public void Export_does_not_upscale_small_images()
    {
        var (page, r, _) = Scene(300, 200);
        var o = ImageExporter.Export(r, page, new ExportProfile { MaxWidth = 1920 });
        Assert.Equal(300, o.Width);
    }

    [Theory]
    [InlineData(ImageFormat.Jpeg)]
    [InlineData(ImageFormat.WebP)]
    public void Hard_size_limit_is_met_and_checked_on_disk(ImageFormat format)
    {
        using var t = new TempDir();
        var (page, r, _) = Scene(1200, 800);
        var limit = 120 * 1024;
        var profile = new ExportProfile { Format = format, Lossless = false, Quality = 95, MaxBytes = limit, LimitKind = SizeLimitKind.Hard, MinQuality = 20, MinWidth = 200 };
        var path = t.File("limit" + profile.FileExtension);
        var o = ImageExporter.Export(r, page, profile, path);
        Assert.True(o.Success, o.Message);
        Assert.True(o.LimitMet);
        Assert.True(new FileInfo(path).Length <= limit, $"{new FileInfo(path).Length} > {limit}");
        Assert.Contains("geprüft", o.Message);
    }

    [Fact]
    public void Unreachable_hard_limit_writes_no_file_and_explains()
    {
        using var t = new TempDir();
        var (page, r, _) = Scene(800, 600);
        var profile = new ExportProfile { Format = ImageFormat.Png, MaxBytes = 2_000, LimitKind = SizeLimitKind.Hard, MinWidth = 400 };
        var path = t.File("never.png");
        var o = ImageExporter.Export(r, page, profile, path);
        Assert.False(o.Success);
        Assert.False(File.Exists(path));
        Assert.Contains("nicht erreichbar", o.Message);
    }

    [Fact]
    public void Jpeg_export_contains_no_exif()
    {
        var (page, r, _) = Scene();
        var o = ImageExporter.Export(r, page, new ExportProfile { Format = ImageFormat.Jpeg, Lossless = false });
        var d = o.Data!;
        // APP1 marker (FF E1) carries EXIF/XMP
        for (var i = 0; i < Math.Min(d.Length - 1, 4096); i++)
            Assert.False(d[i] == 0xFF && d[i + 1] == 0xE1, "APP1 (EXIF) marker found");
    }

    [Fact]
    public void Acceptance_screenshot_with_three_layers_survives_save_and_reload_pixel_identically()
    {
        using var t = new TempDir();
        var (page, r, store) = Scene(500, 300);
        page.Layers.Add(new LineLayer { Start = new(20, 20), End = new(200, 120) });
        page.Layers.Add(new TextLayer { Rect = new RectD(220, 40, 200, 0), Text = "Hier klicken", Style = TextStyle.InfoBox });
        page.Layers.Add(new StepLayer { Center = new(400, 200), Number = 1 });
        var project = new CaptureProject { Title = "Akzeptanz 1" };
        project.Pages.Add(page);
        var path = t.File("akzeptanz.capturedoc");
        ProjectFile.Save(path, project, store);
        using var before = SKBitmap.FromImage(r.RenderPage(page));

        var loaded = ProjectFile.Load(path);
        var r2 = new SceneRenderer(new AssetImageCache(loaded.Assets));
        var p2 = loaded.Project.Pages[0];
        using var after = SKBitmap.FromImage(r2.RenderPage(p2));
        Assert.Equal(before.Bytes, after.Bytes);

        // continue editing all layers
        var step = p2.Layers.OfType<StepLayer>().Single();
        step.Number = 2; step.Fill = ColorRgba.Blue; step.Translate(-100, 0);
        p2.Layers.OfType<TextLayer>().Single().Text = "Geändert";
        p2.Layers.OfType<LineLayer>().Single().End = new PointD(300, 200);
        using var edited = SKBitmap.FromImage(r2.RenderPage(p2));
        Assert.NotEqual(before.Bytes, edited.Bytes);
    }

    [Fact]
    public void Text_wraps_inside_box_width()
    {
        var t = new TextLayer { Rect = new RectD(0, 0, 120, 0), Text = "eins zwei drei vier fünf sechs sieben", FontSize = 20 };
        var layout = TextLayout.Layout(t);
        Assert.True(layout.Lines.Count > 1);
        Assert.Equal(120, layout.Box.Width);
        var fit = TextLayout.FitBox(t);
        Assert.True(fit.Height > 40);
    }
}
