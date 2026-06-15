using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Desktop.Views.Overlays;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>The sort levels and filters a <see cref="SortFilterDialog"/> hands back on OK.</summary>
public sealed record SortFilterPlan(SortSpec? Sort, FilterSpec? Filter, bool HasHeader);

/// <summary>
/// The unified <b>Sort &amp; Filter</b> dialog (PRD §8.11–§8.12): one ordered, editable list of steps —
/// each a <em>Sort</em> (column + direction) or a <em>Filter</em> (column + values/condition) — so a
/// chained "sort by A, then by B, then filter C" is visible and adjustable in one place instead of
/// being spread across the per-column dropdowns. Steps compile to one multi-level <see cref="SortSpec"/>
/// (top→bottom = tie-break priority) plus one ANDed <see cref="FilterSpec"/>; the dialog previews how
/// many rows survive without touching the sheet. Result is a <see cref="SortFilterPlan"/> or null on cancel.
/// </summary>
public sealed class SortFilterDialog : ModalDialog<SortFilterPlan?>
{
    private readonly Worksheet _sheet;
    private readonly CellRange _range;
    private readonly CultureInfo _culture;
    private readonly CheckBox _headerCheck;
    private readonly StackPanel _steps;
    private readonly TextBlock _count;
    private bool _ready;

