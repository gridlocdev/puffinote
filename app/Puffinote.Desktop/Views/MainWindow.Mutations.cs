using System.ComponentModel;
using System.Text;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Cellestra.Desktop.Services;
using Pivotask.Core;
using Puffinote.Components.Overlays;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Panes;
using Puffinote.Desktop.Shell;

namespace Puffinote.Desktop.Views;

public partial class MainWindow
{
    // ---- Editor tabs ------------------------------------------------------------------------------

    /// <summary>
    /// Open <paramref name="path"/> in a tab, focusing an existing tab for the same file, and record it in
    /// the notebook's recent list. Unsupported types (no editor pane) never enter the recent list.
    /// </summary>
    internal async Task OpenFileAsync(string path)
    {
        await _tabHost.OpenAsync(path);

        // Record it only when it actually opened: OpenAsync no-ops on an unsupported type or a failed load,
        // leaving some other tab active — so match the now-active pane's path against the requested file.
        var full = System.IO.Path.GetFullPath(path);
        if (_tabHost.ActivePane?.Path is { } opened && string.Equals(opened, full, StringComparison.OrdinalIgnoreCase))
        {
            _recent.Push(opened);
            SidebarForTest.SetRecent(_notebook.RootPath, _recent.Paths);
            PersistSession();
        }
    }

    // ---- Tree mutations (PRD §7.2) ----------------------------------------------------------------
    // New/rename/delete run through the shell so name-entry and confirmation use the shared overlay
    // dialogs; the filesystem rules live in the testable NotebookMutations helper. Reached from the tree
    // (F2 / Delete / context menu) and the command palette / File menu.

    private bool CanMutateSelected() =>
        SidebarForTest.SelectedTreeNode is { } node && !IsRootNode(node);

    private bool IsRootNode(NotebookNode node) =>
        _notebook.RootPath is { } root && string.Equals(node.Path, root, StringComparison.OrdinalIgnoreCase);

    /// <summary>The folder a new entry lands in: the selection if a folder, else its parent, else the notebook root.</summary>
    private string? TargetDirectory(NotebookNode? node)
    {
        if (node is { IsDirectory: true })
        {
            return node.Path;
        }

        if (node is { } file && System.IO.Path.GetDirectoryName(file.Path) is { } parent)
        {
            return parent;
        }

        return _notebook.RootPath;
    }

    private Task PromptNewNoteAsync(NotebookNode? context) =>
        // A note is a plain Markdown file — an empty .md opens fine.
        PromptNewDocumentAsync(context, DocumentTypes.NoteExtension, "New Note", "note", static () => []);

    private Task PromptNewSpreadsheetAsync(NotebookNode? context) =>
        // A spreadsheet must be seeded with a blank-workbook image: an empty (0-byte) .ods isn't a valid
        // workbook and would fail to open (that's the empty-file trap this split avoids).
        PromptNewDocumentAsync(context, DocumentTypes.SpreadsheetExtension, "New Spreadsheet", "spreadsheet",
            static () => WorkbookSession.NewBlank().SerializeToBytes());

    private Task PromptNewTaskboardAsync(NotebookNode? context) =>
        // Seeded for the same reason a spreadsheet is: Pivotask reads a board *out of* JSON, so a 0-byte
        // ".pivotask.json" is not an empty board, it's a parse error. BoardDocument's own starter is the
        // seed — the shell doesn't get its own opinion about what a new board contains.
        PromptNewDocumentAsync(context, DocumentTypes.BoardSuffix, "New Taskboard", "task board",
            static () => Encoding.UTF8.GetBytes(new BoardDocument().Serialize()));

