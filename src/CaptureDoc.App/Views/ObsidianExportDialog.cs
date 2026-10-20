using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CaptureDoc.App.Services;
using CaptureDoc.App.Util;
using CaptureDoc.Core.Obsidian;

namespace CaptureDoc.App.Views;

/// <summary>Shows what will be written (new / update / unchanged / conflict) before anything touches the vault.</summary>
public sealed class ObsidianExportDialog : Window
{
    private readonly Func<ConflictPolicy, ExportPlan> _plan;
    private readonly Guid _exportId;
    private readonly StackPanel _files = new() { Spacing = 2 };
    private readonly TextBox _preview = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontSize = 12 };
    private readonly StackPanel _warnings = new() { Spacing = 4 };
    private readonly ComboBox _policy = new() { ItemsSource = new[] { "Als neue Version speichern (empfohlen)", "Vorhandene Datei behalten (überspringen)", "Überschreiben" } };
    private ExportPlan? _current;

    public ExportResult? Result { get; private set; }

    public ObsidianExportDialog(Func<ConflictPolicy, ExportPlan> plan, Guid exportId)
    {
        _plan = plan; _exportId = exportId;
        Title = "Obsidian-Export – Vorschau";
        Width = 980; Height = 720; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _policy.SelectedIndex = 0;
        _policy.SelectionChanged += (_, _) => Refresh();

        var export = Ui.Button("In Vault exportieren", async () => await ExecuteAsync(), accent: true);
        var cancel = Ui.Button("Abbrechen", Close);
        var privacy = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, 0, 120, 215)), CornerRadius = new CornerRadius(6), Padding = new Thickness(10),
            Child = Ui.Label("Übertragen werden nur die unten gelisteten Dateien: die Markdown-Notiz und die fertig gerenderten Bilder (Schwärzungen eingebrannt). " +
                             "Originalaufnahmen und die Projektdatei werden nicht in den Vault kopiert. Obsidian-Einstellungen (.obsidian) werden nie verändert.", 12),
        };

        var left = new DockPanel();
        var lh = Ui.Label("Dateien", 14, bold: true); DockPanel.SetDock(lh, Dock.Top); left.Children.Add(lh);
        var pol = Ui.Field("Bei Konflikten (Datei außerhalb von CaptureDoc geändert)", _policy); DockPanel.SetDock(pol, Dock.Bottom); left.Children.Add(pol);
        var warn = new ScrollViewer { Content = _warnings, MaxHeight = 180 }; DockPanel.SetDock(warn, Dock.Bottom); left.Children.Add(warn);
        left.Children.Add(new ScrollViewer { Content = _files });

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("420,*"), Margin = new Thickness(0, 10) };
        Grid.SetColumn(left, 0);
        var right = new DockPanel { Margin = new Thickness(12, 0, 0, 0) };
        var rh = Ui.Label("Markdown-Vorschau", 14, bold: true); DockPanel.SetDock(rh, Dock.Top); right.Children.Add(rh);
        right.Children.Add(_preview);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left); grid.Children.Add(right);

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(privacy, Dock.Top); root.Children.Add(privacy);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, export } };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        root.Children.Add(grid);
        Content = root;
        Opened += (_, _) => Refresh();
    }

    private ConflictPolicy Policy => _policy.SelectedIndex switch { 1 => ConflictPolicy.Skip, 2 => ConflictPolicy.Overwrite, _ => ConflictPolicy.NewVersion };

    private void Refresh()
    {
        try { _current = _plan(Policy); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _files.Children.Clear();
            _files.Children.Add(Ui.Label("⚠ " + ex.Message, 13));
            _current = null;
            return;
        }
        _files.Children.Clear();
        foreach (var f in _current.AllFiles)
        {
            var (badge, color) = f.Action switch
            {
                PlannedAction.Create => ("NEU", Color.FromRgb(48, 164, 108)),
                PlannedAction.Update => ("AKTUALISIEREN", Color.FromRgb(47, 128, 237)),
                PlannedAction.Unchanged => ("UNVERÄNDERT", Color.FromRgb(130, 130, 130)),
                PlannedAction.ConflictNewVersion => ("NEUE VERSION", Color.FromRgb(230, 140, 20)),
                PlannedAction.ConflictSkip => ("ÜBERSPRINGEN", Color.FromRgb(230, 140, 20)),
                _ => ("ÜBERSCHREIBEN", Color.FromRgb(229, 72, 77)),
            };
            var b = new Border { Background = new SolidColorBrush(color), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1), Child = new TextBlock { Text = badge, Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeight.Bold }, VerticalAlignment = VerticalAlignment.Center };
            var t = Ui.Label(f.VaultRelativePath, 12);
            t.TextWrapping = TextWrapping.NoWrap;
            ToolTip.SetTip(t, f.FullPath);
            _files.Children.Add(Ui.Row(b, t));
        }
        _warnings.Children.Clear();
        foreach (var w in _current.Warnings) _warnings.Children.Add(Ui.Label("⚠ " + w, 12));
        _preview.Text = _current.Markdown;
    }

    private async Task ExecuteAsync()
    {
        if (_current is null) return;
        if (_current.AllFiles.Any(f => f.Action == PlannedAction.ConflictOverwrite))
        {
            var a = await Dialogs.AskAsync(this, "Überschreiben bestätigen",
                "Mindestens eine Datei wurde außerhalb von CaptureDoc geändert und wird jetzt überschrieben. Manuelle Änderungen in Obsidian gehen dabei verloren.",
                "Abbrechen", "Ja, überschreiben");
            if (a != "Ja, überschreiben") return;
        }
        try
        {
            Result = ObsidianExporter.Execute(_current, _exportId);
            Log.Info($"Obsidian export: {Result.Written.Count} written, {Result.Skipped.Count} skipped, missing {Result.MissingAssets.Count}");
            var msg = $"{Result.Written.Count} Datei(en) geschrieben, {Result.Skipped.Count} übersprungen.\n\nNotiz: {Result.NotePath}\n\n" +
                      (Result.Verified ? "✔ Alle Bild-Verknüpfungen wurden geprüft und sind gültig." : "⚠ Fehlende Dateien: " + string.Join(", ", Result.MissingAssets));
            await Dialogs.InfoAsync(this, "Export abgeschlossen", msg);
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.InfoAsync(this, "Export fehlgeschlagen", ex.Message);
        }
    }
}
