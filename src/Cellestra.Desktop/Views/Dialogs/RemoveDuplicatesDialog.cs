using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Cellestra.Core.Model;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>Remove Duplicates dialog (PRD §8.14): choose key columns and whether the range has a header.</summary>
public sealed class RemoveDuplicatesDialog : Window
{
    public sealed record Result(IReadOnlyList<int> KeyColumns, bool HasHeader);

    private readonly CheckBox _headerCheck;
    private readonly List<(int Column, CheckBox Check)> _columnChecks = new();

    public RemoveDuplicatesDialog(Worksheet sheet, CellRange range, bool hasHeader)
    {
        Title = "Remove Duplicates";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _headerCheck = new CheckBox { Content = "My data has a header row", IsChecked = hasHeader };

        var columnList = new StackPanel { Spacing = 2 };
        for (int c = range.Left; c <= range.Right; c++)
        {
            string letter = SheetDimensions.ColumnToLetters(c);
            string label = hasHeader && sheet.GetValue(range.Top, c).IsText ? sheet.GetValue(range.Top, c).AsText : $"Column {letter}";
            var check = new CheckBox { Content = label, IsChecked = true };
            _columnChecks.Add((c, check));
            columnList.Children.Add(check);
        }

        var ok = new Button { Content = "Remove", IsDefault = true };
        ok.Click += (_, _) => Close(new Result(
            _columnChecks.Where(x => x.Check.IsChecked == true).Select(x => x.Column).ToList(),
            _headerCheck.IsChecked == true));
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 10,
            Children =
            {
                _headerCheck,
                new TextBlock { Text = "Columns to compare:" },
                columnList,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, ok },
                },
            },
        };
    }
}
