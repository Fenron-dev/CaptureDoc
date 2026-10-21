using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CaptureDoc.App.Editor;
using CaptureDoc.App.Services;
using CaptureDoc.App.Util;
using CaptureDoc.App.Video;

namespace CaptureDoc.App.Views;

/// <summary>Overview of the files (videos) a project uses: where they are, how big, where used; re-link or collect.</summary>
public sealed class ProjectMediaWindow : Window
{
    private readonly AppServices _app;
    private readonly ProjectSession _session;
    private readonly StackPanel _list = new() { Spacing = 6 };
    private readonly TextBlock _status = Ui.Label("", 12, opacity: 0.8);

    public event EventHandler? Changed;

    public ProjectMediaWindow(AppServices app, ProjectSession session)
    {
        _app = app; _session = session;
        Title = "Projektdateien – CaptureDoc";
        Width = 820; Height = 560; MinWidth = 600; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = IconFactory.WindowIcon;
        var head = Ui.Column(4,
            Ui.Label("Dateien dieses Projekts", 15, bold: true),
            Ui.Label("Videos werden nicht in die .capturedoc-Datei eingebettet, sondern verknüpft. Zum Weitergeben: „In Projektordner sammeln“ kopiert alles neben das Projekt; dann den ganzen Ordner weitergeben.", 11, opacity: 0.7));
        var collect = Ui.Button("📦 In Projektordner sammeln", async () => await CollectAsync(), "Alle Dateien außerhalb des Projektordners in „<Projekt>-Medien“ kopieren und das Projekt auf die Kopien umstellen", accent: true);
        var buttons = Ui.Row(collect, Ui.Button("↻", Rebuild, "Liste aktualisieren"), Ui.Button("Schließen", Close));
        var dock = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(head, Dock.Top); dock.Children.Add(head);
        DockPanel.SetDock(buttons, Dock.Bottom); dock.Children.Add(buttons);
        DockPanel.SetDock(_status, Dock.Bottom); dock.Children.Add(_status);
        dock.Children.Add(new ScrollViewer { Content = _list, Margin = new Thickness(0, 10) });
        Content = dock;
        Rebuild();
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        var entries = ProjectMediaService.List(_app, _session);
        if (entries.Count == 0) { _list.Children.Add(Ui.Label("Dieses Projekt verwendet keine Videos.", 12, opacity: 0.7)); return; }
        long total = 0;
        foreach (var e in entries)
        {
            var entry = e;
            total += e.Size;
            var used = e.Ref.UsedBy.Count == 0 ? "nur in der Medienliste (nicht in der Timeline)" : string.Join(" · ", e.Ref.UsedBy);
            var info = Ui.Column(2,
                Ui.Label((e.Missing ? "⚠ " : "🎞 ") + e.Name, 13, bold: true),
                Ui.Label(e.Missing ? $"Nicht gefunden: {e.Ref.Stored}" : $"{e.Resolved} · {Core.Export.SizeOptimizer.FormatBytes(e.Size)}", 11, opacity: 0.75),
                Ui.Label("Verwendet: " + used, 11, opacity: 0.65));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            if (!e.Missing) actions.Children.Add(Ui.Button("📂", () => OpenFolder(entry.Resolved!), "Im Ordner zeigen"));
            actions.Children.Add(Ui.Button(e.Missing ? "Neu verknüpfen…" : "Ersetzen…", async () => await RelinkAsync(entry), "Eine andere Datei an diese Stelle setzen (z. B. verschobene Aufnahme)", accent: e.Missing));
            var row = new DockPanel();
            DockPanel.SetDock(actions, Dock.Right);
            row.Children.Add(actions);
            row.Children.Add(info);
            _list.Children.Add(new Border
            {
                Child = row, Padding = new Thickness(8), CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(e.Missing ? Color.FromArgb(40, 220, 60, 40) : Color.FromArgb(25, 128, 128, 128)),
            });
        }
        var missing = entries.Count(x => x.Missing);
        _status.Text = $"{entries.Count} Datei(en), zusammen {Core.Export.SizeOptimizer.FormatBytes(total)}" + (missing > 0 ? $" · ⚠ {missing} fehlen" : "") +
                       (ProjectMediaService.CollectFolder(_session) is { } f ? $" · Sammelordner: {f}" : " · Projekt noch nicht gespeichert");
    }

    private async Task RelinkAsync(ProjectMediaService.Entry e)
    {
        var path = await Dialogs.OpenFileAsync(this, "Datei auswählen", e.Resolved is null ? _app.Env.MediaDir : Path.GetDirectoryName(e.Resolved), ("Videos", new[] { "*.mp4", "*.mkv", "*.webm", "*.mov", "*.avi" }));
        if (path is null) return;
        var n = ProjectMediaService.Relink(_app, _session, e.Ref.Stored, path);
        _status.Text = $"{n} Verweis(e) auf {Path.GetFileName(path)} umgestellt.";
        Changed?.Invoke(this, EventArgs.Empty);
        Rebuild();
    }

    private async Task CollectAsync()
    {
        try
        {
            IsEnabled = false;
            var r = await ProjectMediaService.CollectAsync(_app, _session, new Progress<string>(s => _status.Text = s));
            Changed?.Invoke(this, EventArgs.Empty);
            Rebuild();
            _status.Text = $"{r.Copied} kopiert, {r.AlreadyInside} lagen schon im Projektordner" + (r.Missing > 0 ? $", {r.Missing} fehlen" : "") + ". Projekt speichern nicht vergessen.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            await Dialogs.InfoAsync(this, "Sammeln nicht möglich", ex.Message);
        }
        finally { IsEnabled = true; }
    }

    private static void OpenFolder(string file)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = Path.GetDirectoryName(file)!, UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Open folder: " + ex.Message); }
    }
}
