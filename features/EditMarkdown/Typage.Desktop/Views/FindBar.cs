using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Typage.Control.Views;
using Typage.Engine.Document;

namespace Typage.Desktop.Views;

/// <summary>
/// The Find / Replace bar (PRD §7.2 — plain and regex find, replace, replace-all). Docks at the top
/// of the editor, hidden until opened (⌘F find, ⌘⌥F / Ctrl+H replace). Matches are computed by the
/// engine's <see cref="DocumentSearch"/> and highlighted in the editor; navigation selects each hit.
/// </summary>
public sealed class FindBar : Border
{
    private readonly MarkdownEditorView _view;
    private readonly TextBox _find = new() { PlaceholderText = "Find", Width = 220 };
    private readonly TextBox _replace = new() { PlaceholderText = "Replace with", Width = 220 };
    private readonly ToggleButton _matchCase = new() { Content = "Aa", Padding = new Avalonia.Thickness(6, 2) };
    private readonly ToggleButton _regex = new() { Content = ".*", Padding = new Avalonia.Thickness(6, 2) };
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 80 };
    private readonly StackPanel _replaceRow;

    private IReadOnlyList<SearchMatch> _matches = System.Array.Empty<SearchMatch>();
    private int _current = -1;

    public FindBar(MarkdownEditorView view, IBrush background, IBrush border)
    {
        _view = view;
        IsVisible = false;
        Background = background; // shared chrome brush — adapts on theme change
        BorderBrush = border;
        BorderThickness = new Avalonia.Thickness(0, 0, 0, 1);
        Padding = new Avalonia.Thickness(8, 6);

        ToolTip.SetTip(_matchCase, "Match case");
        ToolTip.SetTip(_regex, "Regular expression");

        var findRow = Row(
            _find,
            Button("‹", "Previous match (⇧⏎)", FindPrevious),
            Button("›", "Next match (⏎)", FindNext),
            _count,
            _matchCase,
            _regex,
            Button("✕", "Close (Esc)", Hide));

        _replaceRow = Row(
            _replace,
            Button("Replace", "Replace this match", ReplaceCurrent),
            Button("All", "Replace all matches", ReplaceAll));
        _replaceRow.IsVisible = false;

        Child = new StackPanel { Spacing = 6, Children = { findRow, _replaceRow } };

        _find.TextChanged += (_, _) => Recompute(resetCurrent: true);
        _matchCase.IsCheckedChanged += (_, _) => Recompute(resetCurrent: true);
        _regex.IsCheckedChanged += (_, _) => Recompute(resetCurrent: true);
        _find.KeyDown += OnFindKeyDown;
        _replace.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { ReplaceCurrent(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        };
    }

    public void Show(bool replace)
    {
        IsVisible = true;
        _replaceRow.IsVisible = replace;

        // Prefill from the current selection, if any.
        var selected = _view.Editor.SelectedText;
        if (selected.Length > 0 && !selected.Contains('\n'))
        {
            _find.Text = selected;
        }

        Recompute(resetCurrent: true);
        _find.Focus();
        _find.SelectAll();
    }

    public void Hide()
    {
        IsVisible = false;
        _view.Editor.ClearSearchMatches();
        _view.Editor.Focus();
    }

    private void OnFindKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                FindPrevious();
                e.Handled = true;
                break;
            case Key.Enter:
                FindNext();
                e.Handled = true;
                break;
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
        }
    }

    private void Recompute(bool resetCurrent)
    {
        var query = _find.Text ?? string.Empty;
        var useRegex = _regex.IsChecked == true;
        var caseSensitive = _matchCase.IsChecked == true;
        _matches = DocumentSearch.FindAll(_view.Text, query, useRegex, caseSensitive);

        if (_matches.Count == 0)
        {
            _current = -1;
            _count.Text = query.Length == 0 ? string.Empty : "No results";
        }
        else
        {
            _current = resetCurrent ? 0 : System.Math.Clamp(_current, 0, _matches.Count - 1);
            _count.Text = $"{_current + 1} of {_matches.Count}";
        }

        _view.Editor.SetSearchMatches(_matches, _current);
        SelectCurrent();
    }

    private void FindNext() => Step(1);

    private void FindPrevious() => Step(-1);

    private void Step(int delta)
    {
        if (_matches.Count == 0)
        {
            return;
        }

        _current = (_current + delta + _matches.Count) % _matches.Count; // wrap
        _count.Text = $"{_current + 1} of {_matches.Count}";
        _view.Editor.SetSearchMatches(_matches, _current);
        SelectCurrent();
    }

    private void SelectCurrent()
    {
        if (_current >= 0 && _current < _matches.Count)
        {
            var m = _matches[_current];
            _view.Editor.Select(m.Start, m.Length);
        }
    }

    private void ReplaceCurrent()
    {
        if (_current < 0 || _current >= _matches.Count)
        {
            return;
        }

        var m = _matches[_current];
        _view.Editor.Replace(m.Start, m.Length, _replace.Text ?? string.Empty);
        Recompute(resetCurrent: false); // offsets shifted; re-find and keep position
    }

    private void ReplaceAll()
    {
        var replacement = _replace.Text ?? string.Empty;
        // Replace from the end backward so earlier offsets stay valid.
        for (var i = _matches.Count - 1; i >= 0; i--)
        {
            var m = _matches[i];
            _view.Editor.Replace(m.Start, m.Length, replacement);
        }

        Recompute(resetCurrent: true);
        _find.Focus();
    }

    private static StackPanel Row(params Avalonia.Controls.Control[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var c in children)
        {
            panel.Children.Add(c);
        }

        return panel;
    }

    private static Button Button(string content, string tooltip, System.Action action)
    {
        var button = new Button { Content = content, Padding = new Avalonia.Thickness(8, 2) };
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }
}
