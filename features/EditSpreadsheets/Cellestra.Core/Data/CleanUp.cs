using System.Globalization;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Core.Values;

namespace Cellestra.Core.Data;

/// <summary>What Clean Up changed on one worksheet, in the words the report and the status line use.</summary>
public readonly record struct CleanUpNote(string Sheet, string Description)
{
    public override string ToString() => $"{Sheet}: {Description}";
}

/// <summary>
/// What <see cref="CleanUp"/> would do, worked out without touching the workbook: a line per sheet
/// for the reader, and the edits that carry it out. Nothing has happened until
/// <see cref="ToCommand"/>'s command is executed — which is what lets <c>--dry-run</c> report the
/// same plan the app would apply, from the same code.
/// </summary>
public sealed class CleanUpPlan
{
    internal CleanUpPlan(IReadOnlyList<CleanUpNote> notes, IReadOnlyList<IEditCommand> edits)
    {
        Notes = notes;
        Edits = edits;
    }

    public IReadOnlyList<CleanUpNote> Notes { get; }

    public IReadOnlyList<IEditCommand> Edits { get; }

    public bool IsEmpty => Edits.Count == 0;

    /// <summary>One line naming every sheet the plan touches, or that there is nothing to do.</summary>
    public string Summary => Notes.Count == 0
        ? "Nothing to clean up."
        : string.Join("; ", Notes.Select(n => n.ToString()));

    /// <summary>
    /// The whole plan as a single undoable step, or null when there is nothing to do. Always a
    /// composite, even for one edit, so the menu reads "Undo Clean Up Sheet" rather than naming
    /// whichever part happened to be the only one.
    /// </summary>
    public IEditCommand? ToCommand(string label) =>
        Edits.Count == 0 ? null : EditCommands.Composite(label, Edits);
}

/// <summary>
/// Clean Up (PRD §8.17): reads what is actually in a sheet and formats it the way someone would if
/// they had the patience — a styled, frozen header row, one number format per column inferred from
/// that column's values, alignment back to the type-driven default, columns fitted to their
/// contents, and the two data tidies that are unambiguously safe (stray whitespace, numbers stored
/// as text).
///
/// <para>It is deliberately conservative about the things a spreadsheet cannot un-guess. It never
/// touches a formula, never overwrites a number format someone chose (only a still-General one), and
/// converts text to numbers only when the <i>whole</i> column agrees — one "007" in a column of
/// labels stays the label it is. Banded rows and rules are not here on purpose: static fills are the
/// only way to draw them, since conditional formatting is a v1 non-goal (PRD §9).</para>
/// </summary>
public static class CleanUp
{
    /// <summary>Undo label for a single-sheet clean-up — the shared name for this feature (see AGENTS "Shared vocabulary").</summary>
    public const string SheetLabel = "Clean Up Sheet";

    /// <summary>Undo label for a whole-workbook clean-up.</summary>
    public const string WorkbookLabel = "Clean Up Workbook";

    // Matches the grid's own auto-fit (SpreadsheetGrid.Autofit) so a column fitted by Clean Up and a
    // column fitted by double-clicking its border land on the same width.
    private const double WidthPadding = 10d;
    private const double MinWidth = 24d;
    private const double MaxWidth = 600d;

    /// <summary>Below this, a column of whole numbers reads fine as it is and gets no thousands separator.</summary>
    private const double ThousandsThreshold = 1000d;

    /// <summary>What a currency column shows regardless of what was typed — cents.</summary>
    private const int ConventionalDecimals = 2;

    private const int MaxInferredDecimals = 4;

    /// <summary>A digit string longer than this cannot survive a double, so it is an identifier, not a number.</summary>
    private const int MaxSafeDigits = 15;

    public static CleanUpPlan ForSheet(Workbook workbook, Worksheet sheet, CultureInfo? culture = null, ITextMeasurer? measurer = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return Plan(workbook, [sheet], culture, measurer);
    }

