using Avalonia.Controls;
using Avalonia.Layout;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Desktop.Commands;
using Puffinote.Components.Overlays;
using NumberFormatter = Cellestra.Core.Styling.NumberFormatter;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// The Find / Find &amp; Replace tool (PRD §6.2), rendered as an internal modal. Find Next moves the
/// selection to the next matching cell (wrapping); Replace and Replace All rewrite text, formula, and
/// numeric cells (a numeric replacement is re-parsed, staying a number if it still looks like one).
/// Matching is case-insensitive over each cell's <em>match text</em>: for a formula cell that's the
/// formula source (<c>"="+Formula</c>) — matching Excel/Calc, which find and replace inside the
/// formula text — and otherwise the cell's displayed value. Match scope and replace scope are kept
/// consistent so a listed hit is exactly what gets rewritten. Lists every cell whose match text
/// contains the query; clicking one jumps the selection there. The bool result is unused (the dialog
/// acts on the grid directly).
/// </summary>
public sealed class FindReplaceDialog : ModalDialog<bool>
{
    private readonly IAppEnvironment _env;
    private readonly TextBox _find = new() { Width = 200 };
    private readonly TextBox _replace = new() { Width = 200 };
    private readonly TextBlock _status = new();
    private readonly ListBox _results = new() { MaxHeight = 220, MinWidth = 320 };
    private readonly List<(int Row, int Column)> _matches = [];
    private bool _suppressNavigate;

    public FindReplaceDialog(IAppEnvironment env, bool showReplace)
    {
        _env = env;
        Title = showReplace ? "Find & Replace" : "Find";
        InitialFocus = _find;

        var findNext = new Button { Content = "Find Next", IsDefault = true };
        findNext.Click += (_, _) => FindNext();
        var replaceButton = new Button { Content = "Replace" };
        replaceButton.Click += (_, _) => ReplaceOne();
        var replaceAll = new Button { Content = "Replace All" };
        replaceAll.Click += (_, _) => ReplaceAll();
        var close = new Button { Content = "Close", IsCancel = true };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(findNext);
        if (showReplace)
        {
            buttons.Children.Add(replaceButton);
            buttons.Children.Add(replaceAll);
        }

        buttons.Children.Add(close);

        // Live results: every cell whose displayed value contains the query. Clicking one jumps there.
        _find.TextChanged += (_, _) => RunSearch();
        _results.SelectionChanged += (_, _) => NavigateToSelectedMatch();

        var panel = new StackPanel { Width = 360, Spacing = 10 };
        panel.Children.Add(Row("Find", _find));
        if (showReplace)
        {
            panel.Children.Add(Row("Replace", _replace));
        }

        panel.Children.Add(buttons);
        panel.Children.Add(_status);
        panel.Children.Add(new TextBlock { Text = "Matches (click to go there)" });
        panel.Children.Add(_results);
        DialogContent = panel;
    }

    private void RunSearch()
    {
        _matches.Clear();
        var rows = new List<string>();
        string query = _find.Text ?? string.Empty;
        if (query.Length > 0)
        {
            // Scan only populated cells (not every coordinate of the used rectangle) so a single far
            // cell can't make each keystroke walk millions of empty positions.
            var sheet = _env.Session.ActiveSheet;
            var hits = new List<(int Row, int Column, string Text)>();
            foreach (var (row, column, _) in sheet.EnumerateCells())
            {
                string text = MatchText(sheet, row, column);
                if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add((row, column, text));
                }
            }

            hits.Sort((a, b) => a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.Column.CompareTo(b.Column));
            foreach (var (row, column, text) in hits)
            {
                _matches.Add((row, column));
                rows.Add($"{new CellAddress(column, row)}    {Truncate(text, 48)}");
            }
        }

