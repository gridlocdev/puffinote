using System.Linq;
using Avalonia.Headless.XUnit;
using Cellestra.Core.Formulas.Functions;
using Cellestra.Core.Model;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Views.Dialogs;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// The Insert menu dialogs (PRD §6.2 Insert ▸ Function… / Named Range… / Hyperlink…): the function
/// picker reads the one <see cref="FunctionRegistry"/>, the named-range validator rejects bad names,
/// and the hyperlink dialog builds the right outcome (set / remove / display text).
/// </summary>
public class InsertDialogTests
{
    // ---- Function picker --------------------------------------------------

    [AvaloniaFact]
    public void Function_picker_reads_the_function_registry()
    {
        var dialog = new FunctionPickerDialog();

        // Empty query lists everything from the registry — never a second hand-built list (Golden Rule #5).
        var expected = FunctionRegistry.CreateDefault().AllMetadata.Select(m => m.Name).OrderBy(n => n).ToList();
        var shown = dialog.VisibleNamesForTest(string.Empty).OrderBy(n => n).ToList();
        Assert.Equal(expected, shown);
    }

    [AvaloniaFact]
    public void Function_picker_search_filters_by_name()
    {
        var dialog = new FunctionPickerDialog();

        var shown = dialog.VisibleNamesForTest("vlookup");
        Assert.Contains("VLOOKUP", shown);
        Assert.DoesNotContain("SUM", shown);
    }

    [AvaloniaFact]
    public void Function_picker_returns_selected_metadata()
    {
        var dialog = new FunctionPickerDialog();
        dialog.VisibleNamesForTest("sum");

        var meta = dialog.SelectionAtForTest(0);
        Assert.NotNull(meta);
        Assert.Equal("SUM", meta!.Name);
    }

    // ---- Named ranges -----------------------------------------------------

    [AvaloniaFact]
    public void Named_range_validation_rejects_empty_and_address_like_names()
    {
        Assert.NotNull(NamedRangeDialog.ValidateName(""));
        Assert.NotNull(NamedRangeDialog.ValidateName("   "));
        Assert.NotNull(NamedRangeDialog.ValidateName("A1"));      // looks like a cell reference
        Assert.NotNull(NamedRangeDialog.ValidateName("$B$2"));    // absolute reference

        Assert.Null(NamedRangeDialog.ValidateName("TaxRate"));    // a fine name
        Assert.Null(NamedRangeDialog.ValidateName("Total_2026"));
    }

    // ---- Hyperlink --------------------------------------------------------

    [AvaloniaFact]
    public void Hyperlink_dialog_builds_a_link_from_a_target()
    {
        var dialog = new HyperlinkDialog(existing: null, displayValue: "", cellIsEmpty: true);

        var outcome = dialog.BuildForTest("https://example.com", tooltip: "Go", displayText: "Example");
        Assert.NotNull(outcome);
        Assert.NotNull(outcome!.Link);
        Assert.Equal("https://example.com", outcome.Link!.Target);
        Assert.Equal("Go", outcome.Link.Tooltip);
        // The cell was empty, so the display text is carried back for the caller to set.
        Assert.Equal("Example", outcome.DisplayText);
    }

    [AvaloniaFact]
    public void Hyperlink_dialog_empty_target_means_remove()
    {
        var dialog = new HyperlinkDialog(existing: new Hyperlink("https://old.test"), displayValue: "Anchor", cellIsEmpty: false);

        var outcome = dialog.BuildForTest("");
        Assert.NotNull(outcome);
        Assert.Null(outcome!.Link);            // null link clears it
        Assert.Null(outcome.DisplayText);
    }

    [AvaloniaFact]
    public void Hyperlink_dialog_never_overwrites_a_nonempty_cells_text()
    {
        // A cell that already shows text: even if a display value is somehow present, Build doesn't
        // return it (the field isn't shown), so the cell's own value stays the visible text.
        var dialog = new HyperlinkDialog(existing: null, displayValue: "Anchor", cellIsEmpty: false);

        var outcome = dialog.BuildForTest("https://example.com", displayText: "Replacement");
        Assert.NotNull(outcome!.Link);
        Assert.Null(outcome.DisplayText);
    }

    // ---- Catalog wiring ---------------------------------------------------

    [Fact]
    public void Insert_commands_are_registered_with_their_shortcuts()
    {
        Assert.NotNull(AppCommands.ById("insert.function"));
        Assert.NotNull(AppCommands.ById("insert.namedRange"));
        Assert.NotNull(AppCommands.ById("insert.hyperlink"));

        Assert.Equal("⌃⌥F", AppCommands.ById("insert.function")!.ShortcutText);
        Assert.Equal("⌥⌘K", AppCommands.ById("insert.hyperlink")!.ShortcutText);
        // Named Range has no default shortcut.
        Assert.Equal(string.Empty, AppCommands.ById("insert.namedRange")!.ShortcutText);
    }
}
