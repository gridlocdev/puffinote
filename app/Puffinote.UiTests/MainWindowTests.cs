using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cellestra.Desktop.Services;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Controls;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Typage.Control.Views;
using Xunit;

namespace Puffinote.UiTests;

public sealed class MainWindowTests
{
    [AvaloniaFact]
    public async Task Toggle_editor_toolbar_command_shows_and_hides_the_note_toolbar()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-toolbar");
        try
        {
            var md = Path.Combine(dir.FullName, "note.md");
            File.WriteAllText(md, "# hi");

            var window = new MainWindow(new NotebookService());
            window.Show(); // renders the note toolbar under null drawing
            window.OpenNotebook(dir.FullName);
            await window.OpenFileAsync(md);

            var view = Assert.IsType<MarkdownEditorView>(window.ActiveEditorViewForTest);
            Assert.True(view.ShowCommandBar); // shown by default

            window.InvokeShellForTest("shell.toggleEditorToolbar");
            Assert.False(view.ShowCommandBar);

            window.InvokeShellForTest("shell.toggleEditorToolbar");
            Assert.True(view.ShowCommandBar);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Opening_a_sheet_does_not_warn_about_superseded_palette_or_quick_open_shortcuts()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-conflict");
        try
        {
            var ods = Path.Combine(dir.FullName, "book.ods");
            File.WriteAllBytes(ods, WorkbookSession.NewBlank().SerializeToBytes());

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);
            await window.OpenFileAsync(ods);

            // Cellestra binds ⌘K (its palette) and ⌘O (open) — both reserved by Puffinote — but the shell
            // supersedes them (merged palette / quick-open), so they're not real conflicts: the sheet opens
            // and no "shortcuts are overridden" warning modal appears.
            Assert.Contains("book.ods", window.Title);
            Assert.False(window.IsOverlayOpenForTest);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void Window_shows_and_opening_a_notebook_updates_the_title()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-uitest");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "notes.md"), "# hello");
            Directory.CreateDirectory(Path.Combine(dir.FullName, "sub"));

            var window = new MainWindow(new NotebookService());
            window.Show(); // exercises the full layout/render pass under null drawing

            Assert.Equal("Puffinote", window.Title);

            window.OpenNotebook(dir.FullName);

            Assert.Equal($"Puffinote — {dir.Name}", window.Title);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Focus_editor_and_focus_sidebar_commands_move_keyboard_focus_between_the_two()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-focus");
        try
        {
            var ods = Path.Combine(dir.FullName, "book.ods");
            File.WriteAllBytes(ods, WorkbookSession.NewBlank().SerializeToBytes());

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);
            await window.OpenFileAsync(ods);
            Dispatcher.UIThread.RunJobs(); // let the tab content lay out before focus can land on it

            // Focus Editor puts keyboard focus inside the active document.
            window.InvokeShellForTest("shell.focusEditor");
            Assert.True(FocusIsWithin(window, window.ActiveEditorViewForTest));

            // Focus Sidebar switches to the Files panel and pulls focus off the editor and into the tree.
            window.InvokeShellForTest("shell.focusTree");
            Assert.Equal("files", window.ActiveSidebarPanelForTest);
            Assert.False(FocusIsWithin(window, window.ActiveEditorViewForTest));

            // The default gesture ⌘⌥E (Ctrl+Alt+E off-mac) is wired to Focus Editor and works from the tree.
            window.KeyPressQwerty(PhysicalKey.E, TestKeys.RawCommand | RawInputModifiers.Alt);
            Assert.True(FocusIsWithin(window, window.ActiveEditorViewForTest));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Closing_the_command_palette_returns_focus_to_where_it_was_in_the_editor()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-palette-focus");
        try
        {
            var ods = Path.Combine(dir.FullName, "book.ods");
            File.WriteAllBytes(ods, WorkbookSession.NewBlank().SerializeToBytes());

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);
            await window.OpenFileAsync(ods);
            Dispatcher.UIThread.RunJobs();

            window.InvokeShellForTest("shell.focusEditor"); // caret is in the grid
            Assert.True(FocusIsWithin(window, window.ActiveEditorViewForTest));

            window.InvokeShellForTest("shell.commandPalette");
            Dispatcher.UIThread.RunJobs(); // the palette's search box takes focus
            Assert.True(window.IsOverlayOpenForTest);
            Assert.False(FocusIsWithin(window, window.ActiveEditorViewForTest)); // focus moved to the palette

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); // dismiss the palette
            Dispatcher.UIThread.RunJobs();

