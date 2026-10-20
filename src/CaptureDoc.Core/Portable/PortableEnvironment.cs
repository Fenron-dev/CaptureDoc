namespace CaptureDoc.Core.Portable;

public enum PlatformId { Windows, MacOS, Linux }

/// <summary>
/// Resolves the portable folder layout. All self-managed data lives below <see cref="DataRoot"/>,
/// next to the executable (or next to the .app bundle on macOS).
/// </summary>
public sealed class PortableEnvironment
{
    public const string DataDirOverrideVariable = "CAPTUREDOC_DATA_DIR";

    public string AppRoot { get; }
    public string DataRoot { get; }
    public PlatformId Platform { get; }
    public bool IsFallbackLocation { get; private set; }

    public string SharedDir => Path.Combine(DataRoot, "Shared");
    public string SettingsSharedDir => Path.Combine(DataRoot, "Settings", "Shared");
    public string SettingsPlatformDir => Path.Combine(DataRoot, "Settings", PlatformFolderName);
    public string ProjectsDir => Path.Combine(DataRoot, "Projects");
    public string MediaDir => Path.Combine(DataRoot, "Media");
    public string TemplatesDir => Path.Combine(DataRoot, "Templates");
    public string PresetsDir => Path.Combine(DataRoot, "Presets");
    public string CacheDir => Path.Combine(DataRoot, "Cache");
    public string RecoveryDir => Path.Combine(DataRoot, "Cache", "Recovery");
    public string TempDir => Path.Combine(DataRoot, "Temp");
    public string LogsDir => Path.Combine(DataRoot, "Logs");
    public string DevVaultDir => Path.Combine(DataRoot, "DevVault");

    public string PlatformFolderName => Platform switch { PlatformId.Windows => "Windows", PlatformId.MacOS => "MacOS", _ => "Linux" };

    public PortableEnvironment(string appRoot, string? dataRoot = null, PlatformId? platform = null)
    {
        AppRoot = Path.GetFullPath(appRoot);
        DataRoot = Path.GetFullPath(dataRoot ?? Path.Combine(AppRoot, "Data"));
        Platform = platform ?? CurrentPlatform();
    }

    public static PlatformId CurrentPlatform() =>
        OperatingSystem.IsWindows() ? PlatformId.Windows : OperatingSystem.IsMacOS() ? PlatformId.MacOS : PlatformId.Linux;

    /// <summary>Determines the app root from the running executable.</summary>
    public static PortableEnvironment Detect()
    {
        var overrideDir = Environment.GetEnvironmentVariable(DataDirOverrideVariable);
        var exeDir = AppContext.BaseDirectory;
        var root = FindAppRoot(exeDir);
        return new PortableEnvironment(root, string.IsNullOrWhiteSpace(overrideDir) ? null : overrideDir);
    }

    /// <summary>macOS: .../CaptureDoc/CaptureDoc-macOS.app/Contents/MacOS → .../CaptureDoc</summary>
    public static string FindAppRoot(string exeDir)
    {
        var dir = Path.GetFullPath(exeDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var marker = Path.DirectorySeparatorChar + "Contents" + Path.DirectorySeparatorChar + "MacOS";
        var idx = dir.IndexOf(".app" + marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var appBundle = dir[..(idx + 4)];
            return Path.GetDirectoryName(appBundle)!;
        }
        return dir;
    }

    public IEnumerable<string> AllDirectories() => new[]
    {
        SharedDir, SettingsSharedDir, Path.Combine(DataRoot, "Settings", "Windows"), Path.Combine(DataRoot, "Settings", "MacOS"),
        Path.Combine(DataRoot, "Settings", "Linux"), ProjectsDir, MediaDir, TemplatesDir, PresetsDir, CacheDir, RecoveryDir, TempDir, LogsDir,
    };

    public sealed record WritableCheck(bool IsWritable, string? Problem);

    /// <summary>Creates the folder layout and verifies that it can be written (USB stick may be read-only).</summary>
    public WritableCheck EnsureWritable()
    {
        try
        {
            foreach (var d in AllDirectories()) Directory.CreateDirectory(d);
            var probe = Path.Combine(TempDir, $".write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            var back = File.ReadAllText(probe);
            File.Delete(probe);
            return back == "ok" ? new(true, null) : new(false, "Schreibtest lieferte falsche Daten zurück.");
        }
        catch (UnauthorizedAccessException ex) { return new(false, "Keine Schreibrechte: " + ex.Message); }
        catch (IOException ex) { return new(false, "Ein-/Ausgabefehler: " + ex.Message); }
    }

    /// <summary>Fallback when the portable folder is read-only: per-user folder (documented exception).</summary>
    public static PortableEnvironment CreateUserFallback(string appRoot)
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var env = new PortableEnvironment(appRoot, Path.Combine(baseDir, "CaptureDoc", "Data")) { IsFallbackLocation = true };
        return env;
    }

    /// <summary>Stores a path relative to the app root when it lies inside it ("./Data/..."), else absolute.</summary>
    public string ToPortablePath(string absolutePath)
    {
        var full = Path.GetFullPath(absolutePath);
        var rel = Path.GetRelativePath(AppRoot, full);
        if (rel == "." ) return "./";
        if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel)) return full;
        return "./" + rel.Replace('\\', '/');
    }

    public string FromPortablePath(string stored)
    {
        if (stored.StartsWith("./", StringComparison.Ordinal) || stored.StartsWith(".\\", StringComparison.Ordinal))
            return Path.GetFullPath(Path.Combine(AppRoot, stored[2..].Replace('/', Path.DirectorySeparatorChar)));
        return stored;
    }

    /// <summary>Deletes stale temp files (best effort).</summary>
    public int CleanupTemp(TimeSpan olderThan)
    {
        var n = 0;
        if (!Directory.Exists(TempDir)) return 0;
        foreach (var f in Directory.EnumerateFileSystemEntries(TempDir))
        {
            try
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(f);
                if (age < olderThan) continue;
                if (Directory.Exists(f)) Directory.Delete(f, true); else File.Delete(f);
                n++;
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return n;
    }
}
