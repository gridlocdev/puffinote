using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Plaintain.Control.Controls;
using Xunit;

namespace Plaintain.UiTests;

/// <summary>
/// The one assumption the whole surface rests on: the font it draws with is the monospace font its
/// arithmetic assumes. Every row is <c>RowHeight</c> tall and every character <c>CharWidth</c> wide, so
/// the caret, the selection, the find highlights and click-to-position are all multiplications rather
/// than measurements — and all four are wrong together the moment the surface is drawing in some other
/// face than the one it measured.
/// </summary>
public class CaretGeometryTests
{
    /// <summary>
    /// The regression this file exists for: the caret drifting further from the glyphs the further along
    /// a line it got. <c>FontFamily</c>/<c>FontSize</c> used to be adopted from <see cref="TextBlock"/>,
    /// whose text properties <b>inherit</b> — so the ambient UI font of whatever hosted the surface (a
    /// themed window, a Puffinote tab) beat the monospace default underneath it, and nothing sets a
    /// family explicitly. The surface then measured one advance from "0" and stepped every column by it
    /// while the renderer laid out proportional glyphs, so the error accumulated across the row.
    /// </summary>
    [AvaloniaFact]
    public void The_surface_keeps_its_monospace_font_when_its_host_sets_another()
    {
        var editor = new PlainTextEditor { Text = "0123456789abcdef" };
        var window = new Window
        {
            Width = 400,
            Height = 300,

            // A host with loud opinions about text — the ambient values that used to leak in.
            FontFamily = new FontFamily("Arial"),
            FontSize = 27,
            Content = editor,
        };
        window.Show();
        window.UpdateLayout();

        Assert.Contains("Menlo", editor.FontFamily.Name, StringComparison.Ordinal);
        Assert.Equal(13.0, editor.FontSize);
    }

    /// <summary>A host that <i>means</i> to set the font still can — this is not inheritance, it is intent.</summary>
    [AvaloniaFact]
    public void A_host_that_sets_the_font_explicitly_is_still_obeyed()
    {
        var editor = new PlainTextEditor { Text = "abc", FontSize = 21 };
        var window = new Window { Width = 400, Height = 300, FontSize = 27, Content = editor };
        window.Show();
        window.UpdateLayout();

        Assert.Equal(21.0, editor.FontSize);
    }

    /// <summary>
    /// The invariant the caret's position is computed from: a column is a multiplication, so <c>n</c>
    /// columns of text must occupy exactly <c>n * CharWidth</c> when laid out the way the renderer lays
    /// it out. A proportional face — the state the inheritance bug left the surface in — fails this by
    /// roughly a character per line, which is precisely how far the caret sat from the text.
    /// </summary>
    [AvaloniaFact]
    public void A_row_of_text_occupies_exactly_the_width_the_column_arithmetic_predicts()
    {
        const string row = "0123456789abcdefMWil ."; // digits, wide letters, narrow letters, a space
        var editor = new PlainTextEditor { Text = row };
        var window = new Window { Width = 600, Height = 300, FontFamily = new FontFamily("Arial"), Content = editor };
        window.Show();
        window.UpdateLayout();

        double measured = editor.MeasureRowForTest(row);
        Assert.Equal(row.Length * editor.CharWidth, measured, precision: 3);
    }
}
