using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Threading;
using Typage.Control.Rendering;
using Typage.Control.Views;
using Typage.Engine.Document;
using Typage.Engine.Editing;
using Typage.Engine.Markdown;

namespace Typage.Control.Controls;

/// <summary>
/// Task states (PRD §8.3.2): the surface's half of the three-state checkbox — the commands that set a state and
/// the picker that offers them. The <i>writing</i> of a state is
/// <see cref="SetTaskStateOnLine"/> (in the pointer partial, next to the click that also calls it): one edit
/// path, whether the change came from a click, a shortcut, or the picker.
/// </summary>
public partial class MarkdownEditor
{
    /// <summary>The state of the task on the caret's line, or null when that line carries no box (⌥⌘L's precondition,
    /// and what the picker opens on).</summary>
    public TaskState? CaretTaskState => TaskStateOf(ListTargetLine());

    /// <summary>
    /// Put the caret's line in <paramref name="state"/>, adding a box to the line if it hasn't one — the command
    /// behind the picker's rows, and the seam a host can drive directly ("mark this in progress").
    /// </summary>
    public void SetTaskState(TaskState state)
    {
        var line = ListTargetLine();
        if (LinePrefix.Parse(Buffer.Document.GetLineText(line)).HasTask)
        {
            SetTaskStateOnLine(line, state);
        }
        else
        {
            InsertTaskMarker(line, state);
        }

        Focus();
    }

    /// <summary>
    /// Open the status picker on the caret's task (⌥⌘L). A line with no box gets one first — asking for a
    /// status on a plain line plainly means "make this a task", so it does, rather than doing nothing.
    /// </summary>
    public void OpenTaskStatusPicker()
    {
        var line = ListTargetLine();
        if (!LinePrefix.Parse(Buffer.Document.GetLineText(line)).HasTask)
        {
            InsertTaskMarker(line, TaskState.NotStarted);
        }

        ShowTaskStatusPicker(line);
    }

    /// <summary>
    /// Drop the picker under <paramref name="line"/>'s checkbox — the box it is about, wherever that box is on
    /// screen, so a right-click and ⌥⌘L land the palette in the same place: where the eye already is.
    /// </summary>
    private void ShowTaskStatusPicker(int line)
    {
        if (TaskStateOf(line) is not { } current || TaskBoxRect(line) is not { } boxRect)
        {
            return;
        }

        var picker = new TaskStatusPicker(EditorTheme, current);
        var flyout = new Flyout
        {
            Content = new Border { Padding = new Thickness(6), Child = picker },

            // Hang the popup off the editor's top-left corner and push it out to the box: a flyout can only be
            // anchored to a control, and the checkbox is drawn, not a control of its own. Anchor + gravity +
            // offset is how you say "here, in that control's coordinates".
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
            HorizontalOffset = boxRect.X,
            VerticalOffset = boxRect.Bottom + 4,
            ShowMode = FlyoutShowMode.Standard,
        };

        picker.Chosen += state =>
        {
            flyout.Hide();
            SetTaskStateOnLine(line, state);
            Focus();
        };

        picker.Cancelled += () =>
        {
            flyout.Hide();
            Focus();
        };

        // Focus the current state's row once the popup is realized — focusing a control that isn't yet shown
        // silently fails, and the picker would open needing a click before it could be driven by the keyboard.
        flyout.Opened += (_, _) => Dispatcher.UIThread.Post(picker.FocusCurrent);
        flyout.ShowAt(this);
    }

    /// <summary>
    /// Where <paramref name="line"/>'s drawn checkbox sits, in the control's own coordinates (null when the line
    /// has no box, or its box isn't laid out). The picker anchors to this.
    /// </summary>
    private Rect? TaskBoxRect(int line)
    {
        EnsureLayout();
        var block = BlockOf(line);
        var visual = BlockVisual(block);
        var blockStart = BlockStartOffset(block);
        foreach (var run in visual.Runs)
        {
            if (run.Decoration != DecorationKind.Checkbox
                || Buffer.Document.GetPosition(blockStart + run.Col).Line != line)
            {
                continue;
            }

            var rowHeight = RowHeight(block);
            var top = RowTop(block) + PaddingTop - _offset.Y + (run.Row * rowHeight);
            var size = CheckboxBoxSize;
            return new Rect(
                ContentLeft() - _offset.X + run.X + CheckboxLeftPad,
                top + ((rowHeight - size) / 2),
                size,
                size);
        }

        return null;
    }

    /// <summary>
    /// Add a task box in <paramref name="state"/> to <paramref name="line"/>: <c>"[x] "</c> after an existing
    /// list marker, or a whole <c>"- [x] "</c> item after the indentation, carrying the selection across the
    /// insertion the way <see cref="ToggleTask"/> does (each endpoint rides its own text).
    /// </summary>
    private void InsertTaskMarker(int line, TaskState state)
    {
        var doc = Buffer.Document;
        var prefix = LinePrefix.Parse(doc.GetLineText(line));
        var lineStart = doc.GetLineStart(line);
        var insertAt = lineStart + (prefix.HasList ? prefix.Length : prefix.Indent);
        var box = $"[{TaskStates.MarkText(state)}] ";
        var inserted = prefix.HasList ? box : $"- {box}";
        var sel = Buffer.Selection;

        // A selection that reached the line's first non-space character grows to take the new marker in, since
        // the marker lands right where the highlight began and is now part of the item the user has selected.
        var takesMarker = !sel.IsEmpty && sel.Start == lineStart + prefix.Indent;
        int Place(int p) => p > insertAt || (p == insertAt && !takesMarker) ? p + inserted.Length : p;

        Buffer.ReplaceRange(insertAt, 0, inserted);
        Buffer.Selection = new Selection(Place(sel.Anchor), Place(sel.Caret)); // direction preserved
    }
}
