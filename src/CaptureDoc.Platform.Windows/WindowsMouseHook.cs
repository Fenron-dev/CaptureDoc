using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CaptureDoc.Platform.Windows;

/// <summary>
/// Low-level mouse hook used only while a recording runs (visible frame + timer) to mark clicks.
/// Positions are physical virtual-screen pixels. Nothing is stored except time and position of clicks.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsMouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const uint WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public nint extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public nint hwnd; public uint message; public nuint wParam; public nint lParam; public uint time; public POINT pt; }
    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint hMod, uint threadId);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool UnhookWindowsHookEx(nint hook);
    [LibraryImport("user32.dll")] private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")] private static partial int GetMessage(out MSG msg, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool PostThreadMessage(uint id, uint msg, nuint w, nint l);
    [LibraryImport("kernel32.dll")] private static partial uint GetCurrentThreadId();
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)] private static partial nint GetModuleHandle(string? name);

    private readonly HookProc _proc;          // keep the delegate alive
    private readonly Thread _thread;
    private uint _threadId;
    private nint _hook;

    /// <summary>x, y (physical pixels), right button?</summary>
    public event Action<int, int, bool>? Click;

    public WindowsMouseHook()
    {
        _proc = Callback;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            ready.Set();
            while (GetMessage(out _, 0, 0, 0) > 0) { }
            if (_hook != 0) UnhookWindowsHookEx(_hook);
        }) { IsBackground = true, Name = "CaptureDoc mouse hook" };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(3));
    }

    public bool IsActive => _hook != 0;

    private nint Callback(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && (wParam == WM_LBUTTONDOWN || wParam == WM_RBUTTONDOWN))
        {
            var s = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            try { Click?.Invoke(s.pt.X, s.pt.Y, wParam == WM_RBUTTONDOWN); } catch { /* never block input */ }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
    }
}
