using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Typage.Engine.Document;
using Typage.Engine.Markdown;

namespace Typage.Control.Controls;

/// <summary>
/// A link in the document, as the surface acts on it: its whole source span (<c>[text](url)</c>, markers and
/// all), the text the reader sees, and where it points. An <see cref="IsAutolink"/> link is one written bare
/// or in angle brackets (its text <i>is</i> its URL); an <see cref="IsImage"/> one is an <c>![alt](url)</c>
/// embed, which carries a URL worth copying and opening even though it isn't prose.
/// </summary>
public readonly record struct MarkdownLink(int Start, int End, string Text, string Url, bool IsImage, bool IsAutolink);

/// <summary>
/// What a reader expects of a link in an editor: hover it to see where it goes, ⌘/Ctrl-click it to follow,
/// right-click it to copy or edit it. The document is still text under all of it — a plain click puts the
/// caret in the link so it can be typed in, which is why following one takes a modifier (the same bargain
/// VS Code strikes).
/// </summary>
public partial class MarkdownEditor
{
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private MarkdownLink? _hoverLink;   // the link under the pointer, whose URL the tooltip is showing
    private MarkdownLink? _contextLink; // the link the right-click that opened the context menu landed on

    /// <summary>Raised when the reader asks to edit an existing link (context menu ▸ Edit Link…) — the host owns
    /// the dialog, so the surface only says that the caret's link should be edited.</summary>
    public event EventHandler? EditLinkRequested;

    /// <summary>Raised when the reader asks to insert a link (context menu ▸ Link…), for the host's dialog.</summary>
    public event EventHandler? InsertLinkRequested;

    /// <summary>The link the caret is inside, or null — the precondition of Edit Link…</summary>
    public MarkdownLink? LinkAtCaret() => LinkAt(Buffer.Selection.Caret);

    /// <summary>Whether the caret is inside a link, so a host can enable its Edit Link… command.</summary>
    public bool IsCaretInLink => LinkAtCaret() is not null;

    /// <summary>The link whose source span covers <paramref name="offset"/>, or null.</summary>
    public MarkdownLink? LinkAt(int offset)
    {
        var doc = Buffer.Document;
        if (offset < 0 || offset > doc.Length)
        {
            return null;
        }

        foreach (var link in LinksOnLine(doc.GetPosition(offset).Line))
        {
            if (offset >= link.Start && offset <= link.End)
            {
                return link;
            }
        }

        return null;
    }

