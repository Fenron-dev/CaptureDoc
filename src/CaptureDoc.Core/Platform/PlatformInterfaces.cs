using CaptureDoc.Core.Model;

namespace CaptureDoc.Core.Platform;

/// <summary>Uncompressed 32-bit BGRA image (premultiplied not required; captures are opaque). Stride = Width * 4.</summary>
public sealed class RawImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    /// <summary>Where the image came from in virtual-screen pixels.</summary>
    public PixelRect SourceBounds { get; init; }

    public RawImage(int width, int height, byte[] pixels)
    {
        if (pixels.Length < width * height * 4) throw new ArgumentException("Pixel buffer too small.");
        Width = width; Height = height; Pixels = pixels;
    }

    public RawImage Crop(PixelRect virtualRect)
    {
        var r = virtualRect.Intersect(SourceBounds);
        if (r.IsEmpty) throw new ArgumentException("Bereich liegt außerhalb der Aufnahme.");
        var ox = r.X - SourceBounds.X; var oy = r.Y - SourceBounds.Y;
        var dst = new byte[r.Width * r.Height * 4];
        for (var y = 0; y < r.Height; y++)
            Buffer.BlockCopy(Pixels, ((oy + y) * Width + ox) * 4, dst, y * r.Width * 4, r.Width * 4);
        return new RawImage(r.Width, r.Height, dst) { SourceBounds = r };
    }
}

public sealed record MonitorInfo(string Name, PixelRect Bounds, PixelRect WorkArea, double Scaling, bool IsPrimary);

public sealed record WindowInfo(nint Handle, string Title, string ProcessName, PixelRect Bounds, bool IsMinimized);

public interface IScreenCaptureService
{
    bool IsSupported { get; }
    string BackendName { get; }
    /// <summary>Bounding box of all monitors in physical pixels (may start at negative coordinates).</summary>
    PixelRect VirtualScreen { get; }
    IReadOnlyList<MonitorInfo> GetMonitors();
    /// <summary>Visible top-level windows, top of z-order first.</summary>
    IReadOnlyList<WindowInfo> GetWindows();
    WindowInfo? GetForegroundWindow();
    RawImage Capture(PixelRect area, bool includeCursor);
    RawImage CaptureVirtualScreen(bool includeCursor) => Capture(VirtualScreen, includeCursor);
}

public interface IClipboardImageService
{
    /// <summary>Puts the image on the clipboard (bitmap for all apps + PNG for transparency-aware apps).</summary>
    bool SetImage(RawImage image, byte[]? png);
}

public interface IGlobalHotkeyService : IDisposable
{
    /// <summary>Registers e.g. "Ctrl+Shift+4". Returns false if the gesture is taken by another program.</summary>
    bool Register(string id, string gesture, Action callback);
    void UnregisterAll();
}

/// <summary>Used where no native implementation exists (yet) – keeps the app usable.</summary>
public sealed class UnsupportedCaptureService : IScreenCaptureService
{
    public bool IsSupported => false;
    public string BackendName => "none";
    public PixelRect VirtualScreen => default;
    public IReadOnlyList<MonitorInfo> GetMonitors() => Array.Empty<MonitorInfo>();
    public IReadOnlyList<WindowInfo> GetWindows() => Array.Empty<WindowInfo>();
    public WindowInfo? GetForegroundWindow() => null;
    public RawImage Capture(PixelRect area, bool includeCursor) => throw new PlatformNotSupportedException("Bildschirmaufnahme ist auf dieser Plattform noch nicht implementiert.");
}
