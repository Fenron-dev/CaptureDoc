using CaptureDoc.Core.Export;
using CaptureDoc.Core.Portable;

namespace CaptureDoc.Core.Tests;

public class PortableTests
{
    [Theory]
    [InlineData(@"C:\Stick\CaptureDoc", @"C:\Stick\CaptureDoc")]
    [InlineData(@"C:\Stick\CaptureDoc\CaptureDoc-macOS.app\Contents\MacOS", @"C:\Stick\CaptureDoc")]
    public void FindAppRoot_handles_mac_bundles(string exeDir, string expected)
    {
        if (!OperatingSystem.IsWindows()) return; // paths in this test are Windows-style
        Assert.Equal(expected, PortableEnvironment.FindAppRoot(exeDir));
    }

    [Fact]
    public void Portable_paths_are_relative_inside_app_root()
    {
        using var t = new TempDir();
        var env = new PortableEnvironment(t.Path);
        var inside = Path.Combine(t.Path, "Data", "Vaults", "Mein Vault");
        var stored = env.ToPortablePath(inside);
        Assert.Equal("./Data/Vaults/Mein Vault", stored);
        Assert.Equal(Path.GetFullPath(inside), env.FromPortablePath(stored));

        var outside = Path.GetFullPath(Path.Combine(t.Path, "..", "elsewhere"));
        Assert.Equal(outside, env.ToPortablePath(outside));
    }

    [Fact]
    public void Relocated_root_resolves_relative_paths_to_new_location()
    {
        using var t1 = new TempDir(); using var t2 = new TempDir();
        var stored = new PortableEnvironment(t1.Path).ToPortablePath(Path.Combine(t1.Path, "Data", "Projects"));
        Assert.Equal(Path.Combine(t2.Path, "Data", "Projects"), new PortableEnvironment(t2.Path).FromPortablePath(stored));
    }

    [Fact]
    public void EnsureWritable_creates_layout()
    {
        using var t = new TempDir();
        var env = new PortableEnvironment(t.Path, platform: PlatformId.Windows);
        var check = env.EnsureWritable();
        Assert.True(check.IsWritable, check.Problem);
        foreach (var d in new[] { "Shared", "Settings/Shared", "Settings/Windows", "Settings/MacOS", "Settings/Linux", "Projects", "Media", "Templates", "Presets", "Cache", "Temp", "Logs" })
            Assert.True(Directory.Exists(Path.Combine(t.Path, "Data", d)), d);
    }

    [Fact]
    public void Settings_are_split_by_platform_and_broken_files_are_backed_up()
    {
        using var t = new TempDir();
        var env = new PortableEnvironment(t.Path, platform: PlatformId.Windows);
        env.EnsureWritable();
        var s = new SettingsStore(env);
        s.Load();
        s.Shared.AfterCapture = AfterCaptureAction.CopyToClipboard;
        s.Platform.IncludeCursor = true;
        s.Save();
        Assert.True(File.Exists(Path.Combine(t.Path, "Data", "Settings", "Shared", "settings.json")));
        Assert.True(File.Exists(Path.Combine(t.Path, "Data", "Settings", "Windows", "settings.json")));

        var s2 = new SettingsStore(env); s2.Load();
        Assert.Equal(AfterCaptureAction.CopyToClipboard, s2.Shared.AfterCapture);
        Assert.True(s2.Platform.IncludeCursor);

        File.WriteAllText(s2.SharedPath, "{ kaputt");
        var s3 = new SettingsStore(env); s3.Load();
        Assert.Single(s3.Problems);
        Assert.Equal(AfterCaptureAction.OpenEditor, s3.Shared.AfterCapture);
        Assert.NotEmpty(Directory.GetFiles(env.SettingsSharedDir, "settings.json.broken-*"));
    }
}

public class SizeOptimizerTests
{
    // Fake encoder: bytes = w*h*quality/100 (+header). Deterministic and monotonic like real encoders.
    private static byte[] Fake(EncodeParameters p) => new byte[100 + p.Width * p.Height * p.Quality / 100];

    private static SizeOptimizer.Request Req(int w, int h, long? max, SizeLimitKind kind, bool lossy = true, int? maxW = null) =>
        new(w, h, maxW, null, 1.0, lossy, 90, 40, max, kind, 320);

    [Fact]
    public void Never_upscales()
    {
        Assert.Equal((800, 450), SizeOptimizer.FitDimensions(800, 450, 1920, 1080, 1.0));
        Assert.Equal((800, 450), SizeOptimizer.FitDimensions(1600, 900, 800, 450, 1.0));
        Assert.Equal((400, 225), SizeOptimizer.FitDimensions(1600, 900, 800, null, 0.25));
        Assert.Equal((800, 450), SizeOptimizer.FitDimensions(800, 450, null, null, 2.0));
    }

    [Fact]
    public void Lossy_finds_highest_quality_under_limit()
    {
        var r = SizeOptimizer.Optimize(Req(1000, 1000, 600_000, SizeLimitKind.Hard), Fake);
        Assert.True(r.Success);
        Assert.True(r.Bytes <= 600_000);
        Assert.Equal(59, r.Parameters.Quality); // 1e6*59/100 + 100 <= 600000 < q=60
        Assert.Equal(1000, r.Parameters.Width);
    }

    [Fact]
    public void Shrinks_dimensions_when_quality_alone_is_not_enough()
    {
        var r = SizeOptimizer.Optimize(Req(2000, 1000, 300_000, SizeLimitKind.Hard), Fake);
        Assert.True(r.Success);
        Assert.True(r.Bytes <= 300_000);
        Assert.True(r.Parameters.Width < 2000);
        Assert.InRange(r.Parameters.Width / (double)r.Parameters.Height, 1.95, 2.05);
    }

    [Fact]
    public void Hard_limit_that_cannot_be_met_returns_no_data()
    {
        var r = SizeOptimizer.Optimize(Req(2000, 1000, 1_000, SizeLimitKind.Hard), Fake);
        Assert.False(r.Success);
        Assert.Null(r.Data);
        Assert.Contains("nicht erreichbar", r.Message);
        Assert.True(r.Parameters.Width >= 320);
    }

    [Fact]
    public void Soft_limit_returns_smallest_candidate_with_message()
    {
        var r = SizeOptimizer.Optimize(Req(2000, 1000, 1_000, SizeLimitKind.Soft), Fake);
        Assert.False(r.Success);
        Assert.NotNull(r.Data);
        Assert.Contains("Weiches Limit", r.Message);
    }

    [Fact]
    public void Lossless_reduces_only_dimensions()
    {
        var r = SizeOptimizer.Optimize(Req(1000, 1000, 200_000, SizeLimitKind.Hard, lossy: false), p => new byte[p.Width * p.Height]);
        Assert.True(r.Success);
        Assert.True(r.Bytes <= 200_000);
        Assert.Equal(90, r.Parameters.Quality);
    }

    [Fact]
    public void Profiles_can_be_copied_and_edited()
    {
        using var t = new TempDir();
        var store = new ExportProfileStore(t.Path);
        var copy = store.Duplicate(BuiltInProfiles.Get("obsidian"));
        copy.MaxWidth = 1000;
        store.Save();
        var again = new ExportProfileStore(t.Path); again.Load();
        Assert.Equal(1000, again.Get(copy.Id).MaxWidth);
        Assert.False(again.Get(copy.Id).BuiltIn);
        Assert.Equal(1400, BuiltInProfiles.Get("obsidian").MaxWidth); // original unchanged
    }
}
