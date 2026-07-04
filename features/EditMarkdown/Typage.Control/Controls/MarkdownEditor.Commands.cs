using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using Typage.Control.Rendering;
using Typage.Engine.Document;
using Typage.Engine.Editing;
using Typage.Engine.Markdown;


namespace Typage.Control.Controls;

public partial class MarkdownEditor
{
    /// <summary>Set the caret's line to an ATX heading of <paramref name="level"/> (replacing any existing one).</summary>
    public void ApplyHeading(int level)
    {
        level = Math.Clamp(level, 1, 6);
        var doc = _buffer.Document;
        var line = doc.GetPosition(_buffer.Selection.Caret).Line;
        var lineStart = doc.GetLineStart(line);
        var text = doc.GetLineText(line).TrimEnd('\r');
        var trimmed = text.TrimStart('#', ' ');
        var replacement = new string('#', level) + " " + trimmed;
        _buffer.ReplaceRange(lineStart, text.Length, replacement);
    }

    /// <summary>
    /// Toggle an inline emphasis format (bold/italic/strikethrough/highlight/inline-code) over the
    /// selection — or, with no selection, the word the caret is inside (so ⌘B on a bare caret in a word
    /// bolds the whole word). Applying the format a <b>second</b> time to text that is already wrapped in
    /// exactly that format <b>removes</b> it (unwraps the run it expanded to). If the caret isn't touching a
    /// word, an empty marker pair is inserted to type into. Either way the affected content is left selected.
    /// </summary>
    /// <param name="left">Opening marker, e.g. <c>**</c>.</param>
    /// <param name="right">Closing marker, e.g. <c>**</c>.</param>
    /// <param name="contentType">The engine token type of this format's content (e.g. <see cref="TokenType.Strong"/>),
    /// used to detect an existing run so a second press toggles it off.</param>
    public void ToggleEmphasis(string left, string right, TokenType contentType)
    {
        var sel = _buffer.Selection;
        int start, end;
        if (!sel.IsEmpty)
        {
            (start, end) = (sel.Start, sel.End);
        }
        else if (WordAround(sel.Caret) is { } word)
        {
            (start, end) = (word.Start, word.End);
        }
        else
        {
            _buffer.ReplaceRange(sel.Caret, 0, left + right);
            _buffer.Selection = Selection.At(sel.Caret + left.Length);
            Focus();
            return;
        }

        // Already wrapped in exactly this format? Strip the markers (one undo step) and re-select the content.
        if (FindEmphasisRun(start, end, contentType) is { } run)
        {
            var inner = _buffer.Document.GetText(run.InnerStart, run.InnerEnd - run.InnerStart);
            _buffer.ReplaceRange(run.OuterStart, run.OuterEnd - run.OuterStart, inner);
            _buffer.Selection = new Selection(run.OuterStart, run.OuterStart + inner.Length);
            Focus();
            return;
        }

        var content = _buffer.Document.GetText(start, end - start);
        _buffer.ReplaceRange(start, end - start, left + content + right);
        _buffer.Selection = new Selection(start + left.Length, start + left.Length + content.Length);
        Focus();
    }

    /// <summary>
    /// If <c>[start, end)</c> is exactly the content of an existing inline construct of
    /// <paramref name="contentType"/> (e.g. the <c>word</c> inside <c>**word**</c>), return the construct's
    /// outer span (markers included) and its inner content span so the caller can unwrap it; otherwise null.
    /// Detection is token-based — the markers grouped with the content node (<see cref="Token.Node"/>) must
    /// flank it — so a second ⌘B on a bolded word toggles bold off without disturbing surrounding emphasis.
    /// </summary>
    private (int OuterStart, int OuterEnd, int InnerStart, int InnerEnd)? FindEmphasisRun(int start, int end, TokenType contentType)
    {
        var doc = _buffer.Document;
        if (start < 0 || end > doc.Length || start >= end)
        {
            return null;
        }

        var line = doc.GetPosition(start).Line;
        if (doc.GetPosition(end).Line != line)
        {
            return null; // emphasis never crosses a line; a multi-line selection can't be a single run
        }

        var toks = _model.LineTokens(line);
        foreach (var t in toks)
        {
            if (t.Type != contentType || t.Role != TokenRole.Content || t.Start != start || t.End != end)
            {
                continue;
            }

            var outerStart = t.Start;
            var outerEnd = t.End;
            foreach (var m in toks)
            {
                if (m.Node == t.Node && m.Role == TokenRole.Marker)
                {
                    outerStart = Math.Min(outerStart, m.Start);
                    outerEnd = Math.Max(outerEnd, m.End);
                }
            }

            if (outerStart < t.Start && outerEnd > t.End)
            {
                return (outerStart, outerEnd, t.Start, t.End);
            }
        }

        return null;
    }

