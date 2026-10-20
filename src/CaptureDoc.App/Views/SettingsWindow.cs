using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CaptureDoc.App.Services;
using CaptureDoc.App.Util;
using CaptureDoc.Core.Platform;
using CaptureDoc.Core.Portable;

namespace CaptureDoc.App.Views;

public sealed class SettingsWindow : Window
{
    public SettingsWindow(AppServices app)
    {
        Title = "Einstellungen";
        Width = 560; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var s = app.Settings;

        var after = new ComboBox { ItemsSource = new[] { "Editor öffnen", "In Zwischenablage kopieren", "Als Datei speichern", "Kopieren und Editor öffnen" }, SelectedIndex = (int)s.Shared.AfterCapture };
        var theme = new ComboBox { ItemsSource = new[] { "System", "Hell", "Dunkel" }, SelectedIndex = (int)s.Shared.Theme };
        var profiles = app.Profiles.All.ToList();
        var profile = new ComboBox { ItemsSource = profiles.Select(p => p.Name).ToList(), SelectedIndex = Math.Max(0, profiles.FindIndex(p => p.Id == s.Shared.DefaultExportProfile)) };
        var pattern = new TextBox { Text = s.Shared.FileNamePattern };
        var autosave = new NumericUpDown { Value = s.Shared.AutoSaveSeconds, Minimum = 15, Maximum = 3600, FormatString = "0", Width = 140 };
        var warn = new CheckBox { Content = "Beim Speichern von Projekten mit Schwärzungen warnen", IsChecked = s.Shared.WarnOnRedactedProjectSave };
        var tray = new CheckBox { Content = "Beim Start nur im Infobereich (Tray) starten", IsChecked = s.Platform.StartMinimizedToTray };

        var hotkeyBoxes = new List<(HotkeySetting setting, TextBox box)>();
        var hk = new StackPanel { Spacing = 4 };
        foreach (var h in s.Platform.Hotkeys)
        {
            var tb = new TextBox { Text = h.Gesture, Width = 200 };
            var status = app.HotkeyStatus.TryGetValue(h.Action, out var ok) ? (ok ? "✔ aktiv" : "⚠ von anderem Programm belegt") : "";
            hk.Children.Add(Ui.Row(Ui.Label(h.Action switch { "region" => "Bereich", "window" => "Aktives Fenster", "screen" => "Bildschirm", "video" => "Video", _ => "Letzter Bereich" }, 12), tb, Ui.Label(status, 11, opacity: 0.7)));
            hotkeyBoxes.Add((h, tb));
        }

        var recFps = new NumericUpDown { Value = s.Platform.RecordingFps, Minimum = 5, Maximum = 60, FormatString = "0", Width = 120 };
        var recCountdown = new NumericUpDown { Value = s.Platform.RecordingCountdown, Minimum = 0, Maximum = 10, FormatString = "0", Width = 120 };
        var recCursor = new CheckBox { Content = "Mauszeiger im Video", IsChecked = s.Platform.RecordCursor };
        var recMic = new CheckBox { Content = "Mikrofon aufnehmen", IsChecked = s.Platform.RecordMicrophone };
        var micDevice = new ComboBox { MinWidth = 260, PlaceholderText = "Gerät wird gesucht …" };
        if (app.Ffmpeg is { } ff)
            _ = Task.Run(() => CaptureDoc.Media.MediaProbe.ListMicrophonesAsync(ff)).ContinueWith(t =>
            {
                var list = t.IsCompletedSuccessfully ? t.Result : new List<string>();
                micDevice.ItemsSource = list;
                micDevice.SelectedIndex = Math.Max(0, list.IndexOf(s.Platform.MicrophoneDevice ?? ""));
                if (list.Count == 0) { micDevice.PlaceholderText = "Kein Mikrofon gefunden"; recMic.IsEnabled = false; }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        var ffInfo = Ui.Label(app.Ffmpeg is { } f2 ? $"FFmpeg: {f2.Source} ({f2.Ffmpeg})" : "⚠ FFmpeg nicht gefunden – ffmpeg.exe und ffprobe.exe nach Runtime\\ffmpeg\\ kopieren.", 11, opacity: 0.7);

        var dataInfo =Ui.Label($"Datenordner: {app.Env.DataRoot}" + (app.Env.IsFallbackLocation ? "\n⚠ Ausweichort, da der portable Ordner schreibgeschützt ist." : ""), 11, opacity: 0.7);

        var save = Ui.Button("Speichern", () =>
        {
            s.Shared.AfterCapture = (AfterCaptureAction)Math.Max(0, after.SelectedIndex);
            s.Shared.Theme = (ThemePreference)Math.Max(0, theme.SelectedIndex);
            s.Shared.DefaultExportProfile = profiles[Math.Max(0, profile.SelectedIndex)].Id;
            s.Shared.FileNamePattern = string.IsNullOrWhiteSpace(pattern.Text) ? "Screenshot_{yyyy-MM-dd_HH-mm-ss}" : pattern.Text;
            s.Shared.AutoSaveSeconds = (int)(autosave.Value ?? 60);
            s.Shared.WarnOnRedactedProjectSave = warn.IsChecked == true;
            s.Platform.StartMinimizedToTray = tray.IsChecked == true;
            s.Platform.RecordingFps = (int)(recFps.Value ?? 30);
            s.Platform.RecordingCountdown = (int)(recCountdown.Value ?? 3);
            s.Platform.RecordCursor = recCursor.IsChecked == true;
            s.Platform.RecordMicrophone = recMic.IsChecked == true && micDevice.SelectedItem is string;
            if (micDevice.SelectedItem is string mic) s.Platform.MicrophoneDevice = mic;
            foreach (var (setting, box) in hotkeyBoxes)
                if (HotkeyGesture.TryParse(box.Text, out var g)) setting.Gesture = g.ToString();
            app.SaveSettings();
            App.Current.ApplyTheme();
            App.Current.RegisterHotkeys();
            Close();
        }, accent: true);

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(18), Spacing = 10,
                Children =
                {
                    Ui.Field("Nach der Aufnahme", after),
                    Ui.Field("Standard-Exportprofil (Schnell speichern, Datei-Modus)", profile),
                    Ui.Field("Dateiname ({…} = Datumsformat)", pattern),
                    Ui.Row(Ui.Field("Design", theme), Ui.Field("Auto-Save (Sekunden)", autosave)),
                    warn, tray,
                    Ui.Label("Globale Tastenkürzel", 13, bold: true),
                    Ui.Label("Hinweis Windows 11: Wenn „Druck-Taste zum Öffnen des Bildschirmausschnitts verwenden“ aktiv ist, belegt das Snipping Tool die Druck-Taste. Dann z. B. Strg+Umschalt+1 verwenden.", 11, opacity: 0.7),
                    hk,
                    Ui.Label("Video / GIF", 13, bold: true),
                    Ui.Row(Ui.Field("Bilder pro Sekunde", recFps), Ui.Field("Countdown (s)", recCountdown)),
                    recCursor,
                    Ui.Row(recMic, micDevice),
                    Ui.Label("Systemton wird noch nicht aufgenommen (geplant).", 11, opacity: 0.6),
                    ffInfo,
                    dataInfo,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { save } },
                },
            },
        };
    }
}
