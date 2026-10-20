using System.Diagnostics;
using System.Text.Json;

namespace CaptureDoc.Core.Storage;

/// <summary>
/// Advisory lock next to a project file (<c>name.capturedoc.lock</c>). Held open with FileShare.None;
/// on Windows the OS deletes it when the process ends (also after crashes). Stale locks from
/// dead processes on the same machine are taken over.
/// </summary>
public sealed class ProjectLock : IDisposable
{
    private FileStream? _stream;
    public string LockPath { get; }

    private ProjectLock(string lockPath, FileStream stream) { LockPath = lockPath; _stream = stream; }

    public sealed record LockInfo(string Machine, int ProcessId, DateTimeOffset Since, string App);

    public static string GetLockPath(string projectPath) => Path.GetFullPath(projectPath) + ".lock";

    public static bool TryAcquire(string projectPath, out ProjectLock? acquired, out LockInfo? heldBy)
    {
        acquired = null; heldBy = null;
        var lockPath = GetLockPath(projectPath);
        {
            try
            {
                var fs = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
                if (fs.Length > 0)
                {
                    // Existing file we could open exclusively: previous owner's handle is gone (Unix, or crash on a share).
                    var existing = TryRead(fs);
                    if (existing is not null && IsAlive(existing))
                    {
                        fs.Dispose();
                        heldBy = existing;
                        return false;
                    }
                }
                fs.SetLength(0);
                var info = new LockInfo(Environment.MachineName, Environment.ProcessId, DateTimeOffset.Now, "CaptureDoc");
                JsonSerializer.Serialize(fs, info);
                fs.Flush(true);
                acquired = new ProjectLock(lockPath, fs);
                return true;
            }
            catch (IOException)
            {
                heldBy = TryReadShared(lockPath) ?? new LockInfo("?", 0, DateTimeOffset.MinValue, "?");
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                // read-only media: no lock possible, caller decides (open read-only)
                return false;
            }
        }
    }

    private static LockInfo? TryRead(FileStream fs)
    {
        try { fs.Position = 0; return JsonSerializer.Deserialize<LockInfo>(fs); } catch (JsonException) { return null; }
    }

    private static LockInfo? TryReadShared(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<LockInfo>(fs);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static bool IsAlive(LockInfo info)
    {
        if (!string.Equals(info.Machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            return DateTimeOffset.Now - info.Since < TimeSpan.FromHours(12); // other machine: trust for a while
        if (info.ProcessId == Environment.ProcessId) return false;
        try { using var p = Process.GetProcessById(info.ProcessId); return !p.HasExited; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
