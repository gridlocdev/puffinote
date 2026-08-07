using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Puffinote.Text.Document;
using Typage.Control.Controls;
using Typage.Control.Rendering;
using Typage.Control.Views;
using Typage.Engine.Markdown;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// The three-state task checkbox (PRD §8.3.2): the states the surface reads and writes, what a click means, what
/// the picker offers, and how the three are told apart on screen — by shape first, colour second.
/// </summary>
public class TaskStateTests
{
    [AvaloniaFact]
    public void EveryState_DrawsItsOwnBox_CarryingTheStateItIsIn()
    {
        var editor = new MarkdownEditor { Text = "- [ ] a\n- [/] b\n- [x] c" };

        Assert.Equal(
            [TaskState.NotStarted, TaskState.InProgress, TaskState.Done],
            Enumerable.Range(0, 3).Select(line => BoxState(editor, line)).ToArray());
    }

    [AvaloniaFact]
    public void TheRetiredCharacters_AreNotBoxes()
    {
        // The dialect no longer admits [?], [>] or [-]. A file that still spells them keeps them as literal text
        // — no box is drawn, and nothing rewrites the author's bytes (golden rule 5).
        var editor = new MarkdownEditor { Text = "- [?] a\n- [>] b\n- [-] c" };

        for (var line = 0; line < 3; line++)
        {
            Assert.DoesNotContain(
                editor.BuildVisualLineForTest(line).Runs,
                r => r.Decoration == DecorationKind.Checkbox);
        }

        Assert.Equal("- [?] a\n- [>] b\n- [-] c", editor.Text);
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
        // The primary click is spent on the action worth a click. From either unfinished state a click means
        // "done"; from done it means "not started". It does not stop at in-progress on the way — a click that
        // sometimes completes and sometimes doesn't is a click you have to look at. In progress is the picker's job.
        foreach (var mark in new[] { " ", "/" })
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
        editor.SetTaskState(TaskState.InProgress);

        Assert.Equal("- [/] write the spec", editor.Text);
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
        editor.SetTaskState(TaskState.Done);
        Assert.Equal("- [x] task", editor.Text);

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
        var editor = new MarkdownEditor { Text = "- [/] doing\n\nplain" };
        editor.Buffer.Selection = Selection.At(8);
        Assert.Equal(TaskState.InProgress, editor.CaretTaskState);

        editor.Buffer.Selection = Selection.At(15); // on the plain paragraph
        Assert.Null(editor.CaretTaskState);
    }

    [AvaloniaFact]
    public void ADoneItem_ReadsAsClosed_AndAnUnfinishedOneDoesNot()
    {
        // Done is struck through and dimmed — off the active board. The two unfinished states are ordinary text:
        // a task under way is still work to do, and must not read as finished.
        Assert.True(IsStruck(new MarkdownEditor { Text = "- [x] done\nx" }));

        Assert.False(IsStruck(new MarkdownEditor { Text = "- [ ] todo\nx" }));
        Assert.False(IsStruck(new MarkdownEditor { Text = "- [/] doing\nx" }));
    }

    [AvaloniaFact]
    public void OnlyCompletingATask_SinksIt()
    {
        // The completed-task sort separates work still to do from work that isn't. Starting a task doesn't
        // finish it, so an item that goes in-progress stays where it is; completing one sinks it.
        var doing = new MarkdownEditor
        {
            Text = "- [ ] cut this\n- [ ] todo",
            CompletedTaskSorting = MarkdownEditor.CompletedTaskSortMode.Bottom,
        };

        doing.Buffer.Selection = Selection.At(0);
        doing.SetTaskState(TaskState.InProgress);
        Assert.Equal("- [/] cut this\n- [ ] todo", doing.Text); // still outstanding: it holds its place

        doing.SetTaskState(TaskState.Done);
        Assert.Equal("- [ ] todo\n- [x] cut this", doing.Text);
    }

    [AvaloniaFact]
    public void TheTwoLitStates_ShareOneAccent_AndTheRestingBoxSpendsNone()
    {
        // Single-hue (PRD §8.3.2): the dash and the check are drawn in the same accent, so "under way" and "done"
        // read as two points on one path rather than two unrelated alarms. Shape, not colour, is what tells the
        // three apart — which is what keeps them legible in monochrome and to a colour-blind reader (§9.6).
        foreach (var theme in new[] { MarkdownTheme.Dark, MarkdownTheme.Light, MarkdownTheme.HighContrast })
        {
            Assert.NotEqual(theme.TaskAccent, theme.TaskBorder);

            // The check is read against the accent it sits on, not against the page: they must not be the same.
            Assert.NotEqual(theme.TaskCheck, theme.TaskAccent);
        }

        // In high contrast the accent is a *light* blue, so the check flips to black — a white check on it would
        // be the one unreadable thing on the page.
        Assert.Equal(Colors.Black, ((ISolidColorBrush)MarkdownTheme.HighContrast.TaskCheck).Color);
        Assert.Equal(Colors.White, ((ISolidColorBrush)MarkdownTheme.Dark.TaskCheck).Color);
    }

    [AvaloniaFact]
    public void ThePicker_OffersTheThreeStates_InTheOrderTheWorkMoves()
    {
        var picker = new TaskStatusPicker(MarkdownTheme.Dark, TaskState.InProgress);
        var offered = picker.Children.Select(c => (TaskState)((Button)c).Tag!).ToArray();

        Assert.Equal(TaskStates.Order, offered);
        Assert.Equal([TaskState.NotStarted, TaskState.InProgress, TaskState.Done], offered);
    }

    [AvaloniaFact]
    public void ThePicker_ReportsTheStateThatWasChosen()
    {
        var picker = new TaskStatusPicker(MarkdownTheme.Dark, TaskState.NotStarted);
        TaskState? chosen = null;
        picker.Chosen += state => chosen = state;

        var inProgress = picker.Children.OfType<Button>().First(b => (TaskState)b.Tag! == TaskState.InProgress);
        inProgress.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(TaskState.InProgress, chosen);
    }

    [AvaloniaFact]
    public void ADocumentOfEveryState_Renders()
    {
        // The boxes are custom-drawn (a rounded rect, a pushed transform, a stroked path), so the one thing a
        // headless host *can* prove about the draw path is that it runs: a malformed glyph path or a mis-scoped
        // transform throws here rather than on someone's screen. (Pixels can't be checked — this CI has no GPU
        // and the test host uses headless drawing; see TestAppBuilder.)
        var view = new MarkdownEditorView { Text = "- [ ] a\n- [/] b\n- [x] c" };
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

    /// <summary>Whether the item's text is struck through — how a done task reads as closed.</summary>
    private static bool IsStruck(MarkdownEditor editor) =>
        editor.BuildVisualLineForTest(0).Runs
            .Any(r => r.Kind == VisualRunKind.Text && r.Style.Decorations is { Count: > 0 });
}
