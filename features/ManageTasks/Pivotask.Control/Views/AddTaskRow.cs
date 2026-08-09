using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Pivotask.Core;
using Pivotask.Core.Editing;
using Pivotask.Core.Model;
using Puffinote.Components.Theming;

namespace Pivotask.Control.Views;

/// <summary>
/// Which group's "add a task" row is currently open as a field, for one view. A view owns one of these
/// and hands it to every row it builds, so opening one closes the last — the prototype's single
/// <c>ui.focusAdd</c>, and the reason two columns can't both be waiting for a title at once.
/// </summary>
internal sealed class AddTaskState
{
    /// <summary>The group whose row is a field rather than a button, or null when none is.</summary>
    public string? OpenGroupId { get; set; }

    /// <summary>Repaint the view. Assigned by the owner, because only it knows what "the view" is.</summary>
    public required Action Rebuild { get; init; }
}

/// <summary>
/// The row at the foot of a group: a quiet "＋ Add a task" button that becomes a field when you press it,
/// and stays a field — focused, empty — after every Enter.
///
/// <para>That last part is the whole design. Adding tasks is something people do in runs of five, and a
/// field that surrenders the keyboard after the first one turns each of the other four into a trip back
/// to the pointer. Escape closes it and throws away what is half-typed; clicking away keeps it, because
/// a title you typed is work and Escape is how you say you didn't mean it.</para>
/// </summary>
internal static class AddTaskRow
{
    public static Avalonia.Controls.Control Build(BoardDocument document, TaskGroup group, AddTaskState state)
    {
        if (state.OpenGroupId != group.Id)
        {
            return ClosedRow(group, state);
        }

        // "Task name, then Enter": the placeholder spends its words on the key rather than on repeating
        // the button you just pressed, because the key is the part that isn't obvious.
        var box = new TextBox
        {
            PlaceholderText = "Task name, then Enter",
            Margin = new Thickness(0, 4, 0, 0),
        };
        Avalonia.Automation.AutomationProperties.SetName(box, "Add a task");

        bool settled = false;

        void Settle(bool keep, bool stayOpen)
        {
            if (settled)
            {
                return; // Enter commits and then loses focus, which would otherwise commit a second time
            }

            settled = true;
            string typed = (box.Text ?? string.Empty).Trim();
            state.OpenGroupId = stayOpen ? group.Id : null;

            if (keep && typed.Length > 0)
            {
                // The document's own change event repaints, and the repaint is what puts the fresh
                // empty field back — so there is no second rebuild to ask for here.
                document.Execute(BoardCommands.AddTask(group, new TaskItem { Id = Ids.New(), Title = typed }));
                return;
            }

            state.Rebuild();
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Settle(keep: true, stayOpen: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Settle(keep: false, stayOpen: false);
                e.Handled = true;
            }
        };

        box.LostFocus += (_, _) => Settle(keep: true, stayOpen: false);

        // Posted, not immediate: the field is built during a rebuild and is not in the tree yet.
        box.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => box.Focus());
        return box;
    }

    private static Button ClosedRow(TaskGroup group, AddTaskState state)
    {
        var button = new Button
        {
            Content = "＋  Add a task",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Tokens.Active.MutedText,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 7),
            Margin = new Thickness(0, 4, 0, 0),
        };
        Avalonia.Automation.AutomationProperties.SetName(button, "Add a task");

        button.Click += (_, _) =>
        {
            state.OpenGroupId = group.Id;
            state.Rebuild();
        };

        return button;
    }
}
