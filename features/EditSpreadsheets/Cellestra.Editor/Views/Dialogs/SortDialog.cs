using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Core.Data;
using Cellestra.Core.Model;
using Cellestra.Desktop.Views.Overlays;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// The multi-level Custom Sort dialog (PRD §8.11), rendered as an internal modal: a header-aware
/// list of "Sort by / Then by" levels, each a column and a direction. Its
/// <see cref="ModalDialog{T}.Result"/> is a <see cref="SortSpec"/> or null on cancel.
/// </summary>
public sealed class SortDialog : ModalDialog<SortSpec?>
{
    private readonly Worksheet _sheet;
    private readonly CellRange _range;
    private readonly CheckBox _headerCheck;
    private readonly StackPanel _levels;

    public SortDialog(Worksheet sheet, CellRange range, bool hasHeader)
    {
        _sheet = sheet;
        _range = range;
        Title = "Sort";

        _headerCheck = new CheckBox { Content = "My data has a header row", IsChecked = hasHeader };
        _headerCheck.IsCheckedChanged += (_, _) => RebuildLevelColumns();
        _levels = new StackPanel { Spacing = 6 };

        var addButton = new Button { Content = "+ Add level" };
        addButton.Click += (_, _) => AddLevel(range.Left, true);

        var ok = new Button { Content = "Sort", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => Close(BuildSpec());
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        DialogContent = new StackPanel
        {
            Width = 420,
            Spacing = 12,
            Children =
            {
                _headerCheck,
                _levels,
                addButton,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, ok },
                },
            },
        };

        AddLevel(range.Left, true);
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

    private void AddLevel(int column, bool ascending)
    {
        var combo = new ComboBox { Width = 220, Tag = column };
        var directions = new ComboBox { Width = 120, SelectedIndex = ascending ? 0 : 1 };
        directions.ItemsSource = new[] { "Ascending", "Descending" };
        combo.ItemsSource = ColumnChoices().Select(c => c.Label).ToList();
        combo.SelectedIndex = column - _range.Left;

        var remove = new Button { Content = "✕" };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        remove.Click += (_, _) => _levels.Children.Remove(row);
        row.Children.Add(new TextBlock { Text = _levels.Children.Count == 0 ? "Sort by" : "Then by", Width = 60, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(combo);
        row.Children.Add(directions);
        row.Children.Add(remove);
        _levels.Children.Add(row);
    }

    private void RebuildLevelColumns()
    {
        var labels = ColumnChoices().Select(c => c.Label).ToList();
        foreach (var row in _levels.Children.OfType<StackPanel>())
        {
            if (row.Children.OfType<ComboBox>().FirstOrDefault() is { } combo)
            {
                int index = combo.SelectedIndex;
                combo.ItemsSource = labels;
                combo.SelectedIndex = index;
            }
        }
    }

    private SortSpec BuildSpec()
    {
        var levels = new List<SortLevel>();
        foreach (var row in _levels.Children.OfType<StackPanel>())
        {
            var combos = row.Children.OfType<ComboBox>().ToList();
            if (combos.Count < 2 || combos[0].SelectedIndex < 0)
            {
                continue;
            }

            int column = _range.Left + combos[0].SelectedIndex;
            bool ascending = combos[1].SelectedIndex == 0;
            levels.Add(new SortLevel(column, ascending));
        }

        return new SortSpec(levels, _headerCheck.IsChecked == true);
    }
}
