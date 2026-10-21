using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using CaptureDoc.App.Editor;
using CaptureDoc.App.Views;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Platform;
using CaptureDoc.Core.Portable;
using CaptureDoc.Rendering;
using SkiaSharp;

namespace CaptureDoc.App.Services;

public enum CaptureMode { Region, Window, ActiveWindow, FullScreen, Monitor, LastRegion }

/// <summary>Runs a capture: hide own windows → (countdown) → grab → select → hand the image to the configured action.</summary>
public sealed class CaptureController
{
    private readonly AppServices _app;
    private bool _busy;

    public CaptureController(AppServices app) => _app = app;

    /// <summary>
    /// Series: choose an area once, then take <paramref name="count"/> screenshots every <paramref name="interval"/> seconds.
    /// A visible counter (excluded from the screenshots) shows progress; Esc in the counter window stops.
    /// </summary>
    public async Task<List<RawImage>> CaptureSeriesAsync(int count, double interval)
    {
        var shots = new List<RawImage>();
        if (_busy || !_app.Capture.IsSupported) return shots;
        _busy = true;
        var hidden = App.Current.OpenWindows.Where(w => w.IsVisible).ToList();
        try
        {
            foreach (var w in hidden) w.Hide();
            await Task.Delay(220);
            var frozen = _app.Capture.CaptureVirtualScreen(false);
            var area = await RegionSelector.SelectAsync(frozen, _app.Capture.GetWindows(), SelectorMode.RegionOrWindow, App.Current.AnyWindow);
            if (area is not { } region) return shots;
            var counter = new Window
            {
                WindowDecorations = WindowDecorations.None, Topmost = true, ShowInTaskbar = false, SizeToContent = SizeToContent.WidthAndHeight, CanResize = false,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(230, 25, 25, 30)), WindowStartupLocation = WindowStartupLocation.Manual,
            };
            var label = new TextBlock { Foreground = Avalonia.Media.Brushes.White, Margin = new Avalonia.Thickness(14, 8), FontSize = 14 };
            counter.Content = label;
            var stop = false;
            counter.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) stop = true; };
            counter.PointerPressed += (_, _) => stop = true;
            counter.Opened += (_, _) =>
            {
                if (OperatingSystem.IsWindows()) CaptureDoc.Platform.Windows.WindowTools.ExcludeFromCapture(counter.TryGetPlatformHandle()?.Handle ?? 0);
                var wa = counter.Screens.Primary?.WorkingArea ?? new Avalonia.PixelRect(0, 0, 1600, 900);
                counter.Position = new Avalonia.PixelPoint(wa.Right - 260, wa.Bottom - 70);
            };
            counter.Show();
            for (var i = 0; i < count && !stop; i++)
            {
                label.Text = $"📷 Serie {i + 1}/{count} · Klick = stoppen";
                shots.Add(_app.Capture.Capture(region, _app.Settings.Platform.IncludeCursor));
                if (i < count - 1) await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.2, interval)));
            }
            counter.Close();
            Log.Info($"Series: {shots.Count} shots {region.Width}x{region.Height}");
            return shots;
        }
        catch (Exception ex)
        {
            Log.Error("Series failed", ex);
            await Dialogs.InfoAsync(null, "Serienaufnahme fehlgeschlagen", ex.Message);
            return shots;
        }
        finally
        {
            foreach (var w in hidden) w.Show();
            _busy = false;
        }
    }

    public async Task CaptureAsync(CaptureMode mode, int? delaySeconds = null)
    {
        if (_busy) return;
        if (!_app.Capture.IsSupported)
        {
            await Dialogs.InfoAsync(null, "Aufnahme nicht verfügbar", "Bildschirmaufnahme ist auf diesem Betriebssystem noch nicht implementiert.");
            return;
        }
        _busy = true;
        var hidden = new List<Window>();
        Window? opened = null;
        try
        {
            var includeCursor = _app.Settings.Platform.IncludeCursor;
            var delay = delaySeconds ?? _app.Settings.Platform.DelaySeconds;

            // Active window must be read before our own windows change focus.
            var foreground = mode == CaptureMode.ActiveWindow ? _app.Capture.GetForegroundWindow() : null;

            foreach (var w in App.Current.OpenWindows.Where(w => w.IsVisible && w is not EditorWindow))
            {
                hidden.Add(w);
                w.Hide();
            }
            if (delay > 0) await CountdownWindow.RunAsync(delay);
            await Task.Delay(hidden.Count > 0 ? 220 : 60); // let DWM remove our windows from the screen

            var sw = Stopwatch.StartNew();
            RawImage? raw = null;
            PixelRect? area = null;
            switch (mode)
            {
                case CaptureMode.FullScreen:
                    raw = _app.Capture.CaptureVirtualScreen(includeCursor);
                    break;
                case CaptureMode.Monitor:
                {
                    var cursor = CursorPosition();
                    var mon = _app.Capture.GetMonitors().FirstOrDefault(m => m.Bounds.Contains(cursor.X, cursor.Y)) ?? _app.Capture.GetMonitors().FirstOrDefault();
                    if (mon is not null) raw = _app.Capture.Capture(mon.Bounds, includeCursor);
                    break;
                }
                case CaptureMode.ActiveWindow:
                    if (foreground is not null) raw = _app.Capture.Capture(foreground.Bounds, includeCursor);
                    break;
                case CaptureMode.LastRegion:
                    if (_app.Settings.Platform.LastRegion is { } last) raw = _app.Capture.Capture(last, includeCursor);
                    else mode = CaptureMode.Region;
                    break;
            }

            if (mode is CaptureMode.Region or CaptureMode.Window)
            {
                var windows = _app.Capture.GetWindows();
                var frozen = _app.Capture.CaptureVirtualScreen(includeCursor);
                Log.Info($"Frozen desktop {frozen.Width}x{frozen.Height} in {sw.ElapsedMilliseconds} ms");
                area = await RegionSelector.SelectAsync(frozen, windows, mode == CaptureMode.Window ? SelectorMode.WindowOnly : SelectorMode.RegionOrWindow, App.Current.AnyWindow);
                if (area is null) return;
                raw = frozen.Crop(area.Value);
                _app.Settings.Platform.LastRegion = area;
                _app.SaveSettings();
            }
            if (raw is null) return;
            Log.Info($"Captured {raw.Width}x{raw.Height} ({mode}) in {sw.ElapsedMilliseconds} ms");
            opened = await HandleResultAsync(raw, mode);
        }
        catch (Exception ex)
        {
            Log.Error("Capture failed", ex);
            await Dialogs.InfoAsync(null, "Aufnahme fehlgeschlagen", ex.Message);
        }
        finally
        {
            foreach (var w in hidden) w.Show();
            opened?.Activate(); // the new editor goes on top of the quick bar
            _busy = false;
        }
    }

    private static (int X, int Y) CursorPosition()
    {
        var w = App.Current.AnyWindow;
        // Avalonia has no global cursor API; use the primary monitor centre as fallback.
        return OperatingSystem.IsWindows() && Win32Cursor.TryGet(out var x, out var y) ? (x, y) : (0, 0);
    }

    private async Task<Window?> HandleResultAsync(RawImage raw, CaptureMode mode)
    {
        var image = SkiaConvert.ToSkImage(raw);
        var name = FileNamePattern.Format(_app.Settings.Shared.FileNamePattern, DateTime.Now);
        var action = _app.Settings.Shared.AfterCapture;

        if (action is AfterCaptureAction.CopyToClipboard or AfterCaptureAction.CopyAndOpenEditor && _app.Clipboard is not null)
            _app.Clipboard.SetImage(raw, null);

        if (action == AfterCaptureAction.SaveToFile)
        {
            var profile = _app.Profiles.Get(_app.Settings.Shared.DefaultExportProfile);
            var dir = _app.Settings.Platform.LastSaveDirectory ?? _app.Env.MediaDir;
            var path = Path.Combine(dir, name + profile.FileExtension);
            var r = await Task.Run(() => ImageExporter.Export(image, profile, path));
            image.Dispose();
            App.Current.Notify(r.Success ? $"Gespeichert: {Path.GetFileName(path)}" : r.Message);
            return null;
        }
        if (action == AfterCaptureAction.CopyToClipboard)
        {
            image.Dispose();
            App.Current.Notify("Screenshot in der Zwischenablage");
            return null;
        }

        // Open the editor immediately; PNG encoding for the asset store runs in the background.
        var png = await Task.Run(() => SkiaConvert.EncodePng(raw));
        var session = ProjectSession.FromCapture(image, png, name);
        var editor = new EditorWindow(_app, session, session.Project.Pages[0]);
        editor.Show();
        editor.Activate();
        return editor;
    }
}

internal static class Win32Cursor
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT p);
    private struct POINT { public int X, Y; }
    public static bool TryGet(out int x, out int y)
    {
        var ok = GetCursorPos(out var p);
        x = p.X; y = p.Y;
        return ok;
    }
}
