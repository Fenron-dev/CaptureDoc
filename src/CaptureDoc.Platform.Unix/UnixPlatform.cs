using System.Diagnostics;
using System.Runtime.InteropServices;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Platform;
using SkiaSharp;

namespace CaptureDoc.Platform.Unix;

/// <summary>Runs a command-line tool (no shell) and reports success.</summary>
internal static class Tool
{
    public static bool Exists(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
            if (File.Exists(Path.Combine(dir, name))) return true;
        return File.Exists(name);
    }

    public static (bool ok, string err) Run(string exe, IEnumerable<string> args, byte[]? stdin = null, int timeoutMs = 15000)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, RedirectStandardInput = stdin is not null, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return (false, "start failed");
            if (stdin is not null) { p.StandardInput.BaseStream.Write(stdin); p.StandardInput.Close(); }
            var err = p.StandardError.ReadToEndAsync();
            _ = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { p.Kill(true); return (false, "timeout"); }
            return (p.ExitCode == 0, err.Result);
        }
        catch (System.ComponentModel.Win32Exception ex) { return (false, ex.Message); }
    }

    /// <summary>PNG/JPEG file → opaque BGRA raw image.</summary>
    public static RawImage Decode(string file, PixelRect source)
    {
        using var bmp = SKBitmap.Decode(file) ?? throw new InvalidDataException("Aufnahme konnte nicht gelesen werden: " + file);
        var info = new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var conv = new SKBitmap(info);
        using (var c = new SKCanvas(conv)) { c.Clear(SKColors.Black); c.DrawBitmap(bmp, 0, 0); }
        var px = conv.GetPixelSpan().ToArray();
        for (var i = 3; i < px.Length; i += 4) px[i] = 255;
        return new RawImage(bmp.Width, bmp.Height, px) { SourceBounds = source with { Width = bmp.Width, Height = bmp.Height } };
    }

    public static string TempPng() => Path.Combine(Path.GetTempPath(), $"capturedoc-{Guid.NewGuid():N}.png");
}

/// <summary>
/// macOS and Linux capture via the system's own tools (no native bridge yet).
/// Monitors come from the UI toolkit (Avalonia) because the tools cannot list them reliably.
/// STATUS: implemented without a test machine – see DevVault Platform-Integration.
/// </summary>
public sealed class UnixCaptureService : IScreenCaptureService
{
    private readonly Func<IReadOnlyList<MonitorInfo>> _monitors;
    private readonly string? _ffmpeg;

    public UnixCaptureService(Func<IReadOnlyList<MonitorInfo>> monitors, string? ffmpeg)
    {
        _monitors = monitors; _ffmpeg = ffmpeg;
        BackendName = OperatingSystem.IsMacOS() ? "screencapture" : IsWayland ? WaylandTool ?? "wayland (kein Werkzeug)" : "x11";
    }

    public static bool IsWayland => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    private static string? WaylandTool => new[] { "grim", "gnome-screenshot", "spectacle" }.FirstOrDefault(Tool.Exists);

    public bool IsSupported => OperatingSystem.IsMacOS() || (IsWayland ? WaylandTool is not null : Tool.Exists("import") || _ffmpeg is not null);
    public string BackendName { get; }

    public PixelRect VirtualScreen
    {
        get
        {
            var m = _monitors();
            if (m.Count == 0) return new PixelRect(0, 0, 1920, 1080);
            return m.Select(x => x.Bounds).Aggregate((a, b) => a.Union(b));
        }
    }

    public IReadOnlyList<MonitorInfo> GetMonitors() => _monitors();
    public IReadOnlyList<WindowInfo> GetWindows() => Array.Empty<WindowInfo>();   // no portable window list → click captures the monitor
    public WindowInfo? GetForegroundWindow() => null;

    public RawImage Capture(PixelRect area, bool includeCursor)
    {
        var full = CaptureAll(includeCursor);
        return area == full.SourceBounds ? full : full.Crop(area);
    }