    public SortFilterDialog(Worksheet sheet, CellRange range, bool hasHeader, CultureInfo? culture = null)
    {
        _sheet = sheet;
        _range = range;
        _culture = culture ?? CultureInfo.CurrentCulture;
        Title = "Sort & Filter";

        _headerCheck = new CheckBox { Content = "My data has a header row", IsChecked = hasHeader };
        _headerCheck.IsCheckedChanged += (_, _) =>
        {
            RebuildColumnChoices();
            UpdateCount();
        };
        _steps = new StackPanel { Spacing = 6 };
        _count = new TextBlock { Foreground = Brushes.Gray };

        var addSort = new Button { Content = "+ Add sort" };
        addSort.Click += (_, _) => AddStep(StepKind.Sort, range.Left, ascending: true, filterText: string.Empty);
        var addFilter = new Button { Content = "+ Add filter" };
        addFilter.Click += (_, _) => AddStep(StepKind.Filter, range.Left, ascending: true, filterText: string.Empty);

        var ok = new Button { Content = "Apply", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => Close(BuildPlan());
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        DialogContent = new StackPanel
        {
            Width = 520,
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Steps apply top to bottom — sorts set the tie-break order, filters hide non-matching rows.",
                    Foreground = Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap,
                },
                _headerCheck,
                _steps,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { addSort, addFilter },
                },
                _count,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, ok },
                },
            },
        };

        SeedFromActiveState(hasHeader);
        _ready = true;
        UpdateCount();
    }

    private enum StepKind { Sort, Filter }

    /// <summary>
    /// Pre-populate the step list from the sort/filter that belongs to <em>this</em> region (the table's
    /// own state, or the sheet AutoFilter only when it covers exactly this range — via
    /// <see cref="DataScope"/>), so a dialog opened on one table never shows another region's filters.
    /// </summary>
    private void SeedFromActiveState(bool hasHeader)
    {
        if (DataScope.EffectiveSort(_sheet, _range) is { Levels.Count: > 0 } sort)
        {
            foreach (var level in sort.Levels)
            {
                AddStep(StepKind.Sort, level.Column, level.Ascending, string.Empty);
            }
        }

        if (DataScope.EffectiveFilter(_sheet, _range) is { } filter)
        {
            foreach (var (column, columnFilter) in filter.Columns.OrderBy(kv => kv.Key))
            {
                AddStep(StepKind.Filter, column, ascending: true, FilterText(columnFilter));
            }
        }

        // No fallback step: an unsorted, unfiltered region opens with an empty list (just the Add
        // buttons), so the dialog never implies a sort that isn't there — applying it would otherwise
        // silently sort by the first column. The user adds steps explicitly via "+ Add sort/filter".
    }

    private IEnumerable<(int Column, string Label)> ColumnChoices()
    {
        bool header = _headerCheck.IsChecked == true;
        for (int c = _range.Left; c <= _range.Right; c++)
        {
            string letter = SheetDimensions.ColumnToLetters(c);
            string label = header && _sheet.GetValue(_range.Top, c).IsText
                ? _sheet.GetValue(_range.Top, c).AsText
                : $"Column {letter}";
            yield return (c, $"{letter} — {label}");
        }
    }

    private void AddStep(StepKind kind, int column, bool ascending, string filterText)
    {
        var kindCombo = new ComboBox { Width = 90, ItemsSource = new[] { "Sort", "Filter" }, SelectedIndex = (int)kind };
        var columnCombo = new ComboBox { Width = 190, ItemsSource = ColumnChoices().Select(c => c.Label).ToList(), SelectedIndex = column - _range.Left };
        var direction = new ComboBox { Width = 120, ItemsSource = new[] { "Ascending", "Descending" }, SelectedIndex = ascending ? 0 : 1 };
        var filterBox = new TextBox
        {
            Width = 120,
            Text = filterText,
            PlaceholderText = "Open, Done  ·  >100",
        };
        ToolTip.SetTip(filterBox, "List values separated by commas, or a condition like >100, <=0, or *text*.");

        void SyncKind()
        {
            bool isSort = kindCombo.SelectedIndex == (int)StepKind.Sort;
            direction.IsVisible = isSort;
            filterBox.IsVisible = !isSort;
        }

        SyncKind();
        kindCombo.SelectionChanged += (_, _) =>
        {
            SyncKind();
            UpdateCount();
        };
        columnCombo.SelectionChanged += (_, _) => UpdateCount();
        filterBox.TextChanged += (_, _) => UpdateCount();

        var up = new Button { Content = "▲" };
        ToolTip.SetTip(up, "Move step up");
        var down = new Button { Content = "▼" };
        ToolTip.SetTip(down, "Move step down");
        var remove = new Button { Content = "✕" };
        ToolTip.SetTip(remove, "Remove step");

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        up.Click += (_, _) => MoveStep(row, -1);
        down.Click += (_, _) => MoveStep(row, +1);
        remove.Click += (_, _) =>
        {
            _steps.Children.Remove(row);
            UpdateCount();
        };

        row.Children.Add(kindCombo);
        row.Children.Add(columnCombo);
        row.Children.Add(direction);
        row.Children.Add(filterBox);
        row.Children.Add(up);
        row.Children.Add(down);
        row.Children.Add(remove);
        _steps.Children.Add(row);
        UpdateCount();
    }

    private void MoveStep(StackPanel row, int delta)
    {
        int index = _steps.Children.IndexOf(row);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= _steps.Children.Count)
        {
            return;
        }

        _steps.Children.Move(index, target);
    }

    private void RebuildColumnChoices()
    {
        var labels = ColumnChoices().Select(c => c.Label).ToList();
        foreach (var row in _steps.Children.OfType<StackPanel>())
        {
            // The column combo is the second child in each row (after the kind combo).
            if (row.Children.OfType<ComboBox>().Skip(1).FirstOrDefault() is { } combo)
            {
                int index = combo.SelectedIndex;
                combo.ItemsSource = labels;
                combo.SelectedIndex = index;
            }
        }
    }

    private void UpdateCount()
    {
        if (!_ready)
        {
            return;
        }

        bool header = _headerCheck.IsChecked == true;
        int total = _range.RowCount - (header ? 1 : 0);
        var filter = BuildFilterSpec(header);
        int visible = filter is null ? total : FilterEngine.CountVisible(_sheet, _range, filter, _culture);
        _count.Text = $"Showing {visible} of {total} rows";
    }

    private SortFilterPlan BuildPlan()
    {
        bool header = _headerCheck.IsChecked == true;
        var levels = new List<SortLevel>();
        foreach (var (kind, column, ascending, _) in Steps())
        {
            if (kind == StepKind.Sort)
            {
                levels.Add(new SortLevel(column, ascending));
            }
        }

        var sort = levels.Count > 0 ? new SortSpec(levels, header) : null;
        return new SortFilterPlan(sort, BuildFilterSpec(header), header);
    }

    private FilterSpec? BuildFilterSpec(bool header)
    {
        var columns = new Dictionary<int, ColumnFilter>();
        foreach (var (kind, column, _, filterText) in Steps())
        {
            if (kind != StepKind.Filter)
            {
                continue;
            }

            if (ParseFilter(filterText) is { } filter)
            {
                // A later filter step on the same column wins (last edit applied).
                columns[column] = filter;
            }
        }

        return columns.Count > 0 ? new FilterSpec(columns, header) : null;
    }

    private IEnumerable<(StepKind Kind, int Column, bool Ascending, string FilterText)> Steps()
    {
        foreach (var row in _steps.Children.OfType<StackPanel>())
        {
            var combos = row.Children.OfType<ComboBox>().ToList();
            if (combos.Count < 2 || combos[1].SelectedIndex < 0)
            {
                continue;
            }

            var kind = combos[0].SelectedIndex == (int)StepKind.Filter ? StepKind.Filter : StepKind.Sort;
            int column = _range.Left + combos[1].SelectedIndex;
            bool ascending = combos.Count < 3 || combos[2].SelectedIndex == 0;
            string filterText = row.Children.OfType<TextBox>().FirstOrDefault()?.Text ?? string.Empty;
            yield return (kind, column, ascending, filterText);
        }
    }

    /// <summary>The editable text for a saved column filter: its value list, or its condition.</summary>
    private static string FilterText(ColumnFilter filter) =>
        filter.AllowedValues is { } values ? string.Join(", ", values) : filter.Condition ?? string.Empty;

    // ---- Test seams (headless UI tests; InternalsVisibleTo Cellestra.UiTests) ----

    /// <summary>The plan the dialog would commit from its current rows (after seeding/construction).</summary>
    internal SortFilterPlan BuildPlanForTest() => BuildPlan();

    /// <summary>The "Showing X of N rows" preview text.</summary>
    internal string CountTextForTest => _count.Text ?? string.Empty;

    /// <summary>Parse one filter cell's text the way a filter row does.</summary>
    internal static ColumnFilter? ParseFilterForTest(string? text) => ParseFilter(text);

    /// <summary>The tooltips on every step's button carrying <paramref name="glyph"/> (▲/▼/✕).</summary>
    internal IReadOnlyList<string?> StepButtonTooltipsForTest(string glyph) =>
        _steps.Children.OfType<StackPanel>()
            .SelectMany(row => row.Children.OfType<Button>())
            .Where(b => b.Content as string == glyph)
            .Select(b => ToolTip.GetTip(b) as string)
            .ToList();

    /// <summary>
    /// Parse a filter cell: a condition (leading comparison operator or a <c>*</c>/<c>?</c> wildcard)
    /// becomes a SUMIF-style <see cref="ColumnFilter.Condition"/>; anything else is a comma-separated
    /// value list (<see cref="ColumnFilter.AllowedValues"/>). Blank means "no filter on this column".
    /// </summary>
    private static ColumnFilter? ParseFilter(string? text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        bool isCondition = trimmed[0] is '>' or '<' or '=' || trimmed.Contains('*') || trimmed.Contains('?');
        if (isCondition)
        {
            return new ColumnFilter { Condition = trimmed };
        }

        var values = trimmed.Split(',')
            .Select(v => v.Trim())
            .Where(v => v.Length > 0)
            .ToHashSet(System.StringComparer.Ordinal);
        return values.Count > 0 ? new ColumnFilter { AllowedValues = values } : null;
    }
}
