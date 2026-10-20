using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Platform;
using static CaptureDoc.Platform.Windows.Native;

namespace CaptureDoc.Platform.Windows;

/// <summary>
/// GDI BitBlt capture of the virtual desktop in physical pixels (process must be per-monitor DPI aware).
/// See DevVault ADR-005.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GdiCaptureService : IScreenCaptureService
{
    private readonly HashSet<uint> _excludedProcesses = new() { (uint)Environment.ProcessId };

    public bool IsSupported => true;
    public string BackendName => "GDI";
    /// <summary>Include CaptureDoc's own windows in the window list (default: no).</summary>
    public bool IncludeOwnWindows { get; set; }

    public PixelRect VirtualScreen => new(
        GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(0, 0, (nint hMon, nint _, ref RECT _, nint _) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = "" };
            if (GetMonitorInfo(hMon, ref mi))
            {
                var scale = 1.0;
                if (GetDpiForMonitor(hMon, 0, out var dx, out _) == 0) scale = dx / 96.0;
                list.Add(new MonitorInfo(mi.szDevice, ToRect(mi.rcMonitor), ToRect(mi.rcWork), scale, (mi.dwFlags & MONITORINFOF_PRIMARY) != 0));
            }
            return true;
        }, 0);
        return list;
    }

    public IReadOnlyList<WindowInfo> GetWindows()
    {
        var result = new List<WindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            var info = Describe(hwnd);
            if (info is not null) result.Add(info);
            return true;
        }, 0);
        return result;
    }

    public WindowInfo? GetForegroundWindow()
    {
        var h = Native.GetForegroundWindow();
        return h == 0 ? null : Describe(h, requireTitle: false);
    }

    private WindowInfo? Describe(nint hwnd, bool requireTitle = true)
    {
        if (!IsWindowVisible(hwnd)) return null;
        if (DwmGetWindowAttributeInt(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return null;
        var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        if ((ex & WS_EX_TOOLWINDOW) != 0 && requireTitle) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (!IncludeOwnWindows && _excludedProcesses.Contains(pid)) return null;

        var len = GetWindowTextLength(hwnd);
        var title = "";
        if (len > 0)
        {
            var buf = new char[len + 1];
            var n = GetWindowText(hwnd, buf, buf.Length);
            title = new string(buf, 0, n);
        }
        if (requireTitle && title.Length == 0) return null;

        RECT r;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(hwnd, out r)) return null;
        var bounds = ToRect(r);
        if (bounds.Width < 2 || bounds.Height < 2) return null;
        return new WindowInfo(hwnd, title, "", bounds, IsIconic(hwnd));
    }

    public RawImage Capture(PixelRect area, bool includeCursor)
    {
        var vs = VirtualScreen;
        var a = area.Intersect(vs);
        if (a.IsEmpty) throw new ArgumentException("Aufnahmebereich liegt außerhalb aller Monitore.");

        var screenDc = GetDC(0);
        var memDc = CreateCompatibleDC(screenDc);
        var bmi = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = a.Width, biHeight = -a.Height, // top-down
            biPlanes = 1, biBitCount = 32, biCompression = 0,
        };
        var hbmp = CreateDIBSection(screenDc, ref bmi, 0, out var bits, 0, 0);
        if (hbmp == 0)
        {
            DeleteDC(memDc); ReleaseDC(0, screenDc);
            throw new InvalidOperationException($"Speicher für {a.Width}×{a.Height} konnte nicht reserviert werden.");
        }
        var old = SelectObject(memDc, hbmp);
        try
        {
            if (!BitBlt(memDc, 0, 0, a.Width, a.Height, screenDc, a.X, a.Y, SRCCOPY | CAPTUREBLT))
                throw new InvalidOperationException("BitBlt fehlgeschlagen (gesperrter Bildschirm oder sicherer Desktop?).");
            if (includeCursor) DrawCursor(memDc, a);

            var size = a.Width * a.Height * 4;
            var pixels = new byte[size];
            Marshal.Copy(bits, pixels, 0, size);
            // GDI leaves alpha undefined → force opaque
            for (var i = 3; i < size; i += 4) pixels[i] = 255;
            return new RawImage(a.Width, a.Height, pixels) { SourceBounds = a };
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hbmp);
            DeleteDC(memDc);
            ReleaseDC(0, screenDc);
        }
    }

    private static void DrawCursor(nint dc, PixelRect area)
    {
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci) || (ci.flags & CURSOR_SHOWING) == 0 || ci.hCursor == 0) return;
        var hx = 0; var hy = 0;
        if (GetIconInfo(ci.hCursor, out var ii))
        {
            hx = ii.xHotspot; hy = ii.yHotspot;
            if (ii.hbmMask != 0) DeleteObject(ii.hbmMask);
            if (ii.hbmColor != 0) DeleteObject(ii.hbmColor);
        }
        DrawIconEx(dc, ci.ptScreenPos.X - hx - area.X, ci.ptScreenPos.Y - hy - area.Y, ci.hCursor, 0, 0, 0, 0, DI_NORMAL);
    }

    private static PixelRect ToRect(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
}
