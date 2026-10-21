using CaptureDoc.App.Editor;
using CaptureDoc.App.Services;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Serialization;

namespace CaptureDoc.App.Video;

/// <summary>Files of a project: list, re-link missing ones, collect everything into a folder next to the project.</summary>
public static class ProjectMediaService
{
    public sealed record Entry(MediaReference Ref, string? Resolved, long Size)
    {
        public bool Missing => Resolved is null;
        public string Name => Path.GetFileName(Resolved ?? Ref.Stored.Replace('\\', '/').Split('/')[^1]);
    }

    public static List<Entry> List(AppServices app, ProjectSession s) =>
        ProjectMedia.Collect(s.Project).Select(r =>
        {
            var res = ProjectFile.ResolveMedia(s.FilePath, r.Stored, app.Env.FromPortablePath);
            return new Entry(r, res, res is null ? 0 : new FileInfo(res).Length);
        }).ToList();

    public static int Relink(AppServices app, ProjectSession s, string oldStored, string newFile)
    {
        var n = ProjectMedia.Relink(s.Project, oldStored, VideoStepRenderer.Store(app, s, newFile));
        if (n > 0) s.MarkDirty();
        return n;
    }

    /// <summary>Folder the files are collected into: "&lt;project&gt;-Medien" next to the project file.</summary>
    public static string? CollectFolder(ProjectSession s) =>
        s.FilePath is null ? null : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(s.FilePath))!, Path.GetFileNameWithoutExtension(s.FilePath) + "-Medien");

    /// <summary>
    /// Copies all referenced files that lie outside the project folder into the media folder and points the project at the copies,
    /// so the project folder can be passed on as a whole. The originals stay where they are.
    /// </summary>
    public static async Task<(int Copied, int AlreadyInside, int Missing)> CollectAsync(AppServices app, ProjectSession s, IProgress<string>? progress = null)
    {
        var folder = CollectFolder(s) ?? throw new InvalidOperationException("Bitte das Projekt zuerst speichern – dann weiß CaptureDoc, wohin die Dateien gehören.");
        var projDir = Path.GetDirectoryName(Path.GetFullPath(s.FilePath!))! + Path.DirectorySeparatorChar;
        int copied = 0, inside = 0, missing = 0;
        foreach (var e in List(app, s))
        {
            if (e.Resolved is null) { missing++; continue; }
            if (Path.GetFullPath(e.Resolved).StartsWith(projDir, StringComparison.OrdinalIgnoreCase)) { inside++; continue; }
            Directory.CreateDirectory(folder);
            var name = Core.Obsidian.SafeFileName.MakeUnique(Path.GetFileName(e.Resolved), n => File.Exists(Path.Combine(folder, n)), slugStyle: false);
            var dest = Path.Combine(folder, name);
            progress?.Report($"Kopiere {Path.GetFileName(e.Resolved)} …");
            await Task.Run(() => File.Copy(e.Resolved, dest));
            ProjectMedia.Relink(s.Project, e.Ref.Stored, ProjectFile.ToProjectRelative(s.FilePath, dest, app.Env.ToPortablePath));
            copied++;
        }
        if (copied > 0) s.MarkDirty();
        return (copied, inside, missing);
    }
}
