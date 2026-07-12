namespace Typage.Engine.Markdown;

/// <summary>
/// What a task-list item's box says about the item (PRD §8.3.2). Markdown's <c>[ ]</c>/<c>[x]</c> is a bit —
/// done or not — which leaves no way to say that a task is <i>under way</i>: the one distinction a reader
/// scanning a list actually acts on. Typage admits a third character for it, and stops there: one character
/// between the brackets, <b>three states</b>.
///
/// <para>The order of the members is the order the work moves in — not started → in progress → done — and is
/// what <see cref="TaskStates.Order"/> and the picker follow. An unrecognized character between the brackets
/// (<c>[?]</c>, <c>[-]</c>, <c>[a]</c>) is not a task box at all: the line stays literal text (tolerant
/// tokenizing).</para>
/// </summary>
public enum TaskState
{
    /// <summary><c>[ ]</c> — defined, no work yet.</summary>
    NotStarted,

    /// <summary><c>[/]</c> — work is under way.</summary>
    InProgress,

    /// <summary><c>[x]</c> — complete.</summary>
    Done,
}

/// <summary>
/// The task-state vocabulary: the mapping between the character in the box and the <see cref="TaskState"/> it
/// spells, plus the neutral names the surfaces label it with. This is <i>dialect</i>, not presentation — the
/// character set is the source syntax, so it lives with the tokenizer that reads it and the renderers that
/// write it. Colour and glyph are the control's business (<c>MarkdownTheme</c>), not this file's.
/// </summary>
public static class TaskStates
{
    /// <summary>The states in canonical order — the order the work moves in (not started → in progress →
    /// done). The picker and any legend read this, so they can't disagree.</summary>
    public static IReadOnlyList<TaskState> Order { get; } =
    [
        TaskState.NotStarted,
        TaskState.InProgress,
        TaskState.Done,
    ];

    /// <summary>The character between the brackets, canonically (<c>[x]</c>, never <c>[X]</c>).</summary>
    public static char MarkOf(TaskState state) => state switch
    {
        TaskState.InProgress => '/',
        TaskState.Done => 'x',
        _ => ' ',
    };

    /// <summary>The mark as a one-character string (for a single-character document replace).</summary>
    public static string MarkText(TaskState state) => state switch
    {
        TaskState.InProgress => "/",
        TaskState.Done => "x",
        _ => " ",
    };

    /// <summary>The box as it is spelled in the source — <c>"[ ]"</c>, <c>"[x]"</c>, … (what a picker shows as its chip).</summary>
    public static string Code(TaskState state) => state switch
    {
        TaskState.InProgress => "[/]",
        TaskState.Done => "[x]",
        _ => "[ ]",
    };

    /// <summary>The state's name, for a menu row, a tooltip, or an exported <c>aria-label</c>.</summary>
    public static string Label(TaskState state) => state switch
    {
        TaskState.InProgress => "In progress",
        TaskState.Done => "Done",
        _ => "Not started",
    };

    /// <summary>What the state means, in a phrase — the second line of a picker row.</summary>
    public static string Description(TaskState state) => state switch
    {
        TaskState.InProgress => "work is under way",
        TaskState.Done => "the task is complete",
        _ => "defined, no work yet",
    };

    /// <summary>Whether <paramref name="mark"/> is a character a task box may hold.</summary>
    public static bool IsMark(char mark) => mark is ' ' or 'x' or 'X' or '/';

    /// <summary>The state <paramref name="mark"/> spells; <see cref="TaskState.NotStarted"/> for anything else.</summary>
    public static TaskState FromMark(char mark) => mark switch
    {
        'x' or 'X' => TaskState.Done,
        '/' => TaskState.InProgress,
        _ => TaskState.NotStarted,
    };

    /// <summary>The state a task marker's source text (<c>"[x] "</c>, box first) spells.</summary>
    public static TaskState FromMarker(ReadOnlySpan<char> markerText) =>
        markerText.Length > 1 ? FromMark(markerText[1]) : TaskState.NotStarted;

    /// <summary>
    /// The state the primary click / ⌘L lands on from <paramref name="state"/>: <b>done</b>, unless it is
    /// already done, in which case back to not-started. Completion is the one action worth a single click —
    /// it does not cycle through in-progress on the way, because a click that sometimes completes and
    /// sometimes doesn't is a click you have to look at. In progress is <i>chosen</i>, from the picker.
    /// </summary>
    public static TaskState Toggled(TaskState state) =>
        state == TaskState.Done ? TaskState.NotStarted : TaskState.Done;
}
