using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Commands;

namespace Typage.Desktop.Views;

public partial class MainWindow
{
    private Border BuildToolbar()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Avalonia.Thickness(8, 5) };
        panel.Children.Add(ToolButton("B", "Bold", () => EditorView.Editor.ToggleBold(), bold: true));
        panel.Children.Add(ToolButton("I", "Italic", () => EditorView.Editor.ToggleItalic(), italic: true));
        panel.Children.Add(ToolButton("</>", "Inline code", () => EditorView.Editor.ToggleInlineCode()));

        // Cellestra-style popout groups: a button opens a rich drawer of related commands (icon + title +
        // description + shortcut), so the common Markdown controls are one click away from the toolbar.
        panel.Children.Add(ToolGroup("Text", "Text",
            ("format.bold", "B"), ("format.italic", "I"), ("format.strike", "S"),
            ("format.highlight", "▰"), ("format.code", "‹›")));
        panel.Children.Add(ToolGroup("Heading", "Heading",
            ("format.h1", "H1"), ("format.h2", "H2"), ("format.h3", "H3"),
            ("format.h4", "H4"), ("format.h5", "H5"), ("format.h6", "H6"), ("format.paragraph", "¶")));
        panel.Children.Add(ToolGroup("List", "List",
            ("format.bulletList", "•"), ("format.numberedList", "1."), ("format.task", "☑"), ("format.quote", "❝")));
        panel.Children.Add(ToolGroup("Insert", "Insert",
            ("format.link", "🔗"), ("insert.image", "▢"), ("insert.codeBlock", "{ }"), ("insert.hr", "—")));

        // A "Find a tool…" entry on the right opens the Command Palette (mirrors Cellestra).
        var paletteShortcut = ShortcutFormatter.Format(Shortcut.Cmd(Key.P, KeyModifiers.Shift));
        var findTool = new Button
        {
            Content = $"Find a tool…    {paletteShortcut}",
            Margin = new Avalonia.Thickness(8, 4),
            Padding = new Avalonia.Thickness(10, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Width = 200,
            FontSize = 12,
            Foreground = _mutedText,
            Background = _chromeInset,
            BorderBrush = _chromeBorder,
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(6),
            Command = new RelayCommand(OpenPalette),
        };
        ToolTip.SetTip(findTool, $"Run any command by name ({paletteShortcut})");

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(panel, 0);
        Grid.SetColumn(findTool, 1);
        grid.Children.Add(panel);
        grid.Children.Add(findTool);

        var border = new Border
        {
            Child = grid,
            Background = _chromeBg,
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            BorderBrush = _chromeBorder,
        };
        DockPanel.SetDock(border, Dock.Top);
        return border;
    }

    private static Button ToolButton(string label, string tooltip, Action action, bool bold = false, bool italic = false)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Avalonia.Thickness(9, 3),
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new Avalonia.CornerRadius(5),
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
            Command = new RelayCommand(action),
        };

        // A sans-serif capital "I" is just a vertical stroke, so italicised it reads as a bare diagonal
        // line. A serif face gives the glyph its top/bottom bars, so the italic "I" is recognisable.
        if (italic)
        {
            button.FontFamily = new FontFamily("Georgia, Times New Roman, serif");
            button.FontSize = 15;
        }

        ToolTip.SetTip(button, tooltip);
        return button;
    }

    /// <summary>
    /// A toolbar button that opens a Cellestra-style popout drawer of related commands. Each
    /// <paramref name="items"/> entry is a (command id, leading glyph) pair; the drawer shows a section
    /// header then one rich row per command (glyph + title + muted description + shortcut hint).
    /// </summary>
    private Button ToolGroup(string label, string sectionTitle, params (string Id, string Glyph)[] items)
    {
        var caret = new TextBlock { Text = "⌄", FontSize = 11, Margin = new Avalonia.Thickness(4, 0, 0, 0), Foreground = _mutedText, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(caret);

        var button = new Button
        {
            Content = content,
            Padding = new Avalonia.Thickness(9, 3),
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new Avalonia.CornerRadius(5),
        };

        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft, ShowMode = FlyoutShowMode.Standard };
        flyout.Content = BuildCommandDrawer(sectionTitle, items, () => flyout.Hide());
        button.Flyout = flyout;
        ToolTip.SetTip(button, $"{sectionTitle} tools");
        return button;
    }

    /// <summary>Build the popout drawer panel: a section header followed by a rich row per command.</summary>
    private Avalonia.Controls.Control BuildCommandDrawer(string sectionTitle, (string Id, string Glyph)[] items, Action close)
    {
        var panel = new StackPanel { Spacing = 2, MinWidth = 256, MaxWidth = 340, Margin = new Avalonia.Thickness(4) };
        panel.Children.Add(new TextBlock
        {
            Text = sectionTitle,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13.5,
            Margin = new Avalonia.Thickness(2, 0, 0, 6),
        });

        foreach (var (id, glyph) in items)
        {
            if (_commandsById.TryGetValue(id, out var command))
            {
                panel.Children.Add(BuildDrawerRow(command, glyph, close));
            }
        }

        return panel;
    }

    /// <summary>One rich drawer row: a leading glyph, the command's title + description, and its shortcut.</summary>
    private Avalonia.Controls.Control BuildDrawerRow(AppCommand command, string glyph, Action close)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontSize = 12.5,
            Width = 26,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 0, 10, 0),
        };
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = command.Title, FontSize = 13, FontWeight = FontWeight.Medium },
                new TextBlock { Text = command.Description, FontSize = 11, Foreground = _mutedText, TextWrapping = TextWrapping.Wrap },
            },
        };

        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(icon, Dock.Left);
        layout.Children.Add(icon);
        if (command.ShortcutText.Length > 0)
        {
            var shortcut = new TextBlock
            {
                Text = command.ShortcutText,
                FontSize = 10.5,
                Foreground = _mutedText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(8, 0, 0, 0),
            };
            DockPanel.SetDock(shortcut, Dock.Right);
            layout.Children.Add(shortcut);
        }

        layout.Children.Add(text);

        var row = new Button
        {
            Content = layout,
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(9, 7),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Command = new RelayCommand(() =>
            {
                if (command.IsEnabled)
                {
                    command.Execute();
                }

                close();
                EditorView.Editor.Focus();
            }, () => command.IsEnabled),
        };
        ToolTip.SetTip(row, command.Description);
        row.PointerEntered += (_, _) => row.Background = _drawerHover;
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        return row;
    }

    /// <summary>The number of toolbar buttons that open a popout drawer (the Cellestra-style groups); for tests.</summary>
    internal int ToolGroupCountForTest() =>
        _toolbar?.Child is Grid grid
            ? grid.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<Button>()).Count(b => b.Flyout is not null)
            : 0;

    /// <summary>The number of command rows a drawer built for <paramref name="ids"/> produces; for tests.</summary>
    internal int DrawerRowCountForTest(params string[] ids) =>
        ((StackPanel)BuildCommandDrawer("Test", [.. ids.Select(i => (i, "•"))], () => { }))
            .Children.OfType<Button>().Count();
}
