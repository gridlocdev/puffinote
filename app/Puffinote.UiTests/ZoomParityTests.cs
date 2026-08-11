using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Cellestra.Desktop.Services;
using Cellestra.Editor;
using Pivotask.Control.Views;
using Pivotask.Core.Model;
using Pivotask.Core.Serialization;
using Plaintain.Control.Views;
using Puffinote.Components.Commands;
using Puffinote.Components.Input;
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
                await PaneForAsync(dir.FullName, "board.pivotask", BlankBoard()),
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

    /// <summary>
    /// Each editor's own zoom factor. There is no interface for it — the shell never needed one, because the
    /// editors reach parity by sharing <see cref="ZoomScale"/> rather than by sharing a base type — so the
    /// test reads each one where it lives, which is also what makes "all four moved" worth asserting.
    /// </summary>
    private static double ZoomOf(IEditorPane pane) => pane.View switch
    {
        MarkdownEditorView v => v.Zoom,
        PlainTextEditorView v => v.Zoom,
        SpreadsheetEditorView v => v.Zoom,
        TaskBoardView v => v.Zoom,
        _ => throw new Xunit.Sdk.XunitException($"{pane.GetType().Name} has no known zoom factor"),
    };

    /// <summary>
    /// Raise the gesture macOS reports for a trackpad pinch, where the backend would put it. Headless input
    /// can synthesise keys and a mouse, but not a platform gesture, so the event is posted directly.
    /// </summary>
    private static void Magnify(Visual target, double magnification, ulong timestampMs) =>
        ((Interactive)target).RaiseEvent(new PointerDeltaEventArgs(
            InputElement.PointerTouchPadGestureMagnifyEvent,
            target,
            new Pointer(0, PointerType.Mouse, isPrimary: true),
            TopLevel.GetTopLevel(target)!,
            default,
            timestampMs,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None,
            new Vector(magnification, magnification)));

    [AvaloniaFact]
    public async Task A_pinch_zooms_every_pane_type_onto_the_same_stop()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-zoom-pinch");
        try
        {
            var panes = new[]
            {
                await PaneForAsync(dir.FullName, "note.md"),
                await PaneForAsync(dir.FullName, "log.txt"),
                await PaneForAsync(dir.FullName, "book.ods", WorkbookSession.NewBlank().SerializeToBytes()),
                await PaneForAsync(dir.FullName, "board.pivotask", BlankBoard()),
            };

            foreach (var pane in panes)
            {
                var window = new Window { Width = 600, Height = 400, Content = pane.View };
                window.Show();
                window.UpdateLayout();

                Assert.Equal(ZoomScale.Default, ZoomOf(pane));

                // One stop's worth of pinch, spread over three increments the way a trackpad delivers it.
                Magnify(pane.View, PinchZoom.MagnificationPerStop / 3, 100);
                Magnify(pane.View, PinchZoom.MagnificationPerStop / 3, 120);
                Magnify(pane.View, PinchZoom.MagnificationPerStop / 3, 140);
                Dispatcher.UIThread.RunJobs();

                // The same 110% a note, a log, a sheet and a board all reach with ⌘=. One gesture, wired
                // once against the shared ladder — not four implementations that agree by luck.
                Assert.Equal(1.1, ZoomOf(pane));

                window.Content = null;
                window.Close();
                pane.Dispose();
            }
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

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
