using CaptureDoc.Core.Export;
using CaptureDoc.Core.Obsidian;
using CaptureDoc.Core.Platform;
using CaptureDoc.Core.Portable;

namespace CaptureDoc.App.Services;

/// <summary>Composition root – one instance per process.</summary>
public sealed class AppServices : IDisposable
{
    public PortableEnvironment Env { get; }
    public SettingsStore Settings { get; }
    public ExportProfileStore Profiles { get; }
    public TemplateStore Templates { get; }
    public IScreenCaptureService Capture { get; }
    public IClipboardImageService? Clipboard { get; }
    public IGlobalHotkeyService? Hotkeys { get; }
    /// <summary>External ffmpeg for video/GIF (null = not found).</summary>
    public CaptureDoc.Media.FfmpegTools? Ffmpeg { get; private set; }
    public void RefreshFfmpeg() => Ffmpeg = CaptureDoc.Media.FfmpegTools.Locate(Env.AppRoot);
    public List<string> StartupProblems { get; } = new();
    public Dictionary<string, bool> HotkeyStatus { get; } = new();

    public AppServices(PortableEnvironment env)
    {
        Env = env;
        Log.Init(env.LogsDir);
        Settings = new SettingsStore(env);
        Settings.Load();
        StartupProblems.AddRange(Settings.Problems);
        Profiles = new ExportProfileStore(env.PresetsDir);
        Profiles.Load();
        Templates = new TemplateStore(env.TemplatesDir);
        try { Templates.WriteDefaultsIfMissing(); } catch (IOException ex) { Log.Warn("Templates: " + ex.Message); }

        if (OperatingSystem.IsWindows())
        {
            Capture = new CaptureDoc.Platform.Windows.GdiCaptureService();
            Clipboard = new CaptureDoc.Platform.Windows.WindowsClipboard();
            Hotkeys = new CaptureDoc.Platform.Windows.WindowsHotkeys();
        }
        else
        {
            // macOS (ScreenCaptureKit) and Linux (Portal/PipeWire) are planned – see DevVault Platform-Integration.
            Capture = new UnsupportedCaptureService();
            StartupProblems.Add("Bildschirmaufnahme ist auf diesem Betriebssystem noch nicht implementiert. Editor, Projekte und Export funktionieren.");
        }

        RefreshFfmpeg();
        Log.Info(Ffmpeg is null ? "ffmpeg not found" : $"ffmpeg: {Ffmpeg.Source}");
        var removed = env.CleanupTemp(TimeSpan.FromDays(1));
        if (removed > 0) Log.Info($"Removed {removed} stale temp entries");
    }

    public void SaveSettings()
    {
        try { Settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error("Settings save failed", ex); }
    }

    public string NewSessionTempDir()
    {
        var d = Path.Combine(Env.TempDir, $"session-{Environment.ProcessId}");
        Directory.CreateDirectory(d);
        return d;
    }

    public void Dispose()
    {
        Hotkeys?.Dispose();
        try
        {
            var d = Path.Combine(Env.TempDir, $"session-{Environment.ProcessId}");
            if (Directory.Exists(d)) Directory.Delete(d, true);
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
