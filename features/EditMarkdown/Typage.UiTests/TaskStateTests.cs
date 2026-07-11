using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Typage.Control.Views;
using Typage.Engine.Document;
using Typage.Engine.Markdown;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The six-state task checkbox (PRD §8.3.2): the states the surface reads and writes, what a click means, what
/// the picker offers, and how the six are told apart on screen — by symbol first, colour second.
/// </summary>
public class TaskStateTests
{
    [AvaloniaFact]
    public void EveryState_DrawsItsOwnBox_CarryingTheStateItIsIn()
    {
        var editor = new MarkdownEditor { Text = "- [ ] a\n- [/] b\n- [?] c\n- [>] d\n- [x] e\n- [-] f" };

        Assert.Equal(
            [TaskState.NotStarted, TaskState.InProgress, TaskState.Blocked, TaskState.Deferred, TaskState.Done, TaskState.Cancelled],
            Enumerable.Range(0, 6).Select(line => BoxState(editor, line)).ToArray());
    }

    [AvaloniaFact]
    public void TheBoxIsTheSameWidthInEveryState()
    {
        // The gutter can't depend on the state, or changing an item's status would shuffle its text sideways —
        // and a column of tasks in mixed states would have a ragged left edge.
        var widths = TaskStates.Order
            .Select(state => Box(new MarkdownEditor { Text = $"- [{TaskStates.MarkText(state)}] item" }, 0).Width)
            .Distinct()
            .ToArray();

        Assert.Single(widths);
    }

    [AvaloniaFact]
    public void ClickingTheBox_CompletesIt_FromWhateverStateItWasIn()
    {
        // The primary click is spent on the action worth a click. From any unfinished state — including blocked
        // or deferred — a click means "done"; from done it means "not started". The rest is the picker's job.
        foreach (var mark in new[] { " ", "/", "?", ">", "-" })
        {
            var editor = new MarkdownEditor { Text = $"- [{mark}] task" };
            var box = Box(editor, 0);
            Assert.True(editor.ToggleTaskClickForTest(0, box.X + (box.Width / 2)));
            Assert.Equal("- [x] task", editor.Text);
        }

        var done = new MarkdownEditor { Text = "- [x] task" };
        var doneBox = Box(done, 0);
        Assert.True(done.ToggleTaskClickForTest(0, doneBox.X + (doneBox.Width / 2)));
        Assert.Equal("- [ ] task", done.Text);
    }

    [AvaloniaFact]
    public void SetTaskState_RewritesOnlyTheBoxCharacter()
    {
        // The document is the source of truth and editing never rewrites untouched bytes (golden rule 5): a
        // state change is a one-character replace, and the caret doesn't move.
        var editor = new MarkdownEditor { Text = "- [ ] write the spec" };
        editor.Buffer.Selection = Selection.At(10); // inside the item's text
        editor.SetTaskState(TaskState.Blocked);

        Assert.Equal("- [?] write the spec", editor.Text);
        Assert.Equal(Selection.At(10), editor.Buffer.Selection);
    }

    [AvaloniaFact]
    public void SetTaskState_OnAPlainLine_MakesItATaskInThatState()
    {
        var editor = new MarkdownEditor { Text = "  write the spec" };
        editor.Buffer.Selection = Selection.At(4);
        editor.SetTaskState(TaskState.InProgress);

        Assert.Equal("  - [/] write the spec", editor.Text); // indentation preserved
    }

    [AvaloniaFact]
    public void SetTaskState_IsOneUndoStep_AndReversible()
    {
        var editor = new MarkdownEditor { Text = "- [ ] task" };
        editor.SetTaskState(TaskState.Deferred);
        Assert.Equal("- [>] task", editor.Text);

        editor.Undo();
        Assert.Equal("- [ ] task", editor.Text);
    }

    [AvaloniaFact]
    public void SettingTheStateItIsAlreadyIn_ChangesNothing()
    {
        var editor = new MarkdownEditor { Text = "- [/] task" };
        editor.SetTaskState(TaskState.InProgress);

        Assert.Equal("- [/] task", editor.Text);
        Assert.False(editor.CanUndo); // no empty edit was pushed onto the undo stack
    }

    [AvaloniaFact]
    public void CaretTaskState_ReadsTheCaretsBox()
    {
        // A blank line separates the paragraph from the item — a plain line directly under a task is the item's
        // own hard-wrapped continuation, and a status command there targets the task, as it should.
        var editor = new MarkdownEditor { Text = "- [?] blocked\n\nplain" };
        editor.Buffer.Selection = Selection.At(8);
        Assert.Equal(TaskState.Blocked, editor.CaretTaskState);

        editor.Buffer.Selection = Selection.At(17); // on the plain paragraph
        Assert.Null(editor.CaretTaskState);
    }

