using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Core.Data;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// Table chrome (banded rows, header band, AutoFilter dropdowns) and data-validation
/// dropdowns/enforcement for the grid (PRD §8.10–§8.13). Drawn directly on the surface; the
/// dropdown popups live on a hit-testable overlay canvas.
/// </summary>
public sealed partial class SpreadsheetGrid
{
    private static readonly Color TableBorder = Color.FromRgb(0x4A, 0x7E, 0xBB);

    private readonly Canvas _popupLayer = new() { IsHitTestVisible = true };
    private readonly List<(Rect Rect, int Column, CellRange Range, bool HasHeader)> _filterButtons = new();
    private (Rect Rect, DataValidation Rule)? _validationButton;

    private readonly Dictionary<int, ColumnFilter> _columnFilters = new();
    private CellRange? _filterRange;
    private bool _popupOpen;

    private void InitChrome() => Children.Add(_popupLayer);

    // ---- Banding ----------------------------------------------------------

    private void DrawTableBanding(DrawingContext context, Worksheet sheet)
    {
        foreach (var table in sheet.Tables)
        {
            foreach (var (row, _, _) in _visibleRows)
            {
                if (row < table.Range.Top || row > table.Range.Bottom)
                {
                    continue;
                }

                if (!TryGetRegionRect(row, table.Range.Left, row, table.Range.Right, out var rowRect))
                {
                    continue;
                }

                bool isHeader = table.HasHeader && row == table.Range.Top;
                int dataIndex = row - table.Range.Top - (table.HasHeader ? 1 : 0);
                if (isHeader)
                {
                    context.FillRectangle(_palette.TableHeaderBand, rowRect);
                }
                else if (dataIndex % 2 == 1)
                {
                    context.FillRectangle(_palette.TableRowBand, rowRect);
                }
            }
        }
    }

    // ---- Filter & validation buttons --------------------------------------

    private void DrawTableChrome(DrawingContext context, Worksheet sheet)
    {
        _filterButtons.Clear();
        _validationButton = null;

        foreach (var table in sheet.Tables)
        {
            if (TryGetRegionRect(table.Range.Top, table.Range.Left, table.Range.Bottom, table.Range.Right, out var border))
            {
                context.DrawRectangle(null, new Pen(new SolidColorBrush(TableBorder), 1.2), border);
            }

            if (table.HasHeader)
            {
                DrawFilterButtons(context, table.Range, hasHeader: true);
            }
        }

        if (sheet.ActiveFilter is { } filter)
        {
            DrawFilterButtons(context, filter, hasHeader: true);
        }

        DrawValidationButton(context, sheet);
    }

    private void DrawFilterButtons(DrawingContext context, CellRange range, bool hasHeader)
    {
        int headerRow = range.Top;
        foreach (var (col, x, w) in _visibleColumns)
        {
            if (col < range.Left || col > range.Right)
            {
                continue;
            }

            if (!TryGetCellRect(headerRow, col, out var cellRect))
            {
                continue;
            }

            double size = Math.Min(14, cellRect.Height - 2);
            var button = new Rect(cellRect.Right - size - 2, cellRect.Y + (cellRect.Height - size) / 2, size, size);
            context.FillRectangle(_palette.HeaderFill, button);
            context.DrawRectangle(null, new Pen(new SolidColorBrush(TableBorder)), button);
            DrawGlyph(context, "▾", button);
            _filterButtons.Add((button, col, range, hasHeader));
        }
    }

    private void DrawValidationButton(DrawingContext context, Worksheet sheet)
    {
        var rule = sheet.FindValidation(Selection.ActiveRow, Selection.ActiveColumn);
        if (rule is not { Kind: ValidationKind.List })
        {
            return;
        }

        if (!TryGetCellRect(Selection.ActiveRow, Selection.ActiveColumn, out var cellRect))
        {
            return;
        }

        double size = Math.Min(16, cellRect.Height);
        var button = new Rect(cellRect.Right - size, cellRect.Bottom - size, size, size);
        context.FillRectangle(_palette.HeaderFill, button);
        context.DrawRectangle(null, _palette.GridLine, button);
        DrawGlyph(context, "▾", button);
        _validationButton = (button, rule);
    }

    private void DrawGlyph(DrawingContext context, string glyph, Rect rect)
    {
        var text = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Helvetica Neue"), 9, _palette.HeaderText);
        context.DrawText(text, new Point(rect.X + (rect.Width - text.Width) / 2, rect.Y + (rect.Height - text.Height) / 2));
    }

    // ---- Click handling ---------------------------------------------------

    private bool TryHandleChromeClick(Point point)
    {
        foreach (var (rect, column, range, hasHeader) in _filterButtons)
        {
            if (rect.Contains(point))
            {
                ShowFilterPopup(rect, column, range, hasHeader);
                return true;
            }
        }

        if (_validationButton is { } validation && validation.Rect.Contains(point))
        {
            ShowValidationPopup(validation.Rect, validation.Rule);
            return true;
        }

        if (_popupOpen)
        {
            ClosePopup();
            return true;
        }

        return false;
    }

