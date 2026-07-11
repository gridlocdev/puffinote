using System.Globalization;
using Typage.Engine.Document;
using Typage.Engine.Editing;

namespace Typage.Control.Controls;

public partial class MarkdownEditor
{
    /// <summary>
    /// Move the selected line(s) up (<paramref name="dir"/> = -1) or down (+1), swapping with the adjacent
    /// <b>sibling subtree</b>, as a single undoable edit (Alt+Shift+Up/Down). The moved unit includes any
    /// deeper-indented descendants of the last selected line — so a list item or task carries its sub-items
    /// with it — and it swaps with the neighbouring item at the same indent (skipping that item's own
    /// descendants). The selection travels with the moved block; a no-op at a boundary is silently ignored.
    /// </summary>
    private void MoveLines(int dir)
    {
        var doc = Buffer.Document;
        var sel = Buffer.Selection;
        var startLine = doc.GetPosition(sel.Start).Line;
        var endLine = doc.GetPosition(sel.End).Line;

        // A selection ending exactly at a line start doesn't really include that last line.
        if (endLine > startLine && doc.GetPosition(sel.End).Column == 0)
        {
            endLine--;
        }

        // Extend the moved unit over its descendants, but carry *only* deeper-indented nested list items of
        // the **same kind** (bulleted / numbered / checkbox) as the line the move was initiated on. Plain
        // continuation text and nested items of a different kind don't apply to this list item, so they are
        // left behind rather than dragged along — the extension stops at the first deeper line that isn't a
        // same-kind item. (This works at any nesting level: moving a parent carries its same-kind sub-items
        // but not, say, the continuation text under one of those sub-items.)
        var baseIndent = LineIndent(startLine);
        var startKind = ListItemKindOf(startLine);
        while (endLine + 1 < doc.LineCount && !IsBlankLine(endLine + 1) && LineIndent(endLine + 1) > baseIndent
               && CarriesAsDescendant(endLine + 1, startKind))
        {
            endLine++;
        }

        // Anything deeper that we didn't carry simply stays put; the moved unit hops over the adjacent block
        // (sibling subtree or a run of left-behind deeper lines), reordering only within the contiguous region.
        // `lineDelta` is how many lines the moved unit shifts (down: the partner's line count; up: negative).
        int regionStartLine, regionEndLine, lineDelta;
        var newOrder = new List<string>();
        if (startKind == ItemKind.None)
        {
            // The moved line isn't a list item (plain text, a heading, a blockquote, …): swap it with the
            // adjacent line, exactly like moving any other line of text. The subtree / sibling logic below is
            // only for list items — applying it here would make a plain line skip over an indented neighbour
            // (e.g. a list item's continuation text) instead of swapping with it. A collapsed blank separator
            // is skipped so two paragraphs trade places as whole units, carrying the blank between them —
            // swapping with the (invisible) blank itself would leave a single newline that flows the two
            // paragraphs back together.
            if (dir < 0)
            {
                var p = startLine - 1;
                while (p >= 0 && IsBlankLine(p))
                {
                    p--;
                }

                if (p < 0)
                {
                    return; // nothing but blank lines above
                }

                regionStartLine = p;
                regionEndLine = endLine;
                newOrder.AddRange(LinesOf(doc, startLine, endLine));   // the moved line(s)...
                newOrder.AddRange(LinesOf(doc, p + 1, startLine - 1)); // ...the blank separator(s), if any...
                newOrder.Add(LineContent(doc, p));                     // ...then the partner line
                lineDelta = p - startLine;
            }
            else
            {
                var p = endLine + 1;
                while (p < doc.LineCount && IsBlankLine(p))
                {
                    p++;
                }

                if (p >= doc.LineCount)
                {
                    return; // nothing but blank lines below
                }

                regionStartLine = startLine;
                regionEndLine = p;
                newOrder.Add(LineContent(doc, p));                     // the partner line...
                newOrder.AddRange(LinesOf(doc, endLine + 1, p - 1));   // ...the blank separator(s), if any...
                newOrder.AddRange(LinesOf(doc, startLine, endLine));   // ...then the moved line(s)
                lineDelta = p - endLine;
            }
        }
        else if (dir < 0)
        {
            if (startLine == 0)
            {
                return; // already at the top
            }

            // Walk back over the previous sibling's descendants to its start.
            var p = startLine - 1;
            while (p > 0 && !IsBlankLine(p) && LineIndent(p) > baseIndent)
            {
                p--;
            }

            if (!IsBlankLine(p) && LineIndent(p) < baseIndent)
            {
                return; // reached a shallower parent — no previous sibling at this level
            }

            regionStartLine = p;
            regionEndLine = endLine;
            newOrder.AddRange(LinesOf(doc, startLine, endLine));    // the moved subtree first...
            newOrder.AddRange(LinesOf(doc, p, startLine - 1));      // ...then the previous sibling subtree
            lineDelta = p - startLine;                              // moved unit shifts up to where the sibling began
        }
        else
        {
            var s = endLine + 1;
            if (s >= doc.LineCount || (!IsBlankLine(s) && LineIndent(s) < baseIndent))
            {
                return; // at the bottom, or the next line is a shallower parent — no next sibling
            }

            // Extend over the next sibling's descendants.
            var swapEnd = s;
            while (swapEnd + 1 < doc.LineCount && !IsBlankLine(swapEnd + 1) && LineIndent(swapEnd + 1) > baseIndent)
            {
                swapEnd++;
            }

            regionStartLine = startLine;
            regionEndLine = swapEnd;
            newOrder.AddRange(LinesOf(doc, s, swapEnd));            // the next sibling subtree first...
            newOrder.AddRange(LinesOf(doc, startLine, endLine));   // ...then the moved subtree
            lineDelta = swapEnd - s + 1;                            // moved unit shifts down past the sibling subtree
        }

        // A move can shift the ordinals of *following* items too — e.g. a plain line moved into or out of a
        // numbered list changes where a run starts. Extend the rewrite down over the rest of this non-blank
        // run (only when it has ordered items) so those are renumbered in the same edit. Lines before the
        // region never change (nothing above them moved), so the run is only extended downward.
        var runEnd = regionEndLine;
        while (runEnd + 1 < doc.LineCount && !IsBlankLine(runEnd + 1))
        {
            runEnd++;
        }

        if (runEnd > regionEndLine && RunHasOrderedItem(doc, regionStartLine, runEnd))
        {
            for (var l = regionEndLine + 1; l <= runEnd; l++)
            {
                newOrder.Add(LineContent(doc, l));
            }

            regionEndLine = runEnd;
        }

        // Renumber the reordered ordered-list items so the SOURCE persists the same sequence the preview
        // computes (always 1-based per nesting level). Done in-place on `newOrder` so the reorder + renumber
        // is a single ReplaceRange (one undo step); lines before the region give the ordinal context.
        var info = RenumberOrdered(newOrder, regionStartLine, doc.GetLineText);

        // Caret/anchor line+column (line-relative) before the edit; the move shifts each by `lineDelta`.
        var caretLine = doc.GetPosition(sel.Caret).Line;
        var anchorLine = doc.GetPosition(sel.Anchor).Line;
        var caretCol = sel.Caret - doc.GetLineStart(caretLine);
        var anchorCol = sel.Anchor - doc.GetLineStart(anchorLine);

        ReplaceLines(regionStartLine, regionEndLine, newOrder);

        // Re-place an endpoint by line+column, nudging the column by its line's renumber delta (when the
        // endpoint sits past the marker), so the selection rides along with the moved, renumbered item.
        var moved = Buffer.Document;
        int Place(int line, int col)
        {
            var i = line - regionStartLine;
            if (i >= 0 && i < info.Count && col > info[i].MarkerStart)
            {
                col += info[i].DigitDelta;
            }

            return moved.GetLineStart(line) + col;
        }

        Buffer.Selection = new Selection(Place(anchorLine + lineDelta, anchorCol), Place(caretLine + lineDelta, caretCol));
        _goalX = double.NaN;

        // Bracket the moved block with the drop indicator at its new line range.
        _reorderStartLine = startLine + lineDelta;
        _reorderEndLine = endLine + lineDelta;
        _reorderActive = true;
        InvalidateVisual();
    }

