using Avalonia.Controls;

namespace Puffinote.Desktop.Panes;

/// <summary>
/// The shell-side handle over one open document, whatever editor renders it (ARCHITECTURE §4.1). The tab
/// host, title bar, and — in later phases — the merged palette and outline treat a note and a sheet
/// uniformly through this. Each implementation is a thin adapter over an <em>existing</em> editor control
/// (composition, never a fork); it only wires the editor's published seams.
/// <para>
/// Named <c>IEditorPane</c> to avoid colliding with <c>Cellestra.Editor</c>'s existing <c>IEditorHost</c>,
/// which is the opposite direction (the editor delegating file ops outward to its embedder).
/// </para>
/// Phase 1 is a single pane; <c>Commands</c> / <c>Outline</c> / <c>Status</c> join in later phases.
/// </summary>
internal interface IEditorPane : IDisposable
{
    /// <summary>The editor control to mount in the editor area.</summary>
    Control View { get; }

    /// <summary>The tab/title label (typically the file name).</summary>
    string Title { get; }

    /// <summary>The open file's absolute path, or null before a load.</summary>
    string? Path { get; }

    /// <summary>True when the document has unsaved edits (delegated to the editor).</summary>
    bool IsDirty { get; }

    /// <summary>Raised when <see cref="IsDirty"/> may have changed, so the shell can repaint the title.</summary>
    event EventHandler? DirtyChanged;

    /// <summary>Open <paramref name="path"/> into this pane.</summary>
    Task LoadAsync(string path);

    /// <summary>Save the document to its current path (byte-faithful, atomic — the editor's own save).</summary>
    Task SaveAsync();
}
