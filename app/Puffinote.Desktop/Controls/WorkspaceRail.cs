using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Puffinote.Components.Controls.Icons;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Controls;

/// <summary>
/// The narrow strip down the far-left edge of the window, switching between the app's four workspaces —
/// Todos, Calendar, Notes, Data (PRD §7.1). Outlook's shape, and Outlook's reason for it: these are
/// different <em>things to be doing</em>, not different views of one thing, so they get a destination each
/// rather than a mode buried in a menu.
///
/// <para>It sits outside everything else, including the sidebar it looks like a relative of. The sidebar
/// switches panels <i>within</i> the notebook — Files, Search, Outline, Recent — and collapses to nothing;
/// this switches which of the four you are in and is always there. Two strips of icons with different
/// jobs, so they are told apart the way the rest of the shell tells things apart: this one carries a
/// <b>word</b> under each glyph, and its active entry wears a leading-edge accent bar rather than the
/// sidebar's underline.</para>
///
/// <para>The active entry is marked in three ways on purpose — the bar, the accent, and a spoken
/// "selected" — because colour alone is no signal to a reader who cannot see it (WCAG 2.2 §1.4.1), and
/// the bar is a shape.</para>
/// </summary>
internal sealed class WorkspaceRail : UserControl
{
    /// <summary>Wide enough for a word under a glyph at 11px, narrow enough to cost the notebook nothing.</summary>
    private const double RailWidth = 64;

    /// <summary>The accent bar down the leading edge of the active entry — reserved on every entry.</summary>
    private const double ActiveBarWidth = 3;

    private readonly StackPanel _entries = new() { Orientation = Orientation.Vertical };

    /// <summary>Marks the entry you are in, so the styles below can treat it differently from the rest.</summary>
    private const string ActiveClass = "workspace-active";

    public WorkspaceRail()
    {
        var palette = Tokens.Active;
        Width = RailWidth;

        StyleEntries(palette);
        foreach (var workspace in Workspaces.All)
        {
            _entries.Children.Add(BuildEntry(workspace));
        }

        Content = new Border
        {
            Background = palette.Surface,
            BorderBrush = palette.SurfaceBorder,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(0, 8, 0, 0),
            Child = _entries,
        };

        Refresh();
    }

    /// <summary>Raised when a different workspace is chosen.</summary>
    public event Action<Workspace>? WorkspaceSelected;