    /// <summary>Clear the reorder drop indicator (any action other than a continued line move ends the session).</summary>
    private void ClearReorderIndicator()
    {
        if (!_reorderActive)
        {
            return;
        }

        _reorderActive = false;
        InvalidateVisual();
    }

    /// <summary>
    /// Classify <paramref name="line"/>'s list marker into one of the three list kinds — bulleted, numbered,
    /// or checkbox (a task item is its own kind, distinct from a plain bullet) — or <see cref="ItemKind.None"/>
    /// when the line isn't a list item at all (plain text, a heading, a blockquote, …).
    /// </summary>
    private ItemKind ListItemKindOf(int line)
    {
        var p = LinePrefix.Parse(Buffer.Document.GetLineText(line));
        return p.List switch
        {
            ListKind.Ordered => ItemKind.Numbered,
            ListKind.Unordered => p.HasTask ? ItemKind.Checkbox : ItemKind.Bullet,
            _ => ItemKind.None,
        };
    }

    /// <summary>
    /// Whether a deeper-indented line is carried as part of a moved list item's subtree: only a nested list
    /// item of the <b>same kind</b> as the line the move started on (<paramref name="startKind"/>) is carried.
    /// Plain continuation text and different-kind nested items aren't — they're left behind, not dragged along.
    /// </summary>
    private bool CarriesAsDescendant(int line, ItemKind startKind)
        => startKind != ItemKind.None && ListItemKindOf(line) == startKind;

