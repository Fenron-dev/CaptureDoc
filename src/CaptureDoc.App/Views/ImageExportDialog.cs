using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CaptureDoc.App.Services;
using CaptureDoc.App.Util;
using CaptureDoc.Core.Export;
using CaptureDoc.Rendering;
using SkiaSharp;

namespace CaptureDoc.App.Views;

/// <summary>Image export with profile, dimensions, quality and a verified size limit.</summary>
public sealed class ImageExportDialog : Window
{
    private readonly AppServices _app;
    private readonly SKImage _flat;
    private readonly ComboBox _profile = new() { MinWidth = 220 };
    private readonly ComboBox _format = new() { ItemsSource = new[] { "PNG", "JPEG", "WebP" } };
    private readonly NumericUpDown _maxW = Num(0, 30000), _maxH = Num(0, 30000), _scale = Num(1, 100), _quality = Num(1, 100), _maxKb = Num(0, 1_000_000);
    private readonly CheckBox _lossless = new() { Content = "Verlustfrei (WebP)" };
    private readonly CheckBox _alpha = new() { Content = "Transparenz behalten" };
    private readonly ComboBox _limit = new() { ItemsSource = new[] { "Kein Limit", "Weiches Ziel", "Harte Obergrenze" } };
    private readonly TextBlock _estimate = Ui.Label("", 12);
    private readonly List<ExportProfile> _profiles;
    private bool _loading;
    private CancellationTokenSource? _estimateCts;

    public string? WrittenPath { get; private set; }

    private static NumericUpDown Num(double min, double max) => new() { Minimum = (decimal)min, Maximum = (decimal)max, FormatString = "0", Increment = 1, Width = 130 };

