using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// The Name Box beside the formula input names the whole <em>selection</em>, not only the cell the caret is
/// in — "A1:B2" while a rectangle is selected. A selection is a thing you act on, and its extent is what you
/// need to know before you sum it, format it, or name it; when it runs off the bottom of the window there is
/// no other way to read it. It stays the box you can type into, so what it writes it can also read back.
/// </summary>
public class NameBoxRangeTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cellestra-namebox-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    private MainWindow Open()
    {
        var window = new MainWindow(new Cellestra.Desktop.Services.SettingsService(_path));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static TextBox NameBox(MainWindow window) =>
        window.GetVisualDescendants().OfType<TextBox>().First(b => b.Name == "NameBox");

    private static Point CellCentre(int column, int row)
    {
        const double w = Cellestra.Core.Defaults.ColumnWidth;
        const double h = Cellestra.Core.Defaults.RowHeight;
        return new Point(
            SpreadsheetGrid.HeaderWidth + (column * w) + (w / 2),
            SpreadsheetGrid.HeaderHeight + (row * h) + (h / 2));
    }

    [AvaloniaFact]
    public void One_cell_still_reads_as_one_address()
    {
        var window = Open();
        window.Editor.Selection.MoveTo(0, 0);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("A1", window.Editor.Grid.SelectionName);
        Assert.Equal("A1", NameBox(window).Text);
    }

    /// <summary>Dragging out a rectangle with the mouse.</summary>
    [AvaloniaFact]
    public void Dragging_out_a_range_names_both_corners()
    {
        var window = Open();
        var from = window.Editor.Grid.TranslatePoint(CellCentre(0, 0), window) ?? default; // A1
        var to = window.Editor.Grid.TranslatePoint(CellCentre(1, 1), window) ?? default;   // B2

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("A1:B2", window.Editor.Grid.SelectionName);
        Assert.Equal("A1:B2", NameBox(window).Text);
    }

    /// <summary>And the keyboard's own way of extending one: ⇧+Arrow.</summary>
    [AvaloniaFact]
    public void Shift_arrow_names_the_range_it_grows()
    {
        var window = Open();
        window.Editor.Selection.MoveTo(0, 0);
        window.Editor.Grid.Focus();
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("A1:B2", NameBox(window).Text);
    }

    /// <summary>Collapsing back to one cell takes the range off the box with it.</summary>
    [AvaloniaFact]
    public void Collapsing_the_selection_puts_the_single_address_back()
    {
        var window = Open();
        window.Editor.Selection.MoveTo(0, 0);
        window.Editor.Grid.Focus();
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("A1:B1", NameBox(window).Text);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); // back to one cell
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("A1", NameBox(window).Text);
    }

    /// <summary>
    /// What the box writes, it reads: typing a range back into it selects that rectangle. Otherwise the box's
    /// own output would be invalid as its input.
    /// </summary>
    [AvaloniaFact]
    public void Typing_a_range_into_the_box_selects_it()
    {
        var window = Open();
        var box = NameBox(window);

        box.Text = "B2:C4";
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Return });
        Dispatcher.UIThread.RunJobs();

        var selection = window.Editor.Selection;
        Assert.Equal(1, selection.Top);
        Assert.Equal(1, selection.Left);
        Assert.Equal(3, selection.Bottom);
        Assert.Equal(2, selection.Right);
        Assert.Equal("B2:C4", NameBox(window).Text); // and it reads back as what was typed
    }

    /// <summary>
    /// The active cell lands on the range's <em>first</em> corner. "B2:C4" is a way of saying "take me to B2
    /// and bring its block with me", so the formula bar beside the box shows B2's contents and the scroll
    /// lands on B2 — not on the far corner, which may be a screen away. A dragged selection is the opposite
    /// case and keeps its active cell under the pointer; that is why this doesn't go through SetRange.
    /// </summary>
    [AvaloniaFact]
    public void A_typed_range_leaves_the_active_cell_on_its_first_corner()
    {
        var window = Open();
        window.Editor.Session.ActiveSheet.SetValue(1, 1, Cellestra.Core.Values.CellValue.Text("top-left"));
        window.Editor.Session.ActiveSheet.SetValue(3, 2, Cellestra.Core.Values.CellValue.Text("bottom-right"));

        var box = NameBox(window);
        box.Text = "B2:C4";
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Return });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, window.Editor.Selection.ActiveRow);    // B2, not C4
        Assert.Equal(1, window.Editor.Selection.ActiveColumn);

        var formula = window.GetVisualDescendants().OfType<TextBox>().First(b => b.Name == "FormulaInput");
        Assert.Equal("top-left", formula.Text);
    }

    /// <summary>
    /// A reference may name a sheet. The one thing the box must never do with that is apply it to whichever
    /// sheet is in front — that silently selects, and then edits, the wrong cells. It used to: the range
    /// branch dropped the qualifier and set the rectangle on the active sheet.
    /// </summary>
    [AvaloniaFact]
    public void A_reference_that_names_another_sheet_goes_there()
    {
        var window = Open();
        window.Editor.Session.AddSheet(); // Sheet2, and AddSheet activates it
        window.Editor.Session.SetActiveSheet(window.Editor.Session.Workbook.Worksheets[0]);
        Dispatcher.UIThread.RunJobs();

        var second = window.Editor.Session.Workbook.Worksheets[1];
        var box = NameBox(window);
        box.Text = $"{second.Name}.B2:C4";
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Return });
        Dispatcher.UIThread.RunJobs();

        Assert.Same(second, window.Editor.Session.ActiveSheet); // the named sheet, not the one we were on
        Assert.Equal(1, window.Editor.Selection.Top);
        Assert.Equal(3, window.Editor.Selection.Bottom);
    }

    [AvaloniaFact]
    public void A_reference_naming_a_sheet_that_isnt_there_is_reported()
    {
        var window = Open();
        var before = window.Editor.Session.ActiveSheet;
        window.Editor.Selection.MoveTo(0, 0);
        var box = NameBox(window);

        box.Text = "Nowhere.B2:C4";
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Return });
        Dispatcher.UIThread.RunJobs();

        Assert.Same(before, window.Editor.Session.ActiveSheet);
        Assert.True(window.Editor.Selection.IsSingleCell); // nothing selected on the sheet we are on
        Assert.Equal("A1", NameBox(window).Text);
    }

    [AvaloniaFact]
    public void A_range_that_isnt_one_is_reported_rather_than_swallowed()
    {
        var window = Open();
        window.Editor.Selection.MoveTo(0, 0);
        var box = NameBox(window);

        box.Text = "A1:not-a-cell";
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Return });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("A1", box.Text); // restored, not left showing the bad text
    }
}