    /// <summary>
    /// The first source line of the flowed block <paramref name="line"/> is drawn in — the line that carries
    /// the block's marker, and the only one of its lines the reader sees as a row start. Equals
    /// <paramref name="line"/> when it begins its own block.
    /// <para>
    /// Answered from the layout's own block grouping rather than by re-deriving the flow rule, so the editing
    /// assists and the rendered rows agree by construction: whatever the reader sees as one flowing paragraph
    /// or item, the assists treat as one unit too. That also makes raw-text mode fall out for free — it groups
    /// every line on its own row, so nothing continues anything there and the assists act on literal lines.
    /// </para>
    /// </summary>
    private int BlockFirstLine(int line)
    {
        // Resolve the block first: BlockOf rebuilds a stale layout, which swaps in a fresh BlockStartLine.
        // Indexing the property in the same expression would read the pre-rebuild array (and mis-index it).
        var block = BlockOf(line);
        return _layout.BlockStartLine[block];
    }

    /// <summary>
    /// Whether <paramref name="line"/> is a <b>continuation line</b>: one the reader never sees as a row of its
    /// own, because it flows into a block that began above it (a hard-wrapped paragraph or list item). Such a
    /// line is not an independent target — a block-level command must act on the block, not on it.
    /// </summary>
    private bool IsBlockContinuationLine(int line) => BlockFirstLine(line) < line;

    /// <summary>
    /// The list/task item that the unmarked line <paramref name="line"/> lazily continues — or <c>-1</c> when it
    /// continues no <em>item</em> (it stands alone, or merely wraps a plain paragraph, which has no marker for
    /// Enter to carry down).
    /// </summary>
    private int ContinuedListItemLine(int line)
    {
        var start = BlockFirstLine(line);
        return start < line && LinePrefix.Parse(Buffer.Document.GetLineText(start)).HasList ? start : -1;
    }

    /// <summary>
    /// The line a caret-scoped list command (⌘L / ⌘⇧L) acts on: the caret's line resolved to the start of the
    /// block it is drawn in — so a checkbox lands on the paragraph/item the reader sees the caret inside, rather
    /// than starting a rival item mid-way through it.
    /// </summary>
    private int ListTargetLine() =>
        BlockFirstLine(Buffer.Document.GetPosition(Buffer.Selection.Caret).Line);

    /// <summary>
    /// The line range a selection-scoped list command acts on: <see cref="SelectedLineRange"/> with its start
    /// resolved back to its block's first line, so the range covers whole blocks. Only the start needs it — the
    /// marker line of a block the range ends inside already sits above.
    /// </summary>
    private (int Start, int End) ListTargetLineRange()
    {
        var (start, end) = SelectedLineRange();
        return (BlockFirstLine(start), end);
    }

    /// <summary>
    /// The <b>visual indentation</b> of <paramref name="line"/> in columns — its nesting depth. Tabs are
    /// measured on <see cref="IndentWidth"/>-column tab stops (matching the tokenizer's
    /// <c>LeadingWhitespace</c> and how the surface draws a leading tab), so that a child indented with one
    /// tab and a sibling indented with four spaces land at the same depth. Comparing raw character counts
    /// instead would treat mixed tab/space indentation as different levels and split a parent from its child.
    /// </summary>
    private int LineIndent(int line)
    {
        var t = Buffer.Document.GetLineText(line);
        var stop = Math.Max(1, IndentWidth);
        var columns = 0;
        for (var i = 0; i < t.Length && (t[i] == ' ' || t[i] == '\t'); i++)
        {
            columns += t[i] == '\t' ? stop - (columns % stop) : 1;
        }

        return columns;
    }