        _suppressNavigate = true;          // resetting ItemsSource fires SelectionChanged; don't treat that as a click
        _results.ItemsSource = rows;
        _suppressNavigate = false;
        _status.Text = query.Length == 0 ? string.Empty : $"{_matches.Count} match{(_matches.Count == 1 ? string.Empty : "es")}.";
    }

    private void NavigateToSelectedMatch()
    {
        if (_suppressNavigate)
        {
            return;
        }

        int index = _results.SelectedIndex;
        if (index >= 0 && index < _matches.Count)
        {
            var (row, column) = _matches[index];
            _env.Selection.MoveTo(row, column);
            _env.RevealActiveCell();
            _env.Refresh();
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>Test seam: run the live search for <paramref name="query"/> and return how many cells matched.</summary>
    internal int SearchForTest(string query)
    {
        _find.Text = query;
        RunSearch();
        return _matches.Count;
    }

    /// <summary>Test seam: set the find/replace text and run Replace All.</summary>
    internal void ReplaceAllForTest(string find, string replace)
    {
        _find.Text = find;
        _replace.Text = replace;
        ReplaceAll();
    }

    /// <summary>Test seam: click the match at <paramref name="index"/>, navigating the grid to it.</summary>
    internal void SelectMatchForTest(int index) => _results.SelectedIndex = index;

    private static StackPanel Row(string label, Control control) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children = { new TextBlock { Text = label, Width = 64, VerticalAlignment = VerticalAlignment.Center }, control },
    };

    private void FindNext()
    {
        string query = _find.Text ?? string.Empty;
        if (query.Length == 0)
        {
            return;
        }

        var sheet = _env.Session.ActiveSheet;
        var used = sheet.GetUsedRange();
        if (used is not { } range)
        {
            _status.Text = "No data to search.";
            return;
        }

        int startRow = _env.Selection.ActiveRow;
        int startColumn = _env.Selection.ActiveColumn;
        int total = range.RowCount * range.ColumnCount;
        int startIndex = ((startRow - range.Top) * range.ColumnCount) + (startColumn - range.Left);

        for (int step = 1; step <= total; step++)
        {
            int index = (startIndex + step) % total;
            int row = range.Top + (index / range.ColumnCount);
            int column = range.Left + (index % range.ColumnCount);
            if (MatchText(sheet, row, column).Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                _env.Selection.MoveTo(row, column);
                _env.RevealActiveCell();
                _env.Refresh();
                _status.Text = string.Empty;
                return;
            }
        }

        _status.Text = "No match found.";
    }

    private void ReplaceOne()
    {
        string query = _find.Text ?? string.Empty;
        if (query.Length == 0)
        {
            return;
        }

        var sheet = _env.Session.ActiveSheet;
        int row = _env.Selection.ActiveRow;
        int column = _env.Selection.ActiveColumn;
        if (MatchText(sheet, row, column).Contains(query, StringComparison.OrdinalIgnoreCase) &&
            ReplaceCell(sheet, row, column, query, _replace.Text ?? string.Empty))
        {
            _env.Refresh();
        }

        FindNext();
        RunSearch();
    }

    private void ReplaceAll()
    {
        string query = _find.Text ?? string.Empty;
        if (query.Length == 0)
        {
            return;
        }

        var sheet = _env.Session.ActiveSheet;

        // Snapshot the matching positions first (populated cells only), then replace — so mutating
        // cells during the loop can't disturb enumeration.
        var targets = new List<(int Row, int Column)>();
        foreach (var (row, column, _) in sheet.EnumerateCells())
        {
            if (MatchText(sheet, row, column).Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                targets.Add((row, column));
            }
        }

        int count = 0;
        foreach (var (row, column) in targets)
        {
            if (ReplaceCell(sheet, row, column, query, _replace.Text ?? string.Empty))
            {
                count++;
            }
        }

        _env.Refresh();
        RunSearch();
        _status.Text = $"Replaced {count} occurrence(s).";
    }

    /// <summary>Test seam: replace <paramref name="find"/> with <paramref name="replace"/> in one cell; returns whether it changed.</summary>
    internal bool ReplaceCellForTest(int row, int column, string find, string replace) =>
        ReplaceCell(_env.Session.ActiveSheet, row, column, find, replace);

    private bool ReplaceCell(Worksheet sheet, int row, int column, string find, string replace)
    {
        var cell = sheet.Find(row, column);
        if (cell is null)
        {
            return false;
        }

        if (cell.IsFormula)
        {
            // Match AND replace against the formula SOURCE text ("="+Formula), the same string Find
            // matched on (MatchText). Excel/Calc find & replace inside the formula text, so editing
            // `=A1+A2` to `=A1+A3` works and references stay valid; rewriting against the computed
            // display (as before) either no-oped or corrupted the formula. Re-commit through
            // SetInput so a result that no longer starts with "=" stops being a formula.
            string source = "=" + cell.Formula;
            if (!source.Contains(find, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string updated = source.Replace(find, replace, StringComparison.OrdinalIgnoreCase);
            _env.Session.Execute(EditCommands.SetInput(sheet.Name, row, column, updated, _env.Session.Workbook.Settings.EffectiveLocale));
            return true;
        }

        if (cell.Value.IsText)
        {
            string updated = cell.Value.AsText.Replace(find, replace, StringComparison.OrdinalIgnoreCase);
            _env.Session.Execute(EditCommands.SetInput(sheet.Name, row, column, updated, _env.Session.Workbook.Settings.EffectiveLocale));
            return true;
        }

        // Numbers (and booleans): replace against the same representation Find matched on — the cell's
        // displayed value (Display) — then commit through SetInput so the result is re-parsed. A still-
        // numeric result stays a number; anything else becomes text. Using Display here keeps Find and
        // Replace consistent: a value that shows up in the match list is the value we rewrite.
        string display = Display(sheet, row, column);
        if (display.Contains(find, StringComparison.OrdinalIgnoreCase))
        {
            string updated = display.Replace(find, replace, StringComparison.OrdinalIgnoreCase);
            _env.Session.Execute(EditCommands.SetInput(sheet.Name, row, column, updated, _env.Session.Workbook.Settings.EffectiveLocale));
            return true;
        }

        return false;
    }

    /// <summary>
    /// The text Find matches against (and Replace rewrites) for a cell, kept consistent between the
    /// two: a formula cell matches on its source text (<c>"="+Formula</c>, like Excel/Calc), any
    /// other cell on its displayed value.
    /// </summary>
    private string MatchText(Worksheet sheet, int row, int column)
    {
        var cell = sheet.Find(row, column);
        if (cell is null)
        {
            return string.Empty;
        }

        return cell.IsFormula ? "=" + cell.Formula : Display(sheet, row, column);
    }

    private string Display(Worksheet sheet, int row, int column)
    {
        var cell = sheet.Find(row, column);
        if (cell is null || cell.Value.IsEmpty)
        {
            return string.Empty;
        }

        var format = _env.Session.Workbook.Styles.Get(cell.StyleId).NumberFormat;
        return NumberFormatter.Format(cell.Value, format, _env.Session.Workbook.Settings.EffectiveLocale);
    }
}
