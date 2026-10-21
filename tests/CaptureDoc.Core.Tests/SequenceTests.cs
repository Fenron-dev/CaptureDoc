using CaptureDoc.Core.Model;

namespace CaptureDoc.Core.Tests;

public class SequenceTests
{
    private static VideoSequence TenSeconds() => SequenceOps.ForVideo("a.mkv", 10, 1280, 720, 30, true, "A");

    private static Layer Ann(string name, double s, double e) =>
        new ShapeLayer { Name = name, Rect = new RectD(0, 0, 10, 10), Timing = new LayerTiming { StartSeconds = s, EndSeconds = e } };

    [Fact]
    public void Split_cuts_a_clip_into_two_without_changing_the_result()
    {
        var s = TenSeconds();
        var a = s.Tracks[0].Clips[0];
        var b = SequenceOps.Split(s, a, 4)!;
        Assert.Equal(2, s.Tracks[0].Clips.Count);
        Assert.Equal((0.0, 4.0, 0.0), (a.SourceIn, a.SourceOut, a.Start));
        Assert.Equal((4.0, 10.0, 4.0), (b.SourceIn, b.SourceOut, b.Start));
        Assert.Equal(10, s.Duration, 6);
        Assert.Null(SequenceOps.Split(s, a, 4));            // at the edge: nothing to split
        Assert.Equal(6.5, b.SourceTime(6.5), 6);
    }

    [Fact]
    public void Ripple_delete_removes_the_part_and_keeps_annotations_in_sync()
    {
        var s = TenSeconds();
        var overlay = new VideoOverlay();
        overlay.Page.Layers.AddRange(new[] { Ann("vorher", 1, 2), Ann("drin", 4.5, 5.5), Ann("über", 3, 7), Ann("danach", 8, 9), Ann("bis Ende", 6, 0) });
        overlay.Zoom.Add(new ZoomKeyframe { TimeSeconds = 5 });
        overlay.Zoom.Add(new ZoomKeyframe { TimeSeconds = 8 });
        SequenceOps.RippleDelete(s, overlay, 4, 6);         // remove 4..6
        var clips = s.Tracks[0].Clips;
        Assert.Equal(2, clips.Count);
        Assert.Equal((0.0, 4.0, 0.0), (clips[0].SourceIn, clips[0].SourceOut, clips[0].Start));
        Assert.Equal((6.0, 10.0, 4.0), (clips[1].SourceIn, clips[1].SourceOut, clips[1].Start));
        Assert.Equal(8, s.Duration, 6);
        var byName = overlay.Page.Layers.ToDictionary(l => l.Name, l => (l.Timing!.StartSeconds, l.Timing.EndSeconds));
        Assert.False(byName.ContainsKey("drin"));             // belonged to the removed part
        Assert.Equal((1.0, 2.0), byName["vorher"]);
        Assert.Equal((3.0, 5.0), byName["über"]);             // shortened by the removed 2 s
        Assert.Equal((6.0, 7.0), byName["danach"]);           // moved left
        Assert.Equal(4.0, byName["bis Ende"].StartSeconds);
        Assert.Equal(new[] { 6.0 }, overlay.Zoom.Select(z => z.TimeSeconds));
    }

    [Fact]
    public void Ripple_insert_makes_room_and_splits_annotations_across_the_point()
    {
        var s = TenSeconds();
        var overlay = new VideoOverlay();
        overlay.Page.Layers.AddRange(new[] { Ann("über", 2, 6), Ann("danach", 7, 8) });
        SequenceOps.RippleInsert(s, overlay, 4, 3);
        var clips = s.Tracks[0].Clips;
        Assert.Equal((4.0, 7.0), (clips[0].End, clips[1].Start));
        Assert.Equal(13, s.Duration, 6);
        var t = overlay.Page.Layers.Select(l => (l.Name, l.Timing!.StartSeconds, l.Timing.EndSeconds)).ToList();
        Assert.Contains(("über", 2.0, 4.0), t);
        Assert.Contains(("über", 7.0, 9.0), t);
        Assert.Contains(("danach", 10.0, 11.0), t);
    }

    [Fact]
    public void Trimming_respects_the_source_limits()
    {
        var s = TenSeconds();
        var b = SequenceOps.Split(s, s.Tracks[0].Clips[0], 5)!;
        SequenceOps.TrimStart(b, 3);                          // would need source 3..5 → allowed
        Assert.Equal((3.0, 3.0), (b.Start, b.SourceIn));
        SequenceOps.TrimStart(b, -5);                         // cannot go before source 0 / sequence 0
        Assert.Equal((0.0, 0.0), (b.Start, b.SourceIn));
        SequenceOps.TrimEnd(b, 99);
        Assert.Equal(10, b.SourceOut, 6);                     // not beyond the source end
    }

