using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Theming;

namespace Puffinote.Components.Controls;

/// <summary>
/// One page of a <see cref="TabbedSettingsView"/>: a title, the form it shows, and — optionally — the
/// config file that form writes to.
/// <para>
/// The file is named by <b>purpose</b> rather than by app: a tab that reads "Notes" tells you what it
/// changes, where "Typage" only tells you which program someone happened to build. <paramref name="ConfigPath"/>
/// backs the page's escape hatch: a button that opens the file itself, because a form can only ever expose the
/// settings someone thought to expose, and the file has all of them.
/// </para>
/// </summary>
public sealed record SettingsTab(string Title, SettingsEditorView Form, string? ConfigPath = null);

/// <summary>
/// A settings dialog with more than one subject: a strip of tabs over a shared body, each tab a
/// <see cref="SettingsEditorView"/> in its own right. Puffinote's Preferences is the case it exists for —
/// the shell's own settings plus one tab per embedded editor, each writing that editor's own config file —
/// but nothing here knows about Puffinote; a host hands it pages and gets back a form.
/// <para>
/// Every page's form is built up front and <see cref="Commit"/> writes back <em>all</em> of them, so edits
/// on a tab you moved away from are not quietly lost. As with the single-page form, nothing is written until
/// the host commits, which is what makes the dialog's Cancel genuinely free.
/// </para>
/// </summary>
public sealed class TabbedSettingsView : UserControl
{
    private readonly IReadOnlyList<Control> _pages;
    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly ContentControl _body = new();

    public TabbedSettingsView(IReadOnlyList<SettingsTab> tabs, double width = 380)
    {
        if (tabs is null || tabs.Count == 0)
        {
            throw new ArgumentException("A tabbed settings view needs at least one tab.", nameof(tabs));
        }

        Tabs = tabs;

        // The pages are built once and kept: a control has one parent, so rebuilding a page on every tab
        // switch would re-parent its form — and lose whatever had been typed into it.
        _pages = [.. tabs.Select(Page)];

        Content = new StackPanel
        {
            Spacing = 12,
            Width = width,
            Children = { _strip, _body },
        };

        SelectedIndex = 0;
    }

    /// <summary>The pages, in the order they were declared (the seam the UI tests drive).</summary>
    public IReadOnlyList<SettingsTab> Tabs { get; }

    /// <summary>Which page is showing. Out-of-range values are clamped rather than throwing.</summary>
    public int SelectedIndex
    {
        get;
        set
        {
            var index = Math.Clamp(value, 0, Tabs.Count - 1);
            if (index == field)
            {
                return;
            }

            field = index;
            _body.Content = _pages[index];
            RebuildStrip();
        }
    } = -1;

    /// <summary>
    /// Which page is showing, named rather than numbered — the form a host can persist across runs and still
    /// resolve later. A title that no longer matches any tab (renamed, dropped, or written by an older version)
    /// selects nothing rather than throwing or landing on whichever tab happens to hold that position now: an
    /// index survives a reordering by pointing confidently at the wrong subject, where a name simply stops
    /// matching, and the caller keeps the tab it already had.
    /// </summary>
    public string SelectedTitle
    {
        get => Tabs[SelectedIndex].Title;
        set
        {
            for (var index = 0; index < Tabs.Count; index++)
            {
                if (string.Equals(Tabs[index].Title, value, StringComparison.Ordinal))
                {
                    SelectedIndex = index;
                    return;
                }
            }
        }
    }

    /// <summary>Raised when a page's "edit the file" button is pressed; the host opens it (this control
    /// builds UI, and launching an external editor is not UI).</summary>
    public event Action<SettingsTab>? OpenConfigFileRequested;

    /// <summary>Write every page's edited values back through their setters.</summary>
    public void Commit()
    {
        foreach (var tab in Tabs)
        {
            tab.Form.Commit();
        }
    }

    private void RebuildStrip()
    {
        var palette = Tokens.Active;
        _strip.Children.Clear();

        for (var index = 0; index < Tabs.Count; index++)
        {
            var isSelected = index == SelectedIndex;
            var target = index;
            var button = new Button
            {
                Content = Tabs[index].Title,
                Padding = new Thickness(10, 4),
                FontSize = 12,
                Background = isSelected ? palette.AccentSubtle : Brushes.Transparent,
                Foreground = isSelected ? palette.Accent : palette.MutedText,
                BorderThickness = default,
            };
            button.Click += (_, _) => SelectedIndex = target;
            _strip.Children.Add(button);
        }
    }

    /// <summary>A config file named as its owning folder and file — "Typage/settings.json".</summary>
    private static string Qualify(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? string.Empty);
        return folder.Length > 0 ? $"{folder}/{name}" : name;
    }

    private Control Page(SettingsTab tab)
    {
        if (tab.ConfigPath is not { Length: > 0 } path)
        {
            return tab.Form;
        }

        var open = new Button
        {
            // Qualified by its folder: every app in the family calls its file settings.json, so the bare
            // file name would label all five buttons identically.
            Content = $"Edit {Qualify(path)}…",
            Padding = new Thickness(8, 3),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        open.Click += (_, _) => OpenConfigFileRequested?.Invoke(tab);

        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                tab.Form,
                new Border
                {
                    BorderBrush = Tokens.Active.SurfaceBorder,
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Margin = new Thickness(0, 8, 0, 0),
                    Padding = new Thickness(0, 10, 0, 0),
                    Child = new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            open,
                            new TextBlock
                            {
                                Text = $"Opens {path} in your default editor — every setting is in there, "
                                    + "including the ones this dialog doesn't show.",
                                FontSize = 11,
                                Foreground = Tokens.Active.MutedText,
                                TextWrapping = TextWrapping.Wrap,
                            },
                        },
                    },
                },
            },
        };
    }
}
