using Avalonia.Controls;
using Pivotask.Control.Views;
using Pivotask.Core;
using Puffinote.Components.Commands;

namespace Puffinote.Desktop.Panes;

/// <summary>
/// Hosts the <em>existing</em> Pivotask <see cref="TaskBoardView"/> unchanged. Composition, not a fork,
/// exactly as <see cref="MarkdownPane"/> wraps Typage: Puffinote adds no task logic — the model, the
/// codec, the undo stack, and the atomic write are all the editor's.
/// </summary>
internal sealed class TaskBoardPane : IEditorPane
{
    private readonly TaskBoardView _view = new();

    public TaskBoardPane() => _view.DocumentChanged += (_, _) => DirtyChanged?.Invoke(this, EventArgs.Empty);

    public Control View => _view;

    public void Focus() => _view.Focus();

    public string Title => Path is { } p ? System.IO.Path.GetFileName(p) : "Untitled board";

    public string? Path => _view.Document.Path;

    public bool IsDirty => _view.Document.IsDirty;

    public IReadOnlyList<AppCommand> Commands => _view.EditingCommands;

    /// <summary>The task dialog is the board's own modal, inside the pane.</summary>
    public bool IsModalOpen => _view.IsModalOpen;

    /// <summary>
    /// The board's groups. Unlike Plaintain — which contributes nothing, permanently, because plaintext
    /// has no structure — a board has real structure to lend, and it maps onto the existing outline
    /// contract without changing it (proposal 0004, Phase 4).
    /// </summary>
    public IReadOnlyList<OutlineItem> GetOutline() =>
        [.. _view.Groups.Select(group => new OutlineItem(group.Name, 0, () => _view.Reveal(group)))];

    public event EventHandler? DirtyChanged;

    public Task LoadAsync(string path)
    {
        _view.Document = BoardDocument.Open(path);
        DirtyChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task SaveAsync()
    {
        // The document owns the write: deterministic bytes, unknown fields preserved, temp-then-rename.
        // A shell reimplementing any of that would be a second answer to a question with one.
        _view.Document.Save();
        DirtyChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void Rebind(string newPath) => _view.Document.Rebind(newPath);

    public void Dispose()
    {
        // The board holds only managed state.
    }
}
