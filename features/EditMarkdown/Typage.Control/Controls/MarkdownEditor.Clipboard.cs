using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Typage.Engine.Markdown;

namespace Typage.Control.Controls;

public partial class MarkdownEditor
{
    public async Task CopyAsync()
    {
        var sel = Buffer.Selection;
        if (sel.IsEmpty || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        var text = Buffer.Document.GetText(sel.Start, sel.Length); // read before the await (UI thread)
        try
        {
            await clipboard.SetTextAsync(text);
        }
        catch (Exception)
        {
            // A clipboard write failure shouldn't crash the editor.
        }
    }

    public async Task CutAsync()
    {
        if (Buffer.Selection.IsEmpty)
        {
            return;
        }

        await CopyAsync();
        RunOnUiThread(() => Buffer.Insert(string.Empty)); // delete the selection on the UI thread
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
            return; // a clipboard read failure shouldn't crash the editor
        }

        if (!string.IsNullOrEmpty(text))
        {
            // Normalize the clipboard's line endings (often CRLF from other apps) to the document's own
            // style, so a paste doesn't sprinkle foreign newlines through the file (golden rule #5).
            RunOnUiThread(() =>
            {
                var nl = DetectNewline(Buffer.Document);
                var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
                Buffer.Insert(nl == "\n" ? normalized : normalized.Replace("\n", nl));
            });
        }
    }

    /// <summary>Run a document edit on the UI thread (clipboard awaits may resume off it).</summary>
    private static void RunOnUiThread(Action action)
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

    /// <summary>
    /// The right-click menu. Its <b>link</b> section — open, copy the address, edit — is the section that
    /// only sometimes applies, so it is built once and hidden unless the click actually landed on a link
    /// (<see cref="_contextLink"/>, recorded by the press that opened the menu). It sits at the top, where a
    /// browser puts it: when you right-click a link, the link is what you meant.
    /// </summary>
    private ContextMenu BuildContextMenu()
    {
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        var openLink = Item("Open Link", () => OnContextLink(link => OpenLink(link)));
        var copyLink = Item("Copy Link Address", () => OnContextLink(CopyLinkUrl));
        var editLink = Item("Edit Link…", () => OnContextLink(RequestEditLink));
        var linkSeparator = new Separator();

        var menu = new ContextMenu();
        menu.Items.Add(openLink);
        menu.Items.Add(copyLink);
        menu.Items.Add(editLink);
        menu.Items.Add(linkSeparator);
        menu.Items.Add(Item("Cut", () => _ = CutAsync()));
        menu.Items.Add(Item("Copy", () => _ = CopyAsync()));
        menu.Items.Add(Item("Paste", () => _ = PasteAsync()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Select All", SelectAll));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Bold", ToggleBold));
        menu.Items.Add(Item("Italic", ToggleItalic));
        menu.Items.Add(Item("Inline Code", ToggleInlineCode));
        menu.Items.Add(Item("Strikethrough", ToggleStrikethrough));
        menu.Items.Add(Item("Link…", RequestInsertLink));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Undo", () => Buffer.Undo()));
        menu.Items.Add(Item("Redo", () => Buffer.Redo()));

        menu.Opening += (_, _) =>
        {
            var link = _contextLink;
            openLink.IsVisible = copyLink.IsVisible = editLink.IsVisible = linkSeparator.IsVisible = link is not null;

            // A link that isn't a web address (a relative path, say) can still be copied and edited — it just
            // can't be handed to a browser, so only that one item goes dim.
            openLink.IsEnabled = link is { } l && TryGetLaunchUri(l.Url, out _);
        };

        return menu;
    }

    /// <summary>Run <paramref name="action"/> on the link the context menu was opened on, if it was opened on one.</summary>
    private void OnContextLink(Action<MarkdownLink> action)
    {
        if (_contextLink is { } link)
        {
            action(link);
        }
    }

    private static async Task SetClipboardSafe(IClipboard clipboard, string text)
    {
        try
        {
            await clipboard.SetTextAsync(text);
        }
        catch (Exception)
        {
            // A clipboard write failure shouldn't crash the editor.
        }
    }

    /// <summary>Copy the code run <c>[first, last]</c> to the clipboard and flash a "Copied!" confirmation.
    /// Triggered by the code block's copy button (see <see cref="OnPointerPressed"/>).</summary>
    private void CopyCodeBlock(int first, int last)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            var text = CodeBlockText(first, last); // read on the UI thread, before the await
            _ = SetClipboardSafe(clipboard, text);
        }

        _copiedFeedbackLine = first;
        _copyFeedbackTimer?.Stop();
        _copyFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
        _copyFeedbackTimer.Tick += (_, _) =>
        {
            _copyFeedbackTimer!.Stop();
            _copiedFeedbackLine = -1;
            InvalidateVisual();
        };
        _copyFeedbackTimer.Start();
        InvalidateVisual();
    }

    /// <summary>The source text of the code run <c>[firstLine, lastLine]</c> to copy: a fenced block's inner
    /// lines (without the <c>```</c> fences), or an indented block's lines dedented by one level.</summary>
    private string CodeBlockText(int firstLine, int lastLine)
    {
        var doc = Buffer.Document;
        var openToks = _model.LineTokens(firstLine);
        var fenced = openToks.Count > 0 && openToks[0].Type == TokenType.CodeFence;
        var from = firstLine;
        var to = lastLine;
        if (fenced)
        {
            from = firstLine + 1;
            var closeToks = _model.LineTokens(lastLine);
            if (lastLine > firstLine && closeToks.Count > 0 && closeToks[0].Type == TokenType.CodeFence)
            {
                to = lastLine - 1; // drop the closing fence (an unterminated fence keeps its last line)
            }
        }

        var lines = new List<string>(Math.Max(0, to - from + 1));
        for (var l = from; l <= to; l++)
        {
            var text = doc.GetLineText(l).TrimEnd('\r');
            lines.Add(fenced ? text : Dedent(text));
        }

        return string.Join("\n", lines);
    }

    /// <summary>Strip one indent level (a leading tab, or up to four leading spaces) from <paramref name="text"/>.</summary>
    private static string Dedent(string text)
    {
        if (text.Length > 0 && text[0] == '\t')
        {
            return text[1..];
        }

        var spaces = 0;
        while (spaces < 4 && spaces < text.Length && text[spaces] == ' ')
        {
            spaces++;
        }

        return text[spaces..];
    }
}
