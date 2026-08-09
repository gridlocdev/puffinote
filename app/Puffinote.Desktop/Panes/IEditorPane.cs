using Avalonia.Controls;
using Puffinote.Components.Commands;

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

    /// <summary>
    /// Move keyboard focus into the editor itself (the note's text area / the sheet's grid), not merely the
    /// hosting control — so the shell's "focus editor" command and focus-restoration land the caret where the
    /// user can type. Delegates to the embedded editor's own focus seam.
    /// </summary>
    void Focus();

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

    /// <summary>
    /// Rebind to <paramref name="newPath"/> after the open file was moved/renamed on disk (no I/O — the
    /// bytes already moved). Updates <see cref="Path"/>/<see cref="Title"/> and where the next save writes;
    /// the document content and dirty state are preserved.
    /// </summary>
    void Rebind(string newPath);

    /// <summary>
    /// This editor's command catalog, for the merged Command Palette (PRD §7.4) and for the window's key
    /// bindings — the shell binds every gesture in here that the editor doesn't hard-handle itself, which is
    /// what makes an embedded editor's keymap identical to its standalone app's. Both editors publish their
    /// full catalog from their own control, so the shell never restates one.
    /// </summary>
    IReadOnlyList<AppCommand> Commands { get; }

    /// <summary>
    /// True while the editor is showing a modal of its own (Typage's link prompt or reference sheet). The
    /// shell stands its shortcuts down while one is up, so a chord typed into the dialog's text field can't
    /// fire the command behind it. Default false: an editor with no modals of its own need not say so.
    /// </summary>
    bool IsModalOpen => false;

    /// <summary>
    /// The reading zoom on the family's shared ladder (<see cref="Puffinote.Components.Theming.ZoomScale"/>).
    /// The shell reads it to remember what a tab was left at and writes it to put the tab back there.
    /// <para>
    /// Every editor already has a zoom of its own and its own ⌘= / ⌘− commands, which the shell binds through
    /// <see cref="Commands"/> — this is not a second mechanism, only the seam that lets the shell <em>see</em>
    /// the value. The default is the no-op pair, for a pane with nothing to scale (<see cref="ErrorPane"/>):
    /// reading gives 100% and writing is ignored, so a remembered zoom lands harmlessly on it.
    /// </para>
    /// </summary>
    double Zoom
    {
        get => Puffinote.Components.Theming.ZoomScale.Default;
        set { }
    }

    /// <summary>
    /// The document's outline for the sidebar's Outline panel (PRD §7.2.3): Markdown headings for a note,
    /// the sheet/chart list for a workbook. Computed on demand from the current document; empty when there
    /// is no structure. Each item can <see cref="OutlineItem.Reveal"/> its location in the editor.
    /// </summary>
    IReadOnlyList<OutlineItem> GetOutline();
}
