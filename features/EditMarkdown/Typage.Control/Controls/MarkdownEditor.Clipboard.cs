using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
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

    /// <summary>Raised when a pasted image couldn't be written to disk; the argument is the reason. The host
    /// surfaces it (<see cref="Views.MarkdownEditorView"/> shows it in the editor's own overlay).</summary>
    public event EventHandler<string>? ImagePasteFailed;

    public async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        // A picture on the clipboard is pasted as a picture: the bytes are written into the document's own
        // folder and the reference to them is what lands in the text (PRD §7.2). Text paste is the fallback,
        // and the common case.
        if (await TryPasteImageAsync(clipboard))
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

    /// <summary>
    /// Paste an image, if the clipboard is carrying one: true when it handled the paste (including when it
    /// reported a failure), false when there was no picture and the text path should run.
    ///
    /// <para>A copied <b>file</b> is preferred over a raw <b>bitmap</b>, and only when it is an image file: it
    /// keeps the original bytes and format, where a bitmap has been decoded and would be re-encoded as PNG.
    /// Anything else on the clipboard — including a non-image file, whose path other apps also offer as text —
    /// falls through untouched.</para>
    /// </summary>
    private async Task<bool> TryPasteImageAsync(IClipboard clipboard)
    {
        string? sourcePath = null;
        Avalonia.Media.Imaging.Bitmap? bitmap = null;
        try
        {
            foreach (var file in await clipboard.TryGetFilesAsync() ?? [])
            {
                if (file.TryGetLocalPath() is { } path && IsImageFile(path))
                {
                    sourcePath = path;
                    break;
                }
            }

            if (sourcePath is null)
            {
                bitmap = await clipboard.TryGetBitmapAsync();
            }
        }
        catch (Exception)
        {
            return false; // a clipboard read failure is not an image paste; let the text path try
        }

        if (sourcePath is null && bitmap is null)
        {
            return false;
        }

        try
        {
            // A clipboard await can resume off the UI thread, and what follows edits the document.
            await Dispatcher.UIThread.InvokeAsync(() => InsertPastedImage(sourcePath, bitmap));
        }
        finally
        {
            bitmap?.Dispose(); // the surface re-loads it from the file it was just written to
        }

        return true;
    }

    /// <summary>
    /// Write the pasted image beside the document and insert the reference to it. A <b>saved</b> document gets
    /// the file in its own folder — <c>image-&lt;TIMESTAMP&gt;.&lt;ext&gt;</c>, deconflicted — referenced
    /// relatively, so moving the note and its folder together keeps the picture. An <b>unsaved</b> one has no
    /// folder yet, so the file is staged exactly as a picked image is, and the host's next save moves it home
    /// (<see cref="ImageStaging.Reconcile"/>).
    /// </summary>
    private void InsertPastedImage(string? sourcePath, Avalonia.Media.Imaging.Bitmap? bitmap)
    {
        // A decoded bitmap has no format left to preserve; Avalonia writes PNG, so that is what it is called.
        var name = ImageStaging.PastedImageName(DateTime.Now, sourcePath is null ? ".png" : Path.GetExtension(sourcePath));
        try
        {
            string destination;
            string url;
            if (DocumentPath is { } documentPath)
            {
                var folder = Path.GetDirectoryName(Path.GetFullPath(documentPath)) ?? ".";
                destination = ImageStaging.UniqueDestination(folder, name, File.Exists);
                url = ImageStaging.RelativeUrl(documentPath, destination);
            }
            else
            {
                destination = ImageStaging.StagedPath(name, Guid.NewGuid().ToString("N"));
                url = destination;
            }

            if (sourcePath is not null)
            {
                File.Copy(sourcePath, destination);
            }
            else
            {
                bitmap!.Save(destination);
            }

            InsertImageReference(ImageStaging.EncodeUrl(url), Path.GetFileNameWithoutExtension(name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ImagePasteFailed?.Invoke(this, ex.Message);
        }
    }

    /// <summary>Whether <paramref name="path"/> names one of the image types the editor inserts (PRD §8.3).</summary>
    private static bool IsImageFile(string path)
    {
        var extension = Path.GetExtension(path);
        foreach (var pattern in ImageStaging.ImagePatterns)
        {
            if (extension.Equals(pattern[1..], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
