using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Controls.CommandBar;
using Puffinote.Text.Document;

namespace Typage.Control.Views;

/// <summary>
/// The Find / Replace bar (PRD §7.2 — plain and regex find, replace, replace-all). Docks at the top
/// of the editor, hidden until opened (⌘F find, ⌘⌥F / Ctrl+H replace). Matches are computed by the
/// engine's <see cref="DocumentSearch"/> and highlighted in the editor; navigation selects each hit.
/// <para>
/// It ships <em>with</em> the editor surface, not with a shell: find-in-document is the editor's own
/// capability, so every host (the standalone app, the Puffinote shell) gets the same bar for free rather
/// than reimplementing it. <see cref="MarkdownEditorView"/> owns the one instance and opens it.
/// </para>
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
    private int _current = -1;
    private bool _suppressReindex; // true while the bar itself is editing the document

    /// <param name="view">The editor this bar searches.</param>
    /// <param name="background">Chrome fill. A host that re-tints its chrome on a theme change passes the
    /// live brush it mutates; omitting it falls back to the shared command-bar chrome, so the control is
    /// self-sufficient in a host that has no chrome of its own.</param>
    /// <param name="border">Chrome hairline, same contract as <paramref name="background"/>.</param>
    public FindBar(MarkdownEditorView view, IBrush? background = null, IBrush? border = null)
    {
        _view = view;
        IsVisible = false;
        Background = background ?? Brushes.Transparent;
        BorderBrush = border ?? CommandBarChrome.DividerBrush;
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

        _find.TextChanged += (_, _) => Recompute(resetCurrent: true, selectMatch: true);
        _matchCase.IsCheckedChanged += (_, _) => Recompute(resetCurrent: true, selectMatch: true);
        _regex.IsCheckedChanged += (_, _) => Recompute(resetCurrent: true, selectMatch: true);
        _find.KeyDown += OnFindKeyDown;

        // Keep matches fresh when the document changes underneath the bar (e.g. the user edits the
        // text while find is open). Don't move the caret on these external edits, and skip the
        // bar's own replace-driven changes (handled explicitly).
        _view.TextChanged += (_, _) =>
        {
            if (IsVisible && !_suppressReindex)
            {
                Recompute(resetCurrent: false, selectMatch: false);
            }
        };
        _replace.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ReplaceCurrent();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
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

        Recompute(resetCurrent: true, selectMatch: true);
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

    private void Recompute(bool resetCurrent, bool selectMatch)
    {
        var query = _find.Text ?? string.Empty;
        var useRegex = _regex.IsChecked == true;
        var caseSensitive = _matchCase.IsChecked == true;
        Matches = DocumentSearch.FindAll(_view.Text, query, useRegex, caseSensitive);

        if (Matches.Count == 0)
        {
            _current = -1;
            _count.Text = query.Length == 0 ? string.Empty : "No results";
        }
        else
        {
            _current = resetCurrent ? 0 : System.Math.Clamp(_current, 0, Matches.Count - 1);
            _count.Text = $"{_current + 1} of {Matches.Count}";
        }

        _view.Editor.SetSearchMatches(Matches, _current);
        if (selectMatch)
        {
            SelectCurrent();
        }
    }

    private void FindNext() => Step(1);

    private void FindPrevious() => Step(-1);

    private void Step(int delta)
    {
        if (Matches.Count == 0)
        {
            return;
        }

        _current = (_current + delta + Matches.Count) % Matches.Count; // wrap
        _count.Text = $"{_current + 1} of {Matches.Count}";
        _view.Editor.SetSearchMatches(Matches, _current);
        SelectCurrent();
    }

    /// <summary>Apply a document edit the bar itself made, without the external-edit reindex firing.</summary>
    private void SelfEdit(System.Action edit)
    {
        _suppressReindex = true;
        try
        {
            edit();
        }
        finally
        {
            _suppressReindex = false;
        }
    }

    private void SelectCurrent()
    {
        if (_current >= 0 && _current < Matches.Count)
        {
            var m = Matches[_current];
            _view.Editor.Select(m.Start, m.Length);
        }
    }

    private void ReplaceCurrent()
    {
        if (_current < 0 || _current >= Matches.Count)
        {
            return;
        }

        var m = Matches[_current];
        SelfEdit(() => _view.Editor.Replace(m.Start, m.Length, _replace.Text ?? string.Empty));
        Recompute(resetCurrent: false, selectMatch: true); // offsets shifted; re-find and keep position
    }

    private void ReplaceAll()
    {
        if (Matches.Count == 0)
        {
            return;
        }

        var replacement = _replace.Text ?? string.Empty;
        var text = _view.Text;

        // Build the whole replaced span once and apply it as a single undoable edit, so Replace All
        // is one operation (one undo step) instead of N — and offsets can't drift mid-loop.
        var first = Matches[0].Start;
        var last = Matches[^1].End;
        var sb = new StringBuilder(last - first);
        var pos = first;
        foreach (var m in Matches)
        {
            sb.Append(text, pos, m.Start - pos);
            sb.Append(replacement);
            pos = m.End;
        }

        SelfEdit(() => _view.Editor.Replace(first, last - first, sb.ToString()));
        Recompute(resetCurrent: true, selectMatch: false);
        _find.Focus();
    }

    // ----- internal test hooks -----
    internal IReadOnlyList<SearchMatch> Matches { get; private set; } = [];

    internal void SetQuery(string query)
    {
        _find.Text = query;
        Recompute(resetCurrent: true, selectMatch: true); // TextChanged isn't raised for programmatic sets headlessly
    }

    internal void SetReplacement(string replacement) => _replace.Text = replacement;

    internal void ReplaceAllForTest() => ReplaceAll();

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
