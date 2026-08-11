using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Puffinote.Components.Controls.Icons;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Controls;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Sidebar;

/// <summary>
/// The file-tree panel (PRD §7.2): the scoped <see cref="FileTreeView"/>, with an <b>"In Folder"</b> bar above it
/// whenever the user has entered one. <em>Enter Folder</em> — a folder row's context-menu command — re-roots the
/// tree at that folder so the sidebar shows nothing else; the bar names where you are and its back arrow returns
/// you to where you came from (the folder you entered it from, or the whole notebook). The panel owns that trail
/// and re-surfaces file-activation as an absolute path so the shell opens the file into a tab.
/// </summary>
internal sealed class FilesPanel : ISidebarPanel
{
    private readonly FileTreeView _tree = new();
    private readonly TextBlock _folderLabel;
    private readonly Button _back;
    private readonly DockPanel _folderBar;
    private readonly Border _divider;
    private readonly DockPanel _view;

    private NotebookNode? _root;

    /// <summary>
    /// The folders entered so far, outermost first — the last one is what the tree is rooted at, and an empty
    /// trail is the whole notebook. Keeping the route (rather than just the current folder) is what lets the back
    /// arrow step out one level at a time when folders are entered from inside folders.
    /// </summary>
    private readonly List<string> _trail = [];

    /// <summary>Absolute path of the folder the tree is rooted at; null = the whole notebook.</summary>
    private string? Scope => _trail.Count > 0 ? _trail[^1] : null;

