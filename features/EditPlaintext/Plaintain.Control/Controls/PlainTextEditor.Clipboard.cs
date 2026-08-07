using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace Plaintain.Control.Controls;

public sealed partial class PlainTextEditor
{
    public async Task CopyAsync()
    {
        var sel = Buffer.Selection;
        if (sel.IsEmpty || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        string text = Buffer.Document.GetText(sel.Start, sel.Length); // read on the UI thread, before the await
        try
        {
            await clipboard.SetTextAsync(text);
        }
        catch (Exception)
        {
            // A clipboard write failure shouldn't take the editor down with it.
        }
    }

    public async Task CutAsync()
    {
        if (Buffer.Selection.IsEmpty)
        {
            return;
        }

        await CopyAsync();
        OnUiThread(() => Buffer.Insert(string.Empty));
    }

    public async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        string? text;
        try
        {
            text = await clipboard.TryGetTextAsync();
        }
        catch (Exception)
        {
            return; // a clipboard read failure shouldn't take the editor down with it
        }

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        OnUiThread(() =>
        {
            // Normalise the clipboard's line endings to the document's own, so a paste from another app
            // never sprinkles foreign newlines through a file whose endings we promise to preserve.
            string nl = NewlineOf(Buffer.Document);
            string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            Buffer.Insert(nl == "\n" ? normalized : normalized.Replace("\n", nl, StringComparison.Ordinal));
            BringCaretIntoView();
        });
    }

    /// <summary>Run a document edit on the UI thread (a clipboard await may resume off it).</summary>
    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}
