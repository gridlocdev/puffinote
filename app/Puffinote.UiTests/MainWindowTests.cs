using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cellestra.Desktop.Services;
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
            window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Control | RawInputModifiers.Alt);
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

    /// <summary>True when keyboard focus currently rests on <paramref name="host"/> or a descendant of it.</summary>
    private static bool FocusIsWithin(MainWindow window, Control? host)
    {
        Dispatcher.UIThread.RunJobs(); // let the posted focus change settle
        return host is { }
            && window.FocusManager?.GetFocusedElement() is Visual focused
            && focused.GetSelfAndVisualAncestors().Contains(host);
    }
}
