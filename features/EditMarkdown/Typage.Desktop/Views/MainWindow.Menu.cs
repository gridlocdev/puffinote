using Avalonia.Controls;
using Avalonia.Input;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls.CommandBar;
using Typage.Control.Controls;
using Typage.Desktop.Services;

namespace Typage.Desktop.Views;

public partial class MainWindow
{
    /// <summary>
    /// Build the command catalog, register one key binding per command (per-platform gesture), and install
    /// the menu: the macOS native menu bar (system bar, ⌘ key-equivalents) on macOS, or the in-window menu
    /// elsewhere (PRD §7.5 — a platform-native menu bar).
    /// </summary>
    private void InstallMenu(DockPanel root)
    {
        _commandsById = BuildCommands().ToDictionary(c => c.Id);
        RegisterCommandKeyBindings();

        if (OperatingSystem.IsMacOS())
        {
            BuildNativeMenu();
        }
        else
        {
            var menu = BuildInWindowMenu();
            DockPanel.SetDock(menu, Dock.Top);
            root.Children.Add(menu);
        }
    }

    /// <summary>
    /// One window key binding per command, using its platform gesture (⌘ on macOS, Ctrl elsewhere). The
    /// standard text-editing chords (⌘/Ctrl + A/C/V/X/Z/Y) are skipped — the focused editor or dialog text
    /// field owns them — and the Windows/Linux "Ctrl+H = Replace" alias is kept since those platforms expect
    /// it. On macOS the native menu's key-equivalents also fire these; the bindings are the cross-platform path.
    /// </summary>
    private void RegisterCommandKeyBindings()
    {
        foreach (var command in _commandsById.Values)
        {
            // AllShortcuts, so a command's unadvertised aliases bind too — Zoom In shows ⌘= and also
            // answers to the ⇧= that actually carries the "+" glyph, and to the keypad's own key.
            foreach (var shortcut in command.AllShortcuts)
            {
                if (shortcut.YieldsToTextInput)
                {
                    continue;
                }

                KeyBindings.Add(new KeyBinding
                {
                    Gesture = shortcut.ToGesture(),
                    Command = CommandFor(command),
                });
            }
        }

        if (!OperatingSystem.IsMacOS())
        {
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.H, KeyModifiers.Control), // Windows/Linux convention for Replace
                Command = new RelayCommand(() => EditorView.ShowFind(replace: true), () => !AnyOverlayOpen),
            });
        }
    }

    /// <summary>
    /// True while a palette or an internal modal is open over the content. The editor surface hosts modals of
    /// its own now (the link prompt, the reference sheet), so those count too — otherwise a chord typed into
    /// the link dialog's text field would still fire the command behind it.
    /// </summary>
    private bool AnyOverlayOpen => _overlayHost.IsOpen || _palette.IsVisible || EditorView.IsOverlayOpen;

    /// <summary>
    /// The ICommand backing a command's shortcut / menu item. Gated on <see cref="AnyOverlayOpen"/> so a
    /// keyboard shortcut (or a macOS native-menu accelerator, which the overlay scrim can't intercept)
    /// can't fire a command while a palette or modal dialog is up.
    /// </summary>
    private RelayCommand CommandFor(AppCommand command) =>
        new(command.Execute, () => command.IsEnabled && !AnyOverlayOpen);

    // ----- in-window menu (Windows/Linux) -----

    private Menu BuildInWindowMenu()
    {
        var file = new MenuItem { Header = "_File" };
        file.Items.Add(InWindowItem("file.new"));
        file.Items.Add(InWindowItem("file.open"));
        file.Items.Add(_recentMenu);
        file.Items.Add(InWindowItem("file.save"));
        file.Items.Add(InWindowItem("file.saveAs"));
        file.Items.Add(InWindowItem("file.revert"));
        file.Items.Add(new Separator());
        file.Items.Add(InWindowItem("file.exportHtml"));
        file.Items.Add(new Separator());
        file.Items.Add(MenuLeaf("E_xit", Close));

        var edit = new MenuItem { Header = "_Edit" };
        edit.Items.Add(InWindowItem("edit.undo"));
        edit.Items.Add(InWindowItem("edit.redo"));
        edit.Items.Add(new Separator());
        edit.Items.Add(InWindowItem("edit.cut"));
        edit.Items.Add(InWindowItem("edit.copy"));
        edit.Items.Add(InWindowItem("edit.paste"));
        edit.Items.Add(new Separator());
        edit.Items.Add(InWindowItem("edit.duplicateLine"));
        edit.Items.Add(InWindowItem("edit.deleteLine"));
        edit.Items.Add(InWindowItem("edit.moveLineUp"));
        edit.Items.Add(InWindowItem("edit.moveLineDown"));
        edit.Items.Add(new Separator());
        edit.Items.Add(InWindowItem("edit.selectAll"));
        edit.Items.Add(InWindowItem("edit.selectWord"));
        edit.Items.Add(InWindowItem("edit.selectParagraph"));
        edit.Items.Add(new Separator());
        edit.Items.Add(InWindowItem("edit.find"));
        edit.Items.Add(InWindowItem("edit.replace"));

        var format = new MenuItem { Header = "F_ormat" };
        format.Items.Add(InWindowItem("format.bold"));
        format.Items.Add(InWindowItem("format.italic"));
        format.Items.Add(InWindowItem("format.strike"));
        format.Items.Add(InWindowItem("format.highlight"));
        format.Items.Add(InWindowItem("format.code"));
        format.Items.Add(new Separator());
        format.Items.Add(InWindowSubmenu("_Heading",
            "format.h1", "format.h2", "format.h3", "format.h4", "format.h5", "format.h6", null, "format.paragraph"));
        format.Items.Add(InWindowItem("format.bulletList"));
        format.Items.Add(InWindowItem("format.numberedList"));
        format.Items.Add(InWindowItem("format.task"));
        format.Items.Add(InWindowItem("format.removeTask"));

        // Blockquote lives here, with the inserts — it isn't a list. It wraps a passage; it has no items to
        // add, doesn't nest, and doesn't renumber.
        var insert = new MenuItem { Header = "_Insert" };
        insert.Items.Add(InWindowItem("format.link"));
        insert.Items.Add(InWindowItem("format.editLink"));
        insert.Items.Add(InWindowItem("insert.image"));
        insert.Items.Add(new Separator());
        insert.Items.Add(InWindowItem("insert.codeBlock"));
        insert.Items.Add(InWindowItem("insert.hr"));
        insert.Items.Add(InWindowItem("format.quote"));

        var view = new MenuItem { Header = "_View" };
        view.Items.Add(InWindowItem("view.commandMenu"));
        view.Items.Add(new Separator());
        // The toolbar's three states as a radio group, the same shape as the view modes below: they are one
        // choice with three answers, not three independent switches.
        foreach (var state in System.Enum.GetValues<CommandBarVisibility>())
        {
            var choice = state;
            var item = new MenuItem
            {
                Header = ToolbarLabel(state),
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "toolbar",
                IsChecked = _settings.Settings.CommandBar == state,
            };
            item.Click += (_, _) => ApplyToolbar(choice, persist: true);
            _toolbarItems.Add((state, new CheckItem(item)));
            view.Items.Add(item);
        }

        _toggleFocusItem = new CheckItem(AddInWindowCheck(view, "_Focus Mode", _focusMode, ToggleFocusMode, _commandsById["view.toggleFocus"].Gesture));
        _highlightLineItem = new CheckItem(AddInWindowCheck(view, "_Highlight Current Line", EditorView.HighlightCurrentLine,
            () => EditorView.HighlightCurrentLine = !EditorView.HighlightCurrentLine));
        view.Items.Add(new Separator());
        foreach (var m in System.Enum.GetValues<MarkdownEditor.ViewMode>())
        {
            var choice = m;
            var item = new MenuItem
            {
                Header = ViewModeLabel(m),
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "viewmode",
                IsChecked = _settings.Settings.View == m,
            };
            item.Click += (_, _) => EditorView.ViewMode = choice; // the surface owns the switch; we persist it
            _viewModeItems.Add((m, new CheckItem(item)));
            view.Items.Add(item);
        }

        view.Items.Add(new Separator());
        view.Items.Add(InWindowItem("view.zoomIn"));
        view.Items.Add(InWindowItem("view.zoomOut"));
        view.Items.Add(InWindowItem("view.actualSize"));

        view.Items.Add(new Separator());

        var themeMenu = new MenuItem { Header = "T_heme" };
        foreach (var t in System.Enum.GetValues<AppTheme>())
        {
            var choice = t;
            var item = new MenuItem
            {
                Header = ThemeLabel(t),
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "theme",
                IsChecked = _settings.Settings.Theme == t,
            };
            item.Click += (_, _) => SetTheme(choice);
            _themeItems.Add((t, new CheckItem(item)));
            themeMenu.Items.Add(item);
        }

        view.Items.Add(themeMenu);
        view.Items.Add(new Separator());
        view.Items.Add(InWindowItem("app.preferences"));

        var help = new MenuItem { Header = "_Help" };
        help.Items.Add(InWindowItem("help.reference"));

        var menu = new Menu();
        menu.Items.Add(file);
        menu.Items.Add(edit);
        menu.Items.Add(format);
        menu.Items.Add(insert);
        menu.Items.Add(view);
        menu.Items.Add(help);
        return menu;
    }

    /// <summary>An in-window menu item from the catalog: its title, action (gated by IsEnabled), displayed
    /// gesture, and a hover tooltip carrying the command's plain-language description (mirrors Cellestra).</summary>
    private MenuItem InWindowItem(string id)
    {
        var c = _commandsById[id];
        var item = new MenuItem { Header = c.Title, Command = CommandFor(c) };
        if (c.Gesture is { } g)
        {
            item.InputGesture = g;
        }

        ToolTip.SetTip(item, c.Description);
        return item;
    }

    /// <summary>A nested in-window submenu (a popout) holding the given catalog command items / separators.</summary>
    private MenuItem InWindowSubmenu(string header, params string?[] ids)
    {
        var sub = new MenuItem { Header = header };
        foreach (var id in ids)
        {
            sub.Items.Add(id is null ? new Separator() : InWindowItem(id));
        }

        return sub;
    }

    private static MenuItem MenuLeaf(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem AddInWindowCheck(MenuItem parent, string header, bool initial, Action onClick, KeyGesture? gesture = null)
    {
        var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = initial };
        if (gesture is not null)
        {
            item.InputGesture = gesture;
        }

        item.Click += (_, _) => onClick();
        parent.Items.Add(item);
        return item;
    }

    // ----- macOS native menu bar -----

    private void BuildNativeMenu()
    {
        var menu = new NativeMenu();

        var file = NativeSubmenu("File");
        file.Menu!.Items.Add(NativeItem("file.new"));
        file.Menu.Items.Add(NativeItem("file.open"));
        _nativeRecentItem = new NativeMenuItem("Open Recent") { Menu = [] };
        file.Menu.Items.Add(_nativeRecentItem);
        file.Menu.Items.Add(NativeItem("file.save"));
        file.Menu.Items.Add(NativeItem("file.saveAs"));
        file.Menu.Items.Add(NativeItem("file.revert"));
        file.Menu.Items.Add(new NativeMenuItemSeparator());
        file.Menu.Items.Add(NativeItem("file.exportHtml"));
        menu.Items.Add(file);

        var edit = NativeSubmenu("Edit");
        foreach (var id in new[] { "edit.undo", "edit.redo", null, "edit.cut", "edit.copy", "edit.paste", null, "edit.duplicateLine", "edit.deleteLine", "edit.moveLineUp", "edit.moveLineDown", null, "edit.selectAll", "edit.selectWord", "edit.selectParagraph", null, "edit.find", "edit.replace" })
        {
            edit.Menu!.Items.Add(id is null ? new NativeMenuItemSeparator() : NativeItem(id));
        }

        menu.Items.Add(edit);

        var format = NativeSubmenu("Format");
        foreach (var id in new[] { "format.bold", "format.italic", "format.strike", "format.highlight", "format.code", null })
        {
            format.Menu!.Items.Add(id is null ? new NativeMenuItemSeparator() : NativeItem(id));
        }

        var headings = NativeSubmenu("Heading");
        foreach (var id in new[] { "format.h1", "format.h2", "format.h3", "format.h4", "format.h5", "format.h6", null, "format.paragraph" })
        {
            headings.Menu!.Items.Add(id is null ? new NativeMenuItemSeparator() : NativeItem(id));
        }

        format.Menu!.Items.Add(headings);
        foreach (var id in new[] { "format.bulletList", "format.numberedList", "format.task", "format.removeTask" })
        {
            format.Menu!.Items.Add(NativeItem(id));
        }

        menu.Items.Add(format);

        // Blockquote is an insert, not a list — see the in-window menu.
        var insert = NativeSubmenu("Insert");
        foreach (var id in new[] { "format.link", "format.editLink", "insert.image", null, "insert.codeBlock", "insert.hr", "format.quote" })
        {
            insert.Menu!.Items.Add(id is null ? new NativeMenuItemSeparator() : NativeItem(id));
        }

        menu.Items.Add(insert);

        var view = NativeSubmenu("View");
        view.Menu!.Items.Add(NativeItem("view.commandMenu"));
        view.Menu.Items.Add(new NativeMenuItemSeparator());
        foreach (var state in System.Enum.GetValues<CommandBarVisibility>())
        {
            var choice = state;
            var item = new NativeMenuItem(ToolbarLabel(state))
            {
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = _settings.Settings.CommandBar == state,
                Command = new RelayCommand(() => ApplyToolbar(choice, persist: true)),
            };
            _toolbarItems.Add((state, new CheckItem(item)));
            view.Menu.Items.Add(item);
        }

        _toggleFocusItem = new CheckItem(AddNativeCheck(view.Menu, "Focus Mode", _focusMode, ToggleFocusMode, _commandsById["view.toggleFocus"].Gesture));
        _highlightLineItem = new CheckItem(AddNativeCheck(view.Menu, "Highlight Current Line", EditorView.HighlightCurrentLine,
            () => EditorView.HighlightCurrentLine = !EditorView.HighlightCurrentLine));
        view.Menu.Items.Add(new NativeMenuItemSeparator());
        foreach (var m in System.Enum.GetValues<MarkdownEditor.ViewMode>())
        {
            var choice = m;
            var item = new NativeMenuItem(ViewModeLabel(m))
            {
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = _settings.Settings.View == m,
                Command = new RelayCommand(() => EditorView.ViewMode = choice), // the surface owns the switch
            };
            _viewModeItems.Add((m, new CheckItem(item)));
            view.Menu.Items.Add(item);
        }

        view.Menu.Items.Add(new NativeMenuItemSeparator());
        view.Menu.Items.Add(NativeItem("view.zoomIn"));
        view.Menu.Items.Add(NativeItem("view.zoomOut"));
        view.Menu.Items.Add(NativeItem("view.actualSize"));

        view.Menu.Items.Add(new NativeMenuItemSeparator());

        var themeMenu = NativeSubmenu("Theme");
        foreach (var t in System.Enum.GetValues<AppTheme>())
        {
            var choice = t;
            var item = new NativeMenuItem(ThemeLabel(t))
            {
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = _settings.Settings.Theme == t,
                Command = new RelayCommand(() => SetTheme(choice)),
            };
            _themeItems.Add((t, new CheckItem(item)));
            themeMenu.Menu!.Items.Add(item);
        }

        view.Menu.Items.Add(themeMenu);
        view.Menu.Items.Add(new NativeMenuItemSeparator());
        view.Menu.Items.Add(NativeItem("app.preferences"));
        menu.Items.Add(view);

        var help = NativeSubmenu("Help");
        help.Menu!.Items.Add(NativeItem("help.reference"));
        menu.Items.Add(help);

        NativeMenu.SetMenu(this, menu);
    }

    /// <summary>A native menu item from the catalog. Standard editing chords get no key-equivalent so the
    /// focused text field/editor keeps them (otherwise macOS swallows ⌘C/⌘A/⌘Z mid-edit); they still work.</summary>
    private NativeMenuItem NativeItem(string id)
    {
        var c = _commandsById[id];
        var item = new NativeMenuItem(c.Title) { Command = CommandFor(c) };
        if (c.Shortcut is { } s && !s.YieldsToTextInput)
        {
            item.Gesture = s.ToGesture(mac: true);
        }

        return item;
    }

    private static NativeMenuItem NativeSubmenu(string header) => new(header) { Menu = [] };

    private static NativeMenuItem AddNativeCheck(NativeMenu parent, string header, bool initial, Action onClick, KeyGesture? gesture = null)
    {
        var item = new NativeMenuItem(header)
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = initial,
            Command = new RelayCommand(onClick),
        };
        if (gesture is not null)
        {
            item.Gesture = gesture;
        }

        parent.Items.Add(item);
        return item;
    }

    // ----- command catalog & palette -----

    private void OpenPalette() => _palette.Open(BuildCommands());

    /// <summary>
    /// The one command catalog (mirroring Cellestra's golden rule #4): the palette reads it, and the menu and
    /// key bindings invoke the same underlying actions.
    /// <para>
    /// It is a <em>merge</em>, not a list of its own: this shell contributes only the commands that need a
    /// shell (files, the palette, preferences, the window's own chrome), and everything that acts on the open
    /// document comes from <see cref="MarkdownEditorView.EditingCommands"/> — the very catalog Puffinote
    /// composes. The ~30 editing commands used to be re-declared here, which is exactly how the two apps came
    /// to disagree about what ⌘K does and which commands exist at all.
    /// </para>
    /// </summary>
    private List<AppCommand> BuildCommands() => [.. ShellCommands(), .. EditorView.EditingCommands];

    private List<AppCommand> ShellCommands() =>
    [
        new("file.new", "New", "Start a new, empty document.", Shortcut.Cmd(Key.N), () => _ = NewDocumentAsync(), icon: "file-plus"),
        new("file.open", "Open…", "Open a Markdown file from disk.", Shortcut.Cmd(Key.O), () => _ = OpenAsync(), icon: "folder-open"),
        new("file.save", "Save", "Save changes to the current file.", Shortcut.Cmd(Key.S), () => _ = SaveAsync(), icon: "save"),
        new("file.saveAs", "Save As…", "Save a copy under a new name.", Shortcut.Cmd(Key.S, KeyModifiers.Shift), () => _ = SaveAsAsync(), icon: "file-up"),
        new("file.revert", "Revert to Saved", "Discard changes since the last save.", null,
            () => _ = RevertAsync(), () => _currentPath is not null && EditorView.IsDirty, icon: "refresh-cw"),

        // ⌘K, matching Puffinote — where it is a reserved global key. It used to be ⌘⇧P here while ⌘K
        // inserted a link; the link prompt is now ⌘⌥K in both apps (see MarkdownEditingCommands).
        new("view.commandMenu", "Command Menu…", "Search and run any command.", Shortcut.Cmd(Key.K), OpenPalette, icon: "command"),
        // One command per state rather than one cycling toggle: the Command Menu is a list of destinations, and
        // "Hide Command Bar" says where you land where "Toggle" doesn't. Each is lit while it is the state in force.
        new("view.commandBarVisible", ToolbarLabel(CommandBarVisibility.Visible), "Show the full command bar.", null,
            () => ApplyToolbar(CommandBarVisibility.Visible, persist: true),
            isActive: () => EditorView.CommandBarVisibility == CommandBarVisibility.Visible, icon: "panel-top"),
        new("view.commandBarCollapsed", ToolbarLabel(CommandBarVisibility.Collapsed), "Fold the command bar to its slim handle.", null,
            () => ApplyToolbar(CommandBarVisibility.Collapsed, persist: true),
            isActive: () => EditorView.CommandBarVisibility == CommandBarVisibility.Collapsed, icon: "chevrons-up"),
        new("view.commandBarHidden", ToolbarLabel(CommandBarVisibility.Hidden), "Hide the command bar entirely, giving its row to the text.", null,
            () => ApplyToolbar(CommandBarVisibility.Hidden, persist: true),
            isActive: () => EditorView.CommandBarVisibility == CommandBarVisibility.Hidden, icon: "x"),
        new("view.toggleFocus", "Toggle Focus Mode", "Distraction-free: hide the command bar and status bar.", Shortcut.Bare(Key.F11), ToggleFocusMode, icon: "maximize"),
        new("app.preferences", "Preferences…", "Theme, tab width, and command bar settings.", Shortcut.Cmd(Key.OemComma), ShowSettings, icon: "settings-2"),
    ];
}