    [Fact]
    public void Clip_moving_carries_its_annotations()
    {
        var s = TenSeconds();
        var overlay = new VideoOverlay();
        var inside = Ann("drin", 6, 7); var outside = Ann("außen", 1, 2);
        overlay.Page.Layers.AddRange(new[] { inside, outside });
        var b = SequenceOps.Split(s, s.Tracks[0].Clips[0], 5)!;
        var linked = SequenceOps.LinkedAnnotations(overlay, b);
        Assert.Equal(new[] { inside }, linked);
        SequenceOps.ShiftAnnotations(linked, 2);
        Assert.Equal(8, inside.Timing!.StartSeconds);
        Assert.Equal(1, outside.Timing!.StartSeconds);
    }

    [Fact]
    public void Legacy_trim_cuts_and_slides_become_a_sequence()
    {
        var page = new Page { Width = 1280, Height = 720 };
        page.Layers.AddRange(new[] { Ann("a", 3, 4), Ann("im Schnitt", 5.2, 5.8), Ann("b", 8, 9) });
        var v = new VideoRef
        {
            Path = "a.mkv", Start = 1, End = 10, Cuts = { new TimeCut { From = 5, To = 6 } },
            Overlay = new VideoOverlay { Page = page, Slides = { new VideoSlide { Name = "Titel", At = 1, Duration = 2 } } },
        };
        Assert.Equal(4, SequenceOps.OutputTime(v, 5), 6);
        Assert.Equal(4, SequenceOps.OutputTime(v, 6), 6);
        var s = SequenceOps.FromLegacy(v, 12, 1280, 720, 30, true, "A");
        var main = s.Tracks[0].Clips;
        Assert.Equal(new[] { ClipKind.Slide, ClipKind.Video, ClipKind.Video }, main.Select(c => c.Kind));
        Assert.Equal((0.0, 2.0), (main[0].Start, main[0].Duration));          // title slide first
        Assert.Equal((1.0, 5.0, 2.0), (main[1].SourceIn, main[1].SourceOut, main[1].Start));
        Assert.Equal((6.0, 10.0, 6.0), (main[2].SourceIn, main[2].SourceOut, main[2].Start));
        Assert.Equal(10, s.Duration, 6);                                       // 4 + 4 kept + 2 slide
        var t = page.Layers.ToDictionary(l => l.Name, l => l.Timing!.StartSeconds);
        Assert.False(t.ContainsKey("im Schnitt"));
        Assert.Equal(4.0, t["a"], 6);      // source 3 → kept 2 → +2 slide
        Assert.Equal(8.0, t["b"], 6);      // source 8 → kept 6 → +2 slide
        Assert.Empty(v.Overlay.Slides);
    }

    [Fact]
    public void Placement_fits_the_source_into_the_canvas()
    {
        var s = TenSeconds();
        var pip = new SequenceClip { SourceWidth = 640, SourceHeight = 480 };
        var r = SequenceOps.Placement(s, pip);
        Assert.Equal((720.0, 960.0), (r.Height, r.Width));        // 4:3 in 16:9 → pillarbox
        Assert.Equal(160, r.X, 6);
    }

    [Fact]
    public void Project_media_lists_every_file_and_relinks_all_references()
    {
        var p = new CaptureProject();
        var seq = SequenceOps.ForVideo("a.mkv", 10, 1280, 720, 30, true, "A");
        seq.Tracks[0].Clips.Add(new SequenceClip { Name = "B-Teil", Path = "b.mkv", SourceOut = 3, Start = 10 });
        seq.Media.Add(new MediaItem { Path = "unused.mkv", Name = "unused" });
        p.Guide.Steps.Add(new GuideStep { Title = "Video", Video = new VideoRef { Path = "a.mkv", Sequence = seq } });
        p.Guide.Steps.Add(new GuideStep { Title = "Alt", Video = new VideoRef { Path = "old.mp4" } });
        seq.SyncMedia();
        Assert.Contains(seq.Media, m => m.Path == "b.mkv");                     // clip source added to the media list
        var refs = ProjectMedia.Collect(p);
        Assert.Equal(new[] { "a.mkv", "b.mkv", "old.mp4", "unused.mkv" }, refs.Select(r => r.Stored));
        Assert.Empty(refs.Single(r => r.Stored == "unused.mkv").UsedBy);
        Assert.Contains("Schritt „Alt“", refs.Single(r => r.Stored == "old.mp4").UsedBy);
        Assert.Equal(3, ProjectMedia.Relink(p, "a.mkv", "Medien/a.mkv"));          // main video, clip, media item
        Assert.Equal("Medien/a.mkv", p.Guide.Steps[0].Video!.Path);
        Assert.Equal("Medien/a.mkv", seq.Tracks[0].Clips[0].Path);
        Assert.Equal(1, seq.UseCount(seq.Media.Single(m => m.Path == "Medien/a.mkv")));
    }
}
