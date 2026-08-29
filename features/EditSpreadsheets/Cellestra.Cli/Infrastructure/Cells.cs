using System.Globalization;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Puffinote.Components.Core.Cli;

namespace Cellestra.Cli.Infrastructure;

/// <summary>
/// Address/range/sheet resolution and value display shared by the read and edit commands. Cell
/// addressing matches the app: <c>Sheet1.B2</c>, ranges <c>Sheet1.A1:C10</c>; omit the sheet to
/// mean the one named by <c>--sheet</c>, else the active/first sheet.
/// </summary>
public static class Cells
{
    /// <summary>The sheet named by <paramref name="name"/>, or the active sheet when null/empty.</summary>
    public static Worksheet PickSheet(Workbook workbook, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return workbook.ActiveSheet;
        }

        return workbook.FindSheet(name)
            ?? throw new CliException($"No sheet named '{name}'. Available: {SheetList(workbook)}.");
    }

    /// <summary>
    /// Resolve a cell reference to its sheet and address (sheet prefix wins over --sheet). A named
    /// range or table name that covers exactly one cell resolves too.
    /// </summary>
    public static (Worksheet Sheet, CellAddress Address) ResolveCell(Workbook workbook, string reference, string? defaultSheet = null)
    {
        if (!CellAddress.TryParse(reference, out var address))
        {
            if (ResolveName(workbook, reference, defaultSheet) is { } named)
            {
                if (named.Range.RowCount != 1 || named.Range.ColumnCount != 1)
                {
                    throw new CliException($"'{reference}' is a range ({named.Range}), not a single cell.");
                }

                return (named.Sheet, named.Range.TopLeft);
            }

            throw new CliException($"'{reference}' is not a valid cell reference (e.g. Sheet1.B2 or B2) or a known name.");
        }

        var sheet = address.Sheet is not null
            ? workbook.FindSheet(address.Sheet) ?? throw new CliException($"No sheet named '{address.Sheet}'.")
            : PickSheet(workbook, defaultSheet);
        return (sheet, address);
    }

    /// <summary>
    /// Resolve a range reference to its sheet and range (sheet prefix wins over --sheet). A named
    /// range or a table name is an address too, so <c>cat --range Spend</c> works.
    /// </summary>
    public static (Worksheet Sheet, CellRange Range) ResolveRange(Workbook workbook, string reference, string? defaultSheet = null)
    {
        if (!CellRange.TryParse(reference, out var range))
        {
            return ResolveName(workbook, reference, defaultSheet)
                ?? throw new CliException($"'{reference}' is not a valid range (e.g. Sheet1.A1:C10 or A1:C10), a named range, or a table.");
        }

        var sheet = range.Sheet is not null
            ? workbook.FindSheet(range.Sheet) ?? throw new CliException($"No sheet named '{range.Sheet}'.")
            : PickSheet(workbook, defaultSheet);
        return (sheet, range);
    }

    /// <summary>The cell's displayed value (number format + locale), the same string the GUI shows.</summary>
    public static string Display(Workbook workbook, Worksheet sheet, int row, int column, CultureInfo culture)
    {
        var cell = sheet.Find(row, column);
        if (cell is null || cell.Value.IsEmpty)
        {
            return string.Empty;
        }

        var format = workbook.Styles.Get(cell.StyleId).NumberFormat;
        return NumberFormatter.Format(cell.Value, format, culture);
    }

    /// <summary>The cell's formula source (with leading <c>=</c>), or its display value if it isn't a formula.</summary>
    public static string FormulaOrValue(Workbook workbook, Worksheet sheet, int row, int column, CultureInfo culture)
    {
        var cell = sheet.Find(row, column);
        return cell is { IsFormula: true } ? "=" + cell.Formula : Display(workbook, sheet, row, column, culture);
    }

    /// <summary>A named range (workbook-scoped, or scoped to the default sheet) or a table, by name.</summary>
    public static (Worksheet Sheet, CellRange Range)? ResolveName(Workbook workbook, string name, string? defaultSheet)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            return null;
        }

        var home = string.IsNullOrEmpty(defaultSheet) ? workbook.ActiveSheet : workbook.FindSheet(defaultSheet);
        var named = workbook.FindNamedRange(name, home?.Name);
        if (named is not null)
        {
            var target = named.Target;
            var sheet = target.Sheet is not null
                ? workbook.FindSheet(target.Sheet) ?? throw new CliException($"Named range '{name}' points at a missing sheet '{target.Sheet}'.")
                : home ?? workbook.ActiveSheet;
            return (sheet, target.WithSheet(null)); // the tuple carries the sheet, as for a parsed A1:C3
        }

        // Tables: the default sheet first, then any sheet — a table name is unique within its sheet.
        var candidates = home is null ? workbook.Worksheets : [home, .. workbook.Worksheets.Where(w => w != home)];
        foreach (var sheet in candidates)
        {
            var table = sheet.Tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (table is not null)
            {
                return (sheet, table.Range.WithSheet(null));
            }
        }

        return null;
    }

    public static string SheetList(Workbook workbook) =>
        string.Join(", ", workbook.Worksheets.Select(s => s.Name));

    /// <summary>Build a rectangular block of display (or formula) strings for rendering.</summary>
    public static string[][] BuildGrid(Workbook workbook, Worksheet sheet, CellRange range, CultureInfo culture, bool formulas = false)
    {
        var rows = new string[range.RowCount][];
        for (int r = 0; r < range.RowCount; r++)
        {
            var cols = new string[range.ColumnCount];
            for (int c = 0; c < range.ColumnCount; c++)
            {
                int row = range.Top + r;
                int column = range.Left + c;
                cols[c] = formulas
                    ? FormulaOrValue(workbook, sheet, row, column, culture)
                    : Display(workbook, sheet, row, column, culture);
            }

            rows[r] = cols;
        }

        return rows;
    }

    /// <summary>The sheet's used range, or a 1×1 A1 range when the sheet is empty.</summary>
    public static CellRange UsedRangeOrA1(Worksheet sheet) =>
        sheet.GetUsedRange() ?? new CellRange(new CellAddress(0, 0));
}
