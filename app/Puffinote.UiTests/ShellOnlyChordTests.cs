using Avalonia.Headless.XUnit;
using Puffinote.Components.Commands;
using Puffinote.Desktop.Panes;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// Some of the shell's chords do something no editor can do for itself — close every tab, move focus between
/// the sidebar and the editor, hide the sidebar. An embedded editor that binds one of those on the same
/// window takes it away, and nothing says so: <c>ShortcutConflictScanner</c> only watches the shell's
/// <em>reserved</em> keys, and none of these are reserved, so the collision is silent in both directions.
/// <para>
/// That is exactly how Plaintain's Word Wrap came to sit on ⌥⌘W beside Close All Tabs. This is the guard that
/// would have caught it.
/// </para>
/// <para>
/// It deliberately does <b>not</b> check the shell's other chords. ⌘K, ⌘O, ⌘S and ⌘, are shared with the
/// editors on purpose — the merged Command Menu is a superset of an editor's own, quick-open is the shell's
/// file-open, and the shell owns saving — so a match there is the design, not a defect.
/// </para>
/// </summary>
public sealed class ShellOnlyChordTests
{
    /// <summary>
    /// Shell capabilities an editor has no equivalent for, so a collision is a binding genuinely lost rather
    /// than one of two roads to the same place.
    /// </summary>
    private static readonly string[] ShellOnly =
    [
        "shell.closeAllTabs", "shell.focusEditor", "shell.focusTree", "shell.toggleSidebar",
    ];

    private static IEnumerable<(string Pane, IReadOnlyList<AppCommand> Commands)> PaneCatalogs()
    {
        yield return ("note", new MarkdownPane().Commands);
        yield return ("sheet", new SheetPane().Commands);
        yield return ("text", new PlainTextPane().Commands);
        yield return ("board", new TaskBoardPane().Commands);
    }

    [AvaloniaFact]
    public void No_editor_claims_a_chord_only_the_shell_can_answer()
    {
        var window = new MainWindow(new NotebookService());
        window.Show();

        var shellOnly = window.ShellCommandsForTest
            .Where(c => ShellOnly.Contains(c.Id) && c.Gesture is not null)
            .ToList();
        Assert.Equal(ShellOnly.Length, shellOnly.Count); // every id still exists and still carries a chord

        var clashes = new List<string>();
        foreach (var (pane, commands) in PaneCatalogs())
        {
            foreach (var editor in commands.Where(c => c.Gesture is not null))
            {
                foreach (var shell in shellOnly)
                {
                    if (shell.Gesture!.Key == editor.Gesture!.Key
                        && shell.Gesture.KeyModifiers == editor.Gesture.KeyModifiers)
                    {
                        clashes.Add($"{pane}: “{editor.Title}” ({editor.Id}) takes {shell.Id}'s chord");
                    }
                }
            }
        }

        Assert.Empty(clashes);
    }
}