    /// <summary>
    /// Toggle the bold (<c>**</c>) or italic (<c>*</c>) component of the target's emphasis, treating bold and
    /// italic as <b>independent layers</b> over the same <c>*</c>/<c>_</c> delimiter (CommonMark nests them as
    /// a single 1–3 long run). So italic on a bold word gives bold-italic (<c>***w***</c>); italic again drops
    /// just the italic back to bold (<c>**w**</c>); bold on bold-italic leaves italic (<c>*w*</c>). With no
    /// emphasis present it simply wraps; with no word/selection it inserts an empty delimiter pair.
    /// </summary>
    private void ToggleStarEmphasis(bool bold)
    {
        var sel = _buffer.Selection;
        int start, end;
        if (!sel.IsEmpty)
        {
            (start, end) = (sel.Start, sel.End);
        }
        else if (WordAround(sel.Caret) is { } word)
        {
            (start, end) = (word.Start, word.End);
        }
        else
        {
            var d = bold ? "**" : "*";
            _buffer.ReplaceRange(sel.Caret, 0, d + d);
            _buffer.Selection = Selection.At(sel.Caret + d.Length);
            Focus();
            return;
        }

        // Read the emphasis run already wrapping the target (its delimiter run is 1=italic, 2=bold, 3=both),
        // flip the requested layer, and rewrite the whole construct with the new run length (0 = unwrap).
        var existing = FindStarEmphasis(start, end);
        var ch = existing?.Delim ?? '*';
        var level = existing?.RunLength ?? 0;
        var hasBold = level >= 2;
        var hasItalic = level is 1 or 3;
        if (bold)
        {
            hasBold = !hasBold;
        }
        else
        {
            hasItalic = !hasItalic;
        }

        var newLevel = (hasBold ? 2 : 0) + (hasItalic ? 1 : 0);
        var content = _buffer.Document.GetText(start, end - start);
        var delim = new string(ch, newLevel);
        var outerStart = existing?.OuterStart ?? start;
        var outerEnd = existing?.OuterEnd ?? end;

        _buffer.ReplaceRange(outerStart, outerEnd - outerStart, delim + content + delim);
        _buffer.Selection = new Selection(outerStart + delim.Length, outerStart + delim.Length + content.Length);
        Focus();
    }

    /// <summary>
    /// The <c>*</c>/<c>_</c> emphasis construct (bold, italic, or bold-italic) whose content is exactly
    /// <c>[start, end)</c>: its outer span, its delimiter run length (1–3), and the delimiter char — or null.
    /// </summary>
    private (int OuterStart, int OuterEnd, int RunLength, char Delim)? FindStarEmphasis(int start, int end)
    {
        var doc = _buffer.Document;
        if (start < 0 || end > doc.Length || start >= end)
        {
            return null;
        }

        var line = doc.GetPosition(start).Line;
        if (doc.GetPosition(end).Line != line)
        {
            return null;
        }

        var toks = _model.LineTokens(line);
        foreach (var t in toks)
        {
            if (t.Role != TokenRole.Content || t.Start != start || t.End != end ||
                t.Type is not (TokenType.Strong or TokenType.Emphasis or TokenType.StrongEmphasis))
            {
                continue;
            }

            var outerStart = t.Start;
            var outerEnd = t.End;
            Token? open = null;
            foreach (var m in toks)
            {
                if (m.Node != t.Node || m.Role != TokenRole.Marker)
                {
                    continue;
                }

                outerStart = Math.Min(outerStart, m.Start);
                outerEnd = Math.Max(outerEnd, m.End);
                if (open is null || m.Start < open.Value.Start)
                {
                    open = m;
                }
            }

            if (open is { } o && outerStart < t.Start && outerEnd > t.End)
            {
                return (outerStart, outerEnd, o.Length, doc[o.Start]);
            }
        }

        return null;
    }

