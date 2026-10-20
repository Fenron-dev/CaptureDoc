using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Platform;
using AvPixelRect = Avalonia.PixelRect;
using CdPixelRect = CaptureDoc.Core.Model.PixelRect;

namespace CaptureDoc.App.Views;

public enum SelectorMode { RegionOrWindow, WindowOnly }

/// <summary>
/// Shows the frozen desktop on every monitor (one window per monitor → correct with mixed DPI).
/// Drag = region, click = window under cursor, Esc/right click = cancel.
/// Result is in virtual-screen physical pixels.
/// </summary>
public sealed class RegionSelector
{
    private readonly List<OverlayWindow> _windows = new();
    private readonly TaskCompletionSource<CdPixelRect?> _tcs = new();

    public static Task<CdPixelRect?> SelectAsync(RawImage frozen, IReadOnlyList<WindowInfo> windows, SelectorMode mode, Window? anyWindow)
    {
        var sel = new RegionSelector();
        var top = anyWindow ?? new Window();
        var screens = top.Screens.All;
        var bitmap = Util.Ui.ToBitmap(frozen);
        foreach (var s in screens)
        {
            var w = new OverlayWindow(sel, s, frozen.SourceBounds, bitmap, windows, mode);
            sel._windows.Add(w);
            w.Show();
        }
        sel._windows.FirstOrDefault(w => w.Screen.IsPrimary)?.Activate();
        return sel._tcs.Task.ContinueWith(t => { bitmap.Dispose(); return t.Result; }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    internal void Finish(CdPixelRect? result)
    {
        if (_tcs.Task.IsCompleted) return;
        foreach (var w in _windows) w.Close();
        _tcs.TrySetResult(result is { IsEmpty: false } ? result : null);
    }

    internal void InvalidateAll() { foreach (var w in _windows) w.Surface.InvalidateVisual(); }

    // shared hover/selection state across monitors
    internal CdPixelRect? Hover;
    internal CdPixelRect? Drag;

    private sealed class OverlayWindow : Window
    {
        public Screen Screen { get; }
        public OverlaySurface Surface { get; }

        public OverlayWindow(RegionSelector owner, Screen screen, CdPixelRect frozenBounds, Bitmap bitmap, IReadOnlyList<WindowInfo> windows, SelectorMode mode)
        {
            Screen = screen;
            WindowDecorations = WindowDecorations.None;
            ShowInTaskbar = false;
            Topmost = true;
            CanResize = false;
            Background = Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = screen.Bounds.Position;
            Width = screen.Bounds.Width / screen.Scaling;
            Height = screen.Bounds.Height / screen.Scaling;
            Title = "CaptureDoc Auswahl";
            Surface = new OverlaySurface(owner, screen, frozenBounds, bitmap, windows, mode);
            Content = Surface;
            Cursor = new Cursor(StandardCursorType.Cross);
            KeyDown += (_, e) => { if (e.Key == Key.Escape) owner.Finish(null); };
            Opened += (_, _) =>
            {
                // Windows may reposition borderless windows on mixed-DPI setups; enforce exact bounds.
                Position = screen.Bounds.Position;
                Width = screen.Bounds.Width / RenderScaling;
                Height = screen.Bounds.Height / RenderScaling;
                Surface.Focus();
            };
            Deactivated += (_, _) => { };
        }
    }

    private sealed class OverlaySurface : Control
    {
        private readonly RegionSelector _owner;
        private readonly Screen _screen;
        private readonly CdPixelRect _frozen;
        private readonly Bitmap _bitmap;
        private readonly IReadOnlyList<WindowInfo> _windows;
        private readonly SelectorMode _mode;
        private (int X, int Y)? _start;
        private (int X, int Y) _cursor;
        private bool _dragging;

        public OverlaySurface(RegionSelector owner, Screen screen, CdPixelRect frozen, Bitmap bitmap, IReadOnlyList<WindowInfo> windows, SelectorMode mode)
        {
            _owner = owner; _screen = screen; _frozen = frozen; _bitmap = bitmap; _windows = windows; _mode = mode;
            Focusable = true;
            ClipToBounds = true;
            RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        }

        private double Scale => TopLevel.GetTopLevel(this)?.RenderScaling ?? _screen.Scaling;

        /// <summary>DIP inside this window → virtual-screen pixel.</summary>
        private (int X, int Y) ToVirtual(Point p) =>
            (_screen.Bounds.X + (int)Math.Round(p.X * Scale), _screen.Bounds.Y + (int)Math.Round(p.Y * Scale));

        private Rect ToLocal(CdPixelRect r) =>
            new((r.X - _screen.Bounds.X) / Scale, (r.Y - _screen.Bounds.Y) / Scale, r.Width / Scale, r.Height / Scale);

        private CdPixelRect ScreenRect => new(_screen.Bounds.X, _screen.Bounds.Y, _screen.Bounds.Width, _screen.Bounds.Height);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var pt = e.GetCurrentPoint(this);
            if (pt.Properties.IsRightButtonPressed) { _owner.Finish(null); return; }
            if (!pt.Properties.IsLeftButtonPressed) return;
            _start = ToVirtual(pt.Position);
            _dragging = false;
            e.Pointer.Capture(this);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            _cursor = ToVirtual(e.GetPosition(this));
            if (_start is { } s && _mode == SelectorMode.RegionOrWindow)
            {
                if (Math.Abs(_cursor.X - s.X) > 3 || Math.Abs(_cursor.Y - s.Y) > 3) _dragging = true;
                if (_dragging)
                {
                    var x0 = Math.Min(s.X, _cursor.X); var y0 = Math.Min(s.Y, _cursor.Y);
                    var r = new CdPixelRect(x0, y0, Math.Abs(_cursor.X - s.X) + 1, Math.Abs(_cursor.Y - s.Y) + 1);
                    _owner.Drag = r.Intersect(ScreenRect); // MVP: selection within one monitor
                    _owner.Hover = null;
                }
            }
            else
            {
                var w = _windows.FirstOrDefault(w => !w.IsMinimized && w.Bounds.Contains(_cursor.X, _cursor.Y));
                _owner.Hover = w is null ? ScreenRect : w.Bounds.Intersect(_frozen);
            }
            _owner.InvalidateAll();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_start is null) return;
            _start = null;
            e.Pointer.Capture(null);
            if (_dragging && _owner.Drag is { } d) _owner.Finish(d);
            else if (_owner.Hover is { } h) _owner.Finish(h);
            _dragging = false;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape) _owner.Finish(null);
            if (e.Key == Key.Enter && (_owner.Drag ?? _owner.Hover) is { } r) _owner.Finish(r);
        }

