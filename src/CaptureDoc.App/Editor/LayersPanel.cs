using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Avalonia.Media;
using CaptureDoc.App.Util;
using CaptureDoc.Core.Model;

using Page = CaptureDoc.Core.Model.Page;

namespace CaptureDoc.App.Editor;

/// <summary>Photoshop-style layer list: top layer first, groups indented, visibility/lock toggles, multi-select.</summary>
public sealed class LayersPanel : DockPanel
{
    private readonly EditorCanvas _canvas;
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Multiple };
    private readonly Slider _opacity = new() { Minimum = 0, Maximum = 100, Width = 120 };
    private bool _syncing;

    private sealed record Row(Layer Layer, int Depth);

    public LayersPanel(EditorCanvas canvas)
    {
        _canvas = canvas;
        var header = Ui.Label("Ebenen", 14, bold: true);
        header.Margin = new Thickness(4, 4, 4, 6);
        SetDock(header, Dock.Top);
        Children.Add(header);

        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Small("▲", "Nach vorne (Strg+])", () => _canvas.MoveSelection(+1)));
        buttons.Children.Add(Small("▼", "Nach hinten (Strg+[)", () => _canvas.MoveSelection(-1)));
        buttons.Children.Add(Small("⧉", "Duplizieren (Strg+D)", _canvas.DuplicateSelection));
        buttons.Children.Add(Small("▣", "Gruppieren (Strg+G)", _canvas.GroupSelection));
        buttons.Children.Add(Small("▢", "Gruppe auflösen (Strg+Umschalt+G)", _canvas.UngroupSelection));
        buttons.Children.Add(Small("🗑", "Löschen (Entf)", _canvas.DeleteSelection));
        SetDock(buttons, Dock.Bottom);
        Children.Add(buttons);

        var opRow = Ui.Row(Ui.Label("Deckkraft", 12), _opacity);
        opRow.Margin = new Thickness(4, 2);
        SetDock(opRow, Dock.Bottom);
        Children.Add(opRow);
        _opacity.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty || _syncing) return;
            var v = _opacity.Value / 100.0;
            var layers = _canvas.SelectedLayers.Where(l => l is not RedactLayer).ToList();
            if (layers.Count > 0) _canvas.Apply("Deckkraft", () => { foreach (var l in layers) l.Opacity = v; }, "opacity");
        };

        Children.Add(_list);
        _list.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            var ids = _list.SelectedItems?.OfType<Control>().Select(c => ((Row)c.Tag!).Layer.Id).ToList() ?? new();
            _syncing = true;
            try { _canvas.Select(ids); } finally { _syncing = false; }
            SyncOpacity();
        };

        _canvas.DocumentChanged += (_, _) => Rebuild();
        _canvas.SelectionChanged += (_, _) => { if (!_syncing) SyncSelection(); SyncOpacity(); };

        // drag & drop reordering (tunnel + handledEventsToo: the ListBox handles presses for selection)
        _list.AddHandler(PointerPressedEvent, (_, e) => { _dragRow = RowAt(e.GetPosition(_list)); _dragStart = e.GetPosition(_list); }, RoutingStrategies.Tunnel, handledEventsToo: true);
        _list.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (_dragRow is null) return;
            var moved = Distance(e.GetPosition(_list), _dragStart) > 6;
            _list.Cursor = moved ? new Cursor(StandardCursorType.DragMove) : Cursor.Default;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        _list.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            var src = _dragRow; _dragRow = null; _list.Cursor = Cursor.Default;
            if (src is null || Distance(e.GetPosition(_list), _dragStart) <= 6) return;
            if (RowAt(e.GetPosition(_list)) is { } target && target.Layer.Id != src.Layer.Id) MoveTo(src.Layer, target.Layer);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Rebuild();
    }

    private Row? _dragRow;
    private Point _dragStart;
    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private Row? RowAt(Point p)
    {
        var hit = _list.InputHitTest(p) as Visual;
        for (var v = hit; v is not null; v = v.GetVisualParent())
            if (v is Control { Tag: Row r }) return r;
        return null;
    }

    /// <summary>Puts <paramref name="src"/> at the position of <paramref name="target"/> (also into/out of groups).</summary>
    private void MoveTo(Layer src, Layer target)
    {
        var page = _canvas.Page;
        if (src is ImageLayer { IsBackground: true } || src.Locked) return;
        if (src is GroupLayer g && g.AllChildren().Any(c => c.Id == target.Id)) return; // not into itself
        var targetList = page.FindContainer(target.Id);
        var srcList = page.FindContainer(src.Id);
        if (targetList is null || srcList is null) return;
        _canvas.Apply("Reihenfolge ändern", () =>
        {
            srcList.Remove(src);
            var idx = targetList.IndexOf(target);
            // list shows the top layer first: dropping on a row means "take its place" (above when moving up)
            var insert = Math.Clamp(idx + 1, 0, targetList.Count);
            if (ReferenceEquals(targetList, page.Layers) && insert == 0) insert = 1;
            if (targetList.Count > 0 && targetList[0] is ImageLayer { IsBackground: true } && insert < 1) insert = 1;
            targetList.Insert(insert, src);
        });
    }

    private static Button Small(string text, string tip, Action a)
    {
        var b = Ui.Button(text, a, tip);
        b.Padding = new Thickness(8, 3);
        b.MinWidth = 32;
        return b;
    }

    private IEnumerable<Row> Flatten()
    {
        IEnumerable<Row> Walk(List<Layer> list, int depth)
        {
            for (var i = list.Count - 1; i >= 0; i--)
            {
                yield return new Row(list[i], depth);
                if (list[i] is GroupLayer g)
                    foreach (var r in Walk(g.Children, depth + 1)) yield return r;
            }
        }
        return Walk(_canvas.Page.Layers, 0);
    }

    public void Rebuild()
    {
        _syncing = true;
        try
        {
            var items = new List<Control>();
            foreach (var row in Flatten()) items.Add(BuildItem(row));
            _list.ItemsSource = items;
            SyncSelection();
        }
        finally { _syncing = false; }
    }

    private Control BuildItem(Row row)
    {
        var l = row.Layer;
        var eye = new ToggleButton { Content = l.Visible ? "👁" : "–", IsChecked = l.Visible, Padding = new Thickness(4, 0), MinWidth = 26, FontSize = 11 };
        ToolTip.SetTip(eye, "Sichtbarkeit");
        eye.Click += (_, _) => _canvas.Apply(l.Visible ? "Ausblenden" : "Einblenden", () => l.Visible = !l.Visible);
        var lockBtn = new ToggleButton { Content = l.Locked ? "🔒" : "🔓", IsChecked = l.Locked, Padding = new Thickness(4, 0), MinWidth = 26, FontSize = 11 };
        ToolTip.SetTip(lockBtn, "Sperren");
        lockBtn.Click += (_, _) => _canvas.Apply(l.Locked ? "Entsperren" : "Sperren", () => l.Locked = !l.Locked);

        var name = string.IsNullOrWhiteSpace(l.Name) ? l.Kind : l.Name;
        var detail = l switch { StepLayer s => $" #{s.Number}", RedactLayer => " ⚠", _ => "" };
        var text = new TextBlock { Text = name + detail, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = l.Visible ? 1 : 0.5 };
        if (l is RedactLayer) ToolTip.SetTip(text, "Schwärzung: im exportierten Bild eingebrannt. Die Projektdatei enthält weiterhin das Original darunter.");
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(row.Depth * 16, 0, 0, 0) };
        row2.Children.Add(eye); row2.Children.Add(lockBtn); row2.Children.Add(text);
        return new Border { Child = row2, Tag = row, Padding = new Thickness(2) };
    }

    private void SyncSelection()
    {
        var was = _syncing; _syncing = true;
        try
        {
            _list.SelectedItems?.Clear();
            if (_list.ItemsSource is IEnumerable<Control> items)
                foreach (var c in items)
                    if (_canvas.Selection.Contains(((Row)c.Tag!).Layer.Id)) _list.SelectedItems?.Add(c);
        }
        finally { _syncing = was; }
    }

    private void SyncOpacity()
    {
        var was = _syncing; _syncing = true;
        try
        {
            var first = _canvas.SelectedLayers.FirstOrDefault();
            _opacity.IsEnabled = first is not null and not RedactLayer;
            _opacity.Value = (first?.Opacity ?? 1) * 100;
        }
        finally { _syncing = was; }
    }
}
