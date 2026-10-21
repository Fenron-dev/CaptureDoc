using System.Text.Json.Serialization;

namespace CaptureDoc.Core.Model;

public enum ClipKind { Video, Slide }

/// <summary>
/// Multi-track video edit (non-destructive). Track 0 is the main track (bottom), higher tracks lie on top
/// (picture-in-picture, overlays). All times are sequence seconds; the source files are never changed.
/// Annotations of the <see cref="VideoOverlay"/> use sequence time as well.
/// </summary>
public sealed class VideoSequence
{
    public int Width { get; set; }
    public int Height { get; set; }
    public double Fps { get; set; } = 30;
    public ColorRgba Background { get; set; } = ColorRgba.Black;
    /// <summary>Bottom → top.</summary>
    public List<VideoTrack> Tracks { get; set; } = new();

    [JsonIgnore] public double Duration => Tracks.SelectMany(t => t.Clips).Select(c => c.End).DefaultIfEmpty(0).Max();
    [JsonIgnore] public IEnumerable<SequenceClip> AllClips => Tracks.SelectMany(t => t.Clips);

    public SequenceClip? Find(Guid id) => AllClips.FirstOrDefault(c => c.Id == id);
    public VideoTrack? TrackOf(Guid clipId) => Tracks.FirstOrDefault(t => t.Clips.Any(c => c.Id == clipId));
}

public sealed class VideoTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Video";
    public bool Hidden { get; set; }
    public bool Muted { get; set; }
    public List<SequenceClip> Clips { get; set; } = new();
}

/// <summary>A part of a source video (or a slide) placed on a track.</summary>
public sealed class SequenceClip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Clip";
    public ClipKind Kind { get; set; } = ClipKind.Video;
    /// <summary>Video file (relative to the project file, portable or absolute).</summary>
    public string Path { get; set; } = "";
    public double SourceIn { get; set; }
    public double SourceOut { get; set; }
    /// <summary>Length of the whole source file (limits trimming).</summary>
    public double SourceDuration { get; set; }
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }
    public bool HasAudio { get; set; }
    /// <summary>Position on the sequence timeline.</summary>
    public double Start { get; set; }
    /// <summary>Slide content (Kind = Slide); its layer times are relative to the clip start.</summary>
    public VideoSlide? Slide { get; set; }
    /// <summary>Placement on the output canvas; null = fit into the whole canvas.</summary>
    public RectD? Rect { get; set; }
    public double Opacity { get; set; } = 1;
    public double Volume { get; set; } = 1;
    public bool Muted { get; set; }

    [JsonIgnore] public double Duration => Kind == ClipKind.Slide ? Slide?.Duration ?? 0 : Math.Max(0, SourceOut - SourceIn);
    [JsonIgnore] public double End => Start + Duration;
    public bool Contains(double t) => t >= Start - 1e-9 && t < End - 1e-9;
    /// <summary>Source time shown at sequence time <paramref name="t"/>.</summary>
    public double SourceTime(double t) => SourceIn + Math.Clamp(t - Start, 0, Duration);
}

/// <summary>Editing operations on a sequence that keep annotations and zoom in sync with the content.</summary>
public static class SequenceOps
{
    private const double Eps = 0.02;

    /// <summary>Visible clips at t, bottom → top.</summary>
    public static List<(int Track, SequenceClip Clip)> ClipsAt(VideoSequence s, double t)
    {
        var list = new List<(int, SequenceClip)>();
        for (var i = 0; i < s.Tracks.Count; i++)
        {
            if (s.Tracks[i].Hidden) continue;
            foreach (var c in s.Tracks[i].Clips.Where(c => c.Contains(t))) list.Add((i, c));
        }
        return list;
    }

    /// <summary>Rectangle of a clip on the canvas (aspect kept, centred when no explicit rect).</summary>
    public static RectD Placement(VideoSequence s, SequenceClip c)
    {
        if (c.Rect is { } r) return r;
        if (c.Kind == ClipKind.Slide || c.SourceWidth <= 0 || c.SourceHeight <= 0) return new RectD(0, 0, s.Width, s.Height);
        var f = Math.Min(s.Width / (double)c.SourceWidth, s.Height / (double)c.SourceHeight);
        var w = c.SourceWidth * f; var h = c.SourceHeight * f;
        return new RectD((s.Width - w) / 2, (s.Height - h) / 2, w, h);
    }

    /// <summary>Splits a video clip at t into two clips. Returns the right part, or null if t is not inside.</summary>
    public static SequenceClip? Split(VideoSequence s, SequenceClip clip, double t)
    {
        if (clip.Kind != ClipKind.Video || t <= clip.Start + Eps || t >= clip.End - Eps) return null;
        var track = s.TrackOf(clip.Id);
        if (track is null) return null;
        var cut = clip.SourceTime(t);
        var right = Clone(clip);
        right.Id = Guid.NewGuid();
        right.SourceIn = cut; right.Start = t;
        clip.SourceOut = cut;
        track.Clips.Insert(track.Clips.IndexOf(clip) + 1, right);
        return right;
    }

