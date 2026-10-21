using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CaptureDoc.App.Views;

/// <summary>Screenshot pinned on top of all windows (reference while working). Drag = move, wheel = size, Esc/double-click = close.</summary>
public sealed class PinWindow : Window
{
    private readonly Bitmap _bitmap;
    private double _scale = 1;

    public PinWindow(Bitmap bitmap)
    {
        _bitmap = bitmap;
        WindowDecorations = WindowDecorations.None;
        Topmost = true; ShowInTaskbar = false; CanResize = false;
        Title = "CaptureDoc – angeheftet";
        Content = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0, 140, 255)), BorderThickness = new Thickness(2), Child = new Image { Source = bitmap, Stretch = Stretch.Fill } };
        ToolTip.SetTip(this, "Ziehen = verschieben · Mausrad = Größe · Doppelklick/Esc = schließen");
        Opened += (_, _) => ApplySize();
        PointerPressed += (_, e) =>
        {
            if (e.ClickCount == 2) { Close(); return; }
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) Close();
        };
        PointerWheelChanged += (_, e) => { _scale = Math.Clamp(_scale * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1), 0.1, 4); ApplySize(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => _bitmap.Dispose();
    }

    private void ApplySize()
    {
        // 100 % = one image pixel per physical screen pixel
        var s = RenderScaling;
        Width = _bitmap.PixelSize.Width * _scale / s + 4;
        Height = _bitmap.PixelSize.Height * _scale / s + 4;
    }
}
