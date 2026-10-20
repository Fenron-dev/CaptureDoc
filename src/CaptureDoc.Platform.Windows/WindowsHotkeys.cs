using System.Collections.Concurrent;
using System.Runtime.Versioning;
using CaptureDoc.Core.Platform;
using static CaptureDoc.Platform.Windows.Native;

namespace CaptureDoc.Platform.Windows;

/// <summary>
/// Global hotkeys via RegisterHotKey on a dedicated thread with its own message loop
/// (no window needed). Callbacks are raised on that thread – marshal to the UI thread.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHotkeys : IGlobalHotkeyService
{
    private readonly Thread _thread;
    private uint _threadId;
    private readonly ManualResetEventSlim _ready = new();
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly Dictionary<int, Action> _callbacks = new();
    private int _nextId = 1;

    public WindowsHotkeys()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "CaptureDoc Hotkeys" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void Loop()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, 0, 0, 0, PM_NOREMOVE); // create message queue
        _ready.Set();
        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            if (msg.message == WM_APP)
            {
                while (_work.TryDequeue(out var w)) w();
            }
            else if (msg.message == WM_HOTKEY)
            {
                if (_callbacks.TryGetValue((int)msg.wParam, out var cb))
                {
                    try { cb(); } catch { /* never kill the hotkey thread */ }
                }
            }
        }
        foreach (var id in _callbacks.Keys) UnregisterHotKey(0, id);
    }

    private T Invoke<T>(Func<T> f)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Enqueue(() => { try { tcs.SetResult(f()); } catch (Exception e) { tcs.SetException(e); } });
        PostThreadMessage(_threadId, WM_APP, 0, 0);
        return tcs.Task.Wait(TimeSpan.FromSeconds(5)) ? tcs.Task.Result : default!;
    }

    public bool Register(string id, string gesture, Action callback)
    {
        if (!HotkeyGesture.TryParse(gesture, out var g) || !TryMapKey(g.Key, out var vk)) return false;
        uint mods = MOD_NOREPEAT;
        if (g.Modifiers.HasFlag(HotkeyModifiers.Alt)) mods |= MOD_ALT;
        if (g.Modifiers.HasFlag(HotkeyModifiers.Control)) mods |= MOD_CONTROL;
        if (g.Modifiers.HasFlag(HotkeyModifiers.Shift)) mods |= MOD_SHIFT;
        if (g.Modifiers.HasFlag(HotkeyModifiers.Win)) mods |= MOD_WIN;
        return Invoke(() =>
        {
            var hid = _nextId++;
            if (!RegisterHotKey(0, hid, mods, vk)) return false;
            _callbacks[hid] = callback;
            return true;
        });
    }

    public void UnregisterAll() => Invoke(() =>
    {
        foreach (var hid in _callbacks.Keys) UnregisterHotKey(0, hid);
        _callbacks.Clear();
        return true;
    });

    public static bool TryMapKey(string key, out uint vk)
    {
        vk = 0;
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') { vk = c; return true; }
        }
        if (key.Length is 2 or 3 && (key[0] == 'F' || key[0] == 'f') && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24) { vk = (uint)(0x70 + f - 1); return true; }
        vk = key.ToLowerInvariant() switch
        {
            "printscreen" => 0x2C, "insert" => 0x2D, "delete" => 0x2E, "home" => 0x24, "end" => 0x23,
            "pageup" => 0x21, "pagedown" => 0x22, "pause" => 0x13, "scrolllock" => 0x91, "space" => 0x20,
            _ => 0u,
        };
        return vk != 0;
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
    }
}