    /// <summary>
    /// The span of the word the caret at <paramref name="caret"/> is inside — the maximal run of
    /// word characters (letters, digits, underscore) touching the caret on either side — or null when the
    /// caret sits on whitespace/punctuation and isn't adjacent to a word. Never crosses a line break.
    /// </summary>
    private (int Start, int End)? WordAround(int caret)
    {
        var doc = _buffer.Document;
        bool IsWordChar(int i) => i >= 0 && i < doc.Length && CharClass(doc[i]) == 0;

        if (!IsWordChar(caret - 1) && !IsWordChar(caret))
        {
            return null;
        }

        var start = caret;
        while (start > 0 && CharClass(doc[start - 1]) == 0)
        {
            start--;
        }

        var end = caret;
        while (end < doc.Length && CharClass(doc[end]) == 0)
        {
            end++;
        }

        return (start, end);
    }

    /// <summary>
    /// Insert a link <c>[text](url)</c> over the current target (selection or word under the caret), reducing
    /// any inner link/image the target touches to its visible text so the new link wins (no nesting). When
    /// <paramref name="text"/> is empty it falls back to the URL, then to the existing target text. The caller
    /// is responsible for encoding the URL (e.g. spaces → <c>%20</c>); the caret lands just after the link.
    /// </summary>
    public void InsertLinkWithText(string text, string url)
    {
        var (start, end, existing) = LinkTarget();
        var display = text.Length > 0 ? text : url.Length > 0 ? url : existing;
        var replacement = $"[{display}]({url})";
        _buffer.ReplaceRange(start, end - start, replacement);
        _buffer.Selection = Selection.At(start + replacement.Length);
        Focus();
    }

    private void InsertReference(bool image)
    {
        var (start, end, text) = LinkTarget();
        var lead = image ? "![" : "[";
        var replacement = $"{lead}{text}]()";
        _buffer.ReplaceRange(start, end - start, replacement);

        // Caret in the URL slot when there's link text to keep; otherwise in the (empty) text slot to type into.
        var caret = text.Length > 0
            ? start + replacement.Length - 1
            : start + lead.Length;
        _buffer.Selection = Selection.At(caret);
        Focus();
    }

    /// <summary>
    /// The span and visible text to turn into a link/image: the selection, or the word under a bare caret.
    /// The span is grown to swallow any link/image construct it touches, and that construct's syntax is
    /// stripped to its text — so wrapping text that already contains a link yields one outer link, not a
    /// nested one (the outer format takes precedence; the inner is removed).
    /// </summary>
    private (int Start, int End, string Text) LinkTarget()
    {
        var sel = _buffer.Selection;
        var doc = _buffer.Document;
        int start, end;
        if (!sel.IsEmpty)
        {
            (start, end) = (sel.Start, sel.End);
        }
        else if (WordAround(sel.Caret) is { } w)
        {
            (start, end) = (w.Start, w.End);
        }
        else
        {
            return (sel.Caret, sel.Caret, string.Empty);
        }

        if (doc.GetPosition(start).Line != doc.GetPosition(Math.Max(start, end - 1)).Line)
        {
            return (start, end, doc.GetText(start, end - start)); // multi-line selection: take it verbatim
        }

        var spans = LinkAndImageSpans(doc.GetPosition(start).Line);

        // Grow the target to fully cover any construct it overlaps (repeat until stable: absorbing one
        // construct can bring its neighbour into range).
        bool grew;
        do
        {
            grew = false;
            foreach (var sp in spans)
            {
                if (sp.FullStart < end && sp.FullEnd > start && (sp.FullStart < start || sp.FullEnd > end))
                {
                    start = Math.Min(start, sp.FullStart);
                    end = Math.Max(end, sp.FullEnd);
                    grew = true;
                }
            }
        }
        while (grew);

        return (start, end, StripSpans(start, end, spans));
    }

