using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using BirdBrain.Components.Commands;
using BirdBrain.Components.Theming;

namespace BirdBrain.Components.Controls;

/// <summary>One runnable entry in a <see cref="PaletteListView"/>.</summary>
/// <param name="Title">The command name shown (and matched against the query).</param>
/// <param name="Run">The action to run when the entry is chosen.</param>
/// <param name="Description">Optional secondary line.</param>
/// <param name="Shortcut">Optional shortcut text shown on the right.</param>
/// <param name="IconKey">Optional icon key, resolved by the host's icon factory.</param>
/// <param name="Category">Optional category; non-empty values group results under section headers.</param>
public sealed record PaletteEntry(
    string Title,
    Action Run,
    string Description = "",
    string Shortcut = "",
    string? IconKey = null,
    string Category = "");

/// <summary>
/// The shared inner of a command palette: a search box over a fuzzy-ranked, optionally category-grouped
/// list of <see cref="PaletteEntry"/> rows, with full keyboard control and Tab trapped inside. Each app
/// hosts it in its own overlay/card — so the surrounding scrim, blur, and modal-state stay app-specific —
/// and reacts to <see cref="Chosen"/> / <see cref="Cancelled"/>. Rows are token-themed; an optional icon
/// factory keeps the control free of any one icon system.
/// </summary>
public sealed class PaletteListView : StackPanel
{
    private sealed record Row(string Title, string Description, string Shortcut, string? IconKey, PaletteEntry? Entry, bool IsHeader);

    private readonly TextBox _search;
    private readonly ListBox _list;
    private readonly Func<string?, Control?>? _iconFactory;
    private IReadOnlyList<PaletteEntry> _entries = Array.Empty<PaletteEntry>();

    /// <param name="iconFactory">
    /// Builds the icon control for a row's <see cref="PaletteEntry.IconKey"/> (return null for none).
    /// Omit for an icon-less palette.
    /// </param>
    public PaletteListView(Func<string?, Control?>? iconFactory = null)
    {
        _iconFactory = iconFactory;
        Spacing = 0;

        _search = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        _list = new ListBox
        {
            MaxHeight = 360,
            BorderThickness = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<Row>((row, _) => BuildRow(row), supportsRecycling: true),
        };

        Children.Add(_search);
        Children.Add(_list);

        _search.TextChanged += (_, _) => Populate(_search.Text ?? string.Empty);
        _search.KeyDown += OnSearchKeyDown;
        _list.PointerReleased += (_, e) =>
        {
            // Run only when the click actually landed on a row (not the empty space below a short list).
            if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null)
            {
                ChooseSelected();
            }
        };

        // Trap Tab between the search box and the list — never out to the host behind the overlay.
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
    }

    /// <summary>The search box, so a host can focus it when the overlay opens.</summary>
    public TextBox SearchBox => _search;

    /// <summary>The search placeholder text.</summary>
    public string Placeholder
    {
        get => _search.Watermark ?? string.Empty;
        set => _search.Watermark = value;
    }

    /// <summary>Raised when a row is chosen (Enter or click). The host typically closes, then runs it.</summary>
    public event Action<PaletteEntry>? Chosen;

    /// <summary>Raised when the user presses Esc inside the palette.</summary>
    public event EventHandler? Cancelled;

    /// <summary>Load (or reload) the entries and reset the query to empty.</summary>
    public void SetItems(IReadOnlyList<PaletteEntry> entries)
    {
        _entries = entries;
        _search.Text = string.Empty;
        Populate(string.Empty);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Cancelled?.Invoke(this, EventArgs.Empty); e.Handled = true; break;
            case Key.Enter: ChooseSelected(); e.Handled = true; break;
            case Key.Down: Move(+1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
        }
    }

    private void Populate(string query)
    {
        // Score and rank, then divide results under their category headers (within a category keep the
        // fuzzy order). Empty categories yield a flat list (no headers).
        var matches = _entries
            .Select(entry => (Entry: entry, Score: FuzzyMatcher.Score(entry.Title, query)))
            .Where(x => x.Score >= 0)
            .OrderByDescending(x => x.Score)
            .ToList();

        var rows = new List<Row>();
        foreach (var group in matches.GroupBy(x => x.Entry.Category))
        {
            if (!string.IsNullOrEmpty(group.Key))
            {
                rows.Add(new Row(group.Key, string.Empty, string.Empty, null, null, IsHeader: true));
            }

            rows.AddRange(group.Select(x => new Row(
                x.Entry.Title, x.Entry.Description, x.Entry.Shortcut, x.Entry.IconKey, x.Entry, IsHeader: false)));
        }

        _list.ItemsSource = rows;
        _list.SelectedIndex = FirstSelectableIndex(rows, 0, +1);
    }

    private void ChooseSelected()
    {
        if (_list.SelectedItem is Row { IsHeader: false, Entry: { } entry })
        {
            Chosen?.Invoke(entry);
        }
    }

    private void Move(int direction)
    {
        if (_list.ItemsSource is not IReadOnlyList<Row> rows || rows.Count == 0)
        {
            return;
        }

        int next = FirstSelectableIndex(rows, _list.SelectedIndex + direction, direction);
        if (next >= 0)
        {
            _list.SelectedIndex = next;
            _list.ScrollIntoView(next);
        }
    }

    /// <summary>The first selectable (non-header) row at or after <paramref name="start"/> stepping by <paramref name="step"/>, or -1.</summary>
    private static int FirstSelectableIndex(IReadOnlyList<Row> rows, int start, int step)
    {
        for (int i = Math.Max(0, start); i >= 0 && i < rows.Count; i += step)
        {
            if (!rows[i].IsHeader)
            {
                return i;
            }
        }

        return -1;
    }

    private Control BuildRow(Row? row)
    {
        // The ListBox recycles containers and may build the template with a null item — render nothing.
        if (row is null)
        {
            return new Control();
        }

        IBrush muted = Tokens.For(ActualThemeVariant).MutedText;

        if (row.IsHeader)
        {
            return new TextBlock
            {
                Text = row.Title.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = muted,
                Margin = new Thickness(2, 8, 0, 2),
            };
        }

        var dock = new DockPanel { LastChildFill = true };

        if (!string.IsNullOrEmpty(row.Shortcut))
        {
            var shortcut = new TextBlock
            {
                Text = row.Shortcut,
                Foreground = muted,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            DockPanel.SetDock(shortcut, Dock.Right);
            dock.Children.Add(shortcut);
        }

        if (_iconFactory?.Invoke(row.IconKey) is { } icon)
        {
            DockPanel.SetDock(icon, Dock.Left);
            dock.Children.Add(icon);
        }

        dock.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = row.Title, FontWeight = FontWeight.Medium },
                new TextBlock { Text = row.Description, Foreground = muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis },
            },
        });

        return dock;
    }
}
