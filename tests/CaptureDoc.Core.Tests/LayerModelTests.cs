using CaptureDoc.Core.Editing;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Serialization;

namespace CaptureDoc.Core.Tests;

public class LayerModelTests
{
    internal static Page NewPage()
    {
        var p = new Page { Width = 400, Height = 300 };
        p.Layers.Add(new ImageLayer { AssetId = "bg", IsBackground = true, Locked = true, Rect = new RectD(0, 0, 400, 300), Name = "Hintergrund" });
        return p;
    }

    [Fact]
    public void Steps_are_renumbered_in_creation_order_and_manual_numbers_are_kept()
    {
        var p = NewPage();
        var a = new StepLayer { Center = new(10, 10) };
        var b = new StepLayer { Center = new(20, 20), Number = 7, AutoNumber = false };
        var c = new StepLayer { Center = new(30, 30) };
        p.Layers.AddRange(new Layer[] { a, b, c });
        PageOperations.RenumberSteps(p);
        Assert.Equal(1, a.Number);
        Assert.Equal(7, b.Number);
        Assert.Equal(8, c.Number);
    }

    [Fact]
    public void Step_remains_editable_after_creation()
    {
        var s = new StepLayer { Center = new(50, 50), Radius = 20, Number = 1 };
        s.Translate(10, 5);
        s.Fill = ColorRgba.Blue;
        s.SetBounds(new RectD(0, 0, 60, 60));
        s.Number = 4; s.AutoNumber = false;
        Assert.Equal(new PointD(30, 30), s.Center);
        Assert.Equal(30, s.Radius);
        Assert.Equal(ColorRgba.Blue, s.Fill);
        Assert.Equal(4, s.Number);
    }

    [Fact]
    public void Layers_cannot_be_moved_below_the_background()
    {
        var p = NewPage();
        var r = new ShapeLayer { Rect = new RectD(1, 1, 10, 10) };
        p.Layers.Add(r);
        PageOperations.SendToBack(p, r.Id);
        Assert.IsType<ImageLayer>(p.Layers[0]);
        Assert.Same(r, p.Layers[1]);
    }

    [Fact]
    public void Move_changes_order()
    {
        var p = NewPage();
        var a = new ShapeLayer(); var b = new ShapeLayer(); var c = new ShapeLayer();
        p.Layers.AddRange(new Layer[] { a, b, c });
        Assert.True(PageOperations.Move(p, a.Id, +1));
        Assert.Equal(new[] { b.Id, a.Id, c.Id }, p.Layers.Skip(1).Select(l => l.Id));
        PageOperations.BringToFront(p, b.Id);
        Assert.Equal(b.Id, p.Layers[^1].Id);
    }

    [Fact]
    public void Group_and_ungroup_preserve_order_and_opacity()
    {
        var p = NewPage();
        var a = new ShapeLayer { Rect = new RectD(0, 0, 10, 10) };
        var b = new LineLayer { Start = new(20, 20), End = new(40, 40) };
        var c = new TextLayer { Rect = new RectD(50, 50, 30, 20) };
        p.Layers.AddRange(new Layer[] { a, b, c });
        var g = PageOperations.Group(p, new[] { a.Id, b.Id })!;
        Assert.NotNull(g);
        Assert.Equal(3, p.Layers.Count);
        Assert.Same(g, p.Layers[1]);
        Assert.Same(c, p.Layers[2]);
        g.Opacity = 0.5;
        g.Translate(5, 5);
        Assert.Equal(5, a.Rect.X);
        var kids = PageOperations.Ungroup(p, g.Id);
        Assert.Equal(2, kids.Count);
        Assert.Equal(new[] { a.Id, b.Id, c.Id }, p.Layers.Skip(1).Select(l => l.Id));
        Assert.Equal(0.5, a.Opacity, 3);
    }

    [Fact]
    public void Duplicate_creates_new_ids_also_for_group_children()
    {
        var p = NewPage();
        var a = new ShapeLayer(); var b = new ShapeLayer();
        p.Layers.AddRange(new Layer[] { a, b });
        var g = PageOperations.Group(p, new[] { a.Id, b.Id })!;
        var copy = (GroupLayer)PageOperations.Duplicate(p, g.Id)!;
        Assert.NotEqual(g.Id, copy.Id);
        Assert.DoesNotContain(copy.Children, ch => ch.Id == a.Id || ch.Id == b.Id);
        Assert.Equal(p.AllLayers().Count(), p.AllLayers().Select(l => l.Id).Distinct().Count());
    }

    [Fact]
    public void Crop_is_non_destructive_and_shifts_layers()
    {
        var p = NewPage();
        var r = new ShapeLayer { Rect = new RectD(100, 100, 50, 50) };
        var m = new MagnifierLayer { Rect = new RectD(200, 200, 40, 40), Source = new RectD(110, 110, 10, 10) };
        p.Layers.Add(r); p.Layers.Add(m);
        PageOperations.Crop(p, new RectD(50, 60, 200, 150));
        Assert.Equal(200, p.Width); Assert.Equal(150, p.Height);
        Assert.Equal(new RectD(50, 40, 50, 50), r.Rect);
        var bg = (ImageLayer)p.Layers[0];
        Assert.Equal(new RectD(-50, -60, 400, 300), bg.Rect); // original image untouched, only positioned
        Assert.Equal(new RectD(60, 50, 10, 10), m.Source);
    }

    [Fact]
    public void Resize_scales_all_layers()
    {
        var p = NewPage();
        var t = new TextLayer { Rect = new RectD(100, 100, 100, 50), FontSize = 20 };
        p.Layers.Add(t);
        PageOperations.Resize(p, 200, 150);
        Assert.Equal(new RectD(50, 50, 50, 25), t.Rect);
        Assert.Equal(10, t.FontSize);
    }

