using Puffinote.Desktop.Panes;
using Xunit;

namespace Puffinote.Tests;

/// <summary>
/// The rule behind "New File…": every extension is yours except the three Puffinote already makes from its
/// own commands. It matters that this is one table with the pane factory's routing — a name this lets
/// through is a name some pane will later be asked to open.
/// </summary>
public sealed class DocumentTypesTests
{
    [Theory]
    [InlineData("notes.txt")]
    [InlineData("data.json")]        // plain JSON is not a board — only the compound suffix is
    [InlineData("run.sh")]
    [InlineData("LICENSE")]          // no extension at all is a perfectly good file name
    [InlineData("archive.tar.gz")]
    [InlineData("cover.png")]        // nothing here opens it, but the notebook may still want one
    [InlineData("mdown")]            // a reserved extension's letters, without the dot
    public void Any_other_extension_is_the_users_to_take(string name) =>
        Assert.Null(DocumentTypes.ReservedFor(name));

    [Theory]
    [InlineData("todo.md", ".md", "New Note…")]
    [InlineData("todo.markdown", ".markdown", "New Note…")]
    [InlineData("todo.mdown", ".mdown", "New Note…")]
    [InlineData("budget.ods", ".ods", "New Spreadsheet…")]
    [InlineData("work.pivotask.json", ".pivotask.json", "New Taskboard…")]
    public void A_type_with_its_own_command_keeps_its_extension(string name, string extension, string command)
    {
        var reserved = DocumentTypes.ReservedFor(name);
        Assert.NotNull(reserved);
        Assert.Equal(extension, reserved.Value.Extension);

        // The refusal names the command that *does* make one, so the dead end is a redirect.
        Assert.Equal(command, reserved.Value.Command);
    }

    [Theory]
    [InlineData("TODO.MD")]
    [InlineData("Budget.ODS")]
    [InlineData("Work.PivoTask.Json")]
    public void The_reservation_is_case_insensitive_because_the_filesystems_are(string name) =>
        Assert.NotNull(DocumentTypes.ReservedFor(name));

    [Fact]
    public void A_board_is_matched_by_its_whole_suffix_not_by_its_extension()
    {
        // Path.GetExtension("work.pivotask.json") is ".json", so anything reading the extension alone would
        // wave a board through and then hand Pivotask a 0-byte file to parse.
        Assert.Equal(".pivotask.json", DocumentTypes.ReservedFor("work.pivotask.json")?.Extension);
        Assert.Null(DocumentTypes.ReservedFor("work.json"));
    }

    [Fact]
    public void The_dialogs_hint_names_every_extension_it_will_refuse()
    {
        // The New File dialog says the rule before you type; it reads the same table that enforces it, so the
        // hint can't come to list something the refusal doesn't (or miss something it does).
        Assert.Contains(DocumentTypes.NoteExtension, DocumentTypes.ReservedSummary, StringComparison.Ordinal);
        Assert.Contains(DocumentTypes.SpreadsheetExtension, DocumentTypes.ReservedSummary, StringComparison.Ordinal);
        Assert.Contains(DocumentTypes.BoardSuffix, DocumentTypes.ReservedSummary, StringComparison.Ordinal);
    }
}
