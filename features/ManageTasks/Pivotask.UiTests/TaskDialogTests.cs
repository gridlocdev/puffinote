using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Pivotask.Control.Views;
using Pivotask.Core;
using Pivotask.Core.Model;
using Xunit;

namespace Pivotask.UiTests;

/// <summary>
/// The task dialog: everything about one task, disclosed progressively. A task is usually a sentence,
/// and a dialog that opens with five empty sections implies you have failed to fill them in.
/// </summary>
public class TaskDialogTests
{
    private static (TaskBoardView View, BoardDocument Document, TaskItem Task) Open(Action<Board>? arrange = null)
    {
        var board = new Board();
        board.Labels.Add(new Pivotask.Core.Model.Label { Id = "ops", Name = "ops", Color = "#4d7c0f" });
        var inbox = new TaskGroup { Id = "g1", Name = "Inbox" };
        var week = new TaskGroup { Id = "g2", Name = "This week" };
        var task = new TaskItem { Id = "t1", Title = "Draft the retro agenda" };
        inbox.Tasks.Add(task);
        board.Groups.Add(inbox);
        board.Groups.Add(week);
        arrange?.Invoke(board);

        var document = new BoardDocument(board);
        var view = new TaskBoardView { Document = document };
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        window.UpdateLayout();

        _ = view.OpenTaskAsync(task);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (view, document, task);
    }

    private static TaskDialog Dialog(TaskBoardView view) =>
        view.OverlayForTest.GetLogicalDescendants().OfType<TaskDialog>().Single();

    /// <summary>Nothing but the title and the notes until the task has more, or you ask for more.</summary>
    [AvaloniaFact]
    public void A_plain_task_opens_with_no_empty_sections()
    {
        var (view, _, _) = Open();
        var headings = Dialog(view).GetLogicalDescendants().OfType<TextBlock>()
            .Select(t => t.Text).ToList();

        Assert.DoesNotContain("Labels", headings);
        Assert.DoesNotContain("Due", headings);
        Assert.DoesNotContain("Steps", headings);
    }

    /// <summary>
    /// One "+" opens the whole field list, rather than a row of "+ Labels" / "+ Due date" / "+ Steps"
    /// buttons sitting permanently under the notes. Three buttons is three things to read every time you
    /// open a task, to add something you usually don't.
    /// </summary>
    [AvaloniaFact]
    public void The_plus_menu_offers_every_field_and_reveals_the_one_picked()
    {
        var (view, _, _) = Open();
        var dialog = Dialog(view);

        Assert.Empty(Named(dialog, "+ Steps")); // the old row of buttons is gone
        Assert.Single(Named(dialog, "Add a field to this task"));

        var rows = MenuRows(dialog);
        Assert.Equal(["Labels", "Due date", "Steps"], rows.Select(Name));

        Click(rows.Single(r => Name(r) == "Steps"));

        Assert.Contains(
            "Steps",
            dialog.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
    }

    /// <summary>
    /// A field the task already has stays in the menu and is marked "on", so the menu is a complete map
    /// of what a task can hold rather than a list that shrinks as you use it.
    /// </summary>
    [AvaloniaFact]
    public void A_field_the_task_already_has_is_listed_and_marked_on()
    {
        var (view, _, _) = Open(board => board.Groups[0].Tasks[0].Due = new DateOnly(2026, 8, 20));
        var rows = MenuRows(Dialog(view));

        Assert.Equal("on", Avalonia.Automation.AutomationProperties.GetHelpText(rows.Single(r => Name(r) == "Due date")));
        Assert.Null(Avalonia.Automation.AutomationProperties.GetHelpText(rows.Single(r => Name(r) == "Labels")));
    }

    /// <summary>Open the "+" menu and return its rows.</summary>
    private static IReadOnlyList<Button> MenuRows(TaskDialog dialog)
    {
        var button = Named(dialog, "Add a field to this task").Single();
        dialog.AddFieldMenu.ShowAt(button);
        Dispatcher.UIThread.RunJobs();

        var panel = (StackPanel)dialog.AddFieldMenu.Content!;
        return [.. panel.Children.OfType<Button>()];
    }

    private static string? Name(Avalonia.Controls.Control control) =>
        Avalonia.Automation.AutomationProperties.GetName(control);

    private static IReadOnlyList<Button> Named(Avalonia.Controls.Control root, string name) =>
        [.. root.GetLogicalDescendants().OfType<Button>().Where(b => Name(b) == name || b.Content as string == name)];

    private static void Click(Button button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
}
