using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls;
using Puffinote.Components.Controls.Icons;
using Puffinote.Components.Overlays;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Commands;
using Puffinote.Desktop.Panes;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Sidebar;

namespace Puffinote.Desktop.Views;

public partial class MainWindow
{
    // ---- Command catalog → menu (macOS native / in-window), key bindings, and palette --------------

    /// <summary>Puffinote's shell catalog — defined once; the menu, key bindings, and palette all read it.</summary>
    private void BuildShellCommands()
    {
        // A command's effective gesture: the user's custom binding (shortcuts editor) if it has one, else the
        // default below. Reserved global keys are fixed — they're never routed through the keymap (PRD §7.6).
        Shortcut? Bound(string id, Shortcut? def) => ShellCommandCatalog.ResolveGesture(_keymap, id, def);

        _shellCommands =
        [
            new("shell.commandPalette", "Command Palette", "Search and run any command", Bound("shell.commandPalette", Shortcut.Cmd(Key.K)),
                ShowCommandPalette, icon: "command"),
            new("shell.quickOpen", "Quick Open…", "Jump to any file in the notebook", Bound("shell.quickOpen", Shortcut.Cmd(Key.O)),
                ShowQuickOpen, () => _notebook.Root is not null, icon: "search"),
            new("shell.openNotebook", "Open Notebook…", "Open a folder as the notebook", Bound("shell.openNotebook", Shortcut.Cmd(Key.O, KeyModifiers.Shift)),
                () => _ = OpenNotebookInteractiveAsync(), icon: "folder-open"),
            new("shell.newNote", "New Note…", "Create a new Markdown note in the selected folder", Bound("shell.newNote", null),
                () => _ = PromptNewNoteAsync(SidebarForTest.SelectedTreeNode), () => _notebook.Root is not null, icon: "file-plus"),
            new("shell.newSpreadsheet", "New Spreadsheet…", "Create a new spreadsheet in the selected folder", Bound("shell.newSpreadsheet", null),
                () => _ = PromptNewSpreadsheetAsync(SidebarForTest.SelectedTreeNode), () => _notebook.Root is not null, icon: "table"),
            new("shell.newFolder", "New Folder…", "Create a new folder in the selected folder", Bound("shell.newFolder", null),
                () => _ = PromptNewFolderAsync(SidebarForTest.SelectedTreeNode), () => _notebook.Root is not null),
            new("shell.renameEntry", "Rename…", "Rename the selected file or folder", Bound("shell.renameEntry", null),
                () =>
                {
                    if (SidebarForTest.SelectedTreeNode is { } n)
                    {
                        _ = PromptRenameAsync(n);
                    }
                }, CanMutateSelected, icon: "pencil"),
            new("shell.deleteEntry", "Delete", "Delete the selected file or folder", Bound("shell.deleteEntry", null),
                () =>
                {
                    if (SidebarForTest.SelectedTreeNode is { } n)
                    {
                        _ = PromptDeleteAsync(n);
                    }
                }, CanMutateSelected, icon: "trash-2"),
            new("shell.save", "Save", "Save the active document", Bound("shell.save", Shortcut.Cmd(Key.S)),
                () => _ = SaveCurrentAsync(), () => _tabHost.ActivePane is not null, icon: "save"),
            new("shell.closeTab", "Close Tab", "Close the active tab", Bound("shell.closeTab", Shortcut.Cmd(Key.W)),
                () => _ = _tabHost.CloseActiveAsync(), () => _tabHost.ActivePane is not null),

            // ⌥⌘W, the platform's "close all" convention. Deliberately *not* in the reserved set: the reserved
            // list is fixed and documented (PRD §7.6), and ⌘W already guarantees a tab can always be closed, so
            // this one stays an ordinary — and rebindable — binding. Each dirty tab still gets its own
            // Save/Don't-Save/Cancel prompt via TabHost.CloseAllAsync; cancelling one stops the whole run.
            new("shell.closeAllTabs", "Close All Tabs", "Close every open tab, prompting for unsaved changes",
                Bound("shell.closeAllTabs", Shortcut.Cmd(Key.W, KeyModifiers.Alt)),
                () => _ = _tabHost.CloseAllAsync(), () => _tabHost.Count > 0),
            new("shell.nextTab", "Next Tab", "Switch to the next tab", Bound("shell.nextTab", Shortcut.Cmd(Key.OemCloseBrackets, KeyModifiers.Shift)),
                () => _tabHost.ActivateRelative(1)),
            new("shell.prevTab", "Previous Tab", "Switch to the previous tab", Bound("shell.prevTab", Shortcut.Cmd(Key.OemOpenBrackets, KeyModifiers.Shift)),
                () => _tabHost.ActivateRelative(-1)),
            new("shell.toggleSidebar", "Toggle Sidebar", "Show or hide the sidebar", Bound("shell.toggleSidebar", Shortcut.Cmd(Key.OemBackslash)),
                ToggleSidebar),
            new("shell.toggleEditorToolbar", "Toggle Editor Toolbar", "Show or hide the formatting toolbar over a note",
                Bound("shell.toggleEditorToolbar", null), ToggleEditorToolbar, () => _tabHost.ActivePane is MarkdownPane, icon: "type"),
            new("shell.focusTree", "Focus Sidebar", "Move keyboard focus to the sidebar's file tree",
                Bound("shell.focusTree", Shortcut.Cmd(Key.S, KeyModifiers.Alt)), () => SidebarForTest.FocusTree(), icon: "panel-left"),
            new("shell.focusEditor", "Focus Editor", "Move keyboard focus back into the active document",
                Bound("shell.focusEditor", Shortcut.Cmd(Key.E, KeyModifiers.Alt)), () => _tabHost.ActivePane?.Focus(),
                () => _tabHost.ActivePane is not null, icon: "pen-line"),
            new("shell.shortcuts", "Keyboard Shortcuts…", "View and customize keyboard shortcuts", Bound("shell.shortcuts", null),
                ShowShortcutsEditor),
            // ⌘, — the platform's Preferences chord, and the same one in every app in the family. An embedded
            // editor that publishes its own ⌘, (Cellestra does) is shadowed here rather than silently racing
            // us: ApplyShellShortcuts binds the shell's commands before BindActivePaneShortcuts adds the
            // pane's, and the first matching binding handles the key. That is the right way round — inside
            // Puffinote, ⌘, is Puffinote's preferences — and the editor's own stays one ⌘K away. Deliberately
            // not reserved: the reserved set is fixed and documented (PRD §7.6), and this is rebindable.
            new("shell.preferences", "Preferences…", "Puffinote's own settings — the overrides it layers over each editor's",
                Bound("shell.preferences", Shortcut.Cmd(Key.OemComma)), ShowSettings, icon: "settings-2"),
            new("shell.about", "About Puffinote", "About this app", null, ShowAbout),
        ];
        _shellById = _shellCommands.ToDictionary(c => c.Id);
    }

