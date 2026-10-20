namespace CaptureDoc.App.Services;

/// <summary>Minimal file log in Data/Logs (no personal data, no telemetry). Keeps 14 days.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _dir;

    public static void Init(string logsDir)
    {
        _dir = logsDir;
        try
        {
            Directory.CreateDirectory(logsDir);
            foreach (var f in Directory.EnumerateFiles(logsDir, "capturedoc-*.log"))
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromDays(14)) File.Delete(f);
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", ex is null ? msg : $"{msg}: {ex}");

    private static void Write(string level, string msg)
    {
        System.Diagnostics.Trace.WriteLine($"[{level}] {msg}");
        if (_dir is null) return;
        try
        {
            lock (Gate)
                File.AppendAllText(Path.Combine(_dir, $"capturedoc-{DateTime.Now:yyyyMMdd}.log"), $"{DateTime.Now:HH:mm:ss.fff} {level} {msg}{Environment.NewLine}");
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
