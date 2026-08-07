using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Puffinote.Components.Commands;
using Puffinote.Components.Theming;

namespace Puffinote.Components.Controls;

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

    /// <summary>The size a row expects its glyph to be built at, for hosts' icon factories.</summary>
    public const double IconSize = 16;

    /// <summary>The gap between a row's glyph and its title.</summary>
    private const double IconGutter = 11;

    /// <summary>The section the recently-run entries are listed under when the query is empty.</summary>
    public const string RecentCategory = "Recent";

    private readonly ListBox _list;
    private readonly Func<string?, Control?>? _iconFactory;
    private IReadOnlyList<PaletteEntry> _entries = [];
    private IReadOnlyList<string> _recent = [];

    /// <param name="iconFactory">
    /// Builds the icon control for a row's <see cref="PaletteEntry.IconKey"/> (return null for none), sized but
    /// otherwise unpositioned — the row centres it and gives it its gutter. Omit for an icon-less palette.
    /// </param>
    public PaletteListView(Func<string?, Control?>? iconFactory = null)
    {
        _iconFactory = iconFactory;
        Spacing = 0;

        SearchBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        _list = new ListBox
        {
            MaxHeight = 360,
            BorderThickness = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<Row>((row, _) => BuildRow(row), supportsRecycling: true),
        };

        Children.Add(SearchBox);
        Children.Add(_list);

        SearchBox.TextChanged += (_, _) => Populate(SearchBox.Text ?? string.Empty);
        SearchBox.KeyDown += OnSearchKeyDown;
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
    public TextBox SearchBox { get; }

    /// <summary>The search placeholder text.</summary>
    public string Placeholder
    {
        get => SearchBox.PlaceholderText ?? string.Empty;
        set => SearchBox.PlaceholderText = value;
    }

    /// <summary>The rows currently listed, section headers included, in display order.</summary>
    public IReadOnlyList<string> ListedTitles =>
        _list.ItemsSource is IEnumerable<Row> rows ? [.. rows.Select(r => r.Title)] : [];

    /// <summary>Raised when a row is chosen (Enter or click). The host typically closes, then runs it.</summary>
    public event Action<PaletteEntry>? Chosen;

    /// <summary>Raised when the user presses Esc inside the palette.</summary>
    public event EventHandler? Cancelled;

    /// <summary>Load (or reload) the entries and reset the query to empty.</summary>
    /// <param name="recent">
    /// Titles the host has seen run lately, most recent first. They are lifted out of their own sections
    /// into a "Recent" one at the top — but only while the query is empty, which is the moment the palette
    /// has nothing better to offer than "the thing you just did, again". Once you type, the ranking is the
    /// answer and a second copy of a row would only be in the way.
    /// </param>
    public void SetItems(IReadOnlyList<PaletteEntry> entries, IReadOnlyList<string>? recent = null)
    {
        _entries = entries;
        _recent = recent ?? [];
        SearchBox.Text = string.Empty;
        Populate(string.Empty);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Cancelled?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.Enter:
                ChooseSelected();
                e.Handled = true;
                break;
            case Key.Down:
                Move(+1);
                e.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;
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

        // With nothing typed, the entries the host has seen run lately are moved — not copied — to the
        // front under their own header, in the order they were last used.
        var recent = query.Length == 0 && _recent.Count > 0
            ? _recent
                .Select(title => matches.Find(x => x.Entry.Title == title).Entry)
                .OfType<PaletteEntry>() // a title the catalog no longer offers (it went disabled) simply drops out
                .ToList()
            : [];

        var rows = new List<Row>();
        if (recent.Count > 0)
        {
            rows.Add(new Row(RecentCategory, string.Empty, string.Empty, null, null, IsHeader: true));
            rows.AddRange(recent.Select(entry => new Row(
                entry.Title, entry.Description, entry.Shortcut, entry.IconKey, entry, IsHeader: false)));
            matches.RemoveAll(x => recent.Contains(x.Entry));
        }

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
            // Place the glyph here rather than trusting each host's factory to: docked left with no alignment it
            // would stretch to the height of the title+description block instead of centring against it, and with
            // no gutter it would sit flush against the title. Factories return the glyph; the row places it.
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(0, 0, IconGutter, 0);
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
