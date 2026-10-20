using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CaptureDoc.App.Services;
using CaptureDoc.App.Util;
using CaptureDoc.App.Views;
using CaptureDoc.Core.Export;
using CaptureDoc.Media;

namespace CaptureDoc.App.Video;

/// <summary>MP4 or GIF export with presets, dimensions and a verified file size limit.</summary>
public sealed class VideoExportDialog : Window
{
    private readonly AppServices _app;
    private readonly VideoExporter _exporter;
    private readonly string _src;
    private readonly MediaInfo _info;
    private readonly ClipRange _clip;
    private readonly bool _gif;
    private readonly ComboBox _preset = new() { MinWidth = 260 };
    private readonly NumericUpDown _maxW = Num(0, 8000), _maxH = Num(0, 8000), _fps = Num(0, 60), _colors = Num(2, 256), _maxMb = Num(0, 4000, 0.5m), _minFps = Num(1, 60), _minW = Num(64, 4000);
    private readonly ComboBox _limit = new() { ItemsSource = new[] { "Kein Limit", "Weiches Ziel", "Harte Obergrenze" } };
    private readonly ComboBox _quality = new() { ItemsSource = new[] { "Hoch", "Mittel", "Klein" } };
    private readonly ComboBox _encoder = new() { ItemsSource = new[] { "Automatisch (libx264)", "libx264 (CPU)", "NVIDIA NVENC", "AMD AMF", "Intel Quick Sync" } };
    private readonly ComboBox _dither = new() { ItemsSource = new[] { "sierra2_4a", "floyd_steinberg", "bayer", "none" } };
    private readonly ComboBox _loop = new() { ItemsSource = new[] { "Endlos", "Einmal abspielen" } };
    private readonly CheckBox _dedupe = new() { Content = "Unveränderte Frames entfernen (kleiner, ideal für UI-Aufnahmen)" };
    private readonly CheckBox _audio = new() { Content = "Ton übernehmen" };
    private readonly TextBlock _status = Ui.Label("", 12);
    private readonly Button _go;
    private CancellationTokenSource? _cts;

    private static NumericUpDown Num(decimal min, decimal max, decimal inc = 1) => new() { Minimum = min, Maximum = max, Increment = inc, FormatString = inc < 1 ? "0.0" : "0", Width = 130 };

    private sealed record Preset(string Name, int? MaxW, double Fps, int Colors, double? MaxMb, SizeLimitKind Kind, VideoQuality Q = VideoQuality.High);

    private static readonly Preset[] GifPresets =
    {
        new("Optimized GIF (≤ 5 MB, 800 px)", 800, 12, 256, 5, SizeLimitKind.Soft),
        new("Dokumentation GIF (≤ 5 MB hart, 1000 px)", 1000, 10, 256, 5, SizeLimitKind.Hard),
        new("Kleines GIF (≤ 2 MB hart, 640 px)", 640, 10, 128, 2, SizeLimitKind.Hard),
        new("Benutzerdefiniert", null, 15, 256, null, SizeLimitKind.None),
    };

    private static readonly Preset[] Mp4Presets =
    {
        new("High Quality Video (Originalgröße)", null, 0, 0, null, SizeLimitKind.None, VideoQuality.High),
        new("Small MP4 (≤ 10 MB, 1280 px)", 1280, 0, 0, 10, SizeLimitKind.Hard, VideoQuality.Medium),
        new("E-Mail-Anhang (≤ 20 MB)", 1600, 0, 0, 20, SizeLimitKind.Hard, VideoQuality.Medium),
        new("Support Ticket (≤ 50 MB)", 1920, 0, 0, 50, SizeLimitKind.Hard, VideoQuality.High),
        new("Benutzerdefiniert", null, 0, 0, null, SizeLimitKind.None),
    };