    /// <summary>The source of <c>[start, end)</c> with each fully-enclosed link/image construct replaced by
    /// just its visible text (link text / image alt) — emphasis and other formatting are left intact.</summary>
    private string StripSpans(int start, int end, List<InlineLinkSpan> spans)
    {
        var doc = _buffer.Document;
        var result = string.Empty;
        var p = start;
        foreach (var s in spans.Where(s => s.FullStart >= start && s.FullEnd <= end).OrderBy(s => s.FullStart))
        {
            if (s.FullStart > p)
            {
                result += doc.GetText(p, s.FullStart - p);
            }

            if (s.TextEnd > s.TextStart)
            {
                result += doc.GetText(s.TextStart, s.TextEnd - s.TextStart);
            }

            p = s.FullEnd;
        }

        if (end > p)
        {
            result += doc.GetText(p, end - p);
        }

        return result;
    }

    /// <summary>Every inline link <c>[text](url)</c> and image <c>![alt](url)</c> on <paramref name="line"/>.</summary>
    private List<InlineLinkSpan> LinkAndImageSpans(int line)
    {
        var spans = new List<InlineLinkSpan>();
        var toks = _model.LineTokens(line);
        foreach (var u in toks)
        {
            if (u.Type != TokenType.LinkUrl && u.Type != TokenType.ImageUrl)
            {
                continue;
            }

            var fullStart = int.MaxValue;
            var fullEnd = -1;
            var openStart = int.MaxValue;
            var openEnd = -1;
            var midStart = -1;
            var altStart = -1;
            var altEnd = -1;
            foreach (var m in toks)
            {
                if (m.Node != u.Node)
                {
                    continue;
                }

                fullStart = Math.Min(fullStart, m.Start);
                fullEnd = Math.Max(fullEnd, m.End);
                if (m.Role == TokenRole.Marker)
                {
                    if (m.Start < openStart)
                    {
                        (openStart, openEnd) = (m.Start, m.End);
                    }

                    if (m.End == u.Start)
                    {
                        midStart = m.Start;
                    }
                }

                if (m.Type == TokenType.ImageAlt)
                {
                    (altStart, altEnd) = (m.Start, m.End);
                }
            }

            // Image alt is its own content token; link text is everything between "[" and "](".
            var (textStart, textEnd) = altStart >= 0 ? (altStart, altEnd) : (openEnd, midStart);
            if (fullEnd > fullStart && textStart >= 0 && textEnd >= textStart)
            {
                spans.Add(new InlineLinkSpan(fullStart, fullEnd, textStart, textEnd));
            }
        }

        return spans;
    }

    /// <summary>Strip any heading marker from the caret's line, turning it back into a paragraph.</summary>
    public void SetParagraph()
    {
        var doc = _buffer.Document;
        var line = doc.GetPosition(_buffer.Selection.Caret).Line;
        var lineStart = doc.GetLineStart(line);
        var text = doc.GetLineText(line).TrimEnd('\r');
        var trimmed = text.TrimStart('#', ' ');
        if (trimmed.Length != text.Length)
        {
            _buffer.ReplaceRange(lineStart, text.Length, trimmed);
        }
    }

    /// <summary>
    /// Add a task checkbox to the caret's line, or toggle an existing one (⌘L / Ctrl+L). If the line
    /// already carries a <c>[ ]</c>/<c>[x]</c>, flip its state (caret stays put); if it is a plain list
    /// item, insert <c>[ ] </c> after its marker; otherwise turn the line into an unordered task item
    /// (<c>- [ ] </c>, preserving any indentation) — one shortcut both creates and toggles checkboxes.
    /// </summary>
    public void ToggleTask()
    {
        var doc = _buffer.Document;
        var line = doc.GetPosition(_buffer.Selection.Caret).Line;
        var prefix = LinePrefix.Parse(doc.GetLineText(line));

        if (prefix.HasTask)
        {
            ToggleTaskOnLine(line);
        }
        else
        {
            // Insert "[ ] " after an existing list marker, or a whole "- [ ] " item after the indentation.
            var insertAt = doc.GetLineStart(line) + (prefix.HasList ? prefix.Length : prefix.Indent);
            var inserted = prefix.HasList ? "[ ] " : "- [ ] ";
            var caret = _buffer.Selection.Caret;
            _buffer.ReplaceRange(insertAt, 0, inserted);
            _buffer.Selection = Selection.At(caret >= insertAt ? caret + inserted.Length : caret);
        }

        Focus();
    }