            Assert.False(window.IsOverlayOpenForTest);
            Assert.True(FocusIsWithin(window, window.ActiveEditorViewForTest)); // focus is back in the editor
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task The_sidebar_highlights_whichever_note_the_active_tab_is_showing()
    {
        // The tab host never told the tree which document was open — the tree only ever knew its own selection,
        // which is a different thing (see FileTreeVisualsTests). Switching tabs from the keyboard therefore left
        // the sidebar pointing at whatever you last clicked. This is the wiring that closes that gap, and it is
        // what makes the highlight land the same way whether you reach a note from the tree or from the tab strip.
        var dir = Directory.CreateTempSubdirectory("puffinote-active-highlight");
        try
        {
            var one = Path.Combine(dir.FullName, "one.md");
            var two = Path.Combine(dir.FullName, "two.md");
            File.WriteAllText(one, "# one");
            File.WriteAllText(two, "# two");

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);
            await window.OpenFileAsync(one);
            await window.OpenFileAsync(two); // two.md is now the active tab

            Assert.Equal(Tokens.Active.AccentSubtle, TreeWash(window, "two.md"));
            Assert.Equal(Brushes.Transparent, TreeWash(window, "one.md"));

            // ⌘/Ctrl+Tab — a route into a document that never touches the tree at all.
            window.InvokeShellForTest("shell.nextTab");

            Assert.Equal(one, window.ActiveTabPathForTest);
            Assert.Equal(Tokens.Active.AccentSubtle, TreeWash(window, "one.md"));
            Assert.Equal(Brushes.Transparent, TreeWash(window, "two.md"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Close_all_tabs_command_empties_the_editor_area_and_is_disabled_with_nothing_open()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-close-all");
        try
        {
            var one = Path.Combine(dir.FullName, "one.md");
            var two = Path.Combine(dir.FullName, "two.md");
            File.WriteAllText(one, "# one");
            File.WriteAllText(two, "# two");

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);

            var closeAll = window.ShellCommandsForTest.Single(c => c.Id == "shell.closeAllTabs");
            Assert.False(closeAll.IsEnabled); // nothing open yet

            await window.OpenFileForTest(one);
            await window.OpenFileForTest(two);
            Assert.True(closeAll.IsEnabled);

            window.InvokeShellForTest("shell.closeAllTabs"); // both are clean → closes without a prompt
            Dispatcher.UIThread.RunJobs();

            Assert.Null(window.ActiveTabPathForTest);
            Assert.False(closeAll.IsEnabled);
            Assert.False(window.IsOverlayOpenForTest);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Close_other_tabs_command_leaves_only_the_active_tab_and_needs_a_second_tab()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-close-others");
        try
        {
            var one = Path.Combine(dir.FullName, "one.md");
            var two = Path.Combine(dir.FullName, "two.md");
            File.WriteAllText(one, "# one");
            File.WriteAllText(two, "# two");

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);

            var closeOthers = window.ShellCommandsForTest.Single(c => c.Id == "shell.closeOtherTabs");
            Assert.False(closeOthers.IsEnabled); // nothing open yet

            await window.OpenFileForTest(one);
            Assert.False(closeOthers.IsEnabled); // one tab has no "others"

            await window.OpenFileForTest(two);
            Assert.True(closeOthers.IsEnabled);

            window.InvokeShellForTest("shell.closeOtherTabs"); // both are clean → closes without a prompt
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(two, window.ActiveTabPathForTest); // the active tab is the one that survives
            Assert.False(closeOthers.IsEnabled);
            Assert.False(window.IsOverlayOpenForTest);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>The wash the sidebar's file tree is painting behind <paramref name="rowName"/>'s row.</summary>
    private static IBrush? TreeWash(MainWindow window, string rowName)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var tree = window.GetVisualDescendants().OfType<FileTreeView>().Single();
        var row = tree.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .Single(i => i.GetVisualDescendants().OfType<Control>().Any(c => AutomationProperties.GetName(c) == rowName));

        return row.GetVisualDescendants().OfType<DockPanel>().First().Background;
    }

    /// <summary>True when keyboard focus currently rests on <paramref name="host"/> or a descendant of it.</summary>
    private static bool FocusIsWithin(MainWindow window, Control? host)
    {
        Dispatcher.UIThread.RunJobs(); // let the posted focus change settle
        return host is { }
            && window.FocusManager?.GetFocusedElement() is Visual focused
            && focused.GetSelfAndVisualAncestors().Contains(host);
    }
}