    private RawImage CaptureAll(bool cursor)
    {
        var vs = VirtualScreen;
        var file = Tool.TempPng();
        try
        {
            (bool ok, string err) r;
            if (OperatingSystem.IsMacOS())
            {
                // -x: no sound. All displays are combined only per display (-D), so capture the main display
                // when there is one monitor, else stitch per display.
                var monitors = _monitors();
                if (monitors.Count > 1) return StitchMac(monitors, cursor);
                var args = new List<string> { "-x" }; if (cursor) args.Add("-C"); args.Add(file);
                r = Tool.Run("/usr/sbin/screencapture", args);
                if (!r.ok || !File.Exists(file))
                    throw new UnauthorizedAccessException("macOS hat die Aufnahme verweigert. Bitte CaptureDoc unter Systemeinstellungen → Datenschutz & Sicherheit → Bildschirmaufnahme erlauben.");
            }
            else if (IsWayland)
            {
                r = WaylandTool switch
                {
                    "grim" => Tool.Run("grim", cursor ? new[] { "-c", file } : new[] { file }),
                    "gnome-screenshot" => Tool.Run("gnome-screenshot", cursor ? new[] { "-p", "-f", file } : new[] { "-f", file }),
                    "spectacle" => Tool.Run("spectacle", new[] { "-b", "-n", "-f", "-o", file }),
                    _ => (false, "Kein Screenshot-Werkzeug für Wayland gefunden (grim, gnome-screenshot oder spectacle installieren)."),
                };
                if (!r.ok) throw new InvalidOperationException(r.err);
            }
            else
            {
                var display = Environment.GetEnvironmentVariable("DISPLAY") ?? ":0";
                r = _ffmpeg is not null
                    ? Tool.Run(_ffmpeg, new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "x11grab", "-draw_mouse", cursor ? "1" : "0", "-video_size", $"{vs.Width}x{vs.Height}", "-i", $"{display}+{vs.X},{vs.Y}", "-frames:v", "1", file })
                    : Tool.Run("import", new[] { "-window", "root", file });
                if (!r.ok) throw new InvalidOperationException("X11-Aufnahme fehlgeschlagen: " + r.err);
            }
            return Tool.Decode(file, vs);
        }
        finally { try { File.Delete(file); } catch (IOException) { } }
    }

    private RawImage StitchMac(IReadOnlyList<MonitorInfo> monitors, bool cursor)
    {
        var vs = VirtualScreen;
        var canvas = new byte[vs.Width * vs.Height * 4];
        // screencapture numbers displays from 1 (main first); Avalonia lists the primary first as well (assumption, untested)
        var ordered = monitors.OrderByDescending(m => m.IsPrimary).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var file = Tool.TempPng();
            try
            {
                var args = new List<string> { "-x", "-D", (i + 1).ToString() }; if (cursor) args.Add("-C"); args.Add(file);
                if (!Tool.Run("/usr/sbin/screencapture", args).ok || !File.Exists(file)) continue;
                var img = Tool.Decode(file, ordered[i].Bounds);
                var b = ordered[i].Bounds;
                var w = Math.Min(img.Width, b.Width); var h = Math.Min(img.Height, b.Height);
                for (var y = 0; y < h; y++)
                    Buffer.BlockCopy(img.Pixels, y * img.Width * 4, canvas, ((b.Y - vs.Y + y) * vs.Width + (b.X - vs.X)) * 4, w * 4);
            }
            finally { try { File.Delete(file); } catch (IOException) { } }
        }
        return new RawImage(vs.Width, vs.Height, canvas) { SourceBounds = vs };
    }
}

/// <summary>Image clipboard via osascript (macOS) or wl-copy / xclip (Linux).</summary>
public sealed class UnixClipboard : IClipboardImageService
{
    public bool SetImage(RawImage image, byte[]? png)
    {
        png ??= Encode(image);
        if (OperatingSystem.IsMacOS())
        {
            var f = Tool.TempPng();
            File.WriteAllBytes(f, png);
            try { return Tool.Run("/usr/bin/osascript", new[] { "-e", $"set the clipboard to (read (POSIX file \"{f}\") as «class PNGf»)" }).ok; }
            finally { try { File.Delete(f); } catch (IOException) { } }
        }
        if (UnixCaptureService.IsWayland && Tool.Exists("wl-copy")) return Tool.Run("wl-copy", new[] { "--type", "image/png" }, png).ok;
        if (Tool.Exists("xclip")) return Tool.Run("xclip", new[] { "-selection", "clipboard", "-t", "image/png", "-i" }, png).ok;
        return false;
    }

    private static byte[] Encode(RawImage raw)
    {
        var info = new SKImageInfo(raw.Width, raw.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var h = GCHandle.Alloc(raw.Pixels, GCHandleType.Pinned);
        try
        {
            using var pm = new SKPixmap(info, h.AddrOfPinnedObject(), raw.Width * 4);
            using var data = pm.Encode(SKPngEncoderOptions.Default) ?? throw new InvalidOperationException("PNG");
            return data.ToArray();
        }
        finally { h.Free(); }
    }
}
