namespace Typage.Engine.Markdown;

/// <summary>
/// What a task-list item's box says about the item (PRD §8.3.2). Markdown's <c>[ ]</c>/<c>[x]</c> is a
/// bit — done or not — but a task that is under way, waiting on someone else, or abandoned is none of
/// those, and authors have long spelled the difference by hand (<c>[/]</c>, <c>[?]</c>, …). Typage reads
/// that vocabulary: one character between the brackets, six states.
///
/// <para>The order of the members is the order a reader tallies them — <b>active first, resolved last</b> —
/// and is what <see cref="TaskStates.Order"/> and any picker or cycle follows. An unrecognized character
/// between the brackets is not a task box at all: the line stays literal text (tolerant tokenizing).</para>
/// </summary>
public enum TaskState
{
    /// <summary><c>[ ]</c> — defined, no work yet.</summary>
    NotStarted,

    /// <summary><c>[/]</c> — work is under way.</summary>
    InProgress,

    /// <summary><c>[?]</c> — waiting on a dependency.</summary>
    Blocked,

    /// <summary><c>[&gt;]</c> — moved to later; dormant, not dead.</summary>
    Deferred,

    /// <summary><c>[x]</c> — complete.</summary>
    Done,

    /// <summary><c>[-]</c> — will not be completed.</summary>
    Cancelled,
}

/// <summary>
/// The task-state vocabulary: the mapping between the character in the box and the <see cref="TaskState"/>
/// it spells, plus the neutral names the surfaces label it with. This is <i>dialect</i>, not presentation —
/// the character set is the source syntax, so it lives with the tokenizer that reads it and the renderers
/// that write it. Colour and glyph are the control's business (<c>MarkdownTheme</c>), not this file's.
/// </summary>
public static class TaskStates
{
    /// <summary>The states in canonical order — active (not started → in progress → blocked → deferred) then
    /// resolved (done, cancelled). Pickers, cycles, and legends all read this, so they can't disagree.</summary>
    public static IReadOnlyList<TaskState> Order { get; } =
    [
        TaskState.NotStarted,
        TaskState.InProgress,
        TaskState.Blocked,
        TaskState.Deferred,
        TaskState.Done,
        TaskState.Cancelled,
    ];

    /// <summary>The character between the brackets, canonically (<c>[x]</c>, never <c>[X]</c>).</summary>
    public static char MarkOf(TaskState state) => state switch
    {
        TaskState.InProgress => '/',
        TaskState.Blocked => '?',
        TaskState.Deferred => '>',
        TaskState.Done => 'x',
        TaskState.Cancelled => '-',
        _ => ' ',
    };

    /// <summary>The mark as a one-character string (for a single-character document replace).</summary>
    public static string MarkText(TaskState state) => state switch
    {
        TaskState.InProgress => "/",
        TaskState.Blocked => "?",
        TaskState.Deferred => ">",
        TaskState.Done => "x",
        TaskState.Cancelled => "-",
        _ => " ",
    };

    /// <summary>The box as it is spelled in the source — <c>"[ ]"</c>, <c>"[x]"</c>, … (what a picker shows as its chip).</summary>
    public static string Code(TaskState state) => state switch
    {
        TaskState.InProgress => "[/]",
        TaskState.Blocked => "[?]",
        TaskState.Deferred => "[>]",
        TaskState.Done => "[x]",
        TaskState.Cancelled => "[-]",
        _ => "[ ]",
    };

    /// <summary>The state's name, for a menu row, a tooltip, or an exported <c>aria-label</c>.</summary>
    public static string Label(TaskState state) => state switch
    {
        TaskState.InProgress => "In progress",
        TaskState.Blocked => "Blocked",
        TaskState.Deferred => "Deferred",
        TaskState.Done => "Done",
        TaskState.Cancelled => "Cancelled",
        _ => "Not started",
    };

    /// <summary>What the state means, in a phrase — the second line of a picker row.</summary>
    public static string Description(TaskState state) => state switch
    {
        TaskState.InProgress => "work is under way",
        TaskState.Blocked => "waiting on a dependency",
        TaskState.Deferred => "moved to later",
        TaskState.Done => "the task is complete",
        TaskState.Cancelled => "will not be completed",
        _ => "defined, no work yet",
    };

    /// <summary>
    /// True when the item is <b>off the active board</b> — done or cancelled. The two are opposite ends of the
    /// same thing (finished vs. abandoned) and behave alike everywhere the distinction is "still to do or not":
    /// struck-through text, and the completed-task sort.
    /// </summary>
    public static bool IsResolved(TaskState state) => state is TaskState.Done or TaskState.Cancelled;

    /// <summary>Whether <paramref name="mark"/> is a character a task box may hold.</summary>
    public static bool IsMark(char mark) => mark is ' ' or 'x' or 'X' or '/' or '?' or '>' or '-';

    /// <summary>The state <paramref name="mark"/> spells; <see cref="TaskState.NotStarted"/> for anything else.</summary>
    public static TaskState FromMark(char mark) => mark switch
    {
        'x' or 'X' => TaskState.Done,
        '/' => TaskState.InProgress,
        '?' => TaskState.Blocked,
        '>' => TaskState.Deferred,
        '-' => TaskState.Cancelled,
        _ => TaskState.NotStarted,
    };

    /// <summary>The state a task marker's source text (<c>"[x] "</c>, box first) spells.</summary>
    public static TaskState FromMarker(ReadOnlySpan<char> markerText) =>
        markerText.Length > 1 ? FromMark(markerText[1]) : TaskState.NotStarted;

    /// <summary>
    /// The state the primary click / ⌘L lands on from <paramref name="state"/>: <b>done</b>, unless it is already
    /// done, in which case back to not-started. Completion is the one action worth a single click; the other four
    /// states are chosen deliberately, from the picker.
    /// </summary>
    public static TaskState Toggled(TaskState state) =>
        state == TaskState.Done ? TaskState.NotStarted : TaskState.Done;
}
