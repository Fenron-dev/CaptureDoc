using System.Text;
using CaptureDoc.Core.Model;
using CaptureDoc.Core.Obsidian;

namespace CaptureDoc.Core.Tests;

public class SafeFileNameTests
{
    [Theory]
    [InlineData("Drucker konfigurieren", "Drucker-konfigurieren")]
    [InlineData("Größe & Maße: Übersicht?", "Groesse-Masse-Uebersicht")]
    [InlineData("  ../../etc/passwd ", "etc-passwd")]
    [InlineData("CON", "_CON")]
    [InlineData("###", "unbenannt")]
    [InlineData("Café crème", "Cafe-creme")]
    public void Slugs_are_safe(string input, string expected) => Assert.Equal(expected, SafeFileName.ToSlug(input));

    [Theory]
    [InlineData("Drucker: Setup [v2] #1", "Drucker Setup v2 1")]
    [InlineData("Notiz.", "Notiz")]
    [InlineData("a|b^c", "a b c")]
    public void Note_names_keep_readable_text(string input, string expected) => Assert.Equal(expected, SafeFileName.ToNoteName(input));

    [Fact]
    public void Unique_names_are_case_insensitive()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Anleitung.md", "anleitung (2).md" };
        Assert.Equal("Anleitung (3).md", SafeFileName.MakeUnique("Anleitung.md", taken.Contains, slugStyle: false));
        Assert.Equal("bild-2.png", SafeFileName.MakeUnique("bild.png", n => n == "bild.png", slugStyle: true));
    }
}

public class MarkdownTests
{
    internal static Guide SampleGuide()
    {
        var g = new Guide
        {
            Title = "Drucker konfigurieren",
            Status = "fertig",
            Tags = { "Support" },
            Prerequisites = "Der Benutzer benötigt Zugriff auf\ndie Druckereinstellungen.",
            Checklist = { "Drucker sichtbar", "Testseite gedruckt" },
        };
        g.Steps.Add(new GuideStep { Title = "Einstellungen öffnen", Text = "Öffne die Windows-Einstellungen." });
        g.Steps.Add(new GuideStep { Title = "Drucker auswählen", Text = "Wähle den gewünschten Drucker.", Callouts = { new Callout { Kind = CalloutKind.Warning, Title = "Hinweis", Text = "Prüfe die korrekte Verbindung." } } });
        return g;
    }

    [Fact]
    public void Builds_obsidian_markdown_with_frontmatter_callouts_embeds_and_checklist()
    {
        var g = SampleGuide();
        var tpl = BuiltInTemplates.All.Single(t => t.Id == "it-support");
        var imgs = new Dictionary<Guid, MarkdownBuilder.ImageRef>
        {
            [g.Steps[0].Id] = new("Dokumentation/Drucker/Assets/drucker-schritt-01.png", 700, "Einstellungen öffnen"),
        };
        var md = MarkdownBuilder.Build(g, tpl, imgs, LinkStyle.Wikilink, new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

        Assert.StartsWith("---\ntitle: Drucker konfigurieren\ntags:\n  - dokumentation\n  - support\n  - anleitung\ncreated: 2026-10-20\nstatus: fertig\n", md);
        Assert.Contains($"capturedoc-id: {g.ExportId:D}\n---\n\n# Drucker konfigurieren\n", md);
        Assert.Contains("> [!info] Voraussetzungen\n> Der Benutzer benötigt Zugriff auf\n> die Druckereinstellungen.\n", md);
        Assert.Contains("## Schritt 1: Einstellungen öffnen\n\n![[Dokumentation/Drucker/Assets/drucker-schritt-01.png|700]]\n\nÖffne die Windows-Einstellungen.\n", md);
        Assert.Contains("## Schritt 2: Drucker auswählen\n\nWähle den gewünschten Drucker.\n\n> [!warning] Hinweis\n> Prüfe die korrekte Verbindung.\n", md);
        Assert.Contains("## Abschluss\n\n- [ ] Drucker sichtbar\n- [ ] Testseite gedruckt\n", md);
        Assert.Contains("## Fehlerbehebung", md);
        Assert.DoesNotContain("\r", md);
        Assert.DoesNotContain("<", md); // no HTML workarounds
    }

    [Fact]
    public void Markdown_links_are_url_encoded_and_have_display_width()
    {
        var e = MarkdownBuilder.Embed(new MarkdownBuilder.ImageRef("Assets/mein bild.png", 500, "Alt [x]"), LinkStyle.Markdown);
        Assert.Equal("![Alt x|500](Assets/mein%20bild.png)", e);
    }

    [Theory]
    [InlineData("einfach", "einfach")]
    [InlineData("mit: doppelpunkt", "\"mit: doppelpunkt\"")]
    [InlineData("true", "\"true\"")]
    [InlineData("2026", "\"2026\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("", "\"\"")]
    public void Yaml_values_are_quoted_when_needed(string input, string expected) => Assert.Equal(expected, MarkdownBuilder.YamlString(input));

    [Theory]
    [InlineData("IT Support", "it-support")]
    [InlineData("#Anleitung", "anleitung")]
    [InlineData("2026", "t2026")]
    public void Tags_are_normalized(string input, string expected) => Assert.Equal(expected, MarkdownBuilder.NormalizeTag(input));

    [Fact]
    public void All_builtin_templates_render()
    {
        var g = SampleGuide();
        foreach (var t in BuiltInTemplates.All)
        {
            var md = MarkdownBuilder.Build(g, t, new Dictionary<Guid, MarkdownBuilder.ImageRef>(), LinkStyle.Wikilink, DateTimeOffset.Now);
            Assert.StartsWith("---\n", md);
            Assert.Contains("# Drucker konfigurieren", md);
        }
        Assert.Equal(9, BuiltInTemplates.All.Count);
    }
}

public class ObsidianExportTests
{
    private static string NewVault(TempDir t)
    {
        var v = Path.Combine(t.Path, "MeinVault");
        Directory.CreateDirectory(Path.Combine(v, ".obsidian"));
        File.WriteAllText(Path.Combine(v, ".obsidian", "app.json"), "{}");
        return v;
    }

