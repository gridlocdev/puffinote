using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Typage.Control.Controls;
using Xunit;

namespace Typage.UiTests;

public class ClipboardTests
{
    [AvaloniaFact]
    public void Clipboard_NoTopLevel_DoesNotThrow()
    {
        // A detached editor has no top level / clipboard; paste/copy/cut must degrade, not crash.
        var editor = new MarkdownEditor { Text = "x" };
        editor.SelectAll();
        var ex = Record.ExceptionAsync(async () =>
        {
            await editor.PasteAsync();
            await editor.CopyAsync();
            await editor.CutAsync();
        }).GetAwaiter().GetResult();
        Assert.Null(ex);
    }

    [AvaloniaFact]
    public async Task Paste_InsertsClipboardText()
    {
        var editor = new MarkdownEditor { Text = "before" };
        var window = new Window { Content = editor };
        window.Show();

        var clipboard = TopLevel.GetTopLevel(editor)?.Clipboard;
        Assert.NotNull(clipboard);
        await clipboard!.SetTextAsync("PASTED");

        editor.SelectAll();
        await editor.PasteAsync();
        Assert.Equal("PASTED", editor.Text);
    }

    [AvaloniaFact]
    public async Task Cut_CopiesAndDeletesSelection()
    {
        var editor = new MarkdownEditor { Text = "hello world" };
        var window = new Window { Content = editor };
        window.Show();

        editor.Buffer.Selection = new Engine.Document.Selection(0, 5); // "hello"
        await editor.CutAsync();
        Assert.Equal(" world", editor.Text);

        var clipboard = TopLevel.GetTopLevel(editor)!.Clipboard!;
        Assert.Equal("hello", await clipboard.TryGetTextAsync());
    }
}
