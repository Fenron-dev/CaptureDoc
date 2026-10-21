namespace CaptureDoc.Core.Model;

/// <summary>A media file referenced by a project and where it is used.</summary>
public sealed record MediaReference(string Stored, List<string> UsedBy);

/// <summary>Lists and re-targets the video files a project refers to (videos are referenced, not embedded).</summary>
public static class ProjectMedia
{
    public static List<MediaReference> Collect(CaptureProject p)
    {
        var map = new Dictionary<string, MediaReference>(StringComparer.OrdinalIgnoreCase);
        void Add(string stored, string? user)
        {
            if (string.IsNullOrWhiteSpace(stored)) return;
            if (!map.TryGetValue(stored, out var r)) map[stored] = r = new MediaReference(stored, new List<string>());
            if (user is not null && !r.UsedBy.Contains(user)) r.UsedBy.Add(user);
        }
        foreach (var s in p.Guide.Steps)
        {
            if (s.Video is not { } v) continue;
            var step = string.IsNullOrWhiteSpace(s.Title) ? "Schritt" : $"Schritt „{s.Title}“";
            if (v.Sequence is { } seq)
            {
                foreach (var c in seq.AllClips.Where(c => c.Kind == ClipKind.Video)) Add(c.Path, $"{step}: Clip „{c.Name}“");
                foreach (var m in seq.Media) Add(m.Path, null);
                if (!seq.AllClips.Any(c => c.Kind == ClipKind.Video && string.Equals(c.Path, v.Path, StringComparison.OrdinalIgnoreCase))) Add(v.Path, null);
            }
            else Add(v.Path, step);
        }
        return map.Values.OrderBy(r => r.Stored, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Replaces a stored path everywhere (main video, clips, media list). Returns the number of changes.</summary>
    public static int Relink(CaptureProject p, string oldStored, string newStored)
    {
        var n = 0;
        bool Same(string a) => string.Equals(a, oldStored, StringComparison.OrdinalIgnoreCase);
        foreach (var v in p.Guide.Steps.Select(s => s.Video).OfType<VideoRef>())
        {
            if (Same(v.Path)) { v.Path = newStored; n++; }
            if (v.Sequence is not { } seq) continue;
            foreach (var c in seq.AllClips.Where(c => Same(c.Path))) { c.Path = newStored; n++; }
            foreach (var m in seq.Media.Where(m => Same(m.Path))) { m.Path = newStored; n++; }
        }
        return n;
    }
}
