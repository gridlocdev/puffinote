using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Desktop.Commands;

namespace Cellestra.Desktop.Controls.Ribbon;

/// <summary>
/// The in-window ribbon (PRD §6.3): five tabs (Home · Insert · Formulas · Data · View) of
/// grouped buttons, every one bound to the single <see cref="AppCommands"/> catalog so it can
/// never drift from the menu or palette. Hidden by default; the host toggles its visibility.
/// </summary>
public sealed class Ribbon : UserControl
{
    // Tab → groups → command ids. The one catalog supplies labels, icons, and shortcuts.
    private static readonly (string Tab, (string Group, string[] Commands)[] Groups)[] Layout =
    {
        ("Home", new[]
        {
            ("Clipboard", new[] { "edit.paste", "edit.cut", "edit.copy" }),
            ("Font", new[] { "format.bold", "format.italic", "format.underline", "format.cells" }),
            ("Cells", new[] { "insert.rowsAbove", "insert.columnsLeft", "edit.delete" }),
            ("Editing", new[] { "edit.find", "data.sortAsc", "data.toggleFilter" }),
        }),
        ("Insert", new[]
        {
            ("Cells", new[] { "insert.rowsAbove", "insert.columnsLeft" }),
            ("Tables", new[] { "data.createTable" }),
            ("Sheets", new[] { "insert.sheet" }),
        }),
        ("Formulas", new[]
        {
            ("Calculation", new[] { "formula.recalc" }),
        }),
        ("Data", new[]
        {
            ("Sort & Filter", new[] { "data.sortAsc", "data.sortDesc", "data.customSort", "data.toggleFilter", "data.clearFilter" }),
            ("Data Tools", new[] { "data.textToColumns", "data.removeDuplicates", "data.validation" }),
            ("Get & Save Data", new[] { "file.importCsv", "file.exportCsv" }),
        }),
        ("View", new[]
        {
            ("Zoom", new[] { "view.zoomIn", "view.zoomOut", "view.actualSize" }),
            ("Show", new[] { "view.showRibbon" }),
        }),
    };

    public Ribbon(IAppEnvironment environment)
    {
        var tabs = new TabControl { Padding = new Avalonia.Thickness(4, 2) };
        foreach (var (tab, groups) in Layout)
        {
            tabs.Items.Add(new TabItem { Header = tab, Content = BuildTab(groups, environment) });
        }

        Content = tabs;
    }

    private static Control BuildTab((string Group, string[] Commands)[] groups, IAppEnvironment environment)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(4) };
        foreach (var (group, commands) in groups)
        {
            row.Children.Add(BuildGroup(group, commands, environment));
        }

        return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = row };
    }

    private static Control BuildGroup(string title, IReadOnlyList<string> commandIds, IAppEnvironment environment)
    {
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 240 };
        foreach (string id in commandIds)
        {
            if (AppCommands.ById(id) is { } command)
            {
                buttons.Children.Add(BuildButton(command, environment));
            }
        }

        return new Border
        {
            // Translucent so the group divider shows on both light and dark themes.
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x88, 0x88, 0x88)),
            BorderThickness = new Avalonia.Thickness(0, 0, 1, 0),
            Padding = new Avalonia.Thickness(6, 2),
            Child = new StackPanel
            {
                Children =
                {
                    buttons,
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Avalonia.Thickness(0, 2, 0, 0),
                    },
                },
            },
        };
    }

    private static Button BuildButton(AppCommand command, IAppEnvironment environment)
    {
        string tip = string.IsNullOrEmpty(command.ShortcutText) ? command.Label : $"{command.Label} ({command.ShortcutText})";
        var button = new Button
        {
            Content = command.Label,
            Margin = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(6, 3),
            FontSize = 12,
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
        };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) =>
        {
            if (command.IsEnabled(environment))
            {
                command.Execute(environment);
            }
        };
        return button;
    }
}
