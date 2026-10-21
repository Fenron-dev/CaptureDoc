namespace CaptureDoc.Core.Portable;

/// <summary>
/// Cross-platform single instance per portable folder: the first instance holds an exclusive lock file;
/// later starts drop a command file into Data/Cache/ipc and exit. Works on Windows, macOS and Linux
/// (named wait handles do not exist on Unix).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly FileStream? _lock;
    private FileSystemWatcher? _watcher;
    public string IpcDir { get; }
    public bool IsFirst => _lock is not null;
    public event EventHandler<string[]>? CommandReceived;

    public SingleInstance(string cacheDir)
    {
        IpcDir = Path.Combine(cacheDir, "ipc");
        try
        {
            Directory.CreateDirectory(IpcDir);
            _lock = new FileStream(Path.Combine(cacheDir, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException) { _lock = null; }
        catch (UnauthorizedAccessException) { _lock = null; }
    }

    /// <summary>Second instance: hand the arguments to the running one.</summary>
    public bool Send(string[] args)
    {
        try
        {
            var tmp = Path.Combine(IpcDir, $"{Guid.NewGuid():N}.tmp");
            File.WriteAllLines(tmp, args.Length == 0 ? new[] { "--show" } : args);
            File.Move(tmp, Path.ChangeExtension(tmp, ".cmd"));
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>First instance: start listening (also processes commands that arrived before).</summary>
    public void Listen()
    {
        if (!IsFirst) return;
        foreach (var f in Directory.EnumerateFiles(IpcDir, "*.cmd")) File.Delete(f); // stale from a crash
        _watcher = new FileSystemWatcher(IpcDir, "*.cmd") { EnableRaisingEvents = true };
        _watcher.Created += (_, e) => Handle(e.FullPath);
        _watcher.Renamed += (_, e) => Handle(e.FullPath);
    }

    private void Handle(string path)
    {
        for (var i = 0; i < 10; i++)
        {
            try
            {
                var lines = File.ReadAllLines(path);
                File.Delete(path);
                CommandReceived?.Invoke(this, lines);
                return;
            }
            catch (IOException) { Thread.Sleep(30); }
            catch (UnauthorizedAccessException) { Thread.Sleep(30); }
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _lock?.Dispose();
    }
}

/// <summary>Command line: --capture region|window|active|screen|monitor|last|video, --studio, --show, &lt;file&gt;.</summary>
public sealed record StartupCommand(string? Capture, bool Studio, bool Show, IReadOnlyList<string> Files)
{
    public static readonly string[] CaptureModes = { "region", "window", "active", "screen", "monitor", "last", "video" };

    public static StartupCommand Parse(IReadOnlyList<string> args)
    {
        string? capture = null; var studio = false; var show = false; var files = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--capture" or "-c" when i + 1 < args.Count:
                    var m = args[++i].ToLowerInvariant();
                    if (CaptureModes.Contains(m)) capture = m;
                    break;
                case "--studio": studio = true; break;
                case "--show": show = true; break;
                default:
                    if (!a.StartsWith('-')) files.Add(a);
                    break;
            }
        }
        return new StartupCommand(capture, studio, show, files);
    }
}
