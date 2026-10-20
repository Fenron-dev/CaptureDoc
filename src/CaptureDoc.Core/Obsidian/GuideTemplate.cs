using System.Text.Json;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Serialization;
using CaptureDoc.Core.Storage;

namespace CaptureDoc.Core.Obsidian;

/// <summary>Obsidian export template: metadata defaults and section structure.</summary>
public sealed class GuideTemplate
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public string DefaultStatus { get; set; } = "entwurf";
    /// <summary>Extra frontmatter properties (value may contain {date}, {title}).</summary>
    public Dictionary<string, string> Properties { get; set; } = new();
    public string PrerequisitesTitle { get; set; } = "Voraussetzungen";
    public CalloutKind PrerequisitesKind { get; set; } = CalloutKind.Info;
    /// <summary>{n} = number, {title} = step title.</summary>
    public string StepHeading { get; set; } = "Schritt {n}: {title}";
    /// <summary>Sections rendered before the steps (heading → placeholder text).</summary>
    public List<string> SectionsBefore { get; set; } = new();
    /// <summary>Sections rendered after the steps.</summary>
    public List<string> SectionsAfter { get; set; } = new();
    public string StepsHeading { get; set; } = "";
    public string ChecklistTitle { get; set; } = "Abschluss";
    public bool IncludeChecklist { get; set; } = true;
}

public static class BuiltInTemplates
{
    public static IReadOnlyList<GuideTemplate> All { get; } = new List<GuideTemplate>
    {
        new() { Id = "step-by-step", Name = "Schritt-für-Schritt-Anleitung", Tags = { "anleitung" } },
        new() { Id = "it-support", Name = "IT-Support", Tags = { "dokumentation", "support", "anleitung" }, Properties = { ["kategorie"] = "it-support" }, SectionsAfter = { "Fehlerbehebung" } },
        new() { Id = "tutorial", Name = "Tutorial", Tags = { "tutorial" }, PrerequisitesTitle = "Was du brauchst", SectionsBefore = { "Ziel" }, SectionsAfter = { "Nächste Schritte" } },
        new() { Id = "bugreport", Name = "Bugreport", Tags = { "bug" }, DefaultStatus = "offen", Properties = { ["schweregrad"] = "mittel", ["version"] = "" }, PrerequisitesTitle = "Umgebung", StepsHeading = "Schritte zur Reproduktion", StepHeading = "{n}. {title}", SectionsAfter = { "Erwartetes Verhalten", "Tatsächliches Verhalten" }, IncludeChecklist = false },
        new() { Id = "software-test", Name = "Softwaretest", Tags = { "test" }, Properties = { ["ergebnis"] = "" }, PrerequisitesTitle = "Testumgebung", StepHeading = "Testschritt {n}: {title}", ChecklistTitle = "Testergebnis" },
        new() { Id = "design-review", Name = "Design Review", Tags = { "design", "review" }, PrerequisitesTitle = "Kontext", StepHeading = "Punkt {n}: {title}", SectionsAfter = { "Entscheidungen" }, ChecklistTitle = "Offene Punkte" },
        new() { Id = "tech-doc", Name = "Technische Dokumentation", Tags = { "dokumentation", "technik" }, SectionsBefore = { "Überblick" }, StepHeading = "{n}. {title}", SectionsAfter = { "Referenzen" }, IncludeChecklist = false },
        new() { Id = "ttrpg", Name = "TTRPG", Tags = { "ttrpg" }, Properties = { ["system"] = "", ["sitzung"] = "" }, PrerequisitesTitle = "Hintergrund", PrerequisitesKind = CalloutKind.Example, StepHeading = "Szene {n}: {title}", SectionsAfter = { "Notizen der Spielleitung" }, ChecklistTitle = "Vorbereitung" },
        new() { Id = "free", Name = "Freie Dokumentation", StepHeading = "{title}", PrerequisitesTitle = "Hinweis", IncludeChecklist = false },
    };
}

/// <summary>Templates in Data/Templates/*.json override/extend the built-ins.</summary>
public sealed class TemplateStore
{
    private readonly string _dir;
    public TemplateStore(string templatesDir) => _dir = templatesDir;

    public List<GuideTemplate> LoadAll()
    {
        var result = BuiltInTemplates.All.Select(t => ProjectJson.Clone(t)).ToDictionary(t => t.Id);
        if (Directory.Exists(_dir))
            foreach (var f in Directory.EnumerateFiles(_dir, "*.template.json"))
            {
                try
                {
                    var t = JsonSerializer.Deserialize<GuideTemplate>(File.ReadAllText(f), ProjectJson.Options);
                    if (t is not null && !string.IsNullOrWhiteSpace(t.Id)) result[t.Id] = t;
                }
                catch (JsonException) { /* ignore broken user template, keep built-in */ }
            }
        return result.Values.ToList();
    }

    /// <summary>Writes the built-ins once so users can copy and edit them.</summary>
    public void WriteDefaultsIfMissing()
    {
        Directory.CreateDirectory(_dir);
        foreach (var t in BuiltInTemplates.All)
        {
            var path = Path.Combine(_dir, t.Id + ".template.json");
            if (!File.Exists(path)) AtomicFile.WriteAllText(path, JsonSerializer.Serialize(t, ProjectJson.Options));
        }
    }
}
