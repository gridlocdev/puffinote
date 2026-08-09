using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Pivotask.Core;
using Pivotask.Core.Editing;
using Pivotask.Core.Model;
using Puffinote.Components.Theming;

namespace Pivotask.Control.Views;

/// <summary>
/// The strip at the top of a group, in either view: its name, how many tasks it is showing, and the way to
/// delete it — plus, in the list only, the caret that folds it and the dotted leader between the name and
/// the count.
/// <para>
/// One builder for both because the prototype has one: a column head and a section head are the same four
/// things in the same order, and the differences are additions rather than a different design. Two
/// builders is how they drifted apart the first time.
/// </para>
/// </summary>
internal static class GroupHeader
{
    /// <summary>The name is written the way the prototype writes it — small, spaced, and upper case.</summary>
    private const double NameSize = 12.5;

    /// <summary>
    /// Build the header for <paramref name="group"/>. <paramref name="caret"/> adds the list's fold
    /// control and its dotted leader; the board has neither, because a board column is not something you
    /// collapse — that affordance belongs to the list, and the prototype puts it only there.
    /// </summary>
    public static Avalonia.Controls.Control Build(
        BoardDocument document, TaskGroup group, int visibleCount, bool caret, Action requestDelete)
    {
        var palette = Tokens.Active;
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };

        if (caret)
        {
            var fold = IconButton(group.Collapsed ? "▸" : "▾", group.Collapsed ? "Expand group" : "Collapse group", palette);
            fold.Click += (_, _) => document.Execute(BoardCommands.SetCollapsed(group, !group.Collapsed));
            DockPanel.SetDock(fold, Dock.Left);
            row.Children.Add(fold);
        }

        // Hover-revealed, like the prototype's `.kill`: a delete button that is always lit invites the
        // one click on this header nobody wants to make by accident.
        var kill = IconButton("✕", $"Delete {group.Name}", palette);
        kill.Opacity = 0;
        kill.Click += (_, _) => requestDelete();
        row.PointerEntered += (_, _) => kill.Opacity = 1;
        row.PointerExited += (_, _) => kill.Opacity = 0;
        kill.GotFocus += (_, _) => kill.Opacity = 1; // reachable by keyboard, not only by pointer
        kill.LostFocus += (_, _) => kill.Opacity = 0;
        DockPanel.SetDock(kill, Dock.Right);
        row.Children.Add(kill);

        var count = new TextBlock
        {
            Text = visibleCount.ToString(System.Globalization.CultureInfo.CurrentCulture),
            Foreground = palette.MutedText,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 4, 0),
        };
        DockPanel.SetDock(count, Dock.Right);
        row.Children.Add(count);

        var name = BuildName(document, group);

        if (caret)
        {
            // The dotted leader runs from the name to the count, which is what makes a list of sections
            // read as a table of contents rather than as a stack of unrelated headings.
            var leader = new Border
            {
                BorderBrush = palette.SurfaceBorder,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(8, 0, 4, 3),
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 1,
            };
            DockPanel.SetDock(name, Dock.Left);
            row.Children.Add(name);
            row.Children.Add(leader);
        }
        else
        {
            row.Children.Add(name);
        }

        return row;
    }

    /// <summary>
    /// The group's name, editable in place. A click turns the label into a text box, Enter and blur commit,
    /// Escape abandons — the prototype's <c>contenteditable</c> in the idiom Avalonia actually has. Renaming
    /// a column is the kind of thing you do while looking at it, so making it a dialog would be a worse
    /// answer even though it would be an easier one.
    /// </summary>
    private static Avalonia.Controls.Control BuildName(BoardDocument document, TaskGroup group)
    {
        var host = new ContentControl { VerticalAlignment = VerticalAlignment.Center };

        var label = new TextBlock
        {
            Text = group.Name.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
            FontWeight = FontWeight.SemiBold,
            FontSize = NameSize,
            LetterSpacing = 0.7,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Ibeam),
        };
        ToolTip.SetTip(label, "Click to rename");

        void StartEditing()
        {
            var box = new TextBox
            {
                Text = group.Name,
                FontSize = NameSize,
                FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(2, 0),
                MinWidth = 80,
            };

            bool settled = false;
            void Commit(bool keep)
            {
                if (settled)
                {
                    return; // Enter commits and then moves focus, which would otherwise commit again
                }

                settled = true;
                string typed = (box.Text ?? string.Empty).Trim();

                // An empty name is not a rename, it is a slip — the prototype puts the old one back too.
                if (keep && typed.Length > 0 && !string.Equals(typed, group.Name, StringComparison.Ordinal))
                {
                    document.Execute(BoardCommands.RenameGroup(group, typed));
                    return; // the document's change event rebuilds the whole header
                }

                host.Content = label;
            }

            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Commit(keep: true);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    Commit(keep: false);
                    e.Handled = true;
                }
            };
            box.LostFocus += (_, _) => Commit(keep: true);

            host.Content = box;
            box.Focus();
            box.SelectAll();
        }

        label.PointerPressed += (_, e) =>
        {
            StartEditing();
            e.Handled = true; // the card beneath must not read this as a click on the column
        };

        host.Content = label;
        return host;
    }

    private static Button IconButton(string glyph, string tip, ThemePalette palette)
    {
        var button = new Button
        {
            Content = glyph,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 0),
            Foreground = palette.MutedText,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        return button;
    }
}
