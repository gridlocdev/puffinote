using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Theming;
using Puffinote.Text.Document;

namespace Plaintain.Control.Views;

/// <summary>
/// The find strip: a query, a live match count, step forward/back, and Esc to dismiss. Plain substring
/// search with an optional regex, over the document's text — the same <see cref="DocumentSearch"/> the
/// rest of the family uses, so "what counts as a match" is one answer everywhere.
///
/// <para>Deliberately not shared with Typage's find bar yet. Proposal 0003 §5 says to promote it only
/// once the duplication is <i>observed</i>, and the two differ where it matters: this one has no
/// replace, no reveal-through-concealment, and it reports matches back as row highlights the editor
/// paints itself.</para>
/// </summary>
public sealed class FindBar : Border
{
    private readonly TextBlock _count;
    private readonly CheckBox _regex;
    private readonly CheckBox _matchCase;

    public FindBar()
    {
        var palette = Tokens.Active;
        Background = palette.Surface;
        BorderBrush = palette.SurfaceBorder;
        BorderThickness = new Thickness(0, 0, 0, 1);
        Padding = new Thickness(10, 6);
        IsVisible = false;

        QueryBox = new TextBox { Width = 240, PlaceholderText = "Find" };
        QueryBox.KeyDown += OnQueryKeyDown;
        QueryBox.TextChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);

        _count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = palette.MutedText, MinWidth = 90 };
        _regex = new CheckBox { Content = "Regex", VerticalAlignment = VerticalAlignment.Center };
        _matchCase = new CheckBox { Content = "Aa", VerticalAlignment = VerticalAlignment.Center };
        _regex.IsCheckedChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _matchCase.IsCheckedChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);

        var previous = new Button { Content = "‹" };
        var next = new Button { Content = "›" };
        var close = new Button { Content = "✕" };
        previous.Click += (_, _) => Step?.Invoke(this, -1);
        next.Click += (_, _) => Step?.Invoke(this, +1);
        close.Click += (_, _) => Dismiss();

        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { QueryBox, previous, next, _count, _matchCase, _regex, close },
        };
    }

    /// <summary>Raised when the query or its options change — the host re-runs the search.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised to step to the previous (-1) or next (+1) match.</summary>
    public event EventHandler<int>? Step;

    /// <summary>Raised when the strip is dismissed (Esc or the close button).</summary>
    public event EventHandler? Dismissed;

    public string Query => QueryBox.Text ?? string.Empty;

    public bool UseRegex => _regex.IsChecked == true;

    public bool MatchCase => _matchCase.IsChecked == true;

    /// <summary>The search box, so a host can focus it when the strip opens.</summary>
    public TextBox QueryBox { get; }

    public void Show(string? seed)
    {
        if (!string.IsNullOrEmpty(seed))
        {
            QueryBox.Text = seed;
        }

        IsVisible = true;
        QueryBox.Focus();
        QueryBox.SelectAll();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dismiss()
    {
        IsVisible = false;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Report the search's result: "3 of 17", "No results", or nothing while the query is empty.</summary>
    public void ReportMatches(int current, int total)
    {
        _count.Text = Query.Length == 0 ? string.Empty
            : total == 0 ? "No results"
            : $"{current + 1} of {total}";
        _count.Foreground = total == 0 && Query.Length > 0 ? Brushes.IndianRed : Tokens.Active.MutedText;
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Dismiss();
                e.Handled = true;
                break;
            case Key.Enter:
                Step?.Invoke(this, e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : +1);
                e.Handled = true;
                break;
        }
    }
}