    /// <summary>Splits every clip under t (all visible tracks). Returns the new right parts.</summary>
    public static List<SequenceClip> SplitAll(VideoSequence s, double t) =>
        s.AllClips.Where(c => c.Contains(t)).ToList().Select(c => Split(s, c, t)).OfType<SequenceClip>().ToList();

    /// <summary>
    /// Removes [a, b] from the whole edit and closes the gap: clips are cut and moved, annotations inside are removed,
    /// annotations across the range are shortened, everything after moves left. The source files are not touched.
    /// </summary>
    public static void RippleDelete(VideoSequence s, VideoOverlay? overlay, double a, double b)
    {
        if (b - a < Eps) return;
        foreach (var track in s.Tracks)
        {
            foreach (var c in track.Clips.Where(c => c.Contains(a)).ToList()) Split(s, c, a);
            foreach (var c in track.Clips.Where(c => c.Contains(b)).ToList()) Split(s, c, b);
            track.Clips.RemoveAll(c => c.Start >= a - Eps && c.End <= b + Eps);
            foreach (var c in track.Clips.Where(c => c.Start >= b - Eps)) c.Start = Math.Max(a, c.Start - (b - a));
        }
        if (overlay is null) return;
        double Map(double x) => x <= a ? x : x >= b ? x - (b - a) : a;
        overlay.Page.Layers.RemoveAll(l => l.Timing is { } tm && tm.EndSeconds > tm.StartSeconds && tm.StartSeconds >= a - Eps && tm.EndSeconds <= b + Eps);
        foreach (var l in overlay.Page.Layers)
        {
            if (l.Timing is not { } tm) continue;
            var untilEnd = tm.EndSeconds <= tm.StartSeconds;
            tm.StartSeconds = Math.Round(Map(tm.StartSeconds), 3);
            if (!untilEnd) tm.EndSeconds = Math.Round(Math.Max(tm.StartSeconds + 0.05, Map(tm.EndSeconds)), 3);
            foreach (var k in tm.Keyframes) k.TimeSeconds = Math.Round(Map(k.TimeSeconds), 3);
        }
        overlay.Zoom.RemoveAll(z => z.TimeSeconds > a + Eps && z.TimeSeconds < b - Eps);
        foreach (var z in overlay.Zoom) z.TimeSeconds = Math.Round(Map(z.TimeSeconds), 3);
    }

    /// <summary>
    /// Makes room of <paramref name="duration"/> seconds at t on all tracks (clips are split and moved right).
    /// Annotations visible across t are split into a part before and after the gap.
    /// </summary>
    public static void RippleInsert(VideoSequence s, VideoOverlay? overlay, double t, double duration)
    {
        if (duration <= 0) return;
        SplitAll(s, t);
        foreach (var c in s.AllClips.Where(c => c.Start >= t - Eps)) c.Start += duration;
        if (overlay is null) return;
        foreach (var l in overlay.Page.Layers.ToList())
        {
            if (l.Timing is not { } tm) continue;
            var untilEnd = tm.EndSeconds <= tm.StartSeconds;
            if (tm.StartSeconds >= t - Eps)
            {
                tm.StartSeconds += duration;
                if (!untilEnd) tm.EndSeconds += duration;
                foreach (var k in tm.Keyframes) k.TimeSeconds += duration;
            }
            else if (!untilEnd && tm.EndSeconds > t + Eps && tm.Keyframes.Count == 0)
            {
                var after = Serialization.ProjectJson.Clone(l);
                after.Id = Guid.NewGuid();
                after.Timing!.StartSeconds = t + duration;
                after.Timing.EndSeconds = tm.EndSeconds + duration;
                tm.EndSeconds = t;
                overlay.Page.Layers.Insert(overlay.Page.Layers.IndexOf(l) + 1, after);
            }
        }
        foreach (var z in overlay.Zoom.Where(z => z.TimeSeconds >= t - Eps)) z.TimeSeconds += duration;
    }

    /// <summary>Annotations lying completely inside a clip (they move with it).</summary>
    public static List<Layer> LinkedAnnotations(VideoOverlay overlay, SequenceClip c) =>
        overlay.Page.Layers.Where(l => l.Timing is { } tm && tm.EndSeconds > tm.StartSeconds && tm.StartSeconds >= c.Start - Eps && tm.EndSeconds <= c.End + Eps).ToList();

    public static void ShiftAnnotations(IEnumerable<Layer> layers, double delta)
    {
        foreach (var l in layers)
        {
            if (l.Timing is not { } tm) continue;
            tm.StartSeconds += delta; tm.EndSeconds += delta;
            foreach (var k in tm.Keyframes) k.TimeSeconds += delta;
        }
    }

    /// <summary>Moves the left edge (trim in) to sequence time <paramref name="newStart"/>.</summary>
    public static void TrimStart(SequenceClip c, double newStart)
    {
        var end = c.End;
        if (c.Kind == ClipKind.Slide && c.Slide is { } sl)
        {
            newStart = Math.Clamp(newStart, 0, end - 0.2);
            sl.Duration = end - newStart;
            c.Start = newStart;
            return;
        }
        var minStart = c.Start - c.SourceIn;                    // cannot go before the source start
        newStart = Math.Clamp(newStart, Math.Max(0, minStart), end - 0.1);
        c.SourceIn += newStart - c.Start;
        c.Start = newStart;
    }

