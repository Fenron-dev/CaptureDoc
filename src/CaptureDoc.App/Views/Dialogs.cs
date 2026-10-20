using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CaptureDoc.App.Util;

namespace CaptureDoc.App.Views;

public static class Dialogs
{
    /// <summary>Simple message box. Returns the clicked button text (or null if closed).</summary>
    public static async Task<string?> AskAsync(Window? owner, string title, string message, params string[] buttons)
    {
        if (buttons.Length == 0) buttons = new[] { "OK" };
        string? result = null;
        var w = new Window
        {
            Title = title, Width = 520, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = owner is null,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        foreach (var b in buttons)
        {
            var text = b;
            row.Children.Add(Ui.Button(text, () => { result = text; w.Close(); }, accent: ReferenceEquals(b, buttons[0])));
        }
        w.Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 16,
            Children =
            {
                new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 },
                row,
            },
        };
        if (owner is not null && owner.IsVisible) await w.ShowDialog(owner);
        else
        {
            var tcs = new TaskCompletionSource();
            w.Closed += (_, _) => tcs.TrySetResult();
            w.Show();
            await tcs.Task;
        }
        return result;
    }

    public static Task InfoAsync(Window? owner, string title, string message) => AskAsync(owner, title, message, "OK");

    public static async Task<string?> SaveFileAsync(TopLevel top, string title, string suggestedName, string? startDir, params (string name, string pattern)[] types)
    {
        IStorageFolder? folder = null;
        if (startDir is not null && Directory.Exists(startDir)) folder = await top.StorageProvider.TryGetFolderFromPathAsync(startDir);
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            SuggestedStartLocation = folder,
            DefaultExtension = types.Length > 0 ? types[0].pattern.TrimStart('*', '.') : null,
            FileTypeChoices = types.Select(t => new FilePickerFileType(t.name) { Patterns = new[] { t.pattern } }).ToList(),
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public static async Task<string?> OpenFileAsync(TopLevel top, string title, string? startDir, params (string name, string[] patterns)[] types)
    {
        IStorageFolder? folder = null;
        if (startDir is not null && Directory.Exists(startDir)) folder = await top.StorageProvider.TryGetFolderFromPathAsync(startDir);
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = false, SuggestedStartLocation = folder,
            FileTypeFilter = types.Select(t => new FilePickerFileType(t.name) { Patterns = t.patterns }).ToList(),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> PickFolderAsync(TopLevel top, string title, string? startDir)
    {
        IStorageFolder? folder = null;
        if (startDir is not null && Directory.Exists(startDir)) folder = await top.StorageProvider.TryGetFolderFromPathAsync(startDir);
        var res = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = folder });
        return res.Count > 0 ? res[0].TryGetLocalPath() : null;
    }
}