    public VideoExportDialog(AppServices app, VideoExporter exporter, string src, MediaInfo info, ClipRange clip, bool gif)
    {
        _app = app; _exporter = exporter; _src = src; _info = info; _clip = clip; _gif = gif;
        Title = gif ? "GIF exportieren" : "MP4 exportieren";
        Width = 600; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var presets = gif ? GifPresets : Mp4Presets;
        _preset.ItemsSource = presets.Select(p => p.Name).ToList();
        _preset.SelectionChanged += (_, _) => { if (_preset.SelectedIndex >= 0) Apply(presets[_preset.SelectedIndex]); };
        _dither.SelectedIndex = 0; _loop.SelectedIndex = 0; _dedupe.IsChecked = true; _encoder.SelectedIndex = 0; _quality.SelectedIndex = 0;
        _audio.IsChecked = info.HasAudio; _audio.IsEnabled = info.HasAudio;
        _minFps.Value = 6; _minW.Value = gif ? 320 : 480;

        _go = Ui.Button("Speichern unter…", async () => await RunAsync(), accent: true);
        var cancel = Ui.Button("Abbrechen", () => { if (_cts is not null) _cts.Cancel(); else Close(); });
        var body = new StackPanel { Margin = new Thickness(18), Spacing = 10 };
        body.Children.Add(Ui.Label($"Quelle: {info.Width}×{info.Height}, {info.Fps:0.#} fps · Ausschnitt {clip.KeptDuration:0.0} s{(clip.Cuts.Count > 0 ? $" ({clip.Cuts.Count} Schnitt(e))" : "")}", 12, opacity: 0.75));
        body.Children.Add(Ui.Field("Profil", _preset));
        body.Children.Add(Ui.Row(Ui.Field("Max. Breite px (0 = original)", _maxW), Ui.Field("Max. Höhe px", _maxH), Ui.Field(gif ? "FPS" : "FPS (0 = original)", _fps)));
        if (gif)
        {
            body.Children.Add(Ui.Row(Ui.Field("Farben", _colors), Ui.Field("Dithering", _dither), Ui.Field("Wiederholung", _loop)));
            body.Children.Add(_dedupe);
        }
        else
        {
            body.Children.Add(Ui.Row(Ui.Field("Qualität (ohne Limit)", _quality), Ui.Field("Encoder", _encoder)));
            body.Children.Add(_audio);
        }
        body.Children.Add(Ui.Row(Ui.Field("Max. Dateigröße MB (0 = keine)", _maxMb), Ui.Field("Art des Limits", _limit)));
        body.Children.Add(Ui.Row(gif ? Ui.Field("Mindest-FPS", _minFps) : new Control(), Ui.Field("Mindestbreite px", _minW)));
        body.Children.Add(Ui.Label(gif
            ? "Ist die Datei zu groß, reduziert CaptureDoc schrittweise Farben, dann FPS, dann Auflösung – jede Variante wird echt erzeugt und gemessen. Nie hochskaliert."
            : "Mit Limit wird die Bitrate aus Dauer und Zielgröße berechnet und bei Überschreitung nachgeregelt; die echte Dateigröße wird geprüft. Nie hochskaliert.", 11, opacity: 0.65));
        body.Children.Add(_status);
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, _go } });
        Content = body;
        _preset.SelectedIndex = 0;
    }

    private void Apply(Preset p)
    {
        _maxW.Value = p.MaxW ?? 0; _maxH.Value = 0;
        _fps.Value = (decimal)p.Fps; _colors.Value = Math.Max(2, p.Colors);
        _maxMb.Value = (decimal)(p.MaxMb ?? 0);
        _limit.SelectedIndex = (int)p.Kind;
        _quality.SelectedIndex = (int)p.Q;
    }

    private long? MaxBytes() => _maxMb.Value is > 0 && _limit.SelectedIndex > 0 ? (long)(_maxMb.Value.Value * 1024 * 1024) : null;
    private static int? Pos(NumericUpDown n) => n.Value is > 0 ? (int)n.Value : null;

    private async Task RunAsync()
    {
        var ext = _gif ? ".gif" : ".mp4";
        var dir = _app.Settings.Platform.LastSaveDirectory ?? _app.Env.MediaDir;
        var path = await Dialogs.SaveFileAsync(this, Title ?? "Export", Path.GetFileNameWithoutExtension(_src) + ext, dir, (_gif ? "GIF" : "MP4-Video", "*" + ext));
        if (path is null) return;
        _go.IsEnabled = false;
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(s => _status.Text = s);
        try
        {
            VideoExportResult r;
            if (_gif)
            {
                var s = new GifSettings
                {
                    MaxWidth = Pos(_maxW), MaxHeight = Pos(_maxH), Fps = (double)(_fps.Value is > 0 ? _fps.Value : 12),
                    MinFps = (double)(_minFps.Value ?? 6), Colors = (int)(_colors.Value ?? 256), MinColors = 32, MinWidth = (int)(_minW.Value ?? 320),
                    Dither = (string)_dither.SelectedItem!, Loop = _loop.SelectedIndex == 0 ? 0 : -1, DropDuplicates = _dedupe.IsChecked == true,
                    MaxBytes = MaxBytes(), LimitKind = (SizeLimitKind)Math.Max(0, _limit.SelectedIndex),
                };
                r = await Task.Run(() => _exporter.ExportGifAsync(_src, path, _clip, s, progress, _cts.Token));
            }
            else
            {
                var s = new Mp4Settings
                {
                    MaxWidth = Pos(_maxW), MaxHeight = Pos(_maxH), Fps = _fps.Value is > 0 ? (double)_fps.Value : null,
                    Quality = (VideoQuality)Math.Max(0, _quality.SelectedIndex),
                    Encoder = _encoder.SelectedIndex switch { 2 => VideoEncoder.Nvenc, 3 => VideoEncoder.Amf, 4 => VideoEncoder.Qsv, _ => VideoEncoder.X264 },
                    IncludeAudio = _audio.IsChecked == true, MaxBytes = MaxBytes(), LimitKind = (SizeLimitKind)Math.Max(0, _limit.SelectedIndex), MinWidth = (int)(_minW.Value ?? 480),
                };
                r = await Task.Run(() => _exporter.ExportMp4Async(_src, path, _clip, s, progress, _cts.Token));
            }
            Log.Info($"Video export {(_gif ? "GIF" : "MP4")}: success={r.Success} {r.Bytes} bytes {r.Width}x{r.Height}");
            if (r.Success)
            {
                _app.Settings.Platform.LastSaveDirectory = Path.GetDirectoryName(path);
                _app.SaveSettings();
                await Dialogs.InfoAsync(this, "Export abgeschlossen", r.Message + "\n\n" + path);
                Close();
            }
            else
            {
                _status.Text = "⚠ " + r.Message;
                await Dialogs.InfoAsync(this, "Export nicht möglich", r.Message);
            }
        }
        catch (OperationCanceledException) { _status.Text = "Abgebrochen."; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            _status.Text = "⚠ " + ex.Message;
        }
        finally { _go.IsEnabled = true; _cts = null; }
    }
}