    /// <summary>Moves the right edge (trim out) to sequence time <paramref name="newEnd"/>.</summary>
    public static void TrimEnd(SequenceClip c, double newEnd)
    {
        if (c.Kind == ClipKind.Slide && c.Slide is { } sl) { sl.Duration = Math.Max(0.2, newEnd - c.Start); return; }
        var max = c.SourceDuration > 0 ? c.SourceDuration : double.MaxValue;
        c.SourceOut = Math.Clamp(c.SourceIn + (newEnd - c.Start), c.SourceIn + 0.1, max);
    }

    /// <summary>A whole video as the first clip of a new sequence.</summary>
    public static VideoSequence ForVideo(string path, double duration, int width, int height, double fps, bool hasAudio, string name)
    {
        var s = new VideoSequence { Width = Math.Max(2, width & ~1), Height = Math.Max(2, height & ~1), Fps = fps > 0 ? Math.Min(60, fps) : 30 };
        s.Tracks.Add(new VideoTrack { Name = "Video 1" });
        s.Tracks[0].Clips.Add(new SequenceClip
        {
            Name = name, Path = path, SourceIn = 0, SourceOut = duration, SourceDuration = duration,
            SourceWidth = width, SourceHeight = height, HasAudio = hasAudio, Start = 0,
        });
        return s;
    }

    /// <summary>
    /// Converts a 0.2–0.4 video step (trim + cuts + slides on source times) into a sequence and moves the
    /// annotation and zoom times from source time to sequence time. Nothing is lost except what was cut away.
    /// </summary>
    public static VideoSequence FromLegacy(VideoRef v, double sourceDuration, int width, int height, double fps, bool hasAudio, string name)
    {
        var s = ForVideo(v.Path, sourceDuration, width, height, fps, hasAudio, name);
        var main = s.Tracks[0];
        var clip = main.Clips[0];
        var start = Math.Clamp(v.Start, 0, sourceDuration);
        var end = v.End > start ? Math.Min(v.End, sourceDuration) : sourceDuration;
        clip.SourceIn = start; clip.SourceOut = end;
        var overlay = v.Overlay;
        // annotations were on source time; the trimmed start becomes 0
        if (overlay is not null)
        {
            foreach (var l in overlay.Page.Layers)
                if (l.Timing is { } tm)
                {
                    var untilEnd = tm.EndSeconds <= tm.StartSeconds;
                    tm.StartSeconds -= start;
                    if (!untilEnd) tm.EndSeconds -= start;
                    foreach (var k in tm.Keyframes) k.TimeSeconds -= start;
                }
            foreach (var z in overlay.Zoom) z.TimeSeconds -= start;
            overlay.Page.Layers.RemoveAll(l => l.Timing is { } tm && tm.EndSeconds > tm.StartSeconds && (tm.EndSeconds <= 0 || tm.StartSeconds >= end - start));
            foreach (var l in overlay.Page.Layers)
                if (l.Timing is { } tm) { tm.StartSeconds = Math.Max(0, tm.StartSeconds); if (tm.EndSeconds > end - start) tm.EndSeconds = end - start; }
        }
        // cuts (source time) from the back, so earlier positions stay valid
        foreach (var cut in v.Cuts.OrderByDescending(c => c.From))
        {
            var a = Math.Clamp(cut.From, start, end) - start;
            var b = Math.Clamp(cut.To, start, end) - start;
            RippleDelete(s, overlay, a, b);
        }
        // old slides were inserted before a source time
        if (overlay is not null)
        {
            foreach (var sl in overlay.Slides.OrderByDescending(x => x.At))
            {
                var at = OutputTime(v, sl.At);
                RippleInsert(s, overlay, at, sl.Duration);
                main.Clips.Add(new SequenceClip { Name = sl.Name, Kind = ClipKind.Slide, Slide = sl, Start = at });
            }
            overlay.Slides.Clear();
        }
        main.Clips.Sort((x, y) => x.Start.CompareTo(y.Start));
        return s;
    }

    /// <summary>Output time of a source time for the old trim/cut model.</summary>
    public static double OutputTime(VideoRef v, double sourceTime)
    {
        var t = Math.Clamp(sourceTime, v.Start, v.End);
        double kept = 0, pos = v.Start;
        foreach (var c in v.Cuts.Where(c => c.To > v.Start && c.From < v.End).OrderBy(c => c.From))
        {
            if (c.From > pos) kept += Math.Max(0, Math.Min(c.From, t) - pos);
            pos = Math.Max(pos, c.To);
            if (pos >= t) return kept;
        }
        return kept + Math.Max(0, t - pos);
    }

    private static SequenceClip Clone(SequenceClip c) => Serialization.ProjectJson.Clone(c);
}
