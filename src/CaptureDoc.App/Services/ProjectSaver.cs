using Avalonia.Controls;
using CaptureDoc.App.Editor;
using CaptureDoc.App.Views;
using CaptureDoc.Core.Serialization;

namespace CaptureDoc.App.Services;

/// <summary>Save/Save-as with redaction warning and conflict handling (shared by editor and studio).</summary>
public static class ProjectSaver
{
    public static async Task<bool> SaveAsync(Window owner, AppServices app, ProjectSession session, bool saveAs)
    {
        if (session.HasRedactions && app.Settings.Shared.WarnOnRedactedProjectSave)
        {
            var a = await Dialogs.AskAsync(owner, "Hinweis zu Schwärzungen",
                "Dieses Projekt enthält Schwärzungen. In der Projektdatei (.capturedoc) bleibt das Originalbild darunter vollständig erhalten, " +
                "damit du weiter bearbeiten kannst.\n\nGib die Projektdatei deshalb nicht weiter, wenn die geschwärzten Daten vertraulich sind. " +
                "Zum Teilen das exportierte Bild verwenden – dort ist die Schwärzung eingebrannt und nicht wiederherstellbar.",
                "Verstanden, speichern", "Nicht mehr anzeigen", "Abbrechen");
            if (a is null or "Abbrechen") return false;
            if (a == "Nicht mehr anzeigen") { app.Settings.Shared.WarnOnRedactedProjectSave = false; app.SaveSettings(); }
        }
        var path = session.FilePath;
        if (saveAs || path is null || session.ReadOnly)
        {
            var name = Core.Obsidian.SafeFileName.ToNoteName(session.Project.Title);
            path = await Dialogs.SaveFileAsync(owner, "Projekt speichern", name + ProjectFile.Extension, app.Env.ProjectsDir, ("CaptureDoc-Projekt", "*" + ProjectFile.Extension));
            if (path is null) return false;
        }
        try
        {
            session.Save(path);
            Log.Info("Project saved");
            return true;
        }
        catch (ProjectConflictException ex)
        {
            var a = await Dialogs.AskAsync(owner, "Konflikt beim Speichern", ex.Message + "\n\nEin anderes Programm oder ein anderer Rechner hat die Datei inzwischen gespeichert.", "Als Kopie speichern", "Trotzdem überschreiben", "Abbrechen");
            if (a == "Trotzdem überschreiben") { session.Save(path, force: true); return true; }
            if (a == "Als Kopie speichern") return await SaveAsync(owner, app, session, true);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectFormatException or InvalidOperationException)
        {
            Log.Error("Save failed", ex);
            await Dialogs.InfoAsync(owner, "Speichern fehlgeschlagen", ex.Message + "\n\nDie vorherige Datei wurde nicht verändert.");
            return false;
        }
    }
}