    /// <summary>
    /// Replace <paramref name="link"/> with one carrying <paramref name="text"/> and <paramref name="url"/>
    /// (the Edit Link… dialog's Save). An image keeps its <c>!</c>; a bare autolink given a display text
    /// becomes an ordinary <c>[text](url)</c> link. An empty text falls back to the URL, so a link always has
    /// something to click.
    /// </summary>
    public void ReplaceLink(MarkdownLink link, string text, string url)
    {
        var display = text.Length > 0 ? text : url;
        var replacement = $"{(link.IsImage ? "![" : "[")}{display}]({url})";
        Buffer.ReplaceRange(link.Start, link.End - link.Start, replacement);
        Buffer.Selection = Selection.At(link.Start + replacement.Length);
        Focus();
    }

    /// <summary>
    /// Hand <paramref name="link"/> to the OS's default handler (⌘/Ctrl-click, context menu ▸ Open Link),
    /// returning false when it isn't a destination we follow. A document is untrusted text, so only an
    /// absolute web or mail address is ever launched — see <see cref="TryGetLaunchUri"/>.
    /// </summary>
    public bool OpenLink(MarkdownLink link) => TryGetLaunchUri(link.Url, out var uri) && Launch(uri);

    /// <summary>Hand <paramref name="uri"/> to the OS's default handler (the browser, the mail client), false
    /// when there is none to hand it to. Virtual so a test can watch a link being followed without a browser
    /// opening on whoever is running the suite.</summary>
    protected virtual bool Launch(Uri uri)
    {
        if (TopLevel.GetTopLevel(this)?.Launcher is not { } launcher)
        {
            return false;
        }

        _ = launcher.LaunchUriAsync(uri);
        return true;
    }

    /// <summary>Put <paramref name="link"/>'s destination on the clipboard (context menu ▸ Copy Link).</summary>
    public void CopyLinkUrl(MarkdownLink link)
    {
        if (link.Url.Length > 0 && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            _ = SetClipboardSafe(clipboard, link.Url);
        }
    }

    /// <summary>
    /// The URI a link is launched as, or false when it isn't one to launch.
    ///
    /// <para>Only an <b>absolute http/https/mailto</b> address qualifies. Everything a Markdown destination can
    /// otherwise hold is deliberately refused: a relative path (Typage edits one file — following it isn't
    /// ours to do), and the schemes that make "opening a link" mean "run something" — <c>file:</c>,
    /// <c>javascript:</c>, <c>data:</c>. The document is text someone else may have written, and a click on it
    /// must not be able to reach past the browser.</para>
    /// </summary>
    internal static bool TryGetLaunchUri(string url, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeMailto)
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>The platform's "follow this link" modifier: ⌘ on macOS (Ctrl-click there is a right-click),
    /// Ctrl elsewhere.</summary>
    private static bool IsCommandModifier(KeyModifiers modifiers) =>
        OperatingSystem.IsMacOS() ? modifiers.HasFlag(KeyModifiers.Meta) : modifiers.HasFlag(KeyModifiers.Control);

    /// <summary>Every link on <paramref name="line"/>: inline links and images, plus autolinks (bare or
    /// bracketed URLs, whose text is their destination).</summary>
    private List<MarkdownLink> LinksOnLine(int line)
    {
        var doc = Buffer.Document;
        var links = new List<MarkdownLink>();
        foreach (var span in LinkAndImageSpans(line))
        {
            links.Add(new MarkdownLink(
                span.FullStart,
                span.FullEnd,
                doc.GetText(span.TextStart, span.TextEnd - span.TextStart),
                span.Url,
                span.IsImage,
                IsAutolink: false));
        }

        var toks = _model.LineTokens(line);
        foreach (var t in toks)
        {
            if (t.Type != TokenType.Autolink || t.Role != TokenRole.Content)
            {
                continue;
            }

            // A bracketed autolink (<https://…>) carries its angle brackets as markers grouped with it; a bare
            // URL is emitted on its own (node 0, no markers), so its span is the token's.
            var (start, end) = (t.Start, t.End);
            if (t.Node != 0)
            {
                foreach (var m in toks)
                {
                    if (m.Node == t.Node && m.Role == TokenRole.Marker)
                    {
                        start = Math.Min(start, m.Start);
                        end = Math.Max(end, m.End);
                    }
                }
            }

            var url = t.Meta ?? doc.GetText(t.Start, t.End - t.Start);
            links.Add(new MarkdownLink(start, end, url, url, IsImage: false, IsAutolink: true));
        }

        return links;
    }

    /// <summary>
    /// The link the pointer at <paramref name="point"/> is actually over — not merely the link nearest the
    /// offset the click resolves to. A caret hit-test answers with the closest character even out in the empty
    /// space past the end of a line; a link's <i>glyphs</i> are what a reader means when they hover or click
    /// one, so the point is also checked against the laid-out spans of the link's own columns.
    /// </summary>
    private MarkdownLink? LinkAtPoint(Point point)
    {
        EnsureLayout();
        var contentY = point.Y - PaddingTop + _offset.Y;
        var contentX = point.X - ContentLeft() + _offset.X;
        var block = BlockAtY(contentY);
        var row = (int)((contentY - RowTop(block)) / RowHeight(block));
        if (row < 0 || row >= _rowCounts[block])
        {
            return null; // in the spacing between blocks, or past the last row
        }

        if (LinkAt(OffsetAt(block, row, contentX)) is not { } link)
        {
            return null;
        }

        var visual = BlockVisual(block);
        var blockStart = BlockStartOffset(block);
        var (from, to) = (link.Start - blockStart, link.End - blockStart);
        if (from < 0 || to > visual.RawLength)
        {
            return null;
        }

        foreach (var (spanRow, x1, x2) in visual.Spans(from, to))
        {
            if (spanRow == row && contentX >= x1 && contentX <= x2)
            {
                return link;
            }
        }

        return null;
    }

    /// <summary>
    /// Track the link under the pointer: its URL goes in a tooltip (a reader can't see a destination that the
    /// live-preview view has concealed, and asking for it shouldn't mean editing the line), and while the
    /// follow modifier is held the cursor becomes a hand — the same "this click will open it" promise a
    /// browser makes, so the modifier is discoverable instead of secret.
    /// </summary>
    private void UpdateLinkHover(Point point, KeyModifiers modifiers)
    {
        var link = LinkAtPoint(point);
        if (link != _hoverLink)
        {
            _hoverLink = link;
            if (link is { Url.Length: > 0 } hit)
            {
                ToolTip.SetTip(this, hit.Url);
                ToolTip.SetIsOpen(this, true);
            }
            else
            {
                ToolTip.SetIsOpen(this, false);
                ToolTip.SetTip(this, null);
            }
        }

        Cursor = _hoverLink is { } l && IsCommandModifier(modifiers) && TryGetLaunchUri(l.Url, out _) ? HandCursor : null;
    }

    /// <summary>Drop the hover state when the pointer leaves the surface (the tooltip goes with it).</summary>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        if (_hoverLink is not null)
        {
            _hoverLink = null;
            ToolTip.SetIsOpen(this, false);
            ToolTip.SetTip(this, null);
        }

        Cursor = null;
        base.OnPointerExited(e);
    }

    /// <summary>The link under the pointer, if any; for tests (the hover tooltip and ⌘-click read it).</summary>
    internal MarkdownLink? LinkAtPointForTest(Point point) => LinkAtPoint(point);

    /// <summary>Whether <paramref name="url"/> is one the surface would hand to the OS; for tests.</summary>
    internal static bool TryGetLaunchUriForTest(string url) => TryGetLaunchUri(url, out _);

    /// <summary>A control-space point inside the glyph at document <paramref name="offset"/>, as a pointer
    /// hovering that character would be; for tests.</summary>
    internal Point PointAtColumnForTest(int offset)
    {
        EnsureLayout();
        var (block, blockStart, visual, _) = ResolveCaret(offset);
        var col = offset - blockStart;
        var row = visual.RowOf(col);
        return new Point(
            visual.ColumnToX(col) + ContentLeft() - _offset.X + 1,
            RowTop(block) + PaddingTop - _offset.Y + ((row + 0.5) * RowHeight(block)));
    }

    /// <summary>The link a right-click landed on, which the context menu's link items act on; for tests.</summary>
    internal MarkdownLink? ContextLinkForTest() => _contextLink;

    /// <summary>Drive the hover as a pointer move would; for tests.</summary>
    internal void HoverForTest(Point point, KeyModifiers modifiers = KeyModifiers.None) => UpdateLinkHover(point, modifiers);

    /// <summary>Raise the host's insert-link dialog (the context menu's Link… item).</summary>
    private void RequestInsertLink() => InsertLinkRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Move the caret into <paramref name="link"/> and ask the host to open its edit dialog.</summary>
    private void RequestEditLink(MarkdownLink link)
    {
        MoveCaret(Math.Min(link.Start + 1, link.End), extend: false);
        EditLinkRequested?.Invoke(this, EventArgs.Empty);
    }
}