    private static ObsidianExportRequest Request(string vault, Guide g, ConflictPolicy policy = ConflictPolicy.NewVersion, LinkStyle links = LinkStyle.Wikilink, byte seed = 1) => new()
    {
        VaultRoot = vault,
        NoteFolder = "Dokumentation/Drucker-konfigurieren",
        Guide = g,
        Template = BuiltInTemplates.All[0],
        Images = g.Steps.ToDictionary(s => s.Id, s => new RenderedImage(new byte[] { 0x89, 0x50, seed, (byte)g.Steps.IndexOf(s) }, ".png", 10, 10)),
        Policy = policy,
        Links = links,
        NoteName = "Anleitung",
    };

    [Fact]
    public void First_export_creates_note_assets_and_valid_links()
    {
        using var t = new TempDir();
        var vault = NewVault(t);
        var g = MarkdownTests.SampleGuide();
        var plan = ObsidianExporter.Plan(Request(vault, g));
        Assert.All(plan.AllFiles, f => Assert.Equal(PlannedAction.Create, f.Action));
        Assert.Equal("Dokumentation/Drucker-konfigurieren/Assets/drucker-konfigurieren-schritt-01.png", plan.Assets[0].VaultRelativePath);

        var result = ObsidianExporter.Execute(plan, g.ExportId);
        Assert.True(result.Verified, string.Join(", ", result.MissingAssets));
        Assert.Equal(3, result.Written.Count);
        var note = Path.Combine(vault, "Dokumentation", "Drucker-konfigurieren", "Anleitung.md");
        Assert.True(File.Exists(note));
        Assert.Contains("![[Dokumentation/Drucker-konfigurieren/Assets/drucker-konfigurieren-schritt-01.png|700]]", File.ReadAllText(note));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(vault, ".obsidian", "app.json"))); // config untouched
    }

    [Fact]
    public void Markdown_link_style_produces_relative_links_that_verify()
    {
        using var t = new TempDir();
        var vault = NewVault(t);
        var g = MarkdownTests.SampleGuide();
        var result = ObsidianExporter.Execute(ObsidianExporter.Plan(Request(vault, g, links: LinkStyle.Markdown)), g.ExportId);
        Assert.True(result.Verified);
        Assert.Contains("](Assets/drucker-konfigurieren-schritt-01.png)", File.ReadAllText(result.NotePath));
    }

    [Fact]
    public void Reexport_without_changes_is_unchanged_and_updates_are_safe()
    {
        using var t = new TempDir();
        var vault = NewVault(t);
        var g = MarkdownTests.SampleGuide();
        ObsidianExporter.Execute(ObsidianExporter.Plan(Request(vault, g)), g.ExportId);

        var again = ObsidianExporter.Plan(Request(vault, g));
        Assert.All(again.AllFiles, f => Assert.Equal(PlannedAction.Unchanged, f.Action));

        g.Steps[0].Text = "Neuer Text";
        var upd = ObsidianExporter.Plan(Request(vault, g, seed: 2));
        Assert.Equal(PlannedAction.Update, upd.Note.Action);
        Assert.All(upd.Assets, a => Assert.Equal(PlannedAction.Update, a.Action));
        Assert.False(upd.HasConflicts);
    }

    [Fact]
    public void Manual_edit_in_obsidian_is_never_overwritten_silently()
    {
        using var t = new TempDir();
        var vault = NewVault(t);
        var g = MarkdownTests.SampleGuide();
        var first = ObsidianExporter.Execute(ObsidianExporter.Plan(Request(vault, g)), g.ExportId);
        File.AppendAllText(first.NotePath, "\nMeine manuelle Ergänzung\n");
        var edited = File.ReadAllText(first.NotePath);

        g.Steps[1].Text = "Geändert in CaptureDoc";
        var plan = ObsidianExporter.Plan(Request(vault, g));
        Assert.True(plan.HasConflicts);
        Assert.Equal(PlannedAction.ConflictNewVersion, plan.Note.Action);
        Assert.Contains(plan.Warnings, w => w.Contains("außerhalb von CaptureDoc geändert"));
        var res = ObsidianExporter.Execute(plan, g.ExportId);

        Assert.Equal(edited, File.ReadAllText(first.NotePath)); // manual change intact
        Assert.EndsWith("Anleitung (2).md", res.NotePath);
        Assert.Contains("Geändert in CaptureDoc", File.ReadAllText(res.NotePath));
        Assert.True(res.Verified);

        // Next export goes to the new version without another copy
        var next = ObsidianExporter.Plan(Request(vault, g));
        Assert.Equal(PlannedAction.Unchanged, next.Note.Action);
        Assert.EndsWith("Anleitung (2).md", next.Note.FullPath);
    }

    [Fact]
    public void Overwrite_policy_overwrites_and_skip_policy_keeps()
    {
        using var t = new TempDir();
        var vault = NewVault(t);
        var g = MarkdownTests.SampleGuide();
        var first = ObsidianExporter.Execute(ObsidianExporter.Plan(Request(vault, g)), g.ExportId);
        File.WriteAllText(first.NotePath, "fremd");

        var skip = ObsidianExporter.Execute(ObsidianExporter.Plan(Request(vault, g, ConflictPolicy.Skip)), g.ExportId);
        Assert.Equal("fremd", File.ReadAllText(first.NotePath));
        Assert.Single(skip.Skipped);

        ObsidianExporter.Execute(ObsidianExporter.Plan(Request(vault, g, ConflictPolicy.Overwrite)), g.ExportId);
        Assert.StartsWith("---", File.ReadAllText(first.NotePath));
    }

    [Fact]
    public void Foreign_existing_note_gets_unique_name()
    {
        using var t = new TempDir();
        var vault = NewVault(t);
        var dir = Path.Combine(vault, "Dokumentation", "Drucker-konfigurieren");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "anleitung.md"), "von jemand anderem"); // different case!
        var g = MarkdownTests.SampleGuide();
        var plan = ObsidianExporter.Plan(Request(vault, g));
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            Assert.Equal(PlannedAction.ConflictNewVersion, plan.Note.Action);
            Assert.EndsWith("Anleitung (2).md", plan.Note.FullPath);
        }
        ObsidianExporter.Execute(plan, g.ExportId);
        Assert.Equal("von jemand anderem", File.ReadAllText(Path.Combine(dir, "anleitung.md")));
    }

    [Theory]
    [InlineData("../ausserhalb")]
    [InlineData(".obsidian/plugins")]
    [InlineData("C:/Windows")]
    [InlineData("Ordner#1")]
    public void Unsafe_target_folders_are_rejected(string folder)
    {
        using var t = new TempDir();
        var vault = NewVault(t);
        var g = MarkdownTests.SampleGuide();
        var r = Request(vault, g);
        var bad = new ObsidianExportRequest { VaultRoot = r.VaultRoot, NoteFolder = folder, Guide = g, Template = r.Template, Images = r.Images };
        Assert.ThrowsAny<ArgumentException>(() => ObsidianExporter.Plan(bad));
    }

    [Fact]
    public void Missing_vault_and_non_vault_folder_are_reported()
    {
        using var t = new TempDir();
        Assert.False(VaultValidator.ValidateVault(Path.Combine(t.Path, "fehlt")).Ok);
        var plain = Directory.CreateDirectory(Path.Combine(t.Path, "kein-vault")).FullName;
        var res = VaultValidator.ValidateVault(plain);
        Assert.True(res.Ok);
        Assert.False(res.LooksLikeVault);
        Assert.NotNull(res.Warning);
    }

    [Fact]
    public void Verify_detects_missing_assets()
    {
        using var t = new TempDir();
        var vault = NewVault(t);
        var g = MarkdownTests.SampleGuide();
        var res = ObsidianExporter.Execute(ObsidianExporter.Plan(Request(vault, g)), g.ExportId);
        File.Delete(Path.Combine(vault, "Dokumentation", "Drucker-konfigurieren", "Assets", "drucker-konfigurieren-schritt-02.png"));
        var missing = ObsidianExporter.VerifyLinks(res.NotePath, vault);
        Assert.Single(missing);
        Assert.EndsWith("schritt-02.png", missing[0]);
    }
}
