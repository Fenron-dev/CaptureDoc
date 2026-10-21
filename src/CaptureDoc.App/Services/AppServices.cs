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
    public IScreenCaptureService Capture { get; } = new UnsupportedCaptureService();
    public IClipboardImageService? Clipboard { get; }
    /// <summary>Monitor list from the UI toolkit (needed on macOS/Linux where the capture tools cannot list displays).</summary>
    public static Func<IReadOnlyList<MonitorInfo>> MonitorProvider { get; set; } = () => Array.Empty<MonitorInfo>();
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
        RefreshFfmpeg();
        if (!OperatingSystem.IsWindows())
        {
            // macOS/Linux: system tools (screencapture / grim / gnome-screenshot / spectacle / x11grab). Untested – see DevVault.
            Capture = new CaptureDoc.Platform.Unix.UnixCaptureService(() => MonitorProvider(), Ffmpeg?.Ffmpeg);
            Clipboard = new CaptureDoc.Platform.Unix.UnixClipboard();
            if (!Capture.IsSupported)
                StartupProblems.Add(OperatingSystem.IsLinux()
                    ? "Kein Screenshot-Werkzeug gefunden. Wayland: grim, gnome-screenshot oder spectacle installieren. X11: ImageMagick (import) oder FFmpeg."
                    : "Bildschirmaufnahme ist auf diesem System nicht verfügbar.");
            StartupProblems.Add("Globale Tastenkürzel gibt es unter macOS/Linux noch nicht. Tipp: im System ein Kürzel auf „CaptureDoc --capture region“ legen (auch: window, screen, video).");
        }
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
