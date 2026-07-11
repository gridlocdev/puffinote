using Avalonia.Controls;
using Avalonia.Input;
using Puffinote.Components.Commands;
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
            if (command.Shortcut is not { } shortcut || shortcut.YieldsToTextInput)
            {
                continue;
            }

            KeyBindings.Add(new KeyBinding
            {
                Gesture = shortcut.ToGesture(),
                Command = CommandFor(command),
            });
        }

        if (!OperatingSystem.IsMacOS())
        {
            KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.H, KeyModifiers.Control), // Windows/Linux convention for Replace
                Command = new RelayCommand(() => _findBar?.Show(replace: true), () => !AnyOverlayOpen),
            });
        }
    }

    /// <summary>True while a palette or an internal modal is open over the content.</summary>
    private bool AnyOverlayOpen => _overlayHost.IsOpen || _palette.IsVisible;

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
        format.Items.Add(InWindowItem("format.quote"));
        format.Items.Add(InWindowItem("format.bulletList"));
        format.Items.Add(InWindowItem("format.numberedList"));
        format.Items.Add(InWindowItem("format.task"));

        var insert = new MenuItem { Header = "_Insert" };
        insert.Items.Add(InWindowItem("format.link"));
        insert.Items.Add(InWindowItem("insert.image"));
        insert.Items.Add(new Separator());
        insert.Items.Add(InWindowItem("insert.codeBlock"));
        insert.Items.Add(InWindowItem("insert.hr"));
        insert.Items.Add(InWindowItem("format.task"));

        var view = new MenuItem { Header = "_View" };
        view.Items.Add(InWindowItem("view.commandPalette"));
        view.Items.Add(new Separator());
        _toggleToolbarItem = new CheckItem(AddInWindowCheck(view, "Show _Toolbar", _settings.Settings.ShowToolbar, ToggleToolbar));
        _toggleFocusItem = new CheckItem(AddInWindowCheck(view, "_Focus Mode", _focusMode, ToggleFocusMode, _commandsById["view.toggleFocus"].Gesture));
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
            item.Click += (_, _) => SetViewMode(choice);
            _viewModeItems.Add((m, new CheckItem(item)));
            view.Items.Add(item);
        }

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
        foreach (var id in new[] { "format.quote", "format.bulletList", "format.numberedList", "format.task" })
        {
            format.Menu!.Items.Add(NativeItem(id));
        }

        menu.Items.Add(format);

        var insert = NativeSubmenu("Insert");
        foreach (var id in new[] { "format.link", "insert.image", null, "insert.codeBlock", "insert.hr", "format.task" })
        {
            insert.Menu!.Items.Add(id is null ? new NativeMenuItemSeparator() : NativeItem(id));
        }

        menu.Items.Add(insert);

        var view = NativeSubmenu("View");
        view.Menu!.Items.Add(NativeItem("view.commandPalette"));
        view.Menu.Items.Add(new NativeMenuItemSeparator());
        _toggleToolbarItem = new CheckItem(AddNativeCheck(view.Menu, "Show Toolbar", _settings.Settings.ShowToolbar, ToggleToolbar));
        _toggleFocusItem = new CheckItem(AddNativeCheck(view.Menu, "Focus Mode", _focusMode, ToggleFocusMode, _commandsById["view.toggleFocus"].Gesture));
        view.Menu.Items.Add(new NativeMenuItemSeparator());
        foreach (var m in System.Enum.GetValues<MarkdownEditor.ViewMode>())
        {
            var choice = m;
            var item = new NativeMenuItem(ViewModeLabel(m))
            {
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = _settings.Settings.View == m,
                Command = new RelayCommand(() => SetViewMode(choice)),
            };
            _viewModeItems.Add((m, new CheckItem(item)));
            view.Menu.Items.Add(item);
        }

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
    /// The one command catalog (mirroring Cellestra's golden rule #4): the palette reads it, and the
    /// menu/toolbar invoke the same underlying actions. Built per open so CanExecute reflects the
    /// current state.
    /// </summary>
    private List<AppCommand> BuildCommands()
    {
        var ed = EditorView.Editor;
        return
        [
            new("file.new", "New", "Start a new, empty document.", Shortcut.Cmd(Key.N), () => _ = NewDocumentAsync()),
            new("file.open", "Open…", "Open a Markdown file from disk.", Shortcut.Cmd(Key.O), () => _ = OpenAsync()),
            new("file.save", "Save", "Save changes to the current file.", Shortcut.Cmd(Key.S), () => _ = SaveAsync()),
            new("file.saveAs", "Save As…", "Save a copy under a new name.", Shortcut.Cmd(Key.S, KeyModifiers.Shift), () => _ = SaveAsAsync()),
            new("file.revert", "Revert to Saved", "Discard changes since the last save.", null,
                () => _ = RevertAsync(), () => _currentPath is not null && EditorView.IsDirty),
            new("file.exportHtml", "Export HTML…", "Render the document to a self-contained HTML file.", null, () => _ = ExportHtmlAsync()),

            new("edit.undo", "Undo", "Step backward through your changes.", Shortcut.Cmd(Key.Z), () => EditorView.Undo(), () => EditorView.CanUndo),
            new("edit.redo", "Redo", "Re-apply a change you undid.", Shortcut.Cmd(Key.Z, KeyModifiers.Shift), () => EditorView.Redo(), () => EditorView.CanRedo),
            new("edit.cut", "Cut", "Move the selection to the clipboard.", Shortcut.Cmd(Key.X), () => _ = ed.CutAsync()),
            new("edit.copy", "Copy", "Copy the selection to the clipboard.", Shortcut.Cmd(Key.C), () => _ = ed.CopyAsync()),
            new("edit.paste", "Paste", "Paste from the clipboard.", Shortcut.Cmd(Key.V), () => _ = ed.PasteAsync()),
            new("edit.selectAll", "Select All", "Select the whole document.", Shortcut.Cmd(Key.A), ed.SelectAll),
            new("edit.find", "Find…", "Find text in the document.", Shortcut.Cmd(Key.F), () => _findBar?.Show(replace: false)),
            new("edit.replace", "Find & Replace…", "Find text and replace it (plain or regex).", Shortcut.Cmd(Key.F, KeyModifiers.Alt), () => _findBar?.Show(replace: true)),
            new("edit.moveLineUp", "Move Line Up", "Move the current line or block up.", Shortcut.Bare(Key.Up, KeyModifiers.Alt | KeyModifiers.Shift), ed.MoveLineUp),
            new("edit.moveLineDown", "Move Line Down", "Move the current line or block down.", Shortcut.Bare(Key.Down, KeyModifiers.Alt | KeyModifiers.Shift), ed.MoveLineDown),
            new("edit.duplicateLine", "Duplicate Line", "Duplicate the current line or selection.", Shortcut.Cmd(Key.D), ed.DuplicateLine),
            new("edit.deleteLine", "Delete Line", "Delete the current line or selection.", Shortcut.Cmd(Key.K, KeyModifiers.Shift), ed.DeleteLine),
            new("edit.selectWord", "Select Word", "Select the word under the caret.", null, ed.SelectCurrentWord),
            new("edit.selectParagraph", "Select Paragraph", "Select the paragraph under the caret.", null, ed.SelectCurrentParagraph),

            new("format.bold", "Bold", "Make the selection bold.", Shortcut.Cmd(Key.B), ed.ToggleBold),
            new("format.italic", "Italic", "Make the selection italic.", Shortcut.Cmd(Key.I), ed.ToggleItalic),
            new("format.code", "Inline Code", "Format the selection as inline code.", Shortcut.Cmd(Key.E), ed.ToggleInlineCode),
            new("format.strike", "Strikethrough", "Strike through the selection.", Shortcut.Cmd(Key.X, KeyModifiers.Shift), ed.ToggleStrikethrough),
            new("format.highlight", "Highlight", "Highlight the selection (==text==).", Shortcut.Cmd(Key.H, KeyModifiers.Shift), ed.ToggleHighlight),
            new("format.link", "Insert Link…", "Insert a link with display text and a location.", Shortcut.Cmd(Key.K), () => _ = ShowLinkDialogAsync()),
            new("format.task", "Checkbox", "Add a task checkbox to this line, or toggle it.", Shortcut.Cmd(Key.L), ed.ToggleTask),
            new("format.quote", "Blockquote", "Toggle a blockquote on the line(s).", null, ed.ToggleBlockquote),
            new("format.bulletList", "Bulleted List", "Toggle a bulleted list on the line(s).", Shortcut.Cmd(Key.OemPeriod), ed.ToggleBulletList),
            new("format.numberedList", "Numbered List", "Toggle a numbered list on the line(s).", Shortcut.Cmd(Key.OemQuestion), ed.ToggleNumberedList),
            new("format.h1", "Heading 1", "Make this line a level-1 heading.", Shortcut.Cmd(Key.D1), () => ed.ApplyHeading(1)),
            new("format.h2", "Heading 2", "Make this line a level-2 heading.", Shortcut.Cmd(Key.D2), () => ed.ApplyHeading(2)),
            new("format.h3", "Heading 3", "Make this line a level-3 heading.", Shortcut.Cmd(Key.D3), () => ed.ApplyHeading(3)),
            new("format.h4", "Heading 4", "Make this line a level-4 heading.", Shortcut.Cmd(Key.D4), () => ed.ApplyHeading(4)),
            new("format.h5", "Heading 5", "Make this line a level-5 heading.", Shortcut.Cmd(Key.D5), () => ed.ApplyHeading(5)),
            new("format.h6", "Heading 6", "Make this line a level-6 heading.", Shortcut.Cmd(Key.D6), () => ed.ApplyHeading(6)),
            new("format.paragraph", "Paragraph", "Remove the heading from this line.", Shortcut.Cmd(Key.D0), ed.SetParagraph),

            new("insert.image", "Image…", "Pick an image file to insert.", null, () => _ = InsertImageAsync()),
            new("insert.codeBlock", "Code Block", "Insert a fenced code block.", Shortcut.Cmd(Key.E, KeyModifiers.Shift), ed.InsertCodeBlock),
            new("insert.hr", "Horizontal Rule", "Insert a horizontal rule.", null, ed.InsertHorizontalRule),

            new("view.commandPalette", "Command Palette…", "Search and run any command.", Shortcut.Cmd(Key.P, KeyModifiers.Shift), OpenPalette),
            new("view.toggleToolbar", "Toggle Toolbar", "Show or hide the formatting toolbar.", null, ToggleToolbar),
            new("view.viewRich", "View Rich View", "Live preview: formatted, with Markdown markers hidden.", null, () => SetViewMode(MarkdownEditor.ViewMode.Rich)),
            new("view.viewFormatted", "View Formatted View", "Formatted, with the raw Markdown markers revealed.", null, () => SetViewMode(MarkdownEditor.ViewMode.Formatted)),
            new("view.viewRaw", "View Raw Text", "Plain-text source: no rich formatting or decorations.", null, () => SetViewMode(MarkdownEditor.ViewMode.RawText)),
            new("view.toggleFocus", "Toggle Focus Mode", "Distraction-free: hide the toolbar and status bar.", Shortcut.Bare(Key.F11), ToggleFocusMode),
            new("app.preferences", "Preferences…", "Theme, tab width, and toolbar settings.", Shortcut.Cmd(Key.OemComma), ShowSettings),
            new("help.reference", "Markdown Reference", "Open the offline syntax & shortcut reference.", null, ShowHelp),
        ];
    }
}