    private void ClosePopup()
    {
        _popupLayer.Children.Clear();
        _popupOpen = false;
    }

    // ---- Test seams -------------------------------------------------------

    /// <summary>The rendered filter-dropdown chevron rect for a column (grid-local), or null.</summary>
    internal Rect? FilterButtonRectForTest(int column)
    {
        foreach (var (rect, col, _, _) in _filterButtons)
        {
            if (col == column)
            {
                return rect;
            }
        }

        return null;
    }

    /// <summary>True when the open popup contains a text block with the given text (e.g. its title).</summary>
    internal bool PopupHasTextForTest(string text) =>
        Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(_popupLayer)
            .OfType<TextBlock>().Any(t => t.Text == text);

    /// <summary>The centre (grid-local) of an open-popup control with the given text, or null.</summary>
    internal Point? PopupControlCenterForTest(string content)
    {
        foreach (var button in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(_popupLayer).OfType<Button>())
        {
            if (button.Content as string == content)
            {
                return button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), this);
            }
        }

        return null;
    }

    private void OpenPopup(Rect anchor, Control content)
    {
        ClosePopup();
        double left = Math.Min(anchor.X, Math.Max(0, Bounds.Width - 240));
        double top = Math.Min(anchor.Bottom, Math.Max(0, Bounds.Height - 260));
        Canvas.SetLeft(content, left);
        Canvas.SetTop(content, top);
        _popupLayer.Children.Add(content);
        _popupOpen = true;
    }

    private void ShowFilterPopup(Rect anchor, int column, CellRange range, bool hasHeader)
    {
        if (_session is null)
        {
            return;
        }

        var sheet = _session.ActiveSheet;
        if (_filterRange != range)
        {
            _columnFilters.Clear();
            _filterRange = range;
            // Reopened file: seed the checkbox state from the filter conditions restored by the ODS
            // reader (#17b/#18) so the dropdown reflects what's actually filtering the rows.
            if (sheet.ActiveFilter == range && sheet.ActiveFilterSpec is { } saved)
            {
                foreach (var (savedColumn, savedFilter) in saved.Columns)
                {
                    _columnFilters[savedColumn] = savedFilter;
                }
            }
        }

        var distinct = FilterEngine.DistinctValues(sheet, range, column, hasHeader);
        var allowed = _columnFilters.TryGetValue(column, out var existing) ? existing.AllowedValues : null;

        var checks = new List<CheckBox>();
        var list = new StackPanel { Spacing = 1 };
        foreach (var value in distinct)
        {
            string key = value.ToInvariantString();
            var check = new CheckBox
            {
                Content = key.Length == 0 ? "(blank)" : key,
                IsChecked = allowed is null || allowed.Contains(key),
                Tag = key,
            };
            checks.Add(check);
            list.Children.Add(check);
        }

        var apply = new Button { Content = "Apply", IsDefault = true };
        apply.Click += (_, _) =>
        {
            var selected = checks.Where(c => c.IsChecked == true).Select(c => (string)c.Tag!).ToHashSet(StringComparer.Ordinal);
            _columnFilters[column] = selected.Count == checks.Count
                ? new ColumnFilter()                       // all checked → no constraint
                : new ColumnFilter { AllowedValues = selected };
            ApplyColumnFilters(sheet, range, hasHeader);
            ClosePopup();
        };
        var clear = new Button { Content = "Clear" };
        clear.Click += (_, _) =>
        {
            _columnFilters.Remove(column);
            ApplyColumnFilters(sheet, range, hasHeader);
            ClosePopup();
        };

        // Sort controls sit above the filter checkbox list (task 14): sort the whole table/range
        // by this column, then close. Wired to the existing sort engine via DataEditCommands.
        var sortAsc = new Button { Content = "Sort A→Z", HorizontalAlignment = HorizontalAlignment.Stretch };
        sortAsc.Click += (_, _) => SortByFilterColumn(sheet, range, column, ascending: true, hasHeader);
        var sortDesc = new Button { Content = "Sort Z→A", HorizontalAlignment = HorizontalAlignment.Stretch };
        sortDesc.Click += (_, _) => SortByFilterColumn(sheet, range, column, ascending: false, hasHeader);

        // Title the popup by the column's header so it's clear which column is being acted on; fall
        // back to the column letter when there's no header (#6, user wording "Sorting <HEADER_NAME>").
        var headerValue = sheet.GetValue(range.Top, column);
        string headerLabel = hasHeader && !headerValue.IsEmpty
            ? headerValue.ToInvariantString()
            : $"Column {SheetDimensions.ColumnToLetters(column)}";

        OpenPopup(anchor, PopupBox(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = $"Sorting {headerLabel}", FontWeight = FontWeight.SemiBold },
                sortAsc,
                sortDesc,
                new Border { Height = 1, Background = _palette.GridLine.Brush },
                new ScrollViewer { MaxHeight = 180, Content = list },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { clear, apply } },
            },
        }));
    }

    /// <summary>Sort the table/filter range by the dropdown's column, then close the popup (task 14).</summary>
    private void SortByFilterColumn(Worksheet sheet, CellRange range, int column, bool ascending, bool hasHeader)
    {
        if (_session is null)
        {
            return;
        }

        _session.Execute(DataEditCommands.SortByColumn(sheet.Name, range, column, ascending, hasHeader));
        // Persist the sort so it round-trips through table:database-range (#18).
        sheet.ActiveSort = new SortSpec(new[] { new SortLevel(column, ascending) }, hasHeader);
        sheet.ActiveFilter ??= range;
        ClosePopup();
        Redraw();
    }

    private void ApplyColumnFilters(Worksheet sheet, CellRange range, bool hasHeader)
    {
        var active = _columnFilters.Where(kv => kv.Value.AllowedValues is not null || kv.Value.Condition is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        var spec = new FilterSpec(active, hasHeader);
        _session!.Execute(DataEditCommands.ApplyFilter(sheet.Name, range, spec, Culture));
        sheet.ActiveFilter ??= range;
        // Persist the filter conditions in Core so save→reopen restores them (#17b/#18). No active
        // conditions → drop the spec so an empty filter isn't written.
        sheet.ActiveFilterSpec = active.Count > 0 ? spec : null;
        Redraw();
    }

    private void ShowValidationPopup(Rect anchor, DataValidation rule)
    {
        if (_session is null || rule.ListValues is null)
        {
            return;
        }

        var list = new ListBox { ItemsSource = rule.ListValues, MaxHeight = 180 };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is string value)
            {
                _session.Execute(EditCommands.SetInput(_session.Workbook, _session.ActiveSheet.Name, Selection.ActiveRow, Selection.ActiveColumn, value, Culture));
                ClosePopup();
                Redraw();
            }
        };

        OpenPopup(anchor, PopupBox(list));
    }

    private Border PopupBox(Control content) => new()
    {
        Width = 220,
        Background = _palette.PopupSurface,
        BorderBrush = _palette.GridLine.Brush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(8),
        BoxShadow = new BoxShadows(new BoxShadow { Blur = 10, OffsetY = 3, Color = Color.FromArgb(0x33, 0, 0, 0) }),
        Child = content,
    };

    // ---- Validation enforcement -------------------------------------------

    private bool PassesValidation(int row, int column, string text, out string? reason)
    {
        reason = null;
        var rule = _session?.ActiveSheet.FindValidation(row, column);
        if (rule is null || rule.Response != ValidationResponse.Stop)
        {
            return true; // Warning/Information are advisory; we allow the edit
        }

        var parsed = InputParser.Parse(text, Culture);
        if (parsed.IsFormula)
        {
            return true; // a formula's result is validated on recalc, not here
        }

        if (Validator.IsValid(rule, parsed.Value, Culture, r => RangeValues(r)))
        {
            return true;
        }

        reason = rule.ErrorMessage ?? "That value is not allowed for this cell.";
        return false;
    }

    /// <summary>
    /// Pre-flight a paste against Stop data-validation before it executes (#B). Returns true when
    /// every projected value is allowed; on a violation it shows the validation error and returns
    /// false so the caller aborts the whole paste (PRD §8.13).
    /// </summary>
    internal bool TryEnforceValidation(PasteBlockCommand command) =>
        CheckBulkValidation(command.ProjectWrites());

    /// <summary>Pre-flight a fill the same way (#B).</summary>
    internal bool TryEnforceValidation(FillCommand command) =>
        _session is null || CheckBulkValidation(command.ProjectWrites(_session.Workbook));

    /// <summary>Pre-flight an explicit set of incoming writes (e.g. a TSV/value-block paste) (#B).</summary>
    internal bool TryEnforceValidation(IEnumerable<(int Row, int Column, CellValue Value, bool IsFormula)> incoming) =>
        CheckBulkValidation(incoming);

    private bool CheckBulkValidation(IEnumerable<(int Row, int Column, CellValue Value, bool IsFormula)> incoming)
    {
        if (_session is null)
        {
            return true;
        }

        var violations = ValidationPreflight.FindStopViolations(_session.ActiveSheet, incoming, Culture, RangeValues);
        if (violations.Count == 0)
        {
            return true;
        }

        var (row, column) = violations[0];
        var rule = _session.ActiveSheet.FindValidation(row, column);
        ShowValidationError(rule?.ErrorMessage ?? "That value is not allowed for this cell.");
        return false;
    }

    private IEnumerable<CellValue> RangeValues(CellRange range)
    {
        var sheet = _session!.ActiveSheet;
        foreach (var (r, c) in range.Positions())
        {
            yield return sheet.GetValue(r, c);
        }
    }

    private void ShowValidationError(string? reason)
    {
        var box = PopupBox(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Invalid entry", FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = reason ?? string.Empty, TextWrapping = TextWrapping.Wrap },
            },
        });
        if (TryGetCellRect(Selection.ActiveRow, Selection.ActiveColumn, out var rect))
        {
            OpenPopup(rect, box);
        }
    }
}