    /// <summary>
    /// Whether <paramref name="line"/> is <b>truly empty</b> — no characters at all (an optional trailing
    /// <c>\r</c> of a CRLF break aside). This is stricter than <see cref="IsBlankLine"/>, which also accepts
    /// whitespace-only lines: a line the user has put indentation on (e.g. auto-indent carried down by Enter)
    /// is a real editable row, not the empty separator a <c>\n\n</c> paragraph break inserts.
    /// </summary>
    private bool IsEmptyLine(int line)
    {
        var text = Buffer.Document.GetLineText(line);
        return text.Length == 0 || (text.Length == 1 && text[0] == '\r');
    }

    /// <summary>Whether <paramref name="line"/> is a soft paragraph separator, collapsed to zero visual rows.
    /// The layout owns the rule (it turns on which lines are code); see <see cref="LayoutModel.IsCollapsedBlankLine"/>.</summary>
    private bool IsCollapsedBlankLine(int line) => _layout.IsCollapsedBlankLine(line);

    /// <summary>Whether any line in <c>[from, to]</c> carries an ordered-list marker.</summary>
    private static bool RunHasOrderedItem(TextDocument doc, int from, int to)
    {
        for (var l = from; l <= to; l++)
        {
            if (LinePrefix.Parse(doc.GetLineText(l)).List == ListKind.Ordered)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The text of lines <c>[from, to]</c> (inclusive), each without its trailing CR.</summary>
    private static List<string> LinesOf(TextDocument doc, int from, int to)
    {
        var list = new List<string>(Math.Max(0, to - from + 1));
        for (var l = from; l <= to; l++)
        {
            list.Add(LineContent(doc, l));
        }

        return list;
    }

    /// <summary>
    /// A line's content <b>without</b> its trailing CR. A CRLF document stores the <c>\r</c> as line content
    /// (so files round-trip byte-for-byte), but it is really part of the line ending — every line but the
    /// last carries one — so reordering/rebuilding lines must strip it and let <see cref="ReplaceLines"/> put
    /// the right ending back by position.
    /// </summary>
    private static string LineContent(TextDocument doc, int line)
    {
        var t = doc.GetLineText(line);
        return t.Length > 0 && t[^1] == '\r' ? t[..^1] : t;
    }

    /// <summary>The document's line ending — <c>\r\n</c> if it uses CRLF anywhere, else <c>\n</c>.</summary>
    private static string DetectNewline(TextDocument doc)
    {
        for (var i = 0; i < doc.Length; i++)
        {
            if (doc[i] == '\n')
            {
                return i > 0 && doc[i - 1] == '\r' ? "\r\n" : "\n";
            }
        }

        return "\n";
    }

    /// <summary>
    /// Replace document lines <c>[startLine, endLine]</c> with <paramref name="contents"/> (each a line with no
    /// trailing newline), reproducing the document's line ending between and after them so CRLF/LF stays
    /// byte-for-byte even when lines are permuted (the trailing CR belongs to whichever line is last).
    /// </summary>
    private void ReplaceLines(int startLine, int endLine, IReadOnlyList<string> contents)
    {
        var doc = Buffer.Document;
        var nl = DetectNewline(doc);
        var atDocEnd = endLine >= doc.LineCount - 1;
        var regionStart = doc.GetLineStart(startLine);
        var regionEnd = atDocEnd ? doc.Length : doc.GetLineStart(endLine + 1);
        var text = string.Join(nl, contents) + (atDocEnd ? string.Empty : nl);
        Buffer.ReplaceRange(regionStart, regionEnd - regionStart, text);
    }

    /// <summary>Resequence the ordered-list run starting at <paramref name="fromLine"/> as one edit (PRD §7.2).</summary>
    private void RenumberOrderedFrom(int fromLine)
    {
        var doc = Buffer.Document;
        if (fromLine < 0 || fromLine >= doc.LineCount)
        {
            return;
        }

        var first = LinePrefix.Parse(doc.GetLineText(fromLine));
        if (first.List != ListKind.Ordered)
        {
            return;
        }

        // Read only as far as the same-level ordered run extends (not the whole document).
        var lines = new List<string>();
        for (var l = fromLine; l < doc.LineCount; l++)
        {
            var text = doc.GetLineText(l);
            if (l > fromLine)
            {
                var p = LinePrefix.Parse(text);
                if (p.List != ListKind.Ordered || p.Indent != first.Indent || p.Quote != first.Quote)
                {
                    break;
                }
            }

            lines.Add(text);
        }

        if (OrderedListRenumber.Compute(lines, 0) is not { } result)
        {
            return; // already sequential — common when appending at the end of a list
        }

        // The first line (the just-inserted item) never changes, so the caret offset stays valid.
        var caret = Buffer.Selection;
        var lastLine = fromLine + result.EndLineExclusive - 1;
        var regionStart = doc.GetLineStart(fromLine);
        var regionEnd = doc.GetLineEnd(lastLine);
        Buffer.ReplaceRange(regionStart, regionEnd - regionStart, string.Join("\n", result.Lines));
        Buffer.Selection = caret;
    }

    private bool TrySortCompletedTaskGroup(int line, Selection selection)
    {
        if (CompletedTaskSorting == CompletedTaskSortMode.None)
        {
            return false;
        }

        var doc = Buffer.Document;
        if (line < 0 || line >= doc.LineCount)
        {
            return false;
        }

        var indent = LineIndent(line);
        var prefix = LinePrefix.Parse(doc.GetLineText(line));
        if (!prefix.HasTask)
        {
            return false;
        }

        var groupStart = line;
        while (true)
        {
            var previous = PreviousTaskSiblingStart(groupStart, indent);
            if (previous < 0)
            {
                break;
            }

            groupStart = previous;
        }

        var items = ReadTaskSiblingGroup(groupStart, indent);
        if (items.Count < 2)
        {
            return false;
        }

        var sorted = CompletedTaskSorting == CompletedTaskSortMode.Top
            ? items.OrderByDescending(static item => item.IsComplete).ToList()
            : [.. items.OrderBy(static item => item.IsComplete)];
        if (sorted.SequenceEqual(items))
        {
            return false;
        }

        var regionStart = items[0].StartLine;
        var regionEnd = items[^1].EndLine;
        var lineMap = new int[regionEnd - regionStart + 1];
        var contents = new List<string>();
        var nextLine = regionStart;
        foreach (var item in sorted)
        {
            for (var sourceLine = item.StartLine; sourceLine <= item.EndLine; sourceLine++)
            {
                lineMap[sourceLine - regionStart] = nextLine++;
                contents.Add(LineContent(doc, sourceLine));
            }
        }

        var anchor = selection.Anchor;
        var caret = selection.Caret;
        ReplaceLines(regionStart, regionEnd, contents);

        var moved = Buffer.Document;
        Buffer.Selection = new Selection(
            RemapOffset(anchor, regionStart, regionEnd, lineMap, moved, doc),
            RemapOffset(caret, regionStart, regionEnd, lineMap, moved, doc));
        _goalX = double.NaN;
        return true;
    }

    private int PreviousTaskSiblingStart(int startLine, int indent)
    {
        var previous = startLine - 1;
        while (previous >= 0 && !IsBlankLine(previous) && LineIndent(previous) > indent)
        {
            previous--;
        }

        if (previous < 0 || IsBlankLine(previous) || LineIndent(previous) != indent)
        {
            return -1;
        }

        return LinePrefix.Parse(Buffer.Document.GetLineText(previous)).HasTask ? previous : -1;
    }

    private List<TaskSubtree> ReadTaskSiblingGroup(int startLine, int indent)
    {
        var doc = Buffer.Document;
        var items = new List<TaskSubtree>();
        var line = startLine;
        while (line < doc.LineCount)
        {
            if (IsBlankLine(line) || LineIndent(line) != indent)
            {
                break;
            }

            var prefix = LinePrefix.Parse(doc.GetLineText(line));
            if (!prefix.HasTask)
            {
                break;
            }

            var endLine = line;
            while (endLine + 1 < doc.LineCount && !IsBlankLine(endLine + 1) && LineIndent(endLine + 1) > indent)
            {
                endLine++;
            }

            items.Add(new TaskSubtree(line, endLine, IsCompletedTaskSubtree(line, endLine)));
            line = endLine + 1;
        }

        return items;
    }

    private bool IsCompletedTaskSubtree(int startLine, int endLine)
    {
        var doc = Buffer.Document;
        var self = LinePrefix.Parse(doc.GetLineText(startLine));
        if (!self.HasTask || !self.TaskChecked)
        {
            return false;
        }

        for (var line = startLine + 1; line <= endLine; line++)
        {
            var prefix = LinePrefix.Parse(doc.GetLineText(line));
            if (prefix.HasTask && !prefix.TaskChecked)
            {
                return false;
            }
        }

        return true;
    }

    private static int RemapOffset(int offset, int regionStart, int regionEnd, int[] lineMap, TextDocument moved, TextDocument original)
    {
        var position = original.GetPosition(offset);
        if (position.Line < regionStart || position.Line > regionEnd)
        {
            return offset;
        }

        var newLine = lineMap[position.Line - regionStart];
        var originalColumn = offset - original.GetLineStart(position.Line);
        var newLineStart = moved.GetLineStart(newLine);
        var newLineLength = moved.GetLineText(newLine).Length;
        return newLineStart + Math.Min(originalColumn, newLineLength);
    }

    /// <summary>
    /// The ordinal to display for the ordered-list item on <paramref name="line"/>, computed from the
    /// list's structure rather than the literal source digits: walking up, deeper (nested) lines are
    /// skipped, a shallower line ends the level (a sub-list restarts at 1), and a blank or non-ordered
    /// sibling starts a fresh list. So <c>1. 1. 1.</c> renders 1·2·3 and nested levels number themselves.
    /// </summary>
    private int OrderedOrdinal(int line)
    {
        var doc = Buffer.Document;
        var self = LinePrefix.Parse(doc.GetLineText(line));
        return OrderedOrdinalOf(self, k => doc.GetLineText(k), line);
    }

    /// <summary>
    /// The 1-based ordinal an ordered item <paramref name="self"/> at <paramref name="line"/> shows, counting
    /// preceding same-level ordered siblings via <paramref name="lineAt"/> until a blank line, a shallower
    /// level, or a non-ordered same-level sibling. Decoupled from the live document so it can be computed over
    /// a hypothetical (about-to-be-applied) reordering as well.
    /// </summary>
    private static int OrderedOrdinalOf(LinePrefix self, Func<int, string> lineAt, int line)
    {
        if (self.List != ListKind.Ordered)
        {
            return 1;
        }

        var count = 1;
        for (var k = line - 1; k >= 0; k--)
        {
            var (stop, counts) = OrderedScanStep(lineAt(k), self);
            if (counts)
            {
                count++;
            }

            if (stop)
            {
                break;
            }
        }

        return count;
    }

    /// <summary>
    /// One backward-scan step for <see cref="OrderedOrdinalOf"/>: given a preceding <paramref name="text"/>
    /// line and the item <paramref name="self"/>, whether to stop scanning and whether the line is a
    /// same-level ordered sibling that the ordinal should count.
    /// </summary>
    private static (bool Stop, bool Counts) OrderedScanStep(string text, LinePrefix self)
    {
        if (text.TrimEnd('\r').Trim().Length == 0)
        {
            return (true, false); // a blank line starts a fresh list
        }

        var p = LinePrefix.Parse(text);
        if (p.Indent > self.Indent)
        {
            return (false, false); // a deeper nested line / continuation — skip it
        }

        if (p.Indent < self.Indent || p.Quote != self.Quote)
        {
            return (true, false); // a shallower level, or a different quote context, ends the run
        }

        if (p.List == ListKind.Ordered)
        {
            return (false, true); // a same-level ordered sibling
        }

        // The line carries no ordered marker. If it is ordinary prose it is a *lazy continuation* of the item
        // above it — the layout flows it into that item (LayoutModel.AcceptsContinuation), so an item spelled
        // across two source lines is one item, and the count must run straight through it rather than restart.
        // Anything that opens a block of its own — a bullet, a heading, a rule, a fence, a quote — does end the
        // list. Both sides ask the engine the same question, so the ordinal scan and the renderer can't drift
        // apart on what a multi-line item is.
        return (!LinePrefix.IsLazyContinuation(text), false);
    }

    /// <summary>
    /// Rewrite every ordered-list item in <paramref name="lines"/> so its number is the preview ordinal
    /// (1-based, restarting at each nesting level and after a blank line). <paramref name="lines"/> occupies
    /// document lines starting at <paramref name="regionStart"/>; <paramref name="outside"/> supplies lines
    /// before the region for ordinal context (a self-contained run can return <c>""</c>). Mutates
    /// <paramref name="lines"/> in place and returns each line's marker start + digit-count change.
    /// </summary>
    private static IReadOnlyList<RenumberInfo> RenumberOrdered(List<string> lines, int regionStart, Func<int, string> outside)
    {
        string FinalLine(int g) =>
            g >= regionStart && g < regionStart + lines.Count ? lines[g - regionStart] : outside(g);

        var info = new RenumberInfo[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var p = LinePrefix.Parse(lines[i]);
            var delta = 0;
            if (p.List == ListKind.Ordered)
            {
                var ordinal = OrderedOrdinalOf(p, FinalLine, regionStart + i);
                if (ordinal != p.Number)
                {
                    var ordinalStr = ordinal.ToString(CultureInfo.InvariantCulture);
                    lines[i] = lines[i][..p.MarkerStart] + ordinalStr + lines[i][(p.MarkerStart + p.DigitCount)..];
                    delta = ordinalStr.Length - p.DigitCount;
                }
            }

            info[i] = new RenumberInfo(p.MarkerStart, delta);
        }

        return info;
    }

    /// <summary>
    /// After an edit that changed ordered-list structure (e.g. indent/outdent, exiting an item), renumber the
    /// ordered items in the maximal run of non-blank lines containing <paramref name="line"/> so the source
    /// matches the preview. One <see cref="DocumentBuffer.ReplaceRange"/> (or none if already correct); the
    /// caret/anchor stay on their line, nudged by their line's digit-count change.
    /// </summary>
    private void RenumberOrderedRun(int line)
    {
        var doc = Buffer.Document;
        if (line < 0 || line >= doc.LineCount)
        {
            return;
        }

        var start = line;
        while (start > 0 && !IsBlankLine(start - 1))
        {
            start--;
        }

        var end = line;
        while (end + 1 < doc.LineCount && !IsBlankLine(end + 1))
        {
            end++;
        }

        var lines = new List<string>(end - start + 1);
        for (var l = start; l <= end; l++)
        {
            lines.Add(LineContent(doc, l));
        }

        var before = string.Join("\n", lines);
        var info = RenumberOrdered(lines, start, _ => string.Empty); // a non-blank run is self-contained
        if (string.Join("\n", lines) == before)
        {
            return; // already sequential — nothing to rewrite
        }

        var sel = Buffer.Selection;
        var caretLine = doc.GetPosition(sel.Caret).Line;
        var anchorLine = doc.GetPosition(sel.Anchor).Line;
        var caretCol = sel.Caret - doc.GetLineStart(caretLine);
        var anchorCol = sel.Anchor - doc.GetLineStart(anchorLine);

        ReplaceLines(start, end, lines);

        var moved = Buffer.Document;
        int Place(int l, int col)
        {
            var i = l - start;
            if (i >= 0 && i < info.Count && col > info[i].MarkerStart)
            {
                col += info[i].DigitDelta;
            }

            return moved.GetLineStart(l) + col;
        }

        Buffer.Selection = new Selection(Place(anchorLine, anchorCol), Place(caretLine, caretCol));
    }

    /// <summary>The ordered-list delimiter ('.'/')') inside marker columns <c>[s, en)</c>, defaulting to '.'.</summary>
    private static string OrderedDelimiter(string lineText, int s, int en)
    {
        for (var k = s; k < en && k < lineText.Length; k++)
        {
            if (lineText[k] is '.' or ')')
            {
                return lineText[k].ToString();
            }
        }

        return ".";
    }

    /// <summary>Whether the list marker spanning columns <c>[s, en)</c> is an ordered (numbered) one.</summary>
    private static bool IsOrderedMarker(string lineText, int s, int en)
    {
        for (var k = s; k < en && k < lineText.Length; k++)
        {
            if (lineText[k] != ' ')
            {
                return char.IsAsciiDigit(lineText[k]);
            }
        }

        return false;
    }
}
