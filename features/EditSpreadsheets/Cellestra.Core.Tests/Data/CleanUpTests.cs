using System.Globalization;
using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Ods;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;
using Xunit;

namespace Cellestra.Core.Tests.Data;

/// <summary>
/// Clean Up (PRD §8.17): the formatting it infers from what a sheet holds, the two data tidies it
/// is allowed to make, and — as important — everything it deliberately leaves alone (formulas,
/// chosen number formats, identifiers that merely look numeric).
/// </summary>
public class CleanUpTests
{
    // A real locale, not the invariant one: the invariant culture's currency symbol is "¤", so a "$"
    // amount would not parse and the currency case below would be testing nothing.
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    private static Workbook Book(out Worksheet sheet)
    {
        var workbook = Workbook.CreateNew();
        workbook.Settings.Locale = Us;
        sheet = workbook.ActiveSheet;
        return workbook;
    }

    private static void Fill(Worksheet sheet, params string[][] rows)
    {
        for (int r = 0; r < rows.Length; r++)
        {
            for (int c = 0; c < rows[r].Length; c++)
            {
                if (rows[r][c].Length > 0)
                {
                    sheet.SetValue(r, c, CellValue.Text(rows[r][c]));
                }
            }
        }
    }

    private static void Run(Workbook workbook, Worksheet sheet)
    {
        var plan = CleanUp.ForSheet(workbook, sheet, Us);
        plan.ToCommand(CleanUp.SheetLabel)?.Apply(workbook);
    }

    private static CellStyle StyleOf(Workbook workbook, Worksheet sheet, int row, int column) =>
        workbook.Styles.Get(sheet.EffectiveStyleId(row, column));

    // Region | Revenue
    //  West  | 1200.5
    //  East  |  900
    private static Workbook Sales(out Worksheet sheet)
    {
        var workbook = Book(out sheet);
        Fill(sheet,
            ["Region", "Revenue"],
            ["West", "1200.50"],
            ["East", "900"]);
        return workbook;
    }

    [Fact]
    public void Header_row_is_emphasized_and_frozen()
    {
        var workbook = Sales(out var sheet);

        Run(workbook, sheet);

        var header = StyleOf(workbook, sheet, 0, 0);
        Assert.True(header.Font.Bold);
        Assert.Equal(new ArgbColor(Defaults.TableHeaderFillArgb), header.Fill.Background);
        Assert.Equal(new ArgbColor(Defaults.TableHeaderTextArgb), header.Font.Color);
        Assert.Equal(HorizontalAlignment.Center, header.Alignment.Horizontal);
        Assert.Equal(1, sheet.FrozenRows);
        Assert.Equal(0, sheet.FrozenColumns);
    }

    [Fact]
    public void Numbers_stored_as_text_become_numbers_and_the_column_gets_one_format()
    {
        var workbook = Sales(out var sheet);

        Run(workbook, sheet);

        Assert.Equal(1200.5, sheet.GetValue(1, 1).AsNumber);
        Assert.Equal(900d, sheet.GetValue(2, 1).AsNumber);

        // Two decimals seen in the column, so both rows read to two — that is the point of one
        // format per column rather than one per cell.
        string format = StyleOf(workbook, sheet, 1, 1).NumberFormat.Code;
        Assert.Equal("#,##0.00", format);
        Assert.Equal(format, StyleOf(workbook, sheet, 2, 1).NumberFormat.Code);
    }

    [Fact]
    public void A_column_of_whole_numbers_below_a_thousand_is_left_general()
    {
        var workbook = Book(out var sheet);
        Fill(sheet, ["Count"], ["3"], ["12"]);

        Run(workbook, sheet);

        Assert.True(StyleOf(workbook, sheet, 1, 0).NumberFormat.IsGeneral);
    }

    [Fact]
    public void Large_whole_numbers_get_a_thousands_separator()
    {
        var workbook = Book(out var sheet);
        Fill(sheet, ["Count"], ["4200"], ["12"]);

        Run(workbook, sheet);

        Assert.Equal("#,##0", StyleOf(workbook, sheet, 1, 0).NumberFormat.Code);
    }

    [Fact]
    public void Percentages_and_currency_are_read_as_what_they_are()
    {
        var workbook = Book(out var sheet);
        Fill(sheet,
            ["Share", "Price"],
            ["45.5%", "$12.00"],
            ["54.5%", "$8.50"]);

        Run(workbook, sheet);

        Assert.Equal(0.455, sheet.GetValue(1, 0).AsNumber, 10);
        Assert.Equal("0.0%", StyleOf(workbook, sheet, 1, 0).NumberFormat.Code);
        Assert.Equal(12d, sheet.GetValue(1, 1).AsNumber);
        Assert.Equal(NumberFormatKind.Currency, StyleOf(workbook, sheet, 1, 1).NumberFormat.Kind);
    }

