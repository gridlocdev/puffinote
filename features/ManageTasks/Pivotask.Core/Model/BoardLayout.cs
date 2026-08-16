namespace Pivotask.Core.Model;

/// <summary>
/// Board or list — two readings of the same tasks, sharing one filter and one search box.
///
/// <para>It lives in the model rather than in the control because a board <em>records</em> which reading
/// it was last in (<see cref="Board.View"/>), and the file format's fields are the model's to name. An
/// enum is not a UI dependency: nothing here knows what a column looks like, only that there are two
/// ways to read a board and which one this file was left in.</para>
/// </summary>
public enum BoardLayout
{
    Board,
    List,
}
