using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Typage.Desktop.Commands;

namespace Typage.Desktop.Views;

/// <summary>
/// The Command Palette (⌘K) — a VS Code / Cellestra-style overlay that runs any command in the one
/// catalog by name, with fuzzy filtering and full keyboard control. A full-window overlay panel,
/// collapsed until opened; clicking the backdrop or pressing Esc dismisses it.
/// </summary>
public sealed class CommandPalette : Panel
{
    private readonly TextBox _search = new()
    {
        PlaceholderText = "Type a command…",
        FontSize = 15,
        Margin = new Thickness(0, 0, 0, 8),
    };

    private readonly ListBox _list = new() { MaxHeight = 360, BorderThickness = new Thickness(0) };
    private IReadOnlyList<AppCommand> _commands = System.Array.Empty<AppCommand>();

    public CommandPalette()
    {
        IsVisible = false;
        Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)); // dim backdrop

        var card = new Border
        {
            Width = 560,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 90, 0, 0),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2C)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3E)),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { _search, _list } },
        };

        _list.ItemTemplate = new FuncDataTemplate<PaletteRow>((row, _) => BuildRow(row), supportsRecycling: true);

        Children.Add(card);

        // Clicking the dim backdrop (but not the card) closes the palette.
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Source is Visual v && !card.IsVisualAncestorOf(v))
            {
                Close();
            }
        }, handledEventsToo: true);

        _search.KeyDown += OnSearchKeyDown;
        _search.TextChanged += (_, _) => Refilter();
        _list.DoubleTapped += (_, _) => Run(_list.SelectedItem as PaletteRow);
    }

    /// <summary>Open the palette over the given commands and focus the search box.</summary>
    public void Open(IReadOnlyList<AppCommand> commands)
    {
        _commands = commands;
        _search.Text = string.Empty;
        Refilter();
        IsVisible = true;
        _search.Focus();
    }

    public void Close() => IsVisible = false;

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Enter:
                Run(_list.SelectedItem as PaletteRow);
                e.Handled = true;
                break;
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        var count = _list.ItemCount;
        if (count == 0)
        {
            return;
        }

        var index = _list.SelectedIndex + delta;
        _list.SelectedIndex = System.Math.Clamp(index, 0, count - 1);
        _list.ScrollIntoView(_list.SelectedIndex);
    }

    private void Refilter()
    {
        var query = _search.Text ?? string.Empty;
        var rows = new List<(PaletteRow Row, int Score)>();
        foreach (var c in _commands)
        {
            if (!c.IsEnabled)
            {
                continue;
            }

            var score = FuzzyMatcher.Score(c.Title, query);
            if (score < 0)
            {
                continue;
            }

            rows.Add((new PaletteRow(c.Title, c.Shortcut ?? string.Empty, c.Description, c), score));
        }

        rows.Sort((a, b) => b.Score != a.Score
            ? b.Score.CompareTo(a.Score)
            : string.CompareOrdinal(a.Row.Title, b.Row.Title));

        _list.ItemsSource = rows.ConvertAll(r => r.Row);
        if (_list.ItemCount > 0)
        {
            _list.SelectedIndex = 0;
        }
    }

    private void Run(PaletteRow? row)
    {
        if (row is null)
        {
            return;
        }

        Close();
        row.Command.Execute();
    }

    private static Avalonia.Controls.Control BuildRow(PaletteRow row)
    {
        var title = new TextBlock { Text = row.Title, FontSize = 14 };
        var description = new TextBlock
        {
            Text = row.Description,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8E, 0x96)),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var shortcut = new TextBlock
        {
            Text = row.Shortcut,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9E, 0xA6)),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var text = new StackPanel { Children = { title, description } };
        Grid.SetColumn(text, 0);
        Grid.SetColumn(shortcut, 1);
        grid.Children.Add(text);
        grid.Children.Add(shortcut);
        return grid;
    }

    private sealed record PaletteRow(string Title, string Shortcut, string Description, AppCommand Command);
}
