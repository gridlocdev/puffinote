using System.Text.Json;
using System.Text.Json.Nodes;
using Puffinote.Desktop.Panes;
using Xunit;

namespace Puffinote.Tests;

/// <summary>
/// The mirrors Puffinote reads and writes each editor's own settings file through (PRD §7.5). Two things
/// have to hold for the tabbed Preferences dialog to be safe to use: a mirror must default to exactly what
/// the editor it mirrors defaults to (otherwise merely opening the dialog would "change" settings nobody
/// touched), and writing one back must leave the rest of that editor's file alone.
/// </summary>
public sealed class EditorConfigTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("puffinote-editor-config");

    public void Dispose() => _dir.Delete(recursive: true);

    private string At(string relative) => Path.Combine(_dir.FullName, relative);

    private static JsonObject Read(string path) =>
        JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidOperationException("not an object");

    // ---- defaults: an editor Puffinote has never seen behaves as it does standalone ------------------

    [Fact]
    public void Every_mirror_falls_back_to_its_editors_own_defaults_when_the_file_is_absent()
    {
        var notes = NoteConfig.Load(At("nope.json"));
        Assert.Equal(4, notes.TabWidth);              // Typage's AppSettings defaults
        Assert.Equal(760, notes.EditorMaxContentWidth);
        Assert.True(notes.ShowToolbar);

        var sheets = SheetConfig.Load(At("nope.json"));
        Assert.Equal(1.0, sheets.Zoom);               // Cellestra's AppSettings defaults
        Assert.False(sheets.CommandBarCollapsed);

        var plainText = PlainTextConfig.Load(At("nope.json"));
        Assert.False(plainText.WordWrap);             // Plaintain's AppSettings defaults
        Assert.True(plainText.ShowLineNumbers);
        Assert.Equal(13, plainText.FontSize);

        var tasks = BoardConfig.Load(At("nope.json"));
        Assert.Equal(Pivotask.Control.Views.BoardLayout.Board, tasks.Layout); // Pivotask's defaults
        Assert.Equal(Pivotask.Core.Query.TaskFilter.All, tasks.Filter);
    }

    [Fact]
    public void The_mirrors_and_the_files_they_mirror_are_the_editors_own_config_files()
    {
        // Named per app, so a settings edit made here lands where that app looks when it runs alone.
        Assert.EndsWith(Path.Combine("Typage", "settings.json"), NoteConfig.DefaultPath, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine("Plaintain", "settings.json"), PlainTextConfig.DefaultPath, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine("Pivotask", "settings.json"), BoardConfig.DefaultPath, StringComparison.Ordinal);

        // Cellestra is the odd one out on purpose: macOS application support, not the XDG config dir.
        Assert.EndsWith(
            Path.Combine("Library", "Application Support", "Cellestra", "settings.json"),
            SheetConfig.DefaultPath,
            StringComparison.Ordinal);
    }

    // ---- writing back: merge, never replace ---------------------------------------------------------

    [Fact]
    public void Saving_a_mirror_leaves_the_keys_it_doesnt_model_exactly_as_they_were()
    {
        var path = At("typage.json");
        File.WriteAllText(path, """
            {
              "Theme": "Light",
              "RecentFiles": [ "/notes/a.md", "/notes/b.md" ],
              "AutosaveSeconds": 90,
              "TabWidth": 4
            }
            """);

        var notes = NoteConfig.Load(path);
        notes.TabWidth = 2;
        notes.Save(path);

        var written = Read(path);
        Assert.Equal(2, (int)written["TabWidth"]!);
        Assert.Equal("Light", (string?)written["Theme"]);                  // Puffinote sets no theme here
        Assert.Equal(90, (int)written["AutosaveSeconds"]!);               // nor an autosave interval
        Assert.Equal(2, written["RecentFiles"]!.AsArray().Count);         // and it never touches your history
    }

    [Fact]
    public void Saving_a_sheet_mirror_keeps_cellestras_radial_bindings_and_window_size()
    {
        var path = At("cellestra.json");
        File.WriteAllText(path, """
            {
              "WindowWidth": 1400,
              "RadialMenu": { "SegmentCount": 8, "Bindings": [ "edit.copy", "edit.paste" ] },
              "Zoom": 1.0
            }
            """);

        var sheets = SheetConfig.Load(path);
        sheets.Zoom = 1.5;
        sheets.Save(path);

        var written = Read(path);
        Assert.Equal(1.5, (double)written["Zoom"]!);
        Assert.Equal(1400, (int)written["WindowWidth"]!);
        Assert.Equal(8, (int)written["RadialMenu"]!["SegmentCount"]!);
        Assert.Equal(2, written["RadialMenu"]!["Bindings"]!.AsArray().Count);
    }

    [Fact]
    public void Saving_creates_the_file_when_the_editor_has_never_written_one()
    {
        var path = At("subdir/pivotask.json");

        new BoardConfig { Filter = Pivotask.Core.Query.TaskFilter.Unfinished }.Save(path);

        Assert.Equal("Unfinished", (string?)Read(path)["Filter"]); // by name — the file stays hand-editable
        Assert.Equal(Pivotask.Core.Query.TaskFilter.Unfinished, BoardConfig.Load(path).Filter);
    }

    // ---- tolerance ----------------------------------------------------------------------------------

    [Fact]
    public void A_value_this_build_doesnt_recognize_costs_that_setting_and_nothing_else()
    {
        var path = At("stale.json");
        // "Source" was a view mode name once; a file written by an older Typage still says it.
        File.WriteAllText(path, """{ "View": "Source", "TabWidth": 2, "EditorPadding": 40 }""");

        var notes = NoteConfig.Load(path);

        Assert.Equal(Typage.Control.Controls.MarkdownEditor.ViewMode.Rich, notes.View); // back to the default
        Assert.Equal(2, notes.TabWidth);      // …and the rest of the file survives, which is the point
        Assert.Equal(40, notes.EditorPadding);
    }

    [Fact]
    public void A_corrupt_file_reads_as_defaults_and_is_replaced_rather_than_merged_into()
    {
        var path = At("corrupt.json");
        File.WriteAllText(path, "{ this is not json");

        Assert.Equal(4, NoteConfig.Load(path).TabWidth); // tolerant load: a first run, not an error

        new NoteConfig { TabWidth = 3 }.Save(path);

        var written = Read(path);
        Assert.Equal(3, (int)written["TabWidth"]!);
    }

    // ---- resolution ---------------------------------------------------------------------------------

    [Fact]
    public void Numbers_survive_a_round_trip_through_the_form_and_back_to_the_file()
    {
        var path = At("plaintain.json");
        var plainText = PlainTextConfig.Load(path);

        // What the Preferences form does: build the fields over the mirror, edit, commit, save.
        var form = new Puffinote.Components.Controls.SettingsEditorView(plainText.Fields());
        Assert.Equal(3, form.Fields.Count);
        form.Commit(); // committing an untouched form must not change anything
        Assert.Equal(new PlainTextConfig(), plainText);

        plainText.FontSize = 18;
        plainText.WordWrap = true;
        plainText.Save(path);

        var reloaded = PlainTextConfig.Load(path);
        Assert.Equal(18, reloaded.FontSize);
        Assert.True(reloaded.WordWrap);
        Assert.True(reloaded.ShowLineNumbers); // untouched
    }

    [Fact]
    public void Serializing_a_mirror_writes_only_the_keys_it_models()
    {
        var json = JsonSerializer.Serialize(new BoardConfig(), EditorConfig.Json);
        var keys = (JsonNode.Parse(json) as JsonObject)!.Select(pair => pair.Key).ToList();

        Assert.Equal(["Layout", "Filter"], keys);
    }
}