    public ImageExportDialog(AppServices app, SKImage flattened, string suggestedName)
    {
        _app = app; _flat = flattened;
        Title = "Bild exportieren";
        Width = 520; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _profiles = app.Profiles.All.ToList();
        _profile.ItemsSource = _profiles.Select(p => p.Name).ToList();
        _profile.SelectionChanged += (_, _) => { if (_profile.SelectedIndex >= 0) Load(_profiles[_profile.SelectedIndex]); };

        foreach (var c in new Control[] { _format, _maxW, _maxH, _scale, _quality, _maxKb, _lossless, _alpha, _limit })
        {
            switch (c)
            {
                case NumericUpDown n: n.ValueChanged += (_, _) => Estimate(); break;
                case ComboBox cb: cb.SelectionChanged += (_, _) => Estimate(); break;
                case CheckBox ch: ch.IsCheckedChanged += (_, _) => Estimate(); break;
            }
        }

        var save = Ui.Button("Speichern unter…", async () => await SaveAsync(suggestedName), accent: true);
        var saveProfile = Ui.Button("Als Profil speichern", SaveAsProfile, "Aktuelle Einstellungen als eigenes, bearbeitbares Profil speichern");
        Content = new StackPanel
        {
            Margin = new Thickness(18), Spacing = 10,
            Children =
            {
                Ui.Label($"Original: {flattened.Width} × {flattened.Height} px", 12, opacity: 0.75),
                Ui.Field("Exportprofil", _profile),
                Ui.Row(Ui.Field("Format", _format), Ui.Field("Qualität (1–100)", _quality)),
                Ui.Row(Ui.Field("Max. Breite px (0 = original)", _maxW), Ui.Field("Max. Höhe px", _maxH), Ui.Field("Skalierung %", _scale)),
                Ui.Row(_lossless, _alpha),
                Ui.Row(Ui.Field("Max. Dateigröße KB (0 = keine)", _maxKb), Ui.Field("Art des Limits", _limit)),
                Ui.Label("Seitenverhältnis bleibt immer erhalten, es wird nie hochskaliert. Metadaten werden nicht geschrieben. Die tatsächliche Dateigröße wird nach dem Schreiben geprüft.", 11, opacity: 0.65),
                _estimate,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { saveProfile, save } },
            },
        };
        var def = _profiles.FindIndex(p => p.Id == app.Settings.Shared.DefaultExportProfile);
        _profile.SelectedIndex = Math.Max(0, def);
    }

    private void Load(ExportProfile p)
    {
        _loading = true;
        _format.SelectedIndex = (int)p.Format;
        _maxW.Value = p.MaxWidth ?? 0; _maxH.Value = p.MaxHeight ?? 0;
        _scale.Value = (decimal)Math.Round(p.Scale * 100);
        _quality.Value = p.Quality;
        _lossless.IsChecked = p.Lossless; _alpha.IsChecked = p.KeepTransparency;
        _maxKb.Value = p.MaxBytes is { } b ? b / 1024 : 0;
        _limit.SelectedIndex = p.MaxBytes is null ? 0 : (int)p.LimitKind;
        _loading = false;
        Estimate();
    }

    private ExportProfile Current()
    {
        var baseP = _profile.SelectedIndex >= 0 ? _profiles[_profile.SelectedIndex] : BuiltInProfiles.All[0];
        var p = baseP.Copy(baseP.Id, baseP.Name);
        p.Format = (ImageFormat)Math.Max(0, _format.SelectedIndex);
        p.MaxWidth = _maxW.Value is > 0 ? (int)_maxW.Value : null;
        p.MaxHeight = _maxH.Value is > 0 ? (int)_maxH.Value : null;
        p.Scale = (double)(_scale.Value ?? 100) / 100.0;
        p.Quality = (int)(_quality.Value ?? 90);
        p.Lossless = p.Format == ImageFormat.Png || (_lossless.IsChecked == true && p.Format == ImageFormat.WebP);
        p.KeepTransparency = _alpha.IsChecked == true;
        var kb = (long)(_maxKb.Value ?? 0);
        var kind = (SizeLimitKind)Math.Max(0, _limit.SelectedIndex);
        p.MaxBytes = kb > 0 && kind != SizeLimitKind.None ? kb * 1024 : null;
        p.LimitKind = p.MaxBytes is null ? SizeLimitKind.None : kind;
        return p;
    }

    private async void Estimate()
    {
        if (_loading) return;
        _estimateCts?.Cancel();
        var cts = _estimateCts = new CancellationTokenSource();
        var p = Current();
        _estimate.Text = "Berechne Dateigröße…";
        try
        {
            await Task.Delay(250, cts.Token);
            var r = await Task.Run(() => ImageExporter.Export(_flat, p), cts.Token);
            if (cts.IsCancellationRequested) return;
            _estimate.Text = r.Data is null
                ? "⚠ " + r.Message
                : $"Voraussichtlich {r.Width} × {r.Height} px, {SizeOptimizer.FormatBytes(r.Bytes)}" + (p.Format != ImageFormat.Png && !p.Lossless ? $", Qualität {r.Quality}" : "") +
                  (r.LimitMet ? "" : $"  ⚠ {r.Message}");
        }
        catch (OperationCanceledException) { }
    }

    private async Task SaveAsync(string suggestedName)
    {
        var p = Current();
        var dir = _app.Settings.Platform.LastSaveDirectory ?? _app.Env.MediaDir;
        var types = p.Format switch
        {
            ImageFormat.Jpeg => new[] { ("JPEG", "*.jpg") },
            ImageFormat.WebP => new[] { ("WebP", "*.webp") },
            _ => new[] { ("PNG", "*.png") },
        };
        var path = await Dialogs.SaveFileAsync(this, "Bild speichern", suggestedName + p.FileExtension, dir, types);
        if (path is null) return;
        _estimate.Text = "Exportiere…";
        var r = await Task.Run(() => ImageExporter.Export(_flat, p, path));
        if (!r.Success)
        {
            await Dialogs.InfoAsync(this, "Export nicht möglich", r.Message);
            _estimate.Text = "⚠ " + r.Message;
            return;
        }
        _app.Settings.Platform.LastSaveDirectory = Path.GetDirectoryName(path);
        _app.SaveSettings();
        WrittenPath = path;
        Log.Info($"Exported {r.Width}x{r.Height} {r.Bytes} bytes");
        if (!r.LimitMet) await Dialogs.InfoAsync(this, "Größenziel verfehlt", r.Message);
        Close();
    }

    private void SaveAsProfile()
    {
        var p = Current();
        var copy = _app.Profiles.Duplicate(p);
        copy.Name = p.Name + " (angepasst)";
        try { _app.Profiles.Save(); } catch (IOException ex) { Log.Error("Profile save", ex); }
        _profiles.Add(copy);
        _profile.ItemsSource = _profiles.Select(x => x.Name).ToList();
        _profile.SelectedIndex = _profiles.Count - 1;
    }
}