    /// <summary>
    /// Insert an image reference pointing at <paramref name="url"/> (a resolved relative/staged path). The
    /// selection or word under the caret becomes the alt text; if there is none, <paramref name="altFallback"/>
    /// (typically the image's file name) is used. The caret lands just after the inserted reference.
    /// </summary>
    public void InsertImageReference(string url, string? altFallback = null)
    {
        var (start, end, text) = LinkTarget();
        var alt = text.Length > 0 ? text : altFallback ?? string.Empty;
        var replacement = $"![{alt}]({url})";
        _buffer.ReplaceRange(start, end - start, replacement);
        _buffer.Selection = Selection.At(start + replacement.Length);
        Focus();
    }

    /// <summary>Wrap the selection in a fenced code block (<c>```</c>…<c>```</c>), or insert empty fences.</summary>
    public void InsertCodeBlock()
    {
        var sel = _buffer.Selection;
        var inner = sel.IsEmpty ? string.Empty : _buffer.Document.GetText(sel.Start, sel.Length);
        _buffer.ReplaceRange(sel.Start, sel.Length, "```\n" + inner + "\n```");
        _buffer.Selection = sel.IsEmpty
            ? Selection.At(sel.Start + 4) // the empty content line between the fences
            : new Selection(sel.Start + 4, sel.Start + 4 + inner.Length);
        Focus();
    }

    /// <summary>Insert a horizontal rule (<c>---</c>) on its own line.</summary>
    public void InsertHorizontalRule()
    {
        var doc = _buffer.Document;
        var line = doc.GetPosition(_buffer.Selection.Caret).Line;
        if (IsBlankLine(line))
        {
            var start = doc.GetLineStart(line);
            _buffer.ReplaceRange(start, doc.GetLineEnd(line) - start, "---");
            _buffer.Selection = Selection.At(start + 3);
        }
        else
        {
            var end = doc.GetLineEnd(line);
            _buffer.ReplaceRange(end, 0, "\n\n---\n");
            _buffer.Selection = Selection.At(end + 5); // just after the inserted "---"
        }

        Focus();
    }

    /// <summary>Toggle a blockquote (<c>&gt; </c>) prefix on the caret line / selected lines.</summary>
    public void ToggleBlockquote()
    {
        var (startLine, endLine) = SelectedLineRange();
        var allOn = AllNonBlank(startLine, endLine, p => p.HasQuote);
        EditLineRange(startLine, endLine, (line, text) =>
        {
            var p = LinePrefix.Parse(text);
            if (allOn)
            {
                return p.HasQuote ? text[..p.Indent] + text[(p.Indent + p.Quote.Length)..] : text;
            }

            return IsBlankLine(line) ? text : text[..p.Indent] + "> " + text[p.Indent..];
        });
    }

    /// <summary>Toggle an unordered-list (<c>- </c>) marker on the caret line / selected lines.</summary>
    public void ToggleBulletList()
    {
        var (startLine, endLine) = SelectedLineRange();
        var allOn = AllNonBlank(startLine, endLine, p => p.List == ListKind.Unordered && !p.HasTask);
        EditLineRange(startLine, endLine, (line, text) =>
        {
            var p = LinePrefix.Parse(text);
            if (allOn)
            {
                return p.HasList ? text[..p.MarkerStart] + text[p.Length..] : text;
            }

            if (IsBlankLine(line))
            {
                return text;
            }

            var body = StripListMarker(text, p);
            var bp = LinePrefix.Parse(body);
            return body[..bp.Indent] + "- " + body[bp.Indent..];
        });
    }

