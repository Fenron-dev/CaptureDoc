using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CaptureDoc.App.Services;
using CaptureDoc.App.Util;

namespace CaptureDoc.App.Views;

/// <summary>Small always-available capture bar (Quick Capture mode). Closing hides it to the tray.</summary>
public sealed class QuickCaptureWindow : Window
{
    private readonly AppServices _app;
    private readonly ComboBox _delay = new() { ItemsSource = new[] { "Sofort", "3 s", "5 s", "10 s" }, Width = 90 };
    private readonly CheckBox _cursor = new() { Content = "Cursor" };
    private readonly TextBlock _hint = Ui.Label("", 11, opacity: 0.65);

    public QuickCaptureWindow(AppServices app)
    {
        _app = app;
        Title = "CaptureDoc";
        Icon = IconFactory.WindowIcon;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _delay.SelectedIndex = app.Settings.Platform.DelaySeconds switch { >= 10 => 3, >= 5 => 2, >= 3 => 1, _ => 0 };
        _delay.SelectionChanged += (_, _) => { app.Settings.Platform.DelaySeconds = new[] { 0, 3, 5, 10 }[Math.Max(0, _delay.SelectedIndex)]; app.SaveSettings(); };
        _cursor.IsChecked = app.Settings.Platform.IncludeCursor;
        _cursor.IsCheckedChanged += (_, _) => { app.Settings.Platform.IncludeCursor = _cursor.IsChecked == true; app.SaveSettings(); };

        Button Big(string glyph, string text, CaptureMode mode, string tip)
        {
            var b = new Button
            {
                Content = Ui.Column(2, new TextBlock { Text = glyph, FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center }, new TextBlock { Text = text, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center }),
                Width = 116, Height = 66, Margin = new Thickness(3),
            };
            ToolTip.SetTip(b, tip);
            b.Click += async (_, _) => await App.Current.Capture.CaptureAsync(mode);
            return b;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Big("⬚", "Bereich", CaptureMode.Region, "Bereich ziehen oder Fenster anklicken"));
        row.Children.Add(Big("🗔", "Fenster", CaptureMode.Window, "Fenster auswählen"));
        row.Children.Add(Big("🖥", "Monitor", CaptureMode.Monitor, "Monitor unter dem Mauszeiger"));
        row.Children.Add(Big("⧉", "Alle Bildschirme", CaptureMode.FullScreen, "Alle Monitore"));
        row.Children.Add(Big("↺", "Letzter Bereich", CaptureMode.LastRegion, "Zuletzt gewählten Bereich erneut aufnehmen"));
        var video = new Button
        {
            Content = Ui.Column(2, new TextBlock { Text = "●", FontSize = 22, Foreground = new SolidColorBrush(Color.FromRgb(230, 50, 50)), HorizontalAlignment = HorizontalAlignment.Center }, new TextBlock { Text = "Video / GIF", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center }),
            Width = 116, Height = 66, Margin = new Thickness(3),
        };
        ToolTip.SetTip(video, "Bildschirmvideo aufnehmen (sichtbarer Rahmen + Timer), danach schneiden und als MP4/GIF exportieren");
        video.Click += async (_, _) => await App.Current.Recording.StartAsync();
        row.Children.Add(video);

        var second = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(4, 2) };
        second.Children.Add(Ui.Label("Verzögerung", 12));
        second.Children.Add(_delay);
        second.Children.Add(_cursor);
        second.Children.Add(new Separator { Width = 8 });
        second.Children.Add(Ui.Button("📂 Öffnen", async () => await App.Current.OpenFileAsync(this), "Projekt oder Bild öffnen"));
        second.Children.Add(Ui.Button("📘 Studio", () => App.Current.ShowStudio(), "Documentation Studio: Anleitungen bauen und nach Obsidian exportieren"));
        second.Children.Add(Ui.Button("⚙", () => new SettingsWindow(app).ShowDialog(this), "Einstellungen"));

        Content = new StackPanel { Margin = new Thickness(8), Spacing = 4, Children = { row, second, _hint } };
        UpdateHint();
        Closing += (_, e) =>
        {
            if (App.Current.IsExiting || !App.Current.HasTray) return;
            e.Cancel = true; // keep running in the tray
            Hide();
        };
    }

    public void UpdateHint()
    {
        var hk = _app.Settings.Platform.Hotkeys.Select(h => $"{h.Gesture} = {Label(h.Action)}{(_app.HotkeyStatus.TryGetValue(h.Action, out var ok) && !ok ? " (belegt!)" : "")}");
        _hint.Text = "Tastenkürzel: " + string.Join(" · ", hk);
        static string Label(string a) => a switch { "region" => "Bereich", "window" => "aktives Fenster", "screen" => "Bildschirm", "last-region" => "letzter Bereich", "video" => "Video", _ => a };
    }
}
