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
internal sealed class TaskBoardPane : IEditorPane, IConfigurablePane
{
    private readonly TaskBoardView _view = new();

    public TaskBoardPane()
    {
        _view.DocumentChanged += (_, _) => DirtyChanged?.Invoke(this, EventArgs.Empty);
        ApplyConfig(new EditorConfig()); // Pivotask's defaults until the host supplies the resolved config
    }

    /// <summary>
    /// Apply Pivotask's own settings (PRD §7.5), so a board opens here in the reading and slice the
    /// standalone app would open it in, and a Preferences edit reaches the board already in front of you.
    /// </summary>
    public void ApplyConfig(EditorConfig config)
    {
        _view.Layout = config.Tasks.Layout;
        _view.Filter = config.Tasks.Filter;
    }

    /// <summary>The editor's own reading zoom — the seam the shell remembers a tab's zoom through.</summary>
    public double Zoom
    {
        get => _view.Zoom;
        set => _view.Zoom = value;
    }

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

    /// <summary>The board changed on disk — a pull, most likely. Take that version as one undoable step, so
    /// ⌘Z gives the tab's own board back.</summary>
    public Task ReloadAsync(string path)
    {
        if (_view.Document is { } open && string.Equals(open.Path, path, StringComparison.Ordinal))
        {
            open.Reload(File.ReadAllText(path));
        }
        else
        {
            _view.Document = BoardDocument.Open(path);
        }

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
