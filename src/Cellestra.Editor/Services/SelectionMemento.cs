namespace Cellestra.Desktop.Services;

/// <summary>
/// A plain snapshot of the grid selection (a rectangle plus its active cell), captured by the
/// <see cref="WorkbookSession"/> around edits so undo/redo can restore where the user was working.
/// Kept UI-free so the session has no dependency on the grid control.
/// </summary>
public readonly record struct SelectionMemento(int Top, int Left, int Bottom, int Right, int ActiveRow, int ActiveColumn);
