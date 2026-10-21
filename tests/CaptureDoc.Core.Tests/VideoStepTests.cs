using System.Text.Json;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Obsidian;
using CaptureDoc.Core.Serialization;

namespace CaptureDoc.Core.Tests;

public class VideoStepTests
{
    [Fact]
    public void Slides_and_marker_strokes_round_trip_and_keep_their_images()
    {
        using var t = new TempDir();
        var project = new CaptureProject { Title = "Video" };
        var assets = new AssetStore();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var id = assets.Add(png);
        project.Assets[id] = new AssetInfo { Id = id, FileName = id + ".png", Width = 1, Height = 1, Length = png.Length };
        var slide = new VideoSlide { Name = "Zusammenfassung", At = 3, Duration = 6, Page = new Page { Width = 320, Height = 240, Background = new ColorRgba(0, 0, 200) } };
        slide.Page.Layers.Add(new ImageLayer { AssetId = id, Rect = new RectD(10, 10, 50, 50), Timing = new LayerTiming { StartSeconds = 1, EndSeconds = 6 } });
        var overlay = new VideoOverlay { Page = new Page { Width = 320, Height = 240 }, Slides = { slide } };
        overlay.Page.Layers.Add(new FreehandLayer { Highlighter = true, Stroke = ColorRgba.Yellow.WithAlpha(110), StrokeWidth = 16, Points = { new PointD(0, 0), new PointD(10, 10) } });
        project.Guide.Steps.Add(new GuideStep { Title = "V", Video = new VideoRef { Path = "clip.mkv", End = 10, Overlay = overlay } });
        Assert.Contains(id, ProjectFile.CollectUsedAssets(project));      // image only used on a slide is not dropped
        var file = t.File("v.capturedoc");
        ProjectFile.Save(file, project, assets);
        var loaded = ProjectFile.Load(file);
        var ov = loaded.Project.Guide.Steps[0].Video!.Overlay!;
        var sl = Assert.Single(ov.Slides);
        Assert.Equal(("Zusammenfassung", 3.0, 6.0), (sl.Name, sl.At, sl.Duration));
        Assert.Equal(new ColorRgba(0, 0, 200), sl.Page.Background);
        Assert.Equal(1, sl.Page.Layers[0].Timing!.StartSeconds);
        Assert.True(loaded.Assets.Contains(id));
        var marker = Assert.IsType<FreehandLayer>(ov.Page.Layers[0]);
        Assert.True(marker.Highlighter);
        Assert.Equal(110, marker.Stroke.A);
        Assert.Equal("Textmarker", marker.Kind);
    }

    private static (Guide g, string vault, string video) Setup(TempDir t)
    {
        var vault = Path.Combine(t.Path, "Vault");
        Directory.CreateDirectory(Path.Combine(vault, ".obsidian"));
        var video = t.File("clip.mp4");
        File.WriteAllBytes(video, Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray());
        var g = MarkdownTests.SampleGuide();
        g.Steps[1].Video = new VideoRef { Path = video, Start = 0, End = 5 };
        return (g, vault, video);
    }

    private static ObsidianExportRequest Req(Guide g, string vault, string video, LinkStyle links = LinkStyle.Wikilink) => new()
    {
        VaultRoot = vault, NoteFolder = "Doku/Video", Guide = g, Template = BuiltInTemplates.All[0], Links = links, NoteName = "Anleitung",
        Images = g.Steps.ToDictionary(s => s.Id, s => new RenderedImage(new byte[] { 1, 2, (byte)g.Steps.IndexOf(s) }, ".png", 4, 4)),
        Videos = new Dictionary<Guid, RenderedMedia> { [g.Steps[1].Id] = new(video, ".mp4", true) },
    };

    [Fact]
    public void Video_step_is_copied_into_vault_and_embedded()
    {
        using var t = new TempDir();
        var (g, vault, video) = Setup(t);
        var res = ObsidianExporter.Execute(ObsidianExporter.Plan(Req(g, vault, video)), g.ExportId);
        Assert.True(res.Verified, string.Join(",", res.MissingAssets));
        var copied = Path.Combine(vault, "Doku", "Video", "Assets", "drucker-konfigurieren-schritt-02-video.mp4");
        Assert.Equal(File.ReadAllBytes(video), File.ReadAllBytes(copied));
        var md = File.ReadAllText(res.NotePath);
        Assert.Contains("![[Doku/Video/Assets/drucker-konfigurieren-schritt-02-video.mp4|700]]", md);
        Assert.DoesNotContain("schritt-02.png", md);   // video replaces the image
        Assert.Contains("schritt-01.png", md);
    }

