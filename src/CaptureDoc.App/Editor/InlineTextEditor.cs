using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CaptureDoc.App.Util;
using CaptureDoc.Core.Model;
using CaptureDoc.Rendering;

namespace CaptureDoc.App.Editor;

/// <summary>
/// Edits a text layer in place: a TextBox is placed exactly over the layer on an overlay canvas
/// that lies on top of the <see cref="EditorCanvas"/> (same origin). The whole edit is one undo step.
/// </summary>
public sealed class InlineTextEditor
{
    private readonly EditorCanvas _canvas;
    private readonly Canvas _overlay;
    private TextBox? _tb;

    public InlineTextEditor(EditorCanvas canvas, Canvas overlay) { _canvas = canvas; _overlay = overlay; }

    public bool IsActive => _tb is not null;
    public bool IsFocused => _tb?.IsFocused == true;

    public void Begin(TextLayer t)
    {
        Commit();
        _canvas.Select(new[] { t.Id });
        _canvas.EditingTextLayer = t.Id;
        _canvas.NotifyDocumentChanged();
        var r = _canvas.ToView(t.Rect.Normalize());
        var s = _canvas.ViewScale;
        var tb = new TextBox
        {
            Text = t.Text, AcceptsReturn = true, TextWrapping = t.Style == TextStyle.Plain ? TextWrapping.NoWrap : TextWrapping.Wrap,
            FontSize = Math.Max(8, t.FontSize * s), FontFamily = new FontFamily(t.FontFamily),
            FontWeight = t.Bold ? FontWeight.Bold : FontWeight.Normal, FontStyle = t.Italic ? FontStyle.Italic : FontStyle.Normal,
            Foreground = new SolidColorBrush(t.Foreground.ToAvalonia()),
            Background = new SolidColorBrush(t.Background.IsTransparent ? Color.FromArgb(200, 255, 255, 255) : t.Background.ToAvalonia()),
            MinWidth = Math.Max(80, r.Width), MinHeight = Math.Max(24, r.Height), Padding = new Avalonia.Thickness(t.Padding * s),
        };
        Canvas.SetLeft(tb, r.X); Canvas.SetTop(tb, r.Y);
        _overlay.Children.Add(tb);
        _tb = tb;
        _canvas.History.BeginTransaction("Text bearbeiten");
        tb.TextChanged += (_, _) => { t.Text = tb.Text ?? ""; };
        tb.LostFocus += (_, _) => Commit();
        // focus after the canvas finished its pointer handling
        Dispatcher.UIThread.Post(() => { tb.Focus(); tb.SelectAll(); }, DispatcherPriority.Loaded);
    }

    public void Commit()
    {
        if (_tb is null) return;
        var tb = _tb; _tb = null;
        _overlay.Children.Remove(tb);
        if (_canvas.EditingTextLayer is { } id && _canvas.Page.Find(id) is TextLayer t)
        {
            if (t.Style == TextStyle.Plain) t.Rect = t.Rect.Normalize() with { Width = 0 }; // plain text: auto width, no wrapping
            var fit = TextLayout.FitBox(t);
            t.Rect = t.Style == TextStyle.Plain ? fit : t.Rect.Normalize() with { Height = Math.Max(fit.Height, t.Rect.Normalize().Height) };
        }
        _canvas.EditingTextLayer = null;
        _canvas.History.Commit();
        _canvas.NotifyDocumentChanged();
        _canvas.Focus();
    }
}