    public static CleanUpPlan ForWorkbook(Workbook workbook, CultureInfo? culture = null, ITextMeasurer? measurer = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        return Plan(workbook, workbook.Worksheets, culture, measurer);
    }

    /// <summary>Work out what Clean Up would do to each of <paramref name="sheets"/>, changing nothing.</summary>
    public static CleanUpPlan Plan(Workbook workbook, IEnumerable<Worksheet> sheets, CultureInfo? culture = null, ITextMeasurer? measurer = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheets);

        var effective = culture ?? workbook.Settings.EffectiveLocale;
        var measure = measurer ?? ApproximateTextMeasurer.Instance;
        var notes = new List<CleanUpNote>();
        var edits = new List<IEditCommand>();

        foreach (var sheet in sheets)
        {
            // A chart sheet carries a chart, not a grid — there is nothing here to read or format.
            if (!sheet.IsChartSheet)
            {
                PlanSheet(workbook, sheet, effective, measure, notes, edits);
            }
        }

        return new CleanUpPlan(notes, edits);
    }

    /// <summary>
    /// The contiguous blocks of data on a sheet — one per "current region" (the rectangle Sort and
    /// Table already act on), found by walking the populated cells and claiming each region as it is
    /// discovered. Walking the cells rather than the used range matters: a sheet with one sized row
    /// at the bottom has a used range of millions of positions and a handful of cells.
    /// </summary>
    public static IReadOnlyList<CellRange> Blocks(Worksheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var claimed = new HashSet<(int Row, int Column)>();
        var blocks = new List<CellRange>();
        var populated = sheet.EnumerateCells()
            .Where(entry => !entry.Cell.Value.IsEmpty)
            .OrderBy(entry => entry.Row)
            .ThenBy(entry => entry.Column);

        foreach (var (row, column, _) in populated)
        {
            if (!claimed.Add((row, column)))
            {
                continue;
            }

            var region = DataRegion.CurrentRegion(sheet, row, column);
            foreach (var position in region.Positions())
            {
                claimed.Add(position);
            }

            // A lone cell is not a block: there is no column to infer a format for and no header to
            // find, and fitting one stray label's column is not what anyone means by cleaning up.
            if (region.RowCount * region.ColumnCount > 1)
            {
                blocks.Add(region);
            }
        }

        return blocks;
    }

    private static void PlanSheet(
        Workbook workbook,
        Worksheet sheet,
        CultureInfo culture,
        ITextMeasurer measurer,
        List<CleanUpNote> notes,
        List<IEditCommand> edits)
    {
        var blocks = Blocks(sheet);
        if (blocks.Count == 0)
        {
            return;
        }

        var values = new Dictionary<(int Row, int Column), CellValue>();
        var styles = new Dictionary<(int Row, int Column), CellStyle>();
        var widths = new Dictionary<int, double>();
        int tidied = 0, formattedColumns = 0, headerRows = 0, realigned = 0;
        int? freezeRows = null;

        CellStyle Current(int row, int column) =>
            styles.TryGetValue((row, column), out var planned)
                ? planned
                : workbook.Styles.Get(sheet.EffectiveStyleId(row, column));

        // Returns whether this was a change, so the report counts what Clean Up did rather than what
        // it looked at: run it twice and the second run has nothing to say.
        bool Restyle(int row, int column, CellStyle style)
        {
            if (Current(row, column).Equals(style))
            {
                return false;
            }

            styles[(row, column)] = style;
            return true;
        }

        foreach (var block in blocks)
        {
            bool hasHeader = HasHeader(sheet, block, culture);
            if (hasHeader)
            {
                if (StyleHeader(block, Current, Restyle))
                {
                    headerRows++;
                }

                // Only the topmost block gets the freeze: a second table further down the sheet is not
                // what the pane is holding still, and freezing to it would scroll the first one away.
                freezeRows ??= block.Top + 1;
            }

            int bodyTop = hasHeader ? block.Top + 1 : block.Top;
            for (int column = block.Left; column <= block.Right; column++)
            {
                var scan = ScanColumn(sheet, column, bodyTop, block.Bottom, culture);
                tidied += ApplyColumnTidy(column, scan, values);
                if (ApplyColumnFormat(column, bodyTop, block.Bottom, scan, culture, values, sheet, Current, Restyle))
                {
                    formattedColumns++;
                }

                realigned += AlignBody(column, bodyTop, block.Bottom, sheet, Current, Restyle);
            }

            FitColumns(sheet, block, culture, measurer, values, Current, widths);
        }

        if (freezeRows is { } rows && (sheet.FrozenRows != rows || sheet.FrozenColumns != 0))
        {
            edits.Add(EditCommands.SetFrozenPanes(sheet.Name, rows, 0));
        }
        else
        {
            freezeRows = null; // nothing to report: the panes are already where Clean Up would put them
        }

        var touched = values.Keys.Concat(styles.Keys).Distinct().OrderBy(p => p.Row).ThenBy(p => p.Column).ToList();
        if (touched.Count > 0)
        {
            edits.Insert(0, new CellSnapshotCommand("Clean Up Cells", sheet.Name, touched, ws =>
            {
                foreach (var (position, value) in values)
                {
                    ws.SetValue(position.Row, position.Column, value);
                }

                foreach (var (position, style) in styles)
                {
                    var cell = ws.GetOrCreate(position.Row, position.Column);
                    cell.StyleId = workbook.Styles.Intern(style);
                    ws.PruneIfBlank(position.Row, position.Column, cell);
                }
            }));
        }

        foreach (var (column, width) in widths.OrderBy(w => w.Key))
        {
            edits.Add(EditCommands.SetColumnWidth(sheet.Name, column, width));
        }

        var parts = new List<string>();
        Add(parts, tidied, "cell", "tidied");
        Add(parts, formattedColumns, "column", "formatted");
        Add(parts, realigned, "cell", "realigned");
        Add(parts, headerRows, "header row", "styled");
        if (freezeRows is not null)
        {
            parts.Add("froze the header row");
        }

        Add(parts, widths.Count, "column", "resized");
        if (parts.Count > 0)
        {
            notes.Add(new CleanUpNote(sheet.Name, string.Join(", ", parts)));
        }
    }

    private static void Add(List<string> parts, int count, string noun, string verb)
    {
        if (count > 0)
        {
            parts.Add(count == 1 ? $"{verb} 1 {noun}" : $"{verb} {count} {noun}s");
        }
    }

    // ---- Header -----------------------------------------------------------

    /// <summary>
    /// Whether the block's first row is a header — the same question
    /// <see cref="DataRegion.LooksLikeHeader"/> asks, asked through the coercion Clean Up is about to
    /// do. That matters for exactly the sheets Clean Up is for: a CSV import stores every cell as
    /// text, so "the body holds something non-text" finds nothing and every header goes unrecognized.
    /// Here a body cell that <i>would</i> become a number counts as the number it is about to be, and
    /// a first-row cell that is itself a number is not a label.
    /// </summary>
    private static bool HasHeader(Worksheet sheet, CellRange block, CultureInfo culture)
    {
        if (block.RowCount < 2)
        {
            return false;
        }

        bool headerAllLabels = true;
        bool bodyHasNumbers = false;
        for (int column = block.Left; column <= block.Right; column++)
        {
            var header = sheet.GetValue(block.Top, column);
            if (!header.IsEmpty && (!header.IsText || ReadsAsNumber(header.AsText, culture)))
            {
                headerAllLabels = false;
            }

            for (int row = block.Top + 1; row <= block.Bottom; row++)
            {
                var value = sheet.GetValue(row, column);
                if (value.IsEmpty)
                {
                    continue;
                }

                bodyHasNumbers |= !value.IsText || ReadsAsNumber(value.AsText, culture);
            }
        }

        return headerAllLabels && bodyHasNumbers;
    }

    private static bool ReadsAsNumber(string text, CultureInfo culture)
    {
        string trimmed = text.Trim();
        return !IsIdentifier(trimmed) && TryCoerce(trimmed, culture, out _);
    }

    /// <summary>
    /// Make the header row read as one: bold, centred, on the same grey the grid's light-theme table
    /// header band uses (<see cref="Defaults.TableHeaderFillArgb"/>), so a cleaned-up block and a
    /// table look like they came from the same program. Unlike a table's — whose band is theme chrome —
    /// this fill is deliberately real formatting: writing what the sheet should look like is Clean
    /// Up's entire job (§8.17).
    /// </summary>
    private static bool StyleHeader(CellRange block, Func<int, int, CellStyle> current, Func<int, int, CellStyle, bool> restyle)
    {
        bool changed = false;
        for (int column = block.Left; column <= block.Right; column++)
        {
            var style = current(block.Top, column);
            var font = style.Font.WithBold(true) with { Color = new ArgbColor(Defaults.TableHeaderTextArgb) };
            changed |= restyle(block.Top, column, style
                .WithFont(font)
                .WithFill(Fill.Solid(new ArgbColor(Defaults.TableHeaderFillArgb)))
                .WithAlignment(style.Alignment with { Horizontal = HorizontalAlignment.Center }));
        }

        return changed;
    }

    // ---- Column analysis --------------------------------------------------

    /// <summary>How a text cell would read once coerced, and what that says about its column's format.</summary>
    private readonly record struct Coercion(CellValue Value, NumberFormat? DateFormat, int Decimals, NumberFormatKind Kind);

    /// <summary>What one column's body rows turned out to hold.</summary>
    private sealed class ColumnScan
    {
        /// <summary>Rows whose text only needed its surrounding whitespace removed.</summary>
        public Dictionary<int, string> Trims { get; } = [];

        /// <summary>Rows whose text is a number in disguise, and what it would become.</summary>
        public Dictionary<int, Coercion> Coercions { get; } = [];

        /// <summary>False as soon as one body cell holds something that isn't (or can't become) a number.</summary>
        public bool AllNumeric { get; set; } = true;

        /// <summary>True once a text cell is found that a number format must never be guessed from — see <see cref="IsIdentifier"/>.</summary>
        public bool Blocked { get; set; }

        public int NumericCount { get; set; }

        public int DateCount { get; set; }

        public int PercentCount { get; set; }

        public int CurrencyCount { get; set; }

        public bool AllIntegers { get; set; } = true;

        public double MaxMagnitude { get; set; }

        public int Decimals { get; set; }

        public string? DateCode { get; set; }

        /// <summary>
        /// Whether the column reads as one kind of number all the way down — the condition for both
        /// coercing its text and giving it a format. A column that mixes dates with plain numbers does
        /// not qualify: there is one format to give it and a date under a numeric one is a five-digit
        /// serial, so the safe reading is to leave the whole column as it was found.
        /// </summary>
        public bool IsUniformlyNumeric =>
            AllNumeric && !Blocked && NumericCount > 0 && (DateCount == 0 || DateCount == NumericCount);
    }

    private static ColumnScan ScanColumn(Worksheet sheet, int column, int top, int bottom, CultureInfo culture)
    {
        var scan = new ColumnScan();
        for (int row = top; row <= bottom; row++)
        {
            if (sheet.Find(row, column) is not { } cell || cell.Value.IsEmpty)
            {
                continue;
            }

            // A formula's result votes on the column's format, but the formula itself is never rewritten:
            // Clean Up tidies what was typed, and nobody typed a computed value.
            if (cell.Formula is not null)
            {
                Contribute(scan, cell.Value, null);
                continue;
            }

            if (!cell.Value.IsText)
            {
                Contribute(scan, cell.Value, null);
                continue;
            }

            string text = cell.Value.AsText;
            string trimmed = text.Trim();
            if (trimmed.Length != text.Length)
            {
                scan.Trims[row] = trimmed;
            }

            if (trimmed.Length == 0)
            {
                continue; // whitespace only: the trim clears it, and an empty cell votes on nothing
            }

            if (IsIdentifier(trimmed))
            {
                scan.Blocked = true;
                scan.AllNumeric = false;
                continue;
            }

            if (TryCoerce(trimmed, culture, out var coercion))
            {
                scan.Coercions[row] = coercion;
                Contribute(scan, coercion.Value, coercion);
            }
            else
            {
                scan.AllNumeric = false;
            }
        }

        return scan;
    }

    private static void Contribute(ColumnScan scan, CellValue value, Coercion? coercion)
    {
        if (!value.IsNumber)
        {
            scan.AllNumeric = false;
            return;
        }

        scan.NumericCount++;
        double number = value.AsNumber;
        scan.MaxMagnitude = Math.Max(scan.MaxMagnitude, Math.Abs(number));
        if (number != Math.Floor(number))
        {
            scan.AllIntegers = false;
        }

        if (coercion is { } from)
        {
            // Take the decimal count from what was typed, not from the value: "45.5%" is one decimal
            // place on screen even though the number behind it is 0.455.
            scan.Decimals = Math.Max(scan.Decimals, from.Decimals);
            switch (from.Kind)
            {
                case NumberFormatKind.Date:
                    scan.DateCount++;
                    scan.DateCode ??= from.DateFormat?.Code;
                    break;
                case NumberFormatKind.Percent:
                    scan.PercentCount++;
                    break;
                case NumberFormatKind.Currency:
                    scan.CurrencyCount++;
                    break;
                default:
                    break;
            }

            return;
        }

        scan.Decimals = Math.Max(scan.Decimals, FractionDigits(number));
    }

    /// <summary>
    /// Rewrite the column's text cells: trim the stray whitespace, and — only when the whole column
    /// agrees that it is numeric — turn the numbers-stored-as-text into real numbers. The all-or-nothing
    /// rule is the safety: a lone "007" among labels is an identifier and stays text, and a column that
    /// holds one such value is not coerced at all.
    /// </summary>
    private static int ApplyColumnTidy(int column, ColumnScan scan, Dictionary<(int Row, int Column), CellValue> values)
    {
        bool coerce = scan.IsUniformlyNumeric && scan.Coercions.Count > 0;
        int tidied = 0;

        if (coerce)
        {
            foreach (var (row, coercion) in scan.Coercions)
            {
                values[(row, column)] = coercion.Value;
                tidied++;
            }
        }

        foreach (var (row, trimmed) in scan.Trims)
        {
            if (coerce && scan.Coercions.ContainsKey(row))
            {
                continue; // the coercion already replaced this cell's value
            }

            values[(row, column)] = trimmed.Length == 0 ? CellValue.Empty : CellValue.Text(trimmed);
            tidied++;
        }

        return tidied;
    }

    /// <summary>
    /// Give the column one number format, inferred from what it holds, and stamp it on the body cells
    /// that are still General. Cells with a format of their own are left alone — someone chose that,
    /// and Clean Up formats what was never formatted rather than overruling what was.
    /// </summary>
    private static bool ApplyColumnFormat(
        int column,
        int top,
        int bottom,
        ColumnScan scan,
        CultureInfo culture,
        Dictionary<(int Row, int Column), CellValue> values,
        Worksheet sheet,
        Func<int, int, CellStyle> current,
        Func<int, int, CellStyle, bool> restyle)
    {
        if (InferFormat(scan, culture) is not { } format)
        {
            return false;
        }

        bool applied = false;
        for (int row = top; row <= bottom; row++)
        {
            bool willHoldValue = values.TryGetValue((row, column), out var planned)
                ? !planned.IsEmpty
                : sheet.Find(row, column) is { } cell && !cell.Value.IsEmpty;
            if (!willHoldValue)
            {
                continue;
            }

            var style = current(row, column);
            if (!style.NumberFormat.IsGeneral)
            {
                continue;
            }

            applied |= restyle(row, column, style.WithNumberFormat(format));
        }

        return applied;
    }

    private static NumberFormat? InferFormat(ColumnScan scan, CultureInfo culture)
    {
        if (!scan.IsUniformlyNumeric)
        {
            return null;
        }

        if (scan.DateCount == scan.NumericCount && scan.DateCode is { } dateCode)
        {
            return new NumberFormat(dateCode);
        }

        // The column reads to the most precise thing anyone typed in it, so no row silently loses a
        // digit — capped, because a stray 12-decimal float is not a column's precision.
        int decimals = Math.Min(scan.Decimals, MaxInferredDecimals);

        if (scan.PercentCount > 0)
        {
            return new NumberFormat(decimals == 0 ? "0%" : "0." + new string('0', decimals) + "%");
        }

        if (scan.CurrencyCount > 0)
        {
            // Money shows its cents even when every amount in the column happens to be round.
            return new NumberFormat(CurrencySymbolCode(culture) + "#,##0." + new string('0', Math.Max(decimals, ConventionalDecimals)));
        }

        if (scan.AllIntegers)
        {
            // Whole numbers below a thousand read fine as they are; a separator would be noise.
            return scan.MaxMagnitude >= ThousandsThreshold ? new NumberFormat("#,##0") : null;
        }

        // A column with decimals in it is money or a measurement far more often than not, and both
        // are read to two places — so one typed decimal still sets the column to two, and 900 lines
        // up under 1,200.50 rather than sitting a digit short of it.
        return new NumberFormat("#,##0." + new string('0', Math.Max(decimals, ConventionalDecimals)));
    }

    /// <summary>
    /// The currency symbol as a format code fragment. A symbol <c>NumberFormatParser</c> recognizes goes
    /// in bare, so the result classifies as Currency and the command bar's Currency toggle lights up on
    /// it; anything else is quoted, so that it at least *renders* as itself rather than being read as a
    /// pattern letter.
    /// </summary>
    private static string CurrencySymbolCode(CultureInfo culture)
    {
        string symbol = culture.NumberFormat.CurrencySymbol;
        return symbol.All(c => c is '$' or '€' or '£' or '¥' or '¤') ? symbol : $"\"{symbol}\"";
    }

    /// <summary>
    /// Put the body back on the type-driven default: General is left for text and right for numbers
    /// (PRD §8.8), which is the alignment that stays right when a column's contents change.
    /// </summary>
    private static int AlignBody(int column, int top, int bottom, Worksheet sheet, Func<int, int, CellStyle> current, Func<int, int, CellStyle, bool> restyle)
    {
        int realigned = 0;
        for (int row = top; row <= bottom; row++)
        {
            if (sheet.Find(row, column) is not { } cell || cell.Value.IsEmpty)
            {
                continue;
            }

            var style = current(row, column);
            if (style.Alignment.Horizontal != HorizontalAlignment.General &&
                restyle(row, column, style.WithAlignment(style.Alignment with { Horizontal = HorizontalAlignment.General })))
            {
                realigned++;
            }
        }

        return realigned;
    }

    // ---- Widths -----------------------------------------------------------

    /// <summary>
    /// Size each of the block's columns to the widest thing it will hold <i>after</i> the clean-up —
    /// measured against the planned value and the planned style, so a column that just became a date
    /// or picked up a bold header is fitted to what it is about to look like, not what it looked like.
    /// A wrapped cell abstains: its text was told to fold at the column's edge.
    /// </summary>
    private static void FitColumns(
        Worksheet sheet,
        CellRange block,
        CultureInfo culture,
        ITextMeasurer measurer,
        Dictionary<(int Row, int Column), CellValue> values,
        Func<int, int, CellStyle> current,
        Dictionary<int, double> widths)
    {
        for (int column = block.Left; column <= block.Right; column++)
        {
            double widest = 0;
            for (int row = block.Top; row <= block.Bottom; row++)
            {
                var value = values.TryGetValue((row, column), out var planned)
                    ? planned
                    : sheet.GetValue(row, column);
                if (value.IsEmpty)
                {
                    continue;
                }

                var style = current(row, column);
                if (style.Alignment.WrapText)
                {
                    continue;
                }

                string text = NumberFormatter.Format(value, style.NumberFormat, culture);
                widest = Math.Max(widest, measurer.Measure(text, style.Font));
            }

            double target = widest > 0 ? Math.Clamp(widest + WidthPadding, MinWidth, MaxWidth) : Defaults.ColumnWidth;
            if (Math.Abs(target - sheet.GetColumnWidth(column)) > 0.5)
            {
                widths[column] = target;
            }
        }
    }

    // ---- Reading what a text cell really is -------------------------------

    /// <summary>
    /// Text that looks numeric but must never become a number: a leading-zero run (<c>007</c>,
    /// <c>01924</c> — a part number, an area code, a zip) and a digit string too long for a double to
    /// hold exactly. Both survive a round trip as text and neither does as a number.
    /// </summary>
    private static bool IsIdentifier(string text)
    {
        int digits = 0;
        foreach (char c in text)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }

            digits++;
        }

        return digits > 0 && (digits > MaxSafeDigits || (digits > 1 && text[0] == '0'));
    }

    /// <summary>
    /// Whether this text is a number wearing a coat: a plain number, a currency amount, a percentage,
    /// or a date. Numbers, currency and dates go through <see cref="InputParser"/> — the same reading
    /// the cell editor gives what you type, so a cleaned-up cell and a retyped one land identically.
    /// Percentages are read here because typing one is not (yet) a thing the editor coerces.
    /// </summary>
    private static bool TryCoerce(string text, CultureInfo culture, out Coercion coercion)
    {
        coercion = default;
        if (text.Length == 0)
        {
            return false;
        }

        if (text[^1] == '%')
        {
            string body = text[..^1].TrimEnd();
            if (body.Length == 0 || !double.TryParse(body, NumberStyles.Any, culture, out double percent))
            {
                return false;
            }

            coercion = new Coercion(CellValue.Number(percent / 100d), null, FractionDigits(body, culture), NumberFormatKind.Percent);
            return true;
        }

        var parsed = InputParser.Parse(text, culture);
        if (parsed.IsFormula || !parsed.Value.IsNumber)
        {
            return false;
        }

        var kind = parsed.SuggestedFormat is not null
            ? NumberFormatKind.Date
            : LooksLikeCurrency(text, culture) ? NumberFormatKind.Currency : NumberFormatKind.Number;
        coercion = new Coercion(parsed.Value, parsed.SuggestedFormat, FractionDigits(text, culture), kind);
        return true;
    }

    private static bool LooksLikeCurrency(string text, CultureInfo culture)
    {
        string symbol = culture.NumberFormat.CurrencySymbol;
        return (symbol.Length > 0 && text.Contains(symbol, StringComparison.Ordinal))
            || text.Any(c => c is '$' or '€' or '£' or '¥' or '¤');
    }

    /// <summary>How many digits the typed text shows after its decimal separator.</summary>
    private static int FractionDigits(string text, CultureInfo culture)
    {
        string separator = culture.NumberFormat.NumberDecimalSeparator;
        int at = text.LastIndexOf(separator, StringComparison.Ordinal);
        if (at < 0)
        {
            return 0;
        }

        int digits = 0;
        for (int i = at + separator.Length; i < text.Length && char.IsAsciiDigit(text[i]); i++)
        {
            digits++;
        }

        return digits;
    }

    /// <summary>How many decimal places a stored number actually carries (General display, capped).</summary>
    private static int FractionDigits(double number)
    {
        string text = number.ToString("0.##########", CultureInfo.InvariantCulture);
        int dot = text.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? 0 : text.Length - dot - 1;
    }
}
