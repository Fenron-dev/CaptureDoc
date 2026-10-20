using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CaptureDoc.App.Views;

/// <summary>Visible countdown before a delayed capture (no hidden captures).</summary>
public sealed class CountdownWindow : Window
{
    private readonly TextBlock _text = new() { FontSize = 56, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };

    private CountdownWindow()
    {
        WindowDecorations = WindowDecorations.None;
        Topmost = true; ShowInTaskbar = false; CanResize = false;
        Width = 140; Height = 140;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromArgb(210, 20, 20, 20));
        Content = _text;
    }

    public static async Task RunAsync(int seconds)
    {
        var w = new CountdownWindow();
        w.Show();
        for (var i = seconds; i > 0; i--)
        {
            w._text.Text = i.ToString();
            await Task.Delay(1000);
        }
        w.Close();
    }
}
