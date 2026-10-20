using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Serialization;
using CaptureDoc.Core.Storage;

namespace CaptureDoc.Core.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "capturedoc-tests", Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}

public class ProjectFileTests
{
    internal static (CaptureProject, AssetStore) SampleProject()
    {
        var assets = new AssetStore();
        var id = assets.Add(Encoding.ASCII.GetBytes("fake-png-bytes-" + Guid.NewGuid()));
        var page = new Page { Width = 640, Height = 480, Name = "Screenshot" };
        page.Layers.Add(new ImageLayer { AssetId = id, IsBackground = true, Rect = new RectD(0, 0, 640, 480) });
        page.Layers.Add(new LineLayer { Start = new(10, 10), End = new(100, 100), Name = "Pfeil 1" });
        page.Layers.Add(new TextLayer { Text = "Klick hier", Rect = new RectD(120, 40, 200, 50), Name = "Text 1" });
        page.Layers.Add(new StepLayer { Center = new(300, 300), Number = 1, Name = "Schritt 1" });
        var p = new CaptureProject { Title = "Test" };
        p.Pages.Add(page);
        p.Assets[id] = new AssetInfo { Id = id, FileName = id + ".png", Width = 640, Height = 480 };
        p.Guide.Title = "Test";
        p.Guide.Steps.Add(new GuideStep { Title = "Öffnen", PageId = page.Id });
        return (p, assets);
    }

    [Fact]
    public void Save_and_reload_keeps_every_layer_editable()
    {
        using var t = new TempDir();
        var (p, a) = SampleProject();
        var path = t.File("projekt.capturedoc");
        ProjectFile.Save(path, p, a);
        Assert.Equal(1, p.Revision);

        var loaded = ProjectFile.Load(path);
        Assert.Empty(loaded.Warnings);
        var page = loaded.Project.Pages.Single();
        Assert.Equal(4, page.Layers.Count);
        Assert.Equal(new[] { "Pfeil 1", "Text 1", "Schritt 1" }, page.Layers.Skip(1).Select(l => l.Name));
        var step = page.Layers.OfType<StepLayer>().Single();
        step.Number = 5; step.Translate(10, 10);           // still editable
        Assert.Equal(new PointD(310, 310), step.Center);
        Assert.True(loaded.Assets.Contains(page.Layers.OfType<ImageLayer>().Single().AssetId));
    }

    [Fact]
    public void Container_has_mimetype_first_and_manifest_hashes()
    {
        using var t = new TempDir();
        var (p, a) = SampleProject();
        var path = t.File("x.capturedoc");
        ProjectFile.Save(path, p, a, thumbnailPng: new byte[] { 1, 2, 3 });
        using var zip = ZipFile.OpenRead(path);
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        var m = ProjectFile.TryReadManifest(path)!;
        Assert.Contains("project.json", m.Entries.Keys);
        Assert.Contains("thumbnail.png", m.Entries.Keys);
        Assert.Equal(p.Id, m.ProjectId);
    }