    [Fact]
    public void Dates_typed_as_text_become_dates()
    {
        var workbook = Book(out var sheet);
        Fill(sheet, ["When"], ["2026-01-05"], ["2026-02-06"]);

        Run(workbook, sheet);

        Assert.True(sheet.GetValue(1, 0).IsNumber);
        Assert.Equal(NumberFormatKind.Date, StyleOf(workbook, sheet, 1, 0).NumberFormat.Kind);
    }

    [Fact]
    public void An_identifier_column_keeps_its_leading_zeros()
    {
        var workbook = Book(out var sheet);
        Fill(sheet, ["Part"], ["007"], ["0912"]);

        Run(workbook, sheet);

        Assert.Equal("007", sheet.GetValue(1, 0).AsText);
        Assert.True(StyleOf(workbook, sheet, 1, 0).NumberFormat.IsGeneral);
    }

    [Fact]
    public void One_stray_identifier_stops_the_whole_column_being_coerced()
    {
        var workbook = Book(out var sheet);
        Fill(sheet, ["Code"], ["12"], ["007"]);

        Run(workbook, sheet);

        Assert.Equal("12", sheet.GetValue(1, 0).AsText);
        Assert.Equal("007", sheet.GetValue(2, 0).AsText);
    }

    /// <summary>
    /// Dates and plain numbers in one column is the case where coercing would be worse than leaving
    /// it: one column takes one format, and a date under a numeric one is a five-digit serial.
    /// </summary>
    [Fact]
    public void A_column_mixing_dates_and_numbers_is_left_alone()
    {
        var workbook = Book(out var sheet);
        Fill(sheet, ["Value"], ["2026-01-05"], ["42"]);

        Run(workbook, sheet);

        Assert.Equal("2026-01-05", sheet.GetValue(1, 0).AsText);
        Assert.Equal("42", sheet.GetValue(2, 0).AsText);
        Assert.True(StyleOf(workbook, sheet, 1, 0).NumberFormat.IsGeneral);
    }

