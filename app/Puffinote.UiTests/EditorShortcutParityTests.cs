using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Puffinote.Desktop.Panes;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Typage.Control.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// The embedded note editor answers to the same keys as the standalone Typage app.
/// <para>
/// It didn't: the shell bound only <em>its own</em> catalog, so any Typage gesture the control doesn't
/// hard-handle in <c>OnKeyDown</c> — ⌘. bullets, ⌘/ numbers, ⌘⇧H highlight, ⌘D duplicate line, ⌘F find — was a
/// dead key here, even though the toolbar tooltip and the palette row both advertised it. The shell now binds
/// the active pane's catalog, which is the one catalog the standalone app binds too.
/// </para>
/// </summary>
public sealed class EditorShortcutParityTests
{
    // On this (non-mac) test host the platform command modifier is Control.
    private const RawInputModifiers Cmd = RawInputModifiers.Control;

    private static async Task<(MainWindow Window, MarkdownEditorView Editor)> OpenNoteAsync(string dir, string text = "note")
    {
        var path = Path.Combine(dir, "note.md");
        File.WriteAllText(path, text);

        var window = new MainWindow(new NotebookService());
        window.Show();
        window.OpenNotebook(dir);
        await window.OpenFileForTest(path);
        Dispatcher.UIThread.RunJobs();

        var editor = Assert.IsType<MarkdownEditorView>(window.ActiveEditorViewForTest);
        editor.Editor.Focus();
        Dispatcher.UIThread.RunJobs();
        return (window, editor);
    }

    [AvaloniaFact]
    public async Task Bulleted_list_fires_on_cmd_period()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-keys-bullet");
        try
        {
            var (window, editor) = await OpenNoteAsync(dir.FullName);

            // ⌘. — declared by Typage's catalog, never handled inside the control, and (until now) bound by
            // nobody in Puffinote. It is the plainest example of a shortcut the UI promised and never fired.
            window.KeyPressQwerty(PhysicalKey.Period, Cmd);
            Dispatcher.UIThread.RunJobs();

            Assert.StartsWith("- ", editor.Text, StringComparison.Ordinal);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Find_opens_the_editors_own_find_bar_on_cmd_f()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-keys-find");
        try
        {
            var (window, editor) = await OpenNoteAsync(dir.FullName, "alpha beta");

            Assert.False(editor.IsFindOpen); // Puffinote had no find-in-document at all before this
            window.KeyPressQwerty(PhysicalKey.F, Cmd);
            Dispatcher.UIThread.RunJobs();

            Assert.True(editor.IsFindOpen);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Cmd_k_still_opens_the_shells_palette_and_the_link_prompt_moved_to_cmd_alt_k()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-keys-palette");
        try
        {
            var (window, editor) = await OpenNoteAsync(dir.FullName);

            // ⌘K is a reserved global key: it must reach the shell's palette even with the editor focused,
            // and must never be shadowed by the editor's link prompt (which is why that moved to ⌘⌥K).
            window.KeyPressQwerty(PhysicalKey.K, Cmd);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsOverlayOpenForTest);
            Assert.False(editor.IsOverlayOpen);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsOverlayOpenForTest);

            editor.Editor.Focus();
            window.KeyPressQwerty(PhysicalKey.K, Cmd | RawInputModifiers.Alt);
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.IsOverlayOpen); // the link prompt, in the editor's own overlay layer

            // ⌘K is reserved and tunnel-routed, so it would otherwise punch straight through the link prompt
            // and open the palette on top of it. An overlay owns the keyboard — including the editor's own.
            window.KeyPressQwerty(PhysicalKey.K, Cmd);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsOverlayOpenForTest);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public void No_editor_gesture_collides_with_a_reserved_shell_key()
    {
        // Reserved keys are tunnel-routed and always win, so an editor command that declares one is a command
        // the user can never invoke by keyboard. A guard: ⌘K must not creep back into the editor's catalog.
        var reserved = new MarkdownPane().Commands
            .Where(c => c.Gesture is not null)
            .Select(c => c.Gesture!)
            .Intersect(ShellReservedGestures())
            .ToList();

        Assert.Empty(reserved);
    }

    private static IEnumerable<KeyGesture> ShellReservedGestures()
    {
        var window = new MainWindow(new NotebookService());
        return [.. window.ShellCommandsForTest
            .Where(c => ShellCommandCatalog.IsReserved(c.Id) && c.Gesture is not null)
            .Select(c => c.Gesture!)];
    }
}
