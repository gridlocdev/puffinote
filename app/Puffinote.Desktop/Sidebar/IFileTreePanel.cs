using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Sidebar;

/// <summary>
/// A sidebar panel whose body <em>is</em> the notebook's file tree — the Files panel, and the Filter panel
/// that shows the same tree narrowed to what you type (PRD §7.2.3). Two panels, one surface: the same rows,
/// the same keyboard map, the same context menu, and so the same set of things the shell has to fulfil for
/// them.
///
/// <para>This exists so the <see cref="SidebarView"/> says all of that <b>once</b>. A dozen tree events
/// forwarded a second time by hand is a dozen chances for the Filter panel's right-click menu to be quietly
/// missing an entry the Files panel's has — the kind of difference nobody notices until they use the panel
/// the way they use the other one.</para>
/// </summary>
internal interface IFileTreePanel : ISidebarPanel
{
    /// <summary>Raised when a file is activated in the tree (a click, or Enter), to open it in a tab.</summary>
    event Action<string>? FileActivated;

    /// <summary>Raised when the tree selection changes (for command-enablement refresh).</summary>
    event Action<NotebookNode?>? SelectionChanged;

    /// <summary>Tree mutation requests (F2 / Delete / context menu), fulfilled by the shell.</summary>
    event Action<NotebookNode>? RenameRequested;
    event Action<NotebookNode>? DeleteRequested;
    event Action<NotebookNode?>? NewNoteRequested;
    event Action<NotebookNode?>? NewSpreadsheetRequested;
    event Action<NotebookNode?>? NewTaskboardRequested;
    event Action<NotebookNode?>? NewFileRequested;
    event Action<NotebookNode?>? NewFolderRequested;

    /// <summary>Raised when a row is dragged onto a folder: move it there (the shell does the filesystem work).</summary>
    event Action<NotebookNode, string>? MoveRequested;

    /// <summary>Hand-offs to the host OS (tree context menu), fulfilled by the shell.</summary>
    event Action<NotebookNode>? RevealRequested;
    event Action<NotebookNode>? OpenExternallyRequested;

    /// <summary>Raised when the tree re-renders, so the header's expand/collapse toggle can re-label itself.</summary>
    event Action? ExpansionChanged;

    /// <summary>The tree's selected node, or null — what Rename, Delete and the New menu act on.</summary>
    NotebookNode? SelectedNode { get; }

    /// <summary>Whether any folder is open (the header toggle offers Collapse rather than Expand).</summary>
    bool AnyFolderExpanded { get; }

    /// <summary>Whether the tree has a folder worth opening or shutting at all.</summary>
    bool HasExpandableFolder { get; }

    /// <summary>Whether file rows spell their extension out after the name (PRD §7.5 preference).</summary>
    bool ShowExtensions { get; set; }

    /// <summary>Rebuild for a newly opened notebook (or clear the tree when <paramref name="root"/> is null).</summary>
    void SetRoot(NotebookNode? root);

    /// <summary>Re-read the tree from disk after a mutation.</summary>
    void Refresh();

    /// <summary>Mark the active tab's document (or null) as the one washed row.</summary>
    void SetActiveDocument(string? path);

    /// <summary>Shut every folder in the tree.</summary>
    void CollapseAll();

    /// <summary>Open every folder in the tree.</summary>
    void ExpandAll();
}