    private void InstallMenu(DockPanel root)
    {
        _menuRoot = root;
        BuildShellCommands();

        // One shared RelayCommand per catalog entry, resolved by id so it survives a catalog rebuild when a
        // shortcut is rebound (the menu items and key bindings keep pointing at a live command). Enabled
        // state is refreshed for all of them together by RefreshCommandStates.
        _relays = _shellCommands.ToDictionary(
            c => c.Id,
            c => new RelayCommand(() => _shellById[c.Id].Execute(), () => _shellById[c.Id].IsEnabled));

        ApplyShellShortcuts();
        AddHandler(KeyDownEvent, OnReservedKeyDown, RoutingStrategies.Tunnel);
        RefreshMenus();
    }

    /// <summary>
    /// (Re)apply the key bindings from the current catalogs: non-reserved commands become ordinary (bubbling)
    /// key bindings — the focused editor gets first refusal — while reserved ones are collected for the tunnel
    /// router so they always win (PRD §7.6). Called at startup, after a rebinding, and on every pane change, so
    /// a new key (or a new active editor) takes effect live.
    /// </summary>
    private void ApplyShellShortcuts()
    {
        KeyBindings.Clear();
        foreach (var command in _shellCommands)
        {
            if (command.Gesture is { } gesture && !ShellCommandCatalog.IsReserved(command.Id))
            {
                KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = _relays[command.Id] });
            }
        }

        _reservedBindings = [.. _shellCommands
            .Where(c => ShellCommandCatalog.IsReserved(c.Id) && c.Gesture is not null)
            .Select(c => (c.Gesture!, _relays[c.Id]))];

        BindActivePaneShortcuts();
    }

    /// <summary>
    /// Bind the active editor's own catalog, so its published shortcuts actually fire. The shell used to bind
    /// only <em>its</em> commands, which left every editor gesture that the control doesn't hard-handle in
    /// <c>OnKeyDown</c> — ⌘. bullets, ⌘/ numbers, ⌘⇧H highlight, ⌘D duplicate line — a dead key here, even
    /// though its tooltip and palette row advertised it. This is what makes the in-app editor's keymap
    /// identical to the standalone app's, which binds its catalog the same way.
    /// <para>
    /// Two things are deliberately skipped. The text-editing chords (<see cref="Shortcut.YieldsToTextInput"/>:
    /// ⌘A/C/V/X/Z/Y) belong to whatever has focus — an editor, a dialog's text box — not to the window. And a
    /// gesture already claimed by a reserved shell key is left alone: reserved keys are tunnel-routed and win
    /// regardless, so binding it here would only add a second, unreachable handler.
    /// </para>
    /// </summary>
    private void BindActivePaneShortcuts()
    {
        foreach (var command in _tabHost.ActivePane?.Commands ?? [])
        {
            // Every chord the command answers to, not just the advertised one: Zoom In is spelled ⌘= in the
            // palette but must also fire on the ⇧= that carries the "+" glyph, and on the keypad's key.
            foreach (var shortcut in command.AllShortcuts)
            {
                if (shortcut.YieldsToTextInput)
                {
                    continue;
                }

                var gesture = shortcut.ToGesture();
                if (_reservedBindings.Any(b => b.Gesture.Equals(gesture)))
                {
                    continue;
                }

                var captured = command;
                KeyBindings.Add(new KeyBinding
                {
                    Gesture = gesture,

                    // Stand down while an overlay owns the screen — the shell's palette/dialogs, or one of
                    // the editor's own (the link prompt) — so a chord typed into a text field can't fire
                    // behind it.
                    Command = new RelayCommand(captured.Execute, () => captured.IsEnabled && !AnyOverlayOpen),
                });
            }
        }
    }

    /// <summary>True while the shell's overlay host, or the active editor's own, is showing something.</summary>
    private bool AnyOverlayOpen => _overlayHost.IsOpen || _tabHost.ActivePane?.IsModalOpen == true;

    /// <summary>(Re)build the menu bar from the current catalog so rebound gestures show their new keys.</summary>
    private void RefreshMenus()
    {
        if (OperatingSystem.IsMacOS())
        {
            NativeMenu.SetMenu(this, new NativeMenu
            {
                Items =
                {
                    new NativeMenuItem { Header = "File", Menu = BuildNativeMenu("shell.openNotebook", "shell.quickOpen", "shell.newNote", "shell.newSpreadsheet", "shell.newFolder", "shell.save", "shell.closeTab", "shell.closeAllTabs") },
                    new NativeMenuItem { Header = "View", Menu = BuildNativeMenu("shell.commandPalette", "shell.toggleSidebar", "shell.focusTree", "shell.focusEditor", "shell.toggleEditorToolbar", "shell.nextTab", "shell.prevTab", "shell.preferences") },
                    new NativeMenuItem { Header = "Help", Menu = BuildNativeMenu("shell.shortcuts", "shell.about") },
                },
            });
            return;
        }

        if (_inWindowMenu is { } existing)
        {
            _menuRoot.Children.Remove(existing);
        }

        _inWindowMenu = new Menu
        {
            Items =
            {
                new MenuItem { Header = "_File", ItemsSource = BuildMenuItems("shell.openNotebook", "shell.quickOpen", "shell.newNote", "shell.newSpreadsheet", "shell.newFolder", "shell.save", "shell.closeTab", "shell.closeAllTabs") },
                new MenuItem { Header = "_View", ItemsSource = BuildMenuItems("shell.commandPalette", "shell.toggleSidebar", "shell.focusTree", "shell.focusEditor", "shell.toggleEditorToolbar", "shell.nextTab", "shell.prevTab", "shell.preferences") },
                new MenuItem { Header = "_Help", ItemsSource = BuildMenuItems("shell.shortcuts", "shell.about") },
            },
        };
        DockPanel.SetDock(_inWindowMenu, Avalonia.Controls.Dock.Top);
        _menuRoot.Children.Insert(0, _inWindowMenu);
    }

    private List<MenuItem> BuildMenuItems(params string[] ids) =>
        [.. ids.Select(id => _shellById[id]).Select(c =>
            new MenuItem { Header = c.Title, Command = _relays[c.Id], InputGesture = c.Gesture })];

    private NativeMenu BuildNativeMenu(params string[] ids)
    {
        var menu = new NativeMenu();
        foreach (var id in ids)
        {
            var c = _shellById[id];
            menu.Items.Add(new NativeMenuItem { Header = c.Title, Command = _relays[c.Id], Gesture = c.Gesture });
        }

        return menu;
    }

    /// <summary>Open the merged Command Palette (⌘K): Puffinote's shell commands + the active editor's catalog.</summary>
    private void ShowCommandPalette()
    {
        if (_overlayHost.IsOpen)
        {
            return; // an overlay is already up (e.g. a dialog opened from the native menu) — don't clobber it
        }

        var editor = _tabHost.ActivePane?.Commands ?? [];
        var entries = CommandMerge.ToEntries(_shellCommands, editor);

        // Resolve each command's icon key to its glyph, so a palette row reads like the same command's row in
        // the editor's toolbar rather than as a bare line of text.
        var palette = new PaletteListView(LucideIcon.PaletteGlyph)
        {
            Placeholder = "Type a command…",
        };
        palette.SetItems(entries);
        palette.Chosen += entry =>
        {
            _overlayHost.Close();
            entry.Run();
        };
        palette.Cancelled += (_, _) => _overlayHost.Close();
        ShowPaletteOverlay(palette);
    }

    /// <summary>
    /// Quick-open (⌘O, PRD §7.2.5): a fuzzy file switcher, separate from the command palette. It lists every
    /// notebook file — most-recent first for an empty query — and opens the chosen file into a tab. Reuses the
    /// shared <see cref="PaletteListView"/> (which fuzzy-ranks by name as you type).
    /// </summary>
    private void ShowQuickOpen()
    {
        if (_overlayHost.IsOpen || _notebook.Root is null)
        {
            return;
        }

        var ordered = QuickOpen.OrderByRecency(_notebook.EnumerateFiles(), _recent.Paths);
        var entries = ordered.Select(path => new PaletteEntry(
            Title: System.IO.Path.GetFileName(path),
            Run: () => _ = OpenFileAsync(path),
            Description: RelativeToNotebook(path))).ToList();

        var palette = new PaletteListView { Placeholder = "Go to file…" };
        palette.SetItems(entries);
        palette.Chosen += entry =>
        {
            _overlayHost.Close();
            entry.Run();
        };
        palette.Cancelled += (_, _) => _overlayHost.Close();
        ShowPaletteOverlay(palette);
    }

    /// <summary>Present <paramref name="palette"/> in the shared overlay card and focus its search box.</summary>
    private void ShowPaletteOverlay(PaletteListView palette)
    {
        // Remember where focus was (typically the editor's caret) so opening the palette doesn't strand it: on
        // close we return focus there unless a chosen command deliberately moved it (e.g. Focus Sidebar) or
        // opened a follow-on dialog. This is what makes ⌘K feel non-disruptive — you land back where you were.
        var origin = FocusManager?.GetFocusedElement();

        var card = new Border
        {
            Width = 560,
            Background = Tokens.Dark.Surface,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 80, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Child = palette,
        };

        void RestoreFocus(object? sender, EventArgs e)
        {
            _overlayHost.Closed -= RestoreFocus;
            Dispatcher.UIThread.Post(() =>
            {
                // Only reclaim focus if it was stranded by the close — a command that opened another overlay,
                // or one that intentionally moved focus somewhere real (the tree, a new editor), keeps it.
                var current = FocusManager?.GetFocusedElement();
                if (!_overlayHost.IsOpen && (current is null || ReferenceEquals(current, palette.SearchBox)))
                {
                    origin?.Focus();
                }
            });
        }

        _overlayHost.Closed += RestoreFocus;
        _overlayHost.ShowContent(card);
        Dispatcher.UIThread.Post(() => palette.SearchBox.Focus());
    }

    /// <summary>A notebook file's path relative to the notebook root (for the quick-open / recent secondary line).</summary>
    private string RelativeToNotebook(string path) => NotebookPaths.RelativeTo(_notebook.RootPath, path);

    private async Task OpenNotebookInteractiveAsync()
    {
        var top = GetTopLevel(this);
        if (top is null)
        {
            return;
        }

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Notebook",
            AllowMultiple = false,
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            OpenNotebook(path);
        }
    }

    /// <summary>Toggle the formatting toolbar over the active note (shown by default). No-op for other panes.</summary>
    private void ToggleEditorToolbar()
    {
        if (_tabHost.ActivePane is MarkdownPane note)
        {
            note.ToolbarVisible = !note.ToolbarVisible;
        }
    }

    private void ShowAbout()
    {
        if (_overlayHost.IsOpen)
        {
            return;
        }

        _ = _overlayHost.ShowModalAsync(new MessageDialog(
            "Puffinote",
            "A second-brain notes app — composes Typage (Markdown) and Cellestra (spreadsheets)."));
    }

    /// <summary>Re-query every shell command's enabled state (Save/Close need a tab; rename/delete a selection).</summary>
    private void RefreshCommandStates()
    {
        foreach (var relay in _relays.Values)
        {
            relay.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Re-query only the commands whose enablement tracks the tree selection (rename/delete). The tree
    /// raises SelectionChanged on every Up/Down/type-ahead keystroke, so refreshing just these two — rather
    /// than fanning CanExecuteChanged out to the whole catalog per keystroke — keeps navigation cheap (P6).
    /// </summary>
    private void RefreshSelectionCommands()
    {
        _relays["shell.renameEntry"].RaiseCanExecuteChanged();
        _relays["shell.deleteEntry"].RaiseCanExecuteChanged();
    }
}