    /// <summary>The workspace the rail is showing as current.</summary>
    public Workspace Active
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            Refresh();
        }
    } = Workspace.Notes;

    /// <summary>The titles the rail lists, top to bottom (test seam).</summary>
    internal IReadOnlyList<string> TitlesForTest =>
        [.. _entries.Children.OfType<Button>().Select(b => AutomationProperties.GetName(b) ?? string.Empty)];

    /// <summary>Click an entry by its title, as a pointer would (test seam).</summary>
    internal void ClickForTest(string title) =>
        _entries.Children.OfType<Button>()
            .First(b => AutomationProperties.GetName(b) == title)
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    /// <summary>The title the rail is marking as current, as a screen reader would find it (test seam).</summary>
    internal string? MarkedTitleForTest =>
        _entries.Children.OfType<Button>()
            .Where(b => AutomationProperties.GetHelpText(b) == "selected")
            .Select(b => AutomationProperties.GetName(b))
            .FirstOrDefault();

    /// <summary>
    /// The rail's own answer to hover and press, replacing FluentTheme's.
    ///
    /// <para><b>The entry you are in does not react to the pointer.</b> Hovering it did two visible things
    /// before this: the accent wash became the theme's grey, and the accent bar down its edge vanished —
    /// because the theme's <c>:pointerover</c> setters land on the button's template and outrank a brush
    /// assigned to the button itself. Both are wrong for a destination you have already arrived at: hover
    /// says "this would do something", and clicking the entry you are in does nothing at all. So the active
    /// entry paints identically at rest, under the pointer, and pressed.</para>
    ///
    /// <para>The other three keep exactly the hover they had — the shell's ordinary <c>SurfaceHover</c>
    /// wash — because there the pointer is telling the truth.</para>
    ///
    /// <para>These are set on the template's presenter, where the theme sets its own — a brush on the button
    /// only reaches the presenter through a template binding, which any presenter-level style outranks. That
    /// is also why <b>the resting look is not here</b>: with no style matching, the presenter falls back to
    /// that template binding, so the plain state is the button's own Background/BorderBrush
    /// (<see cref="BuildEntry"/>). An unconditional style saying "transparent" is inert — measured, not
    /// assumed — and leaving the button's brushes off is what let FluentTheme's grey chrome show through on
    /// all four entries at once.</para>
    ///
    /// <para>The brushes are <see cref="Tokens.Active"/>'s, which are re-tinted in place on a theme change,
    /// so these styles follow light/dark without being rebuilt.</para>
    /// </summary>
    private void StyleEntries(ThemePalette palette)
    {
        // Avalonia has no CSS specificity: for one property, the last matching style wins. So the active
        // rules come after the ones they displace — and they spell out hover and press even though the
        // plain ".active" rule would already outrank both by position alone, because "what happens when I
        // hover the one I'm in?" is a question a reader should be able to answer from this list.
        Add(x => x.OfType<Button>().Class(":pointerover"), palette.SurfaceHover, Brushes.Transparent);
        Add(x => x.OfType<Button>().Class(":pressed"), palette.SurfaceHover, Brushes.Transparent);
        Add(x => x.OfType<Button>().Class(ActiveClass), palette.AccentSubtle, palette.Accent);
        Add(x => x.OfType<Button>().Class(ActiveClass).Class(":pointerover"), palette.AccentSubtle, palette.Accent);
        Add(x => x.OfType<Button>().Class(ActiveClass).Class(":pressed"), palette.AccentSubtle, palette.Accent);

        void Add(Func<Selector?, Selector> selector, IBrush background, IBrush border)
        {
            var style = new Style(x => selector(x).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
            style.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, background));
            style.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, border));
            Styles.Add(style);
        }
    }

    private Button BuildEntry(WorkspaceInfo workspace)
    {
        var glyph = new LucideIcon
        {
            IconKey = workspace.Icon,
            Size = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var label = new TextBlock
        {
            Text = workspace.Title,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };

        var button = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Children = { glyph, label },
            },
            Tag = workspace.Id,

            // The resting look, and it has to be set here rather than in a style: with no style matching,
            // the presenter falls back to the template binding — this — and what it would otherwise fall
            // back to is FluentTheme's own grey button chrome, on all four entries at once.
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,

            // The active bar's width is reserved on every entry — the styles above decide whether it is
            // painted — so switching workspaces never nudges the icons sideways.
            BorderThickness = new Thickness(ActiveBarWidth, 0, 0, 0),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(0, 8, ActiveBarWidth, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };

        button.Click += (_, _) => Select(workspace.Id);
        ToolTip.SetTip(button, workspace.Promise.Length > 0 ? $"{workspace.Title} — {workspace.Promise}" : workspace.Title);
        AutomationProperties.SetName(button, workspace.Title);
        return button;
    }

    private void Select(Workspace workspace)
    {
        if (Active == workspace)
        {
            return;
        }

        Active = workspace;
        WorkspaceSelected?.Invoke(workspace);
    }

    private void Refresh()
    {
        var palette = Tokens.Active;
        foreach (var button in _entries.Children.OfType<Button>())
        {
            bool active = button.Tag is Workspace id && id == Active;
            var ink = active ? palette.Accent : palette.MutedText;
            button.Foreground = ink;

            // The wash and the bar are the styles' business (see StyleEntries) — this only says which
            // entry it is, so that hover and press can't paint over the answer.
            button.Classes.Set(ActiveClass, active);
            AutomationProperties.SetHelpText(button, active ? "selected" : null);

            // The glyph is a bare Control rather than text, so it takes the ink only if it is handed it.
            if (button.Content is StackPanel stack)
            {
                foreach (var child in stack.Children)
                {
                    if (child is LucideIcon icon)
                    {
                        icon.Foreground = ink;
                    }
                    else if (child is TextBlock text)
                    {
                        text.Foreground = ink;
                    }
                }
            }
        }
    }
}
