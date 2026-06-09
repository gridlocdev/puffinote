using Avalonia.Headless.XUnit;
using Cellestra.Core.Styling;
using Cellestra.Desktop.Views.Dialogs;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// The Format Cells dialog's mixed-selection handling: when the selection's styles differ, the
/// dialog must only rewrite the fields the user actually touched, preserving every other cell's
/// distinct formatting instead of flattening it to the active cell's style.
/// </summary>
public class FormatCellsDialogTests
{
    [AvaloniaFact]
    public void Mixed_selection_leaves_untouched_fields_unchanged()
    {
        var bold = CellStyle.Default.WithFont(CellStyle.Default.Font with { Bold = true, SizePoints = 14 });
        var plain = CellStyle.Default; // not bold, size 10

        // Open on a selection whose bold and size differ. Touch nothing → both fields are preserved.
        var dialog = new FormatCellsDialog(bold, new[] { bold, plain });
        var transform = dialog.BuildTransform();

        // Each cell keeps its own bold/size — the indeterminate checkbox and empty size box mean "leave".
        Assert.True(transform(bold).Font.Bold);
        Assert.Equal(14, transform(bold).Font.SizePoints);
        Assert.False(transform(plain).Font.Bold);
        Assert.Equal(10, transform(plain).Font.SizePoints);
    }

    [AvaloniaFact]
    public void Single_style_selection_applies_all_fields_as_before()
    {
        var style = CellStyle.Default.WithFont(CellStyle.Default.Font with { Bold = true });

        // No selection styles supplied → previous single-cell behaviour: every field is applied.
        var dialog = new FormatCellsDialog(style);
        var transform = dialog.BuildTransform();

        var result = transform(CellStyle.Default.WithFont(CellStyle.Default.Font with { Italic = true }));
        Assert.True(result.Font.Bold);       // bold checkbox was checked from the seed
        Assert.False(result.Font.Italic);    // and italic (unchecked) is applied, clearing it
    }

    [AvaloniaFact]
    public void Mixed_number_format_is_preserved_when_left_on_varies()
    {
        var currency = CellStyle.Default.WithNumberFormat(new NumberFormat("$#,##0.00"));
        var percent = CellStyle.Default.WithNumberFormat(new NumberFormat("0%"));

        var dialog = new FormatCellsDialog(currency, new[] { currency, percent });
        var transform = dialog.BuildTransform();

        // Format combo seeded to "(varies)" and untouched → each cell keeps its own code.
        Assert.Equal("$#,##0.00", transform(currency).NumberFormat.Code);
        Assert.Equal("0%", transform(percent).NumberFormat.Code);
    }

    [AvaloniaFact]
    public void Single_selection_border_is_seeded_and_applied()
    {
        // A single style with an outline border: the dialog seeds the edge checkboxes from it, so an
        // untouched OK re-applies that same border (changeBorder is true when borders all agree).
        var outlined = CellStyle.Default.WithBorder(Border.All(BorderLineStyle.Medium));
        var dialog = new FormatCellsDialog(outlined);
        var transform = dialog.BuildTransform();

        var result = transform(CellStyle.Default);
        Assert.True(result.Border.HasAnyEdge);
        Assert.Equal(BorderLineStyle.Medium, result.Border.Top.Style);
        Assert.Equal(BorderLineStyle.Medium, result.Border.Left.Style);
    }

    [AvaloniaFact]
    public void Mixed_border_is_preserved_when_untouched()
    {
        var boxed = CellStyle.Default.WithBorder(Border.All(BorderLineStyle.Thin));
        var none = CellStyle.Default; // Border.None

        // Borders differ across the selection → the dialog leaves them alone unless touched.
        var dialog = new FormatCellsDialog(boxed, new[] { boxed, none });
        var transform = dialog.BuildTransform();

        Assert.True(transform(boxed).Border.HasAnyEdge);   // the boxed cell keeps its box
        Assert.False(transform(none).Border.HasAnyEdge);   // the plain cell stays borderless
    }
}