    public FilesPanel()
    {
        _tree.FileActivated += node => FileActivated?.Invoke(node.Path);
        _tree.SelectionChanged += node => SelectionChanged?.Invoke(node);
        _tree.RenameRequested += node => RenameRequested?.Invoke(node);
        _tree.DeleteRequested += node => DeleteRequested?.Invoke(node);
        _tree.NewNoteRequested += node => NewNoteRequested?.Invoke(node);
        _tree.NewSpreadsheetRequested += node => NewSpreadsheetRequested?.Invoke(node);
        _tree.NewTaskboardRequested += node => NewTaskboardRequested?.Invoke(node);
        _tree.NewFileRequested += node => NewFileRequested?.Invoke(node);
        _tree.NewFolderRequested += node => NewFolderRequested?.Invoke(node);
        _tree.EnterFolderRequested += node => EnterFolder(node.Path);
        _tree.MoveRequested += (node, directory) => MoveRequested?.Invoke(node, directory);
        _tree.RevealRequested += node => RevealRequested?.Invoke(node);
        _tree.OpenExternallyRequested += node => OpenExternallyRequested?.Invoke(node);

        var palette = Tokens.Active;

        // "In Folder: Projects" rather than a bare "Projects": the tree below is showing a folder's contents the
        // same way it otherwise shows a notebook's, so the row has to say *why* the rest of the notebook is
        // missing. The prefix is muted and the folder's name is not — the name is the answer, the rest is grammar.
        _folderLabel = new TextBlock
        {
            FontSize = 12,
            Foreground = palette.MutedText,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        _back = BuildBackButton(palette);

        _folderBar = new DockPanel { Margin = new Thickness(11, 7, 7, 7), IsVisible = false };
        DockPanel.SetDock(_back, Dock.Right); // pinned right; the name is what ellipsizes as the sidebar narrows
        _folderBar.Children.Add(_back);
        _folderBar.Children.Add(_folderLabel);
        DockPanel.SetDock(_folderBar, Dock.Top);

        // A hairline under the bar: it *acts on* the tree below (it says what the tree is showing, and leaves),
        // so it is a control strip, not the first row of the list.
        _divider = new Border
        {
            Height = 1,
            Background = palette.SurfaceBorder,
            Opacity = 0.6,
            IsVisible = false,
        };
        DockPanel.SetDock(_divider, Dock.Top);

        _view = new DockPanel { Children = { _folderBar, _divider, _tree } };
    }

    public string Id => "files";

    public string Title => "Files";

    public string Icon => "folder";

    public Control View => _view;

    /// <summary>Raised when a file is activated in the tree (double-click / Enter).</summary>
    public event Action<string>? FileActivated;

    /// <summary>Raised when the tree selection changes (for command-enablement refresh).</summary>
    public event Action<NotebookNode?>? SelectionChanged;

    /// <summary>Tree mutation requests (F2 / Delete / context menu), fulfilled by the shell.</summary>
    public event Action<NotebookNode>? RenameRequested;
    public event Action<NotebookNode>? DeleteRequested;
    public event Action<NotebookNode?>? NewNoteRequested;
    public event Action<NotebookNode?>? NewSpreadsheetRequested;
    public event Action<NotebookNode?>? NewTaskboardRequested;
    public event Action<NotebookNode?>? NewFileRequested;
    public event Action<NotebookNode?>? NewFolderRequested;

    /// <summary>Raised when a row is dragged onto a folder: move it there (the shell does the filesystem work).</summary>
    public event Action<NotebookNode, string>? MoveRequested;

    /// <summary>Hand-offs to the host OS (tree context menu), fulfilled by the shell.</summary>
    public event Action<NotebookNode>? RevealRequested;
    public event Action<NotebookNode>? OpenExternallyRequested;

    /// <summary>The tree's selected node, or null.</summary>
    public NotebookNode? SelectedNode => _tree.SelectedNode;

    public void Focus() => _tree.FocusTree();

    /// <summary>Rebuild the tree for a newly opened notebook (or clear it when <paramref name="root"/> is null).</summary>
    public void SetRoot(NotebookNode? root)
    {
        _root = root;
        _trail.Clear(); // a new notebook is never opened part-way inside a folder
        _tree.SetRoot(root);
        RenderScope();
    }

    /// <summary>Re-read the tree from disk after a mutation, preserving expansion, and leave a folder that vanished.</summary>
    public void Refresh()
    {
        // A deleted or renamed folder takes its path with it — and its children's. Dropping every trail entry
        // that no longer exists lands the sidebar on the nearest ancestor that does, or back at the notebook.
        if (_trail.RemoveAll(path => !Directory.Exists(path)) > 0)
        {
            ApplyScope();
            return;
        }

        _tree.Refresh();
    }

    /// <summary>Mark <paramref name="path"/> (the active tab's document, or null) as the bold row in the tree.</summary>
    public void SetActiveDocument(string? path) => _tree.SetActiveDocument(path);

    /// <summary>Whether the tree spells each file's extension out after its name (PRD §7.5 preference).</summary>
    public bool ShowExtensions
    {
        get => _tree.ShowExtensions;
        set => _tree.ShowExtensions = value;
    }

    /// <summary>Reveal and select <paramref name="path"/> in the tree, leaving the entered folder if it's outside it.</summary>
    public void SelectPath(string path)
    {
        if (Scope is { } scope && !PathScope.IsUnderOrEqual(System.IO.Path.GetFullPath(path), scope))
        {
            // Out of scope: leave *every* folder rather than stepping out one at a time — the destination may
            // be nowhere near the trail, and the tree has to be able to show it.
            _trail.Clear();
            ApplyScope();
        }

        _tree.SelectPath(path);
    }

    /// <summary>Shut every folder in the tree (the sidebar's "Collapse all" button).</summary>
    public void CollapseAll() => _tree.CollapseAll();

    // ---- Entering & leaving ---------------------------------------------------------------------

    /// <summary>Narrow the sidebar to <paramref name="path"/>'s contents (a folder row's "Enter Folder").</summary>
    private void EnterFolder(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (string.Equals(Scope, full, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(full))
        {
            return;
        }

        _trail.Add(full);
        ApplyScope();
    }

    /// <summary>Step back out of the current folder, to the one it was entered from (or the whole notebook).</summary>
    private void Back()
    {
        if (_trail.Count == 0)
        {
            return;
        }

        _trail.RemoveAt(_trail.Count - 1);
        ApplyScope();
    }

    private void ApplyScope()
    {
        var displayRoot = Scope is { } scope ? new NotebookNode(scope, isDirectory: true) : _root;
        if (displayRoot is not null)
        {
            _tree.SetScope(displayRoot);
        }

        RenderScope();
    }

    /// <summary>
    /// The bar above the tree: hidden entirely at the notebook's top level, because there is nothing to say and
    /// nowhere to go back to — the sidebar's header already names the notebook.
    /// </summary>
    private void RenderScope()
    {
        if (Scope is not { } scope)
        {
            _folderBar.IsVisible = false;
            _divider.IsVisible = false;
            return;
        }

        var palette = Tokens.Active;
        _folderLabel.Inlines =
        [
            new Run("In Folder: ") { Foreground = palette.MutedText },

            // The destination keeps the inherited default text brush; only the label is muted. (Assigning `null`
            // to mean "the default" would set a local null brush and paint nothing at all.)
            new Run(System.IO.Path.GetFileName(scope)) { FontWeight = FontWeight.Medium },
        ];
        ToolTip.SetTip(_folderLabel, scope); // the full path, for a folder entered several levels down

        // Name the destination, not the gesture: back out of a nested folder and you land in its parent, not at
        // the notebook, and the arrow is the only thing that could have told you so beforehand.
        var back = BackDestination();
        ToolTip.SetTip(_back, $"Back to {back}");
        AutomationProperties.SetName(_back, $"Back to {back}");

        _folderBar.IsVisible = true;
        _divider.IsVisible = true;
    }

    private string BackDestination() =>
        _trail.Count > 1 ? System.IO.Path.GetFileName(_trail[^2]) : "all notes";

    /// <summary>Open every folder in the tree (what that same button offers once the tree is fully collapsed).</summary>
    public void ExpandAll() => _tree.ExpandAll();

    /// <summary>Whether any folder is open — false when the tree is fully collapsed, so the button offers Expand.</summary>
    public bool AnyFolderExpanded => _tree.AnyFolderExpanded;

    /// <summary>Whether the tree has a folder worth opening or shutting at all (a flat notebook has none).</summary>
    public bool HasExpandableFolder => _tree.HasExpandableFolder;

    /// <summary>Raised when the tree re-renders, so the header's toggle can re-label itself.</summary>
    public event Action? ExpansionChanged
    {
        add => _tree.ExpansionChanged += value;
        remove => _tree.ExpansionChanged -= value;
    }

    private Button BuildBackButton(ThemePalette palette)
    {
        var button = new Button
        {
            Content = new LucideIcon { IconKey = "arrow-left", Size = 14, Foreground = palette.MutedText },
            Width = 24, // WCAG 2.2 §2.5.8's floor for a pointer target
            Height = 24,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(5),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        button.Click += (_, _) => Back();
        return button;
    }

    // ---- Test seams -----------------------------------------------------------------------------

    internal string? ScopePathForTest => Scope;

    internal bool FolderBarVisibleForTest => _folderBar.IsVisible;

    internal string FolderLabelTextForTest =>
        _folderLabel.Inlines is null ? string.Empty : string.Concat(_folderLabel.Inlines.OfType<Run>().Select(r => r.Text));

    internal string BackDestinationForTest => BackDestination();

    internal IReadOnlyList<string> TreeVisibleNamesForTest => _tree.VisibleNamesForTest;

    internal void EnterFolderForTest(string path) => EnterFolder(path);

    /// <summary>Click the bar's back arrow, so a test exercises the button and not just <see cref="Back"/>.</summary>
    internal void ClickBackForTest() => _back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    internal string BackTooltipForTest => ToolTip.GetTip(_back) as string ?? string.Empty;

    internal string BackIconKeyForTest => (_back.Content as LucideIcon)?.IconKey ?? string.Empty;
}
