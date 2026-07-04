using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Desktop.Commands;
using Puffinote.Components.Overlays;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// The keyboard shortcuts reference, opened with <c>?</c> (PRD §6), rendered as an internal modal.
/// Reads every shortcut from the one command catalog (Golden Rule #4) so it can never drift from
/// the real bindings, grouped by area, plus the grid navigation keys the grid handles directly.
/// </summary>
public sealed class KeyboardShortcutsDialog : ModalDialog<bool>
{
    // Catalog id prefix → section heading, in display order.
    private static readonly (string Prefix, string Title)[] Categories =
    [
        ("file", "File"),
        ("edit", "Edit"),
        ("format", "Format"),
        ("insert", "Insert"),
        ("data", "Data"),
        ("formula", "Formulas"),
        ("view", "View"),
        ("help", "Help"),
    ];

    // Grid navigation keys handled directly by SpreadsheetGrid, so they aren't in the catalog.
    private static readonly (string Keys, string Label)[] Navigation =
    [
        ("↑ ↓ ← →", "Move between cells"),
        ("⇧ + arrows", "Extend selection"),
        ("Tab / ⇧Tab", "Move right / left"),
        ("↩ / ⇧↩", "Move down / up"),
        ("F2", "Edit active cell"),
        ("⎋", "Cancel edit"),
        ("F4", "Cycle reference type (while editing a formula)"),
    ];

    public KeyboardShortcutsDialog(AppCommands commands)
    {
        Title = "Keyboard Shortcuts";

        var sections = new StackPanel { Spacing = 18 };

        foreach (var (prefix, title) in Categories)
        {
            var rows = commands.All
                .Where(c => c.Shortcut is not null && c.Id.StartsWith(prefix + ".", StringComparison.Ordinal))
                .Select(c => (c.MacShortcut(), c.Title))
                .ToList();
            if (rows.Count > 0)
            {
                sections.Children.Add(Section(title, rows));
            }
        }

        sections.Children.Add(Section("Navigation & Editing", Navigation));

        var close = new Button
        {
            Content = "Close",
            IsCancel = true,
            IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 1);

        var scroll = new ScrollViewer { Content = sections };

        DialogContent = new Grid
        {
            Width = 440,
            Height = 520,
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { scroll, close },
        };
    }

    private static Control Section(string title, IEnumerable<(string Keys, string Label)> rows)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 2),
        });

        foreach (var (keys, label) in rows)
        {
            panel.Children.Add(Row(keys, label));
        }

        return panel;
    }

    private static Control Row(string keys, string label)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });

        var chip = new Border
        {
            // Translucent gray reads as light-gray on a light theme and dark-gray on a dark one.
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0x88, 0x88, 0x88)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2),
            MinWidth = 64,
            Child = new TextBlock { Text = keys, TextAlignment = TextAlignment.Center },
        };
        Grid.SetColumn(chip, 1);
        grid.Children.Add(chip);
        return grid;
    }
}