        public override void Render(DrawingContext ctx)
        {
            var bounds = new Rect(Bounds.Size);
            // Frozen desktop: the part of the virtual screen that this monitor shows, 1:1 physical pixels
            var src = new Rect(_screen.Bounds.X - _frozen.X, _screen.Bounds.Y - _frozen.Y, _screen.Bounds.Width, _screen.Bounds.Height);
            ctx.DrawImage(_bitmap, src, bounds);

            var sel = _owner.Drag ?? _owner.Hover;
            var dim = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
            if (sel is { } s && !s.Intersect(ScreenRect).IsEmpty)
            {
                var r = ToLocal(s.Intersect(ScreenRect));
                var geo = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(bounds), new RectangleGeometry(r));
                ctx.DrawGeometry(dim, null, geo);
                ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0, 150, 255)), 2), r);
                var label = $"{s.Width} × {s.Height}";
                var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 13, Brushes.White);
                var ly = r.Y > 26 ? r.Y - 24 : r.Y + 4;
                ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), null, new Rect(r.X, ly, ft.Width + 12, 20), 4, 4);
                ctx.DrawText(ft, new Point(r.X + 6, ly + 2));
            }
            else
            {
                ctx.DrawRectangle(dim, null, bounds);
            }

            // crosshair while not dragging
            if (!_dragging && _mode == SelectorMode.RegionOrWindow)
            {
                var c = ToLocal(new CdPixelRect(_cursor.X, _cursor.Y, 1, 1));
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1, DashStyle.Dash);
                ctx.DrawLine(pen, new Point(c.X, 0), new Point(c.X, bounds.Height));
                ctx.DrawLine(pen, new Point(0, c.Y), new Point(bounds.Width, c.Y));
            }

            var hint = _mode == SelectorMode.WindowOnly ? "Fenster anklicken · Esc = Abbrechen" : "Ziehen = Bereich · Klick = Fenster/Bildschirm · Esc = Abbrechen";
            var hf = new FormattedText(hint, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 13, Brushes.White);
            var hr = new Rect((bounds.Width - hf.Width) / 2 - 10, 12, hf.Width + 20, 26);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(190, 20, 20, 20)), null, hr, 6, 6);
            ctx.DrawText(hf, new Point(hr.X + 10, hr.Y + 5));
        }
    }
}