    [Fact]
    public void Unchanged_video_is_not_rewritten_and_modified_video_is_protected()
    {
        using var t = new TempDir();
        var (g, vault, video) = Setup(t);
        ObsidianExporter.Execute(ObsidianExporter.Plan(Req(g, vault, video)), g.ExportId);
        var again = ObsidianExporter.Plan(Req(g, vault, video));
        Assert.All(again.AllFiles, f => Assert.Equal(PlannedAction.Unchanged, f.Action));

        var copied = Path.Combine(vault, "Doku", "Video", "Assets", "drucker-konfigurieren-schritt-02-video.mp4");
        File.WriteAllBytes(copied, new byte[] { 9, 9 });                     // edited in the vault
        File.AppendAllText(video, "x");                                      // and re-rendered in CaptureDoc
        var plan = ObsidianExporter.Plan(Req(g, vault, video));
        var v = plan.Assets.Single(a => a.FullPath.EndsWith(".mp4"));
        Assert.Equal(PlannedAction.ConflictNewVersion, v.Action);
        ObsidianExporter.Execute(plan, g.ExportId);
        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(copied));
    }

    [Fact]
    public void Markdown_link_style_embeds_video_relative()
    {
        using var t = new TempDir();
        var (g, vault, video) = Setup(t);
        var res = ObsidianExporter.Execute(ObsidianExporter.Plan(Req(g, vault, video, LinkStyle.Markdown)), g.ExportId);
        Assert.True(res.Verified);
        Assert.Contains("](Assets/drucker-konfigurieren-schritt-02-video.mp4)", File.ReadAllText(res.NotePath));
    }

    [Fact]
    public void Html_uses_video_element_with_poster()
    {
        var g = MarkdownTests.SampleGuide();
        var html = HtmlBuilder.Build(g, BuiltInTemplates.All[0], new Dictionary<Guid, MarkdownBuilder.ImageRef>
        {
            [g.Steps[0].Id] = new("assets/a.png", 700, "A"),
            [g.Steps[1].Id] = new("assets/b.mp4", 640, "B", IsVideo: true, PosterTarget: "assets/b.png"),
        });
        Assert.Contains("<video controls preload=\"metadata\" playsinline width=\"640\" poster=\"assets/b.png\" src=\"assets/b.mp4\"", html);
        Assert.Contains("a class=\"zoom\" href=\"assets/a.png\"", html);
    }

    [Fact]
    public void Video_refs_survive_project_move_when_stored_relative()
    {
        using var t = new TempDir();
        var proj = t.File("p/projekt.capturedoc");
        var vid = Path.Combine(t.Path, "p", "media", "a.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(vid)!);
        File.WriteAllText(vid, "x");
        var stored = ProjectFile.ToProjectRelative(proj, vid, p => p);
        Assert.Equal("media/a.mkv", stored);

        var moved = t.File("q");
        Directory.Move(t.File("p"), moved);
        var resolved = ProjectFile.ResolveMedia(Path.Combine(moved, "projekt.capturedoc"), stored, p => p);
        Assert.Equal(Path.Combine(moved, "media", "a.mkv"), resolved);
    }

    [Fact]
    public void Video_ref_serializes_and_v1_projects_still_load()
    {
        using var t = new TempDir();
        var (p, a) = ProjectFileTests.SampleProject();
        p.Guide.Steps[0].Video = new VideoRef { Path = "media/x.mkv", Start = 1, End = 9, Cuts = { new TimeCut { From = 2, To = 3 } }, Format = VideoOutputFormat.Gif };
        var file = t.File("v.capturedoc");
        ProjectFile.Save(file, p, a);
        var back = ProjectFile.Load(file).Project.Guide.Steps[0].Video!;
        Assert.Equal(VideoOutputFormat.Gif, back.Format);
        Assert.Equal(7, back.KeptDuration, 3);
        Assert.Equal(CaptureProject.CurrentFormatVersion, ProjectFile.TryReadManifest(file)!.FormatVersion);
    }
}