    [Fact]
    public void HitTest_returns_topmost_and_skips_hidden_locked_and_background()
    {
        var p = NewPage();
        var low = new ShapeLayer { Rect = new RectD(0, 0, 100, 100) };
        var high = new ShapeLayer { Rect = new RectD(0, 0, 100, 100) };
        p.Layers.Add(low); p.Layers.Add(high);
        Assert.Same(high, p.HitTest(new PointD(50, 50), 2));
        high.Visible = false;
        Assert.Same(low, p.HitTest(new PointD(50, 50), 2));
        low.Locked = true;
        Assert.Null(p.HitTest(new PointD(50, 50), 2));
    }

    [Fact]
    public void Arrow_hit_test_uses_distance_to_segment()
    {
        var l = new LineLayer { Start = new(0, 0), End = new(100, 0), StrokeWidth = 4 };
        Assert.True(l.HitTest(new PointD(50, 3), 2));
        Assert.False(l.HitTest(new PointD(50, 30), 2));
    }

    [Fact]
    public void Json_roundtrip_keeps_all_layer_types()
    {
        var p = NewPage();
        p.Layers.Add(new ShapeLayer { Shape = ShapeKind.Ellipse, Stroke = ColorRgba.Blue });
        p.Layers.Add(new LineLayer { Start = new(1, 2), End = new(3, 4) });
        p.Layers.Add(new TextLayer { Text = "Hallo\nWelt", Style = TextStyle.Bubble, TailTip = new PointD(9, 9) });
        p.Layers.Add(new StepLayer { Number = 3 });
        p.Layers.Add(new FreehandLayer { Points = { new(1, 1), new(2, 2) } });
        p.Layers.Add(new HighlightLayer());
        p.Layers.Add(new RedactLayer { Mode = RedactMode.Pixelate });
        p.Layers.Add(new SpotlightLayer { Elliptical = true });
        p.Layers.Add(new MagnifierLayer { Source = new RectD(1, 1, 5, 5) });
        p.Layers.Add(new GroupLayer { Children = { new ShapeLayer(), new StepLayer { Number = 9 } } });
        p.Layers[^1].Timing = new LayerTiming { StartSeconds = 5, EndSeconds = 10, Keyframes = { new Keyframe { TimeSeconds = 5 } } };

        var json = ProjectJson.SerializePage(p);
        var back = ProjectJson.DeserializePage(json);
        Assert.Equal(json, ProjectJson.SerializePage(back));
        Assert.Equal(p.Layers.Select(l => l.GetType()), back.Layers.Select(l => l.GetType()));
        Assert.Equal("Hallo\nWelt", back.Layers.OfType<TextLayer>().Single().Text);
        Assert.Equal(10, back.Layers[^1].Timing!.EndSeconds);
    }
}

public class HistoryTests
{
    private static (Page page, History h) Setup()
    {
        var p = LayerModelTests.NewPage();
        var h = new History(() => ProjectJson.SerializePage(p), s => ProjectJson.RestorePageInto(p, s));
        return (p, h);
    }

    [Fact]
    public void Undo_and_redo_restore_states()
    {
        var (p, h) = Setup();
        h.Execute("Rechteck", () => p.Layers.Add(new ShapeLayer { Rect = new RectD(1, 1, 5, 5) }));
        h.Execute("Pfeil", () => p.Layers.Add(new LineLayer()));
        Assert.Equal(3, p.Layers.Count);
        Assert.True(h.Undo());
        Assert.Equal(2, p.Layers.Count);
        Assert.True(h.Undo());
        Assert.Single(p.Layers);
        Assert.False(h.Undo());
        Assert.True(h.Redo());
        Assert.Equal(2, p.Layers.Count);
        Assert.Equal("Pfeil", h.RedoDescription);
    }

    [Fact]
    public void Noop_is_not_recorded_and_new_action_clears_redo()
    {
        var (p, h) = Setup();
        Assert.False(h.Execute("nichts", () => { }));
        h.Execute("a", () => p.Layers.Add(new ShapeLayer()));
        h.Undo();
        Assert.True(h.CanRedo);
        h.Execute("b", () => p.Layers.Add(new StepLayer()));
        Assert.False(h.CanRedo);
    }

    [Fact]
    public void Transaction_collapses_drag_into_one_step()
    {
        var (p, h) = Setup();
        var s = new ShapeLayer { Rect = new RectD(0, 0, 10, 10) };
        h.Execute("add", () => p.Layers.Add(s));
        h.BeginTransaction("Verschieben");
        for (var i = 0; i < 20; i++) ((ShapeLayer)p.Layers[1]).Translate(1, 0);
        h.Commit();
        Assert.Equal(20, ((ShapeLayer)p.Layers[1]).Rect.X);
        h.Undo();
        Assert.Equal(0, ((ShapeLayer)p.Layers[1]).Rect.X);
        Assert.Equal(new[] { "add" }, h.UndoDescriptions);
    }

    [Fact]
    public void Limit_drops_oldest()
    {
        var p = LayerModelTests.NewPage();
        var h = new History(() => ProjectJson.SerializePage(p), s => ProjectJson.RestorePageInto(p, s), limit: 3);
        for (var i = 0; i < 5; i++) h.Execute($"s{i}", () => p.Layers.Add(new ShapeLayer()));
        Assert.Equal(new[] { "s2", "s3", "s4" }, h.UndoDescriptions);
    }
}