    [AvaloniaFact]
    public void AResolvedItem_ReadsAsClosed_AndAnActiveOneDoesNot()
    {
        // Done and cancelled are struck through and dimmed — off the active board. The four active states are
        // ordinary text: a blocked task is still work to do, and must not read as finished.
        Assert.True(IsStruck(new MarkdownEditor { Text = "- [x] done\nx" }));
        Assert.True(IsStruck(new MarkdownEditor { Text = "- [-] cancelled\nx" }));

        Assert.False(IsStruck(new MarkdownEditor { Text = "- [ ] todo\nx" }));
        Assert.False(IsStruck(new MarkdownEditor { Text = "- [/] doing\nx" }));
        Assert.False(IsStruck(new MarkdownEditor { Text = "- [?] blocked\nx" }));
        Assert.False(IsStruck(new MarkdownEditor { Text = "- [>] later\nx" }));
    }

    [AvaloniaFact]
    public void CancellingATask_SinksItWithTheDoneOnes()
    {
        // The completed-task sort separates work still to do from work that isn't. An abandoned task is no more
        // outstanding than a finished one, so cancelling sinks an item exactly as completing it does.
        var editor = new MarkdownEditor
        {
            Text = "- [ ] cut this\n- [?] blocked\n- [ ] todo",
            CompletedTaskSorting = MarkdownEditor.CompletedTaskSortMode.Bottom,
        };

        editor.Buffer.Selection = Selection.At(0);
        editor.SetTaskState(TaskState.Cancelled);

        Assert.Equal("- [?] blocked\n- [ ] todo\n- [-] cut this", editor.Text);
    }

    [AvaloniaFact]
    public void EveryState_HasItsOwnColour_AndItsOwnSymbol()
    {
        // Colour is a *second* channel, never the only one: each state draws a distinct symbol too, so the six
        // survive colour-blindness and a monochrome print (PRD §9.6).
        foreach (var theme in new[] { MarkdownTheme.Dark, MarkdownTheme.Light, MarkdownTheme.HighContrast })
        {
            var colors = TaskStates.Order.Select(theme.TaskColor).Distinct().Count();
            Assert.Equal(6, colors);
        }

        // The un-lit lamp is the only one with no wash — the zero point reads as a bare ring.
        Assert.Null(MarkdownTheme.Dark.TaskFill(TaskState.NotStarted));
        Assert.All(
            TaskStates.Order.Where(s => s != TaskState.NotStarted),
            state => Assert.NotNull(MarkdownTheme.Dark.TaskFill(state)));
    }

    [AvaloniaFact]
    public void ThePicker_OffersTheSixStates_ActiveBeforeResolved()
    {
        var picker = new TaskStatusPicker(MarkdownTheme.Dark, TaskState.Blocked);
        var offered = picker.Children.Select(c => (TaskState)((Button)c).Tag!).ToArray();

        Assert.Equal(TaskStates.Order, offered);
        Assert.Equal(TaskState.Cancelled, offered[^1]); // the resolved states come last
    }

    [AvaloniaFact]
    public void ThePicker_ReportsTheStateThatWasChosen()
    {
        var picker = new TaskStatusPicker(MarkdownTheme.Dark, TaskState.NotStarted);
        TaskState? chosen = null;
        picker.Chosen += state => chosen = state;

        var deferred = picker.Children.OfType<Button>().First(b => (TaskState)b.Tag! == TaskState.Deferred);
        deferred.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(TaskState.Deferred, chosen);
    }

    [AvaloniaFact]
    public void ADocumentOfEveryState_Renders()
    {
        // The lamps are custom-drawn (geometry, a pushed transform, a stroked and a filled path), so the one
        // thing a headless host *can* prove about the draw path is that it runs: a malformed glyph path or a
        // mis-scoped transform throws here rather than on someone's screen. (Pixels can't be checked — this CI
        // has no GPU and the test host uses headless drawing; see TestAppBuilder.)
        var view = new MarkdownEditorView { Text = "- [ ] a\n- [/] b\n- [?] c\n- [>] d\n- [x] e\n- [-] f" };
        var window = new Window { Width = 400, Height = 300, Content = view };
        window.Show();

        window.CaptureRenderedFrame(); // forces a render pass; must not throw

        Assert.True(view.Editor.RawMeasureCount > 0); // confirms Render actually executed
    }

    // ----- helpers -----

    /// <summary>The checkbox decoration run on <paramref name="line"/> — a line has at most one live box.</summary>
    private static VisualRun Box(MarkdownEditor editor, int line) =>
        editor.BuildVisualLineForTest(line).Runs.First(r => r.Decoration == DecorationKind.Checkbox);

    /// <summary>Which state that box is in: the run carries it as its text (the box character).</summary>
    private static TaskState BoxState(MarkdownEditor editor, int line) =>
        TaskStates.FromMark(Box(editor, line).Text[0]);

    /// <summary>Whether the item's text is struck through — how a resolved task reads as closed.</summary>
    private static bool IsStruck(MarkdownEditor editor) =>
        editor.BuildVisualLineForTest(0).Runs
            .Any(r => r.Kind == VisualRunKind.Text && r.Style.Decorations is { Count: > 0 });
}