    [Fact]
    public void Moved_project_opens_from_new_location()
    {
        using var t = new TempDir();
        var (p, a) = SampleProject();
        var path = t.File("a.capturedoc");
        ProjectFile.Save(path, p, a);
        var otherDir = Directory.CreateDirectory(t.File("verschoben/tiefer")).FullName;
        var moved = Path.Combine(otherDir, "a.capturedoc");
        File.Move(path, moved);
        var loaded = ProjectFile.Load(moved);
        Assert.Empty(loaded.Warnings);
        using var zip = ZipFile.OpenRead(moved);
        foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".json")))
        {
            using var r = new StreamReader(e.Open());
            var text = r.ReadToEnd();
            Assert.DoesNotContain(t.Path, text);                                   // no absolute paths inside
            Assert.DoesNotContain(t.Path.Replace("\\", "\\\\"), text);
        }
    }

    [Fact]
    public void Corrupted_project_json_is_detected()
    {
        using var t = new TempDir();
        var (p, a) = SampleProject();
        var path = t.File("c.capturedoc");
        ProjectFile.Save(path, p, a);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var e = zip.GetEntry("project.json")!;
            string json; using (var r = new StreamReader(e.Open())) json = r.ReadToEnd();
            e.Delete();
            var ne = zip.CreateEntry("project.json");
            using var w = new StreamWriter(ne.Open());
            w.Write(json.Replace("Klick hier", "Klick hiex"));
        }
        var ex = Assert.Throws<ProjectFormatException>(() => ProjectFile.Load(path));
        Assert.Contains("Prüfsumme", ex.Message);
    }

    [Fact]
    public void Corrupted_asset_is_reported_but_project_opens()
    {
        using var t = new TempDir();
        var (p, a) = SampleProject();
        var path = t.File("d.capturedoc");
        ProjectFile.Save(path, p, a);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var e = zip.Entries.First(x => x.FullName.StartsWith("assets/"));
            var name = e.FullName; e.Delete();
            using var s = zip.CreateEntry(name).Open(); s.Write(new byte[] { 9, 9, 9 });
        }
        var loaded = ProjectFile.Load(path);
        Assert.NotEmpty(loaded.Warnings);
        Assert.Single(loaded.Project.Pages);
    }

    [Fact]
    public void Truncated_file_is_rejected_with_clear_error()
    {
        using var t = new TempDir();
        var (p, a) = SampleProject();
        var path = t.File("e.capturedoc");
        ProjectFile.Save(path, p, a);
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);
        Assert.Throws<ProjectFormatException>(() => ProjectFile.Load(path));
    }

    [Fact]
    public void Concurrent_save_is_detected_as_conflict()
    {
        using var t = new TempDir();
        var (p, a) = SampleProject();
        var path = t.File("f.capturedoc");
        ProjectFile.Save(path, p, a);
        var userA = ProjectFile.Load(path);
        var userB = ProjectFile.Load(path);
        userA.Project.Title = "A";
        ProjectFile.Save(path, userA.Project, userA.Assets, expectedRevision: userA.Project.Revision);
        userB.Project.Title = "B";
        var ex = Assert.Throws<ProjectConflictException>(() => ProjectFile.Save(path, userB.Project, userB.Assets, expectedRevision: userB.Project.Revision));
        Assert.Equal(2, ex.FoundRevision);
        Assert.Equal("A", ProjectFile.Load(path).Project.Title);
    }

    [Fact]
    public void Failed_save_leaves_previous_file_intact()
    {
        using var t = new TempDir();
        var (p, a) = SampleProject();
        var path = t.File("g.capturedoc");
        ProjectFile.Save(path, p, a);
        var before = File.ReadAllBytes(path);
        p.Pages[0].Layers.Add(new ImageLayer { AssetId = "doesnotexist" });
        Assert.Throws<ProjectFormatException>(() => ProjectFile.Save(path, p, a));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(t.Path, "*.tmp"));
    }

    [Fact]
    public void Newer_format_version_is_rejected()
    {
        using var t = new TempDir();
        var (p, a) = SampleProject();
        var path = t.File("h.capturedoc");
        ProjectFile.Save(path, p, a);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var e = zip.GetEntry("manifest.json")!;
            JsonNode node; using (var s = e.Open()) node = JsonNode.Parse(s)!;
            node["formatVersion"] = 99; e.Delete();
            using var w = new StreamWriter(zip.CreateEntry("manifest.json").Open()); w.Write(node.ToJsonString());
        }
        var ex = Assert.Throws<ProjectFormatException>(() => ProjectFile.Load(path));
        Assert.Contains("neueren", ex.Message);
    }

    private sealed class RenameTitleMigration : IProjectMigration
    {
        public int FromVersion => 0;
        public JsonNode Migrate(JsonNode project)
        {
            project["title"] = project["name"]?.GetValue<string>();
            project.AsObject().Remove("name");
            return project;
        }
    }

    [Fact]
    public void Migration_chain_upgrades_old_json()
    {
        ProjectFile.Migrations.Add(new RenameTitleMigration());
        try
        {
            var old = JsonNode.Parse("""{"formatVersion":0,"name":"Alt","pages":[]}""")!;
            var v = 0;
            var migrated = ProjectFile.Migrate(old, ref v);
            Assert.Equal(1, v);
            Assert.Equal("Alt", migrated["title"]!.GetValue<string>());
        }
        finally { ProjectFile.Migrations.Clear(); }
    }

    [Fact]
    public void Unused_assets_are_not_written()
    {
        using var t = new TempDir();
        var (p, a) = SampleProject();
        var orphan = a.Add(new byte[] { 1, 2, 3, 4 });
        p.Assets[orphan] = new AssetInfo { Id = orphan, FileName = orphan + ".png" };
        var path = t.File("i.capturedoc");
        ProjectFile.Save(path, p, a);
        using var zip = ZipFile.OpenRead(path);
        Assert.Single(zip.Entries.Where(e => e.FullName.StartsWith("assets/")));
        Assert.DoesNotContain(orphan, p.Assets.Keys);
    }

    [Fact]
    public void Lock_prevents_second_writer()
    {
        using var t = new TempDir();
        var path = t.File("lock.capturedoc");
        Assert.True(ProjectLock.TryAcquire(path, out var l1, out _));
        try
        {
            Assert.False(ProjectLock.TryAcquire(path, out var l2, out var held));
            Assert.Null(l2);
            Assert.NotNull(held);
        }
        finally { l1!.Dispose(); }
        Assert.True(ProjectLock.TryAcquire(path, out var l3, out _));
        l3!.Dispose();
    }

    [Fact]
    public void AtomicFile_replaces_content()
    {
        using var t = new TempDir();
        var f = t.File("s.json");
        AtomicFile.WriteAllText(f, "1");
        AtomicFile.WriteAllText(f, "2");
        Assert.Equal("2", File.ReadAllText(f));
        Assert.Single(Directory.GetFiles(t.Path));
    }
}
