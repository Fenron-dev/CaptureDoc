using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CaptureDoc.Platform.Windows;

/// <summary>Helpers for recording overlays (frame + control bar).</summary>
[SupportedOSPlatform("windows")]
public static partial class WindowTools
{
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11; // Windows 10 2004+
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static partial nint GetWindowLongPtr(nint hwnd, int index);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    /// <summary>The window stays visible to the user but does not appear in screen captures/recordings.</summary>
    public static bool ExcludeFromCapture(nint hwnd) => hwnd != 0 && SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);

    /// <summary>Switches click-through on/off at runtime (live drawing layer: pointer mode vs. drawing mode).</summary>
    public static void SetClickThrough(nint hwnd, bool on)
    {
        if (hwnd == 0) return;
        var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE) | WS_EX_LAYERED | WS_EX_TOOLWINDOW;
        ex = on ? ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE : ex & ~WS_EX_TRANSPARENT & ~WS_EX_NOACTIVATE;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)ex);
    }

    /// <summary>Mouse clicks pass through the window (for the recording frame).</summary>
    public static void MakeClickThrough(nint hwnd)
    {
        if (hwnd == 0) return;
        var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)(ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
    }
}
