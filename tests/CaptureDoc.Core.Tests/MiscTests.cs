using CaptureDoc.Core.Model;
using CaptureDoc.Core.Obsidian;
using CaptureDoc.Core.Platform;
using CaptureDoc.Core.Portable;

namespace CaptureDoc.Core.Tests;

public class MiscTests
{
    [Fact]
    public void File_name_pattern_formats_dates_and_is_safe()
    {
        var t = new DateTime(2026, 10, 20, 18, 30, 5);
        Assert.Equal("Screenshot_2026-10-20_18-30-05", FileNamePattern.Format("Screenshot_{yyyy-MM-dd_HH-mm-ss}", t));
        Assert.Equal("Ticket 2026 HH", FileNamePattern.Format("Ticket {yyyy} HH", t));    // text outside braces stays literal
        Assert.DoesNotContain(":", FileNamePattern.Format("a:{HH:mm}", t));
    }

    [Theory]
    [InlineData("Ctrl+Shift+4", HotkeyModifiers.Control | HotkeyModifiers.Shift, "4")]
    [InlineData("Strg+Druck", HotkeyModifiers.Control, "PrintScreen")]
    [InlineData("alt+printscreen", HotkeyModifiers.Alt, "PrintScreen")]
    [InlineData("F9", HotkeyModifiers.None, "F9")]
    public void Hotkeys_parse(string text, HotkeyModifiers mods, string key)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g));
        Assert.Equal(mods, g.Modifiers);
        Assert.Equal(key, g.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Shift")]
    [InlineData("A+B")]
    public void Invalid_hotkeys_are_rejected(string text) => Assert.False(HotkeyGesture.TryParse(text, out _));

    [Fact]
    public void Html_export_escapes_content_and_references_images()
    {
        var g = MarkdownTests.SampleGuide();
        g.Steps[0].Text = "<script>alert(1)</script>";
        var html = HtmlBuilder.Build(g, BuiltInTemplates.All[0], new Dictionary<Guid, MarkdownBuilder.ImageRef>
        {
            [g.Steps[0].Id] = new("assets/schritt 01.png", 700, "Bild"),
        });
        Assert.Contains("<!doctype html>", html);
        Assert.Contains("&lt;script&gt;alert(1)", html);
        Assert.DoesNotContain("<script>alert", html);                 // user text is never executable
        Assert.Contains("src=\"assets/schritt%2001.png\"", html);
        Assert.Contains("class=\"callout warning\"", html);
    }

    [Fact]
    public void Html_images_open_in_offline_lightbox()
    {
        var g = MarkdownTests.SampleGuide();
        var html = HtmlBuilder.Build(g, BuiltInTemplates.All[0], new Dictionary<Guid, MarkdownBuilder.ImageRef>
        {
            [g.Steps[0].Id] = new("assets/a.png", 700, "A"),
            [g.Steps[1].Id] = new("assets/b.png", 700, "B"),
        });
        Assert.Contains("<a class=\"zoom\" href=\"assets/a.png\"", html);   // without JS the link opens the image
        Assert.Contains("id=\"lb\"", html);
        Assert.DoesNotContain("http://", html);                            // no external resources
        Assert.DoesNotContain("https://", html);

        var noImages = HtmlBuilder.Build(g, BuiltInTemplates.All[0], new Dictionary<Guid, MarkdownBuilder.ImageRef>());
        Assert.DoesNotContain("id=\"lb\"", noImages);
    }

    [Fact]
    public void Startup_commands_are_parsed()
    {
        var c = StartupCommand.Parse(new[] { "--capture", "Region", "a.capturedoc", "--studio" });
        Assert.Equal("region", c.Capture);
        Assert.True(c.Studio);
        Assert.Equal(new[] { "a.capturedoc" }, c.Files);
        Assert.Null(StartupCommand.Parse(new[] { "--capture", "unsinn" }).Capture);
    }

    [Fact]
    public void Second_instance_forwards_arguments_to_the_first()
    {
        using var t = new TempDir();
        using var first = new SingleInstance(t.Path);
        Assert.True(first.IsFirst);
        string[]? received = null;
        using var got = new ManualResetEventSlim();
        first.CommandReceived += (_, a) => { received = a; got.Set(); };
        first.Listen();
        using (var second = new SingleInstance(t.Path))
        {
            Assert.False(second.IsFirst);
            Assert.True(second.Send(new[] { "--capture", "video" }));
        }
        Assert.True(got.Wait(TimeSpan.FromSeconds(5)), "command not received");
        Assert.Equal(new[] { "--capture", "video" }, received);
    }

    [Fact]
    public void Raw_image_crop_uses_virtual_coordinates()
    {
        // 4x2 image located at virtual (-4,0) (monitor left of the primary)
        var px = new byte[4 * 2 * 4];
        for (var i = 0; i < 8; i++) px[i * 4] = (byte)i;
        var img = new RawImage(4, 2, px) { SourceBounds = new PixelRect(-4, 0, 4, 2) };
        var c = img.Crop(new PixelRect(-2, 1, 2, 1));
        Assert.Equal(2, c.Width);
        Assert.Equal(6, c.Pixels[0]);
        Assert.Equal(7, c.Pixels[4]);
    }
}
