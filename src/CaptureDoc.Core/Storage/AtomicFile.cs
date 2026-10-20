namespace CaptureDoc.Core.Storage;

/// <summary>
/// Crash-safe file replacement: write to a temp file in the same directory, flush to disk,
/// optionally validate, then rename over the target (atomic on the same volume).
/// </summary>
public static class AtomicFile
{
    public static void Write(string path, Action<Stream> writer, Action<string>? validateTemp = null)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.None))
            {
                writer(fs);
                fs.Flush(flushToDisk: true);
            }
            validateTemp?.Invoke(tmp);
            File.Move(tmp, full, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public static void WriteAllBytes(string path, byte[] data) => Write(path, s => s.Write(data));
    public static void WriteAllText(string path, string text) => WriteAllBytes(path, new System.Text.UTF8Encoding(false).GetBytes(text));

    /// <summary>Removes orphaned temp files left by a crash (older than <paramref name="minAge"/>).</summary>
    public static int CleanupOrphans(string directory, TimeSpan minAge)
    {
        if (!Directory.Exists(directory)) return 0;
        var n = 0;
        foreach (var f in Directory.EnumerateFiles(directory, ".*.tmp"))
        {
            try
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) >= minAge) { File.Delete(f); n++; }
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return n;
    }
}