    /// <summary>Toggle an ordered-list (<c>1. </c>) marker on the caret line / selected lines (renumbering the run).</summary>
    public void ToggleNumberedList()
    {
        var (startLine, endLine) = SelectedLineRange();
        var allOn = AllNonBlank(startLine, endLine, p => p.List == ListKind.Ordered);
        var n = 0;
        EditLineRange(startLine, endLine, (line, text) =>
        {
            var p = LinePrefix.Parse(text);
            if (allOn)
            {
                return p.HasList ? text[..p.MarkerStart] + text[p.Length..] : text;
            }

            if (IsBlankLine(line))
            {
                return text;
            }

            n++;
            var body = StripListMarker(text, p);
            var bp = LinePrefix.Parse(body);
            return body[..bp.Indent] + $"{n}. " + body[bp.Indent..];
        });
    }

    /// <summary>Duplicate the caret line / selected lines, placing the copy below and moving onto it.</summary>
    public void DuplicateLine()
    {
        var doc = _buffer.Document;
        var (startLine, endLine) = SelectedLineRange();
        var start = doc.GetLineStart(startLine);
        var end = doc.GetLineEnd(endLine);
        var block = doc.GetText(start, end - start);
        var sel = _buffer.Selection;
        _buffer.ReplaceRange(end, 0, "\n" + block);
        var delta = end - start + 1; // the block plus its joining newline
        _buffer.Selection = new Selection(sel.Anchor + delta, sel.Caret + delta);
        Focus();
    }

    /// <summary>Delete the caret line / selected lines entirely (including a line break).</summary>
    public void DeleteLine()
    {
        var doc = _buffer.Document;
        var (startLine, endLine) = SelectedLineRange();
        var start = doc.GetLineStart(startLine);
        int end;
        if (endLine + 1 < doc.LineCount)
        {
            end = doc.GetLineStart(endLine + 1); // through the trailing newline
        }
        else
        {
            end = doc.GetLineEnd(endLine);                  // last line — no newline after it
            if (startLine > 0)
            {
                start = doc.GetLineEnd(startLine - 1);      // so eat the newline before it instead
            }
        }

        _buffer.ReplaceRange(start, end - start, string.Empty);
        _buffer.Selection = Selection.At(Math.Min(start, _buffer.Document.Length));
        Focus();
    }

    /// <summary>Select the word the caret is inside (Format ▸ Select Word).</summary>
    public void SelectCurrentWord()
    {
        var caret = _buffer.Selection.Caret;
        SelectWordAt(_buffer.Document.GetPosition(caret).Line, caret);
    }

    /// <summary>The inclusive line range the current selection spans (a selection ending at a line start
    /// doesn't include that last line).</summary>
    private (int Start, int End) SelectedLineRange()
    {
        var doc = _buffer.Document;
        var sel = _buffer.Selection;
        var startLine = doc.GetPosition(sel.Start).Line;
        var endLine = doc.GetPosition(sel.End).Line;
        if (endLine > startLine && doc.GetPosition(sel.End).Column == 0)
        {
            endLine--;
        }

        return (startLine, endLine);
    }

    /// <summary>Whether every non-blank line in <c>[startLine, endLine]</c> satisfies <paramref name="predicate"/>.</summary>
    private bool AllNonBlank(int startLine, int endLine, Func<LinePrefix, bool> predicate)
    {
        var doc = _buffer.Document;
        for (var l = startLine; l <= endLine; l++)
        {
            if (IsBlankLine(l))
            {
                continue;
            }

            if (!predicate(LinePrefix.Parse(doc.GetLineText(l))))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Rewrite lines <c>[startLine, endLine]</c> via <paramref name="transform"/> as one undoable edit,
    /// leaving the rewritten region selected (so a toggle can be repeated). Line endings are preserved.</summary>
    private void EditLineRange(int startLine, int endLine, Func<int, string, string> transform)
    {
        var doc = _buffer.Document;
        var regionStart = doc.GetLineStart(startLine);
        var regionEnd = doc.GetLineEnd(endLine);
        var lines = new List<string>(endLine - startLine + 1);
        for (var l = startLine; l <= endLine; l++)
        {
            lines.Add(transform(l, doc.GetLineText(l)));
        }

        var joined = string.Join("\n", lines);
        _buffer.ReplaceRange(regionStart, regionEnd - regionStart, joined);
        _buffer.Selection = startLine == endLine
            ? Selection.At(regionStart + joined.Length)
            : new Selection(regionStart, regionStart + joined.Length);
        Focus();
    }

}
