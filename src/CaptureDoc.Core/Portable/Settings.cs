using System.Text.Json;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Obsidian;
using CaptureDoc.Core.Serialization;
using CaptureDoc.Core.Storage;

namespace CaptureDoc.Core.Portable;

public enum AfterCaptureAction { OpenEditor, CopyToClipboard, SaveToFile, CopyAndOpenEditor }
public enum ThemePreference { System, Light, Dark }

/// <summary>Settings shared by all OS (Data/Settings/Shared/settings.json).</summary>
public sealed class SharedSettings
{
    public int SettingsVersion { get; set; } = 1;
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public AfterCaptureAction AfterCapture { get; set; } = AfterCaptureAction.OpenEditor;
    public string DefaultExportProfile { get; set; } = "documentation-png";
    public string FileNamePattern { get; set; } = "Screenshot_{yyyy-MM-dd_HH-mm-ss}";
    public ToolStyle Style { get; set; } = new();
    /// <summary>User-saved annotation styles (reusable design styles).</summary>
    public List<ToolStyle> StylePresets { get; set; } = new();
    public int SeriesCount { get; set; } = 10;
    public double SeriesIntervalSeconds { get; set; } = 2;
    public bool WarnOnRedactedProjectSave { get; set; } = true;
    public int AutoSaveSeconds { get; set; } = 60;
}

/// <summary>Default look of new annotations (reusable design style).</summary>
public sealed class ToolStyle
{
    public ColorRgba Color { get; set; } = ColorRgba.Red;
    public double StrokeWidth { get; set; } = 4;
    public double FontSize { get; set; } = 28;
    public string FontFamily { get; set; } = "Segoe UI";
    public double StepRadius { get; set; } = 20;
    public bool Shadow { get; set; } = true;
    public string Name { get; set; } = "";

    public static IReadOnlyList<ToolStyle> BuiltIn { get; } = new List<ToolStyle>
    {
        new() { Name = "Standard (Rot)" },
        new() { Name = "Dezent (Blau)", Color = ColorRgba.Blue, StrokeWidth = 3, FontSize = 22, Shadow = false },
        new() { Name = "Kräftig (Orange)", Color = new ColorRgba(255, 120, 0), StrokeWidth = 6, FontSize = 32, StepRadius = 24 },
        new() { Name = "Präsentation (Grün, groß)", Color = new ColorRgba(40, 170, 70), StrokeWidth = 7, FontSize = 40, StepRadius = 30 },
        new() { Name = "Dunkel / Kontrast", Color = ColorRgba.Black, StrokeWidth = 4, FontSize = 26, Shadow = false },
    };
}

public sealed class HotkeySetting
{
    public string Action { get; set; } = "";
    /// <summary>e.g. "Ctrl+Shift+4", "PrintScreen"</summary>
    public string Gesture { get; set; } = "";
}

/// <summary>Settings for one OS (Data/Settings/&lt;OS&gt;/settings.json): hotkeys, absolute paths, capture options.</summary>
public sealed class PlatformSettings
{
    public int SettingsVersion { get; set; } = 1;
    public List<HotkeySetting> Hotkeys { get; set; } = new()
    {
        new() { Action = "region", Gesture = "PrintScreen" },
        new() { Action = "window", Gesture = "Alt+PrintScreen" },
        new() { Action = "screen", Gesture = "Ctrl+PrintScreen" },
        new() { Action = "last-region", Gesture = "Shift+PrintScreen" },
        new() { Action = "video", Gesture = "Ctrl+Shift+PrintScreen" },
    };
    public int RecordingFps { get; set; } = 30;
    public int RecordingCountdown { get; set; } = 3;
    public bool RecordCursor { get; set; } = true;
    /// <summary>Mark mouse clicks as editable ring annotations in the video editor.</summary>
    public bool HighlightClicks { get; set; } = true;
    public bool RecordMicrophone { get; set; }
    public string? MicrophoneDevice { get; set; }
    public bool IncludeCursor { get; set; }
    public int DelaySeconds { get; set; }
    public PixelRect? LastRegion { get; set; }
    public string? LastSaveDirectory { get; set; }
    public List<VaultProfile> Vaults { get; set; } = new();
    public Guid? ActiveVault { get; set; }
    public bool StartMinimizedToTray { get; set; }
}

/// <summary>Loads/saves settings atomically; broken files are backed up and replaced by defaults.</summary>
public sealed class SettingsStore
{
    private readonly PortableEnvironment _env;
    public SharedSettings Shared { get; private set; } = new();
    public PlatformSettings Platform { get; private set; } = new();
    public List<string> Problems { get; } = new();

    public SettingsStore(PortableEnvironment env) => _env = env;

    public string SharedPath => Path.Combine(_env.SettingsSharedDir, "settings.json");
    public string PlatformPath => Path.Combine(_env.SettingsPlatformDir, "settings.json");

    public void Load()
    {
        Shared = LoadFile<SharedSettings>(SharedPath) ?? new SharedSettings();
        Platform = LoadFile<PlatformSettings>(PlatformPath) ?? new PlatformSettings();
        // settings written by older versions: add hotkey actions introduced later
        foreach (var def in new PlatformSettings().Hotkeys)
            if (Platform.Hotkeys.All(h => h.Action != def.Action)) Platform.Hotkeys.Add(def);
    }

    public void Save()
    {
        AtomicFile.WriteAllText(SharedPath, JsonSerializer.Serialize(Shared, ProjectJson.Options));
        AtomicFile.WriteAllText(PlatformPath, JsonSerializer.Serialize(Platform, ProjectJson.Options));
    }

    private T? LoadFile<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), ProjectJson.Options); }
        catch (JsonException ex)
        {
            var backup = path + $".broken-{DateTime.Now:yyyyMMddHHmmss}";
            try { File.Copy(path, backup, true); } catch (IOException) { }
            Problems.Add($"Einstellungen '{Path.GetFileName(path)}' waren beschädigt ({ex.Message}). Gesichert als {Path.GetFileName(backup)}, Standardwerte geladen.");
            return null;
        }
    }
}
