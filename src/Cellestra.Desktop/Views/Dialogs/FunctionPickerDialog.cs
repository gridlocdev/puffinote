using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Core.Formulas.Functions;
using Cellestra.Desktop.Views.Overlays;

namespace Cellestra.Desktop.Views.Dialogs;

/// <summary>
/// The Insert Function picker (PRD §4 Formulas, §6.2 Insert ▸ Function…), rendered as an internal
/// modal. It lists every function from the one <see cref="FunctionRegistry"/> (Golden Rule #5) — name,
/// signature, summary, and example from <see cref="FunctionMetadata"/> — with a search box. Choosing
/// one returns its <see cref="FunctionMetadata"/>; the caller seeds <c>=NAME(</c> into the active cell
/// and enters edit mode positioned for arguments. Returns null on cancel.
/// </summary>
public sealed class FunctionPickerDialog : ModalDialog<FunctionMetadata?>
{
    // Built once from the same registry the evaluator and autocomplete read, so the picker can never
    // list a different set than the engine supports (Golden Rule #5).
    private static readonly IReadOnlyList<FunctionMetadata> AllFunctions =
        FunctionRegistry.CreateDefault().AllMetadata.ToList();

    private readonly TextBox _search = new() { Width = 360, PlaceholderText = "Search functions…" };
    private readonly ListBox _list = new() { Height = 220, MinWidth = 360 };
    private readonly TextBlock _signature = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
    private readonly TextBlock _example = new() { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("monospace"), Opacity = 0.85 };
    private List<FunctionMetadata> _filtered = new();

    public FunctionPickerDialog()
    {
        Title = "Insert Function";
        InitialFocus = _search;

        _search.TextChanged += (_, _) => RunSearch();
        _list.SelectionChanged += (_, _) => ShowDetails();
        // Double-clicking a row inserts it, matching the picker convention.
        _list.DoubleTapped += (_, _) => Accept();

        var insert = new Button { Content = "Insert", IsDefault = true };
        insert.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        DialogContent = new StackPanel
        {
            Width = 400,
            Spacing = 10,
            Children =
            {
                _search,
                _list,
                new StackPanel { Spacing = 4, Children = { _signature, _summary, _example } },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, insert },
                },
            },
        };

        RunSearch();
    }

    /// <summary>Test seam: the function names currently shown for a given query (verifies it reads the registry).</summary>
    internal IReadOnlyList<string> VisibleNamesForTest(string query)
    {
        _search.Text = query;
        RunSearch();
        return _filtered.Select(f => f.Name).ToList();
    }

    /// <summary>Test seam: the metadata the dialog would return if the user inserted the row at <paramref name="index"/>.</summary>
    internal FunctionMetadata? SelectionAtForTest(int index)
    {
        _list.SelectedIndex = index;
        return _list.SelectedItem is ListBoxItem { Tag: FunctionMetadata meta } ? meta : null;
    }

    private void RunSearch()
    {
        string query = (_search.Text ?? string.Empty).Trim();
        _filtered = string.IsNullOrEmpty(query)
            ? AllFunctions.ToList()
            : AllFunctions
                .Select(f => (Meta: f, Score: FuzzyMatcher.Score(f.Name, query)))
                .Where(x => x.Score >= 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Meta.Name, System.StringComparer.Ordinal)
                .Select(x => x.Meta)
                .ToList();

        _list.ItemsSource = _filtered
            .Select(f => new ListBoxItem { Content = f.Signature, Tag = f })
            .ToList();
        _list.SelectedIndex = _filtered.Count > 0 ? 0 : -1;
    }

    private void ShowDetails()
    {
        if (_list.SelectedItem is ListBoxItem { Tag: FunctionMetadata meta })
        {
            _signature.Text = meta.Signature;
            _summary.Text = meta.Summary;
            _example.Text = string.IsNullOrEmpty(meta.Example) ? string.Empty : $"Example:  {meta.Example}";
        }
        else
        {
            _signature.Text = _summary.Text = _example.Text = string.Empty;
        }
    }

    private void Accept()
    {
        if (_list.SelectedItem is ListBoxItem { Tag: FunctionMetadata meta })
        {
            Close(meta);
        }
    }
}