    /// <summary>
    /// "New File…": whatever the notebook needs that Puffinote has no command for — a <c>.txt</c>, a
    /// <c>.csv</c>, a <c>.gitignore</c>. The whole name is typed here, extension included (the three
    /// commands above each own theirs), and it is created empty: a type the shell knows nothing about is
    /// a type it has no business seeding. Plaintain opens whatever of these reads as text; the rest land
    /// in the tree and open in the host's app.
    /// </summary>
    private async Task PromptNewFileAsync(NotebookNode? context)
    {
        if (_overlayHost.IsOpen || TargetDirectory(context) is not { } dir)
        {
            return;
        }

        var box = new TextBox { Width = 240, PlaceholderText = "name.ext" };
        var body = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                box,

                // Say the rule up front rather than only in the refusal: the reserved extensions each have
                // their own command a few rows up this menu, which is the answer to "why not".
                new TextBlock
                {
                    Text = $"Any extension except {DocumentTypes.ReservedSummary} — those have their own commands.",
                    FontSize = 11,
                    MaxWidth = 240,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Tokens.Dark.MutedText,
                },
            },
        };

        var pending = _overlayHost.ShowModalAsync(new FormDialog("New File", body, "Create"));
        Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        });
        if (!await pending || string.IsNullOrWhiteSpace(box.Text))
        {
            return;
        }

        var name = box.Text.Trim();
        if (DocumentTypes.ReservedFor(name) is { } reserved)
        {
            await _overlayHost.ShowModalAsync(new MessageDialog(
                "Couldn't create the file",
                $"“{reserved.Extension}” is reserved for {reserved.Kind} — use {reserved.Command} to make one."));
            return;
        }

        await MutateAsync("Couldn't create the file", async () =>
        {
            var path = NotebookMutations.CreateFile(dir, name);
            SidebarForTest.RevealInTree(path);
            await OpenFileAsync(path); // no-ops for a type no pane opens — the file is still made and revealed
        });
    }

    /// <summary>
    /// Prompt for a base name (the <paramref name="extension"/> is shown as a suffix after the box, not typed),
    /// create the document with its <paramref name="content"/>, reveal it, and open it in a tab.
    /// </summary>
    private async Task PromptNewDocumentAsync(
        NotebookNode? context, string extension, string heading, string kind, Func<byte[]> content)
    {
        if (_overlayHost.IsOpen || TargetDirectory(context) is not { } dir)
        {
            return;
        }

        var box = new TextBox { Width = 240, PlaceholderText = "name" };
        var body = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                box,
                new TextBlock
                {
                    Text = extension,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Tokens.Dark.MutedText, // the extension is fixed by the command, not editable
                },
            },
        };

        var pending = _overlayHost.ShowModalAsync(new FormDialog(heading, body, "Create"));
        Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        });
        if (!await pending || string.IsNullOrWhiteSpace(box.Text))
        {
            return;
        }

        // Tolerate a user who types the extension anyway, so we never produce "budget.ods.ods".
        var baseName = box.Text.Trim();
        if (baseName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^extension.Length];
        }

        await MutateAsync($"Couldn't create the {kind}", async () =>
        {
            var path = NotebookMutations.CreateFile(dir, baseName + extension, content());
            SidebarForTest.RevealInTree(path);
            await OpenFileAsync(path);
        });
    }

    private async Task PromptNewFolderAsync(NotebookNode? context)
    {
        if (_overlayHost.IsOpen || TargetDirectory(context) is not { } dir)
        {
            return;
        }

        if (await PromptNameAsync("New Folder", initial: string.Empty, okLabel: "Create") is not { } name)
        {
            return;
        }

        await MutateAsync("Couldn't create the folder", () =>
        {
            SidebarForTest.RevealInTree(NotebookMutations.CreateFolder(dir, name));
            return Task.CompletedTask;
        });
    }

    private async Task PromptRenameAsync(NotebookNode node)
    {
        if (_overlayHost.IsOpen || IsRootNode(node))
        {
            return;
        }

        if (await PromptNameAsync($"Rename “{node.Name}”", initial: node.Name, okLabel: "Rename") is not { } name)
        {
            return;
        }

        await MutateAsync("Couldn't rename", () =>
        {
            var newPath = NotebookMutations.Rename(node.Path, name);
            _tabHost.RebindPaths(node.Path, newPath);      // keep any open tab pointing at the moved file
            RemapRecent(node.Path, newPath);
            SidebarForTest.RevealInTree(newPath);
            return Task.CompletedTask;
        });
    }

    private async Task PromptDeleteAsync(NotebookNode node)
    {
        if (_overlayHost.IsOpen || IsRootNode(node))
        {
            return;
        }

        var kind = node.IsDirectory ? "folder" : "file";
        var confirmed = await _overlayHost.ShowModalAsync(new ConfirmDialog(
            "Delete",
            $"Delete the {kind} “{node.Name}”? This can't be undone.",
            confirmLabel: "Delete",
            denyLabel: "Cancel",
            confirmIsDefault: false)); // destructive → Enter shouldn't trigger it
        if (!confirmed)
        {
            return;
        }

        await MutateAsync("Couldn't delete", () =>
        {
            NotebookMutations.Delete(node.Path);
            _tabHost.CloseUnder(node.Path); // the file is gone — close its tab rather than let it be re-saved
            DropRecentUnder(node.Path);
            SidebarForTest.RefreshTree();
            return Task.CompletedTask;
        });
    }

    // ---- OS hand-offs (PRD §7.2) ------------------------------------------------------------------
    // The file tree's two commands that leave Puffinote: show an entry in the host's file manager, and open a
    // file whose type has no editor here (an image, a PDF) in whatever app the host opens it with.

    /// <summary>Show <paramref name="node"/> in the host's file manager — selected, on the platforms that can.</summary>
    private async Task RevealAsync(NotebookNode node)
    {
        try
        {
            if (SystemFileManager.TryReveal(node.Path))
            {
                return;
            }

            // No reveal command on this platform (Linux): open the enclosing folder — the folder itself when the
            // node *is* one — which is as close as a portable launcher gets.
            var folder = node.IsDirectory ? node.Path : System.IO.Path.GetDirectoryName(node.Path);
            if (folder is not null)
            {
                await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception)
        {
            await _overlayHost.ShowModalAsync(new MessageDialog($"Couldn't show “{node.Name}”", e.Message));
        }
    }

    /// <summary>Open <paramref name="node"/> in the host's default app for its type (PRD §7.2: unsupported files).</summary>
    private async Task OpenExternallyAsync(NotebookNode node)
    {
        try
        {
            await Launcher.LaunchFileInfoAsync(new FileInfo(node.Path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception)
        {
            await _overlayHost.ShowModalAsync(new MessageDialog($"Couldn't open “{node.Name}”", e.Message));
        }
    }

    /// <summary>Remap recent entries after a rename (a file, or a folder they live under).</summary>
    private void RemapRecent(string oldPath, string newPath)
    {
        if (_recent.Remap(oldPath, newPath))
        {
            SidebarForTest.SetRecent(_notebook.RootPath, _recent.Paths);
            PersistSession();
        }
    }

    /// <summary>Drop recent entries for a deleted file, or everything under a deleted folder.</summary>
    private void DropRecentUnder(string path)
    {
        if (_recent.DropUnder(path))
        {
            SidebarForTest.SetRecent(_notebook.RootPath, _recent.Paths);
            PersistSession();
        }
    }

    /// <summary>Prompt for a name in the shared overlay, returning it trimmed, or null on cancel / empty.</summary>
    private async Task<string?> PromptNameAsync(string heading, string initial, string okLabel)
    {
        var box = new TextBox { Text = initial };
        var pending = _overlayHost.ShowModalAsync(new FormDialog(heading, box, okLabel));
        Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        });
        var accepted = await pending;
        return accepted && !string.IsNullOrWhiteSpace(box.Text) ? box.Text : null;
    }

    /// <summary>Run a filesystem mutation, surfacing any failure as a message dialog.</summary>
    private async Task MutateAsync(string errorTitle, Func<Task> mutation)
    {
        try
        {
            await mutation();
            StartIndexRebuild(); // the notebook changed — refresh the search index in the background
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await _overlayHost.ShowModalAsync(new MessageDialog(errorTitle, e.Message));
        }
    }

    private async Task SaveCurrentAsync()
    {
        // Don't save (or surface a save-failure message) while a dialog is already open — a second modal
        // on the same overlay host would clobber the first and hang its awaiter.
        if (_overlayHost.IsOpen || _tabHost.ActivePane is not { } pane)
        {
            return;
        }

        try
        {
            await pane.SaveAsync();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await _overlayHost.ShowModalAsync(new MessageDialog("Couldn't save", e.Message));
        }
    }

    /// <summary>The Save / Don't-Save / Cancel prompt the tab host shows before closing a dirty document.</summary>
    private async Task<CloseDecision> PromptCloseAsync(IEditorPane pane)
    {
        if (_overlayHost.IsOpen)
        {
            return CloseDecision.Cancel; // a dialog is already up — cancel this close rather than stack a modal
        }

        var choice = await _overlayHost.ShowModalAsync(new ChoiceDialog<CloseDecision>(
            "Unsaved changes",
            $"“{pane.Title}” has unsaved changes. Save before closing?",
            CloseDecision.Cancel,
            new Choice<CloseDecision>("Save", CloseDecision.Save, IsDefault: true),
            new Choice<CloseDecision>("Don't Save", CloseDecision.Discard),
            new Choice<CloseDecision>("Cancel", CloseDecision.Cancel, IsCancel: true)));
        return choice; // ChoiceDialog resolves Esc/backdrop to its dismiss value (Cancel)
    }

    /// <summary>Closing the window prompts for each dirty tab; hold the close until they're resolved.</summary>
    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closingConfirmed || !_tabHost.HasDirtyTabs)
        {
            return;
        }

        e.Cancel = true;
        if (await _tabHost.CloseAllAsync())
        {
            _closingConfirmed = true;
            Close();
        }
    }
}