    [Fact]
    public void A_mixed_column_is_never_half_converted()
    {
        var workbook = Book(out var sheet);
        Fill(sheet, ["Note"], ["12"], ["pending"]);

        Run(workbook, sheet);

        Assert.Equal("12", sheet.GetValue(1, 0).AsText);
        Assert.Equal("pending", sheet.GetValue(2, 0).AsText);
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_even_when_the_column_stays_text()
    {
        var workbook = Book(out var sheet);
        Fill(sheet, ["Name"], ["  West  "], ["East"]);

        Run(workbook, sheet);

        Assert.Equal("West", sheet.GetValue(1, 0).AsText);
    }

    [Fact]
    public void A_whitespace_only_cell_is_cleared()
    {
        var workbook = Book(out var sheet);
        Fill(sheet, ["Name"], ["West"], ["   "]);

        Run(workbook, sheet);

        Assert.True(sheet.GetValue(2, 0).IsEmpty);
    }

    [Fact]
    public void Formulas_are_never_rewritten()
    {
        var workbook = Book(out var sheet);
        Fill(sheet, ["Region", "Revenue"], ["West", "1200"], ["East", "900"]);
        sheet.SetFormula(3, 1, "SUM(B2:B3)", CellValue.Number(2100));

        Run(workbook, sheet);

        Assert.Equal("SUM(B2:B3)", sheet.Find(3, 1)!.Formula);
        Assert.Equal(2100d, sheet.GetValue(3, 1).AsNumber);
    }

    [Fact]
    public void A_chosen_number_format_survives()
    {
        var workbook = Sales(out var sheet);
        var chosen = new NumberFormat("0.0000");
        var cell = sheet.GetOrCreate(1, 1);
        cell.StyleId = workbook.Styles.Intern(CellStyle.Default.WithNumberFormat(chosen));

        Run(workbook, sheet);

        Assert.Equal("0.0000", StyleOf(workbook, sheet, 1, 1).NumberFormat.Code);
        Assert.Equal("#,##0.00", StyleOf(workbook, sheet, 2, 1).NumberFormat.Code);
    }

    [Fact]
    public void Body_alignment_returns_to_the_type_driven_default()
    {
        var workbook = Sales(out var sheet);
        var cell = sheet.GetOrCreate(1, 0);
        cell.StyleId = workbook.Styles.Intern(
            CellStyle.Default.WithAlignment(new Alignment(HorizontalAlignment.Right)));

        Run(workbook, sheet);

        Assert.Equal(HorizontalAlignment.General, StyleOf(workbook, sheet, 1, 0).Alignment.Horizontal);
    }

    [Fact]
    public void Columns_are_fitted_to_their_contents()
    {
        var workbook = Book(out var sheet);
        Fill(sheet, ["Region"], ["A considerably longer label than the default width"], ["East"]);

        Run(workbook, sheet);

        Assert.True(sheet.GetColumnWidth(0) > Defaults.ColumnWidth,
            $"expected a widened column, got {sheet.GetColumnWidth(0)}");
    }

    [Fact]
    public void A_wrapped_cell_does_not_drag_its_column_wide()
    {
        var workbook = Book(out var sheet);
        Fill(sheet, ["Note"], ["a very long wrapped note that folds at the column edge"], ["ok"]);
        var wrapped = sheet.GetOrCreate(1, 0);
        wrapped.StyleId = workbook.Styles.Intern(
            CellStyle.Default.WithAlignment(new Alignment(WrapText: true)));

        Run(workbook, sheet);

        Assert.True(sheet.GetColumnWidth(0) < 200, $"wrapped text should abstain, got {sheet.GetColumnWidth(0)}");
    }

    [Fact]
    public void Planning_changes_nothing_until_the_command_runs()
    {
        var workbook = Sales(out var sheet);

        var plan = CleanUp.ForSheet(workbook, sheet, Us);

        Assert.False(plan.IsEmpty);
        Assert.Equal("1200.50", sheet.GetValue(1, 1).AsText);
        Assert.Equal(0, sheet.FrozenRows);
    }

    [Fact]
    public void Undo_puts_back_values_styles_widths_and_panes()
    {
        var workbook = Sales(out var sheet);
        double width = sheet.GetColumnWidth(0);
        var command = CleanUp.ForSheet(workbook, sheet, Us).ToCommand(CleanUp.SheetLabel)!;

        command.Apply(workbook);
        command.Undo(workbook);

        Assert.Equal("1200.50", sheet.GetValue(1, 1).AsText);
        Assert.False(StyleOf(workbook, sheet, 0, 0).Font.Bold);
        Assert.Equal(width, sheet.GetColumnWidth(0));
        Assert.Equal(0, sheet.FrozenRows);
    }

    [Fact]
    public void Running_it_twice_is_a_no_op_the_second_time()
    {
        var workbook = Sales(out var sheet);
        Run(workbook, sheet);

        var second = CleanUp.ForSheet(workbook, sheet, Us);

        Assert.True(second.IsEmpty, second.Summary);
    }

    [Fact]
    public void Each_data_block_on_a_sheet_is_cleaned_on_its_own()
    {
        var workbook = Book(out var sheet);
        Fill(sheet, ["Region", "Revenue"], ["West", "1200"]);
        sheet.SetValue(5, 0, CellValue.Text("Owner"));
        sheet.SetValue(5, 1, CellValue.Text("Tasks"));
        sheet.SetValue(6, 0, CellValue.Text("Ada"));
        sheet.SetValue(6, 1, CellValue.Text("4"));

        Run(workbook, sheet);

        Assert.True(StyleOf(workbook, sheet, 0, 0).Font.Bold);
        Assert.True(StyleOf(workbook, sheet, 5, 0).Font.Bold);
        Assert.Equal(4d, sheet.GetValue(6, 1).AsNumber);

        // Only the topmost block's header is what the frozen pane holds still.
        Assert.Equal(1, sheet.FrozenRows);
    }

    [Fact]
    public void A_whole_workbook_clean_up_reports_a_line_per_sheet()
    {
        var workbook = Sales(out var first);
        var second = workbook.AddSheet("Sheet2");
        second.SetValue(0, 0, CellValue.Text("Name"));
        second.SetValue(1, 0, CellValue.Text("Ada"));
        second.SetValue(0, 1, CellValue.Text("Score"));
        second.SetValue(1, 1, CellValue.Text("9"));

        var plan = CleanUp.ForWorkbook(workbook, Us);
        plan.ToCommand(CleanUp.WorkbookLabel)!.Apply(workbook);

        Assert.Equal(2, plan.Notes.Count);
        Assert.Contains(plan.Notes, n => n.Sheet == first.Name);
        Assert.Contains(plan.Notes, n => n.Sheet == "Sheet2");
        Assert.Equal(9d, second.GetValue(1, 1).AsNumber);
    }

    /// <summary>
    /// The one that catches a formatting move Clean Up can make but the file cannot keep: run it,
    /// save, reopen, and there must be nothing left to do. Without this, a `cellestra clean-up` that
    /// reported work every single time would look like a bug in Clean Up rather than a gap in what
    /// round-trips.
    /// </summary>
    [Fact]
    public void Nothing_is_left_to_do_after_a_save_and_reopen()
    {
        var workbook = Sales(out var sheet);
        CleanUp.ForSheet(workbook, sheet, Us).ToCommand(CleanUp.SheetLabel)!.Apply(workbook);

        byte[] bytes = OdsWriter.WriteToBytes(workbook);
        using var stream = new MemoryStream(bytes);
        var reopened = OdsReader.Read(stream);

        var again = CleanUp.ForSheet(reopened, reopened.ActiveSheet, Us);
        Assert.True(again.IsEmpty, again.Summary);
    }

    [Fact]
    public void An_empty_sheet_has_nothing_to_clean()
    {
        var workbook = Book(out var sheet);

        var plan = CleanUp.ForSheet(workbook, sheet, Us);

        Assert.True(plan.IsEmpty);
        Assert.Equal("Nothing to clean up.", plan.Summary);
        Assert.Null(plan.ToCommand(CleanUp.SheetLabel));
    }
}
