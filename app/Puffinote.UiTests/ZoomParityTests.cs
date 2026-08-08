using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Cellestra.Desktop.Services;
using Pivotask.Core.Model;
using Pivotask.Core.Serialization;
using Puffinote.Components.Commands;
using Puffinote.Components.Theming;
using Puffinote.Desktop.Panes;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Typage.Control.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// Every editor Puffinote embeds zooms, under the same three chords. This is the property the shell exists
/// to protect: a reader meets a note, a sheet, a log and a board in one window, and ⌘= has to mean the same
/// thing in all four — the shell composes each editor's own catalog, so the parity is by construction.
/// </summary>
public sealed class ZoomParityTests
{
    private static readonly RawInputModifiers Cmd = TestKeys.RawCommand;

    private static readonly string[] ZoomIds =
        [ZoomCommands.ZoomInId, ZoomCommands.ZoomOutId, ZoomCommands.ActualSizeId];

    private static async Task<IEditorPane> PaneForAsync(string dir, string name, byte[]? bytes = null)
    {
        var path = Path.Combine(dir, name);
        if (bytes is null)
        {
            File.WriteAllText(path, "hello");
        }
        else
        {
            File.WriteAllBytes(path, bytes);
        }

        var pane = EditorPaneFactory.Create(path);
        Assert.True(pane is not null, $"no pane type claims {name}");
        await pane.LoadAsync(path);
        return pane;
    }

    private static byte[] BlankBoard()
    {
        var board = new Board();
        board.Groups.Add(new TaskGroup { Id = "g1", Name = "Inbox" });
        return System.Text.Encoding.UTF8.GetBytes(BoardCodec.Write(board));
    }

    [AvaloniaFact]
    public async Task Every_pane_type_publishes_the_same_three_zoom_commands()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-zoom-parity");
        try
        {
            var panes = new[]
            {
                await PaneForAsync(dir.FullName, "note.md"),
                await PaneForAsync(dir.FullName, "log.txt"),
                await PaneForAsync(dir.FullName, "book.ods", WorkbookSession.NewBlank().SerializeToBytes()),
                await PaneForAsync(dir.FullName, "board.pivotask.json", BlankBoard()),
            };

            foreach (var pane in panes)
            {
                foreach (string id in ZoomIds)
                {
                    var command = pane.Commands.SingleOrDefault(c => c.Id == id);
                    Assert.True(command is not null, $"{pane.GetType().Name} has no {id}");
                }

                // Same chords, not merely the same ids: ⌘= / ⌘− / ⌥⌘0, in every one of them.
                Assert.Equal(Shortcut.Cmd(Key.OemPlus), Zoom(pane, ZoomCommands.ZoomInId).Shortcut);
                Assert.Equal(Shortcut.Cmd(Key.OemMinus), Zoom(pane, ZoomCommands.ZoomOutId).Shortcut);
                Assert.Equal(Shortcut.Cmd(Key.D0, KeyModifiers.Alt), Zoom(pane, ZoomCommands.ActualSizeId).Shortcut);
                pane.Dispose();
            }
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static AppCommand Zoom(IEditorPane pane, string id) => pane.Commands.Single(c => c.Id == id);

    [AvaloniaFact]
    public async Task The_shell_binds_the_active_editors_zoom_chords_including_the_shifted_plus()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-zoom-keys");
        try
        {
            var path = Path.Combine(dir.FullName, "note.md");
            File.WriteAllText(path, "# note");

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);
            await window.OpenFileForTest(path);
            Dispatcher.UIThread.RunJobs();

            var editor = Assert.IsType<MarkdownEditorView>(window.ActiveEditorViewForTest);
            editor.Editor.Focus();
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.Equal, Cmd);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1.1, editor.Zoom);

            // The key that actually carries "+" is ⇧=, which is what someone reading "⌘+" will press.
            window.KeyPressQwerty(PhysicalKey.Equal, Cmd | RawInputModifiers.Shift);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1.25, editor.Zoom);

            window.KeyPressQwerty(PhysicalKey.Minus, Cmd);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1.1, editor.Zoom);

            window.KeyPressQwerty(PhysicalKey.Digit0, Cmd | RawInputModifiers.Alt);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ZoomScale.Default, editor.Zoom);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
