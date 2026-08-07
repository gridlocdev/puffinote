using Avalonia.Media;
using Puffinote.Text.Document;
using Typage.Engine.Markdown;

namespace Typage.Control.Rendering;

/// <summary>
/// The volatile control state a single block layout reads: the document and its token model, the active
/// theme, the block→line index, the two view-mode flags, the wrap width, the checkbox gutter width, and a
/// callback that renders an ordered-list marker's displayed number (kept on the control because it depends
/// on the list's document structure). Passed to <see cref="BlockLayoutBuilder.Build"/> per call so the
/// builder holds no long-lived control references.
/// </summary>
/// <param name="ListLevel">A list item's nesting level (0 for top-level) — like the ordinal, it depends on the
/// list's document structure, so the control owns it.</param>
/// <param name="OrderedMarkerText">Given <c>(line, lineText, sLine, enLine)</c>, the "N. " text to show for
/// an ordered-list marker at that line-relative span, or <c>null</c> when it is not an ordered marker.</param>
internal readonly record struct BlockLayoutContext(
    TextDocument Document,
    MarkdownModel Model,
    MarkdownTheme Theme,
    int[] BlockStartLine,
    bool RawTextMode,
    bool MarkersRevealed,
    double WrapWidth,
    double CheckboxGutter,
    Func<int, int> ListLevel,
    Func<int, string, int, int, string?> OrderedMarkerText);

/// <summary>
/// Turns one document block into a laid-out <see cref="VisualLine"/>: it tiles the block's text into styled
/// segments (filling the gaps between tokens with the default style), decides which markers conceal
/// (PRD §7.4), splits nested inline constructs, and hands the result to <see cref="VisualLine.Build"/> with
/// the shared <see cref="TextMeasurer"/>. It depends only on the <see cref="BlockLayoutContext"/> snapshot
/// plus the measurer — no caret, selection, or scroll state — so a block's layout is caret-independent and
/// can be exercised without a live control. The control owns the <c>VisualLine</c> cache and calls this on a
/// miss.
/// </summary>
internal sealed class BlockLayoutBuilder
{
    private readonly TextMeasurer _measurer;

    // The current build's inputs — assigned at the top of Build() and read by the private helpers below.
    // Block layout is synchronous and UI-thread-confined, so reusing one instance across calls is safe and
    // avoids allocating a builder per block.
    private TextDocument _doc = null!;
    private MarkdownModel _model = null!;
    private MarkdownTheme _theme = null!;
    private int[] _blockStartLine = [];
    private bool _rawTextMode;
    private bool _markersRevealed;
    private double _wrapWidth;
    private double _checkboxGutter;
    private Func<int, int> _listLevel = null!;
    private Func<int, string, int, int, string?> _orderedMarkerText = null!;

    public BlockLayoutBuilder(TextMeasurer measurer) => _measurer = measurer;

    /// <summary>
    /// Lay out one paragraph <paramref name="block"/> as a single (possibly wrapped) <see cref="VisualLine"/>.
    /// A flow block joins its member source lines into one paragraph — the block's text is the contiguous
    /// document range with interior line breaks shown as spaces, so a block column is simply
    /// <c>offset − blockStart</c> and the whole offset↔visual map carries over unchanged. Markers are always
    /// concealed (full WYSIWYG), so the layout is caret-independent.
    /// </summary>
    public VisualLine Build(int block, in BlockLayoutContext ctx)
    {
        _doc = ctx.Document;
        _model = ctx.Model;
        _theme = ctx.Theme;
        _blockStartLine = ctx.BlockStartLine;
        _rawTextMode = ctx.RawTextMode;
        _markersRevealed = ctx.MarkersRevealed;
        _wrapWidth = ctx.WrapWidth;
        _checkboxGutter = ctx.CheckboxGutter;
        _listLevel = ctx.ListLevel;
        _orderedMarkerText = ctx.OrderedMarkerText;

        var doc = _doc;
        var startLine = _blockStartLine[block];
        var endLine = _blockStartLine[block + 1] - 1;
        var blockStart = doc.GetLineStart(startLine);
        var blockEnd = doc.GetLineEnd(endLine);

        // Raw-text view: draw the literal source in one plain monospace style, with no marker concealment,
        // per-token colouring, or synthetic decorations. Each block is a single line here (paragraph flow is
        // off in this mode), so the source maps 1:1 to the drawn columns.
        if (_rawTextMode)
        {
            var lineText = doc.GetText(blockStart, blockEnd - blockStart).TrimEnd('\n', '\r');
            var plain = new List<VisualLine.Segment> { new(0, lineText.Length, _theme.RawTextStyle(), false) };
            return VisualLine.Build(lineText, plain, _measurer.MeasureWidth, _wrapWidth);
        }

        // Interior newlines render as spaces so the paragraph flows; the last line's trailing CR is dropped
        // (matching the old single-line path). Replacement is char-for-char so columns stay 1:1 with offsets.
        var raw = doc.GetText(blockStart, blockEnd - blockStart).Replace('\n', ' ');
        if (raw.Length > 0 && raw[^1] == '\r')
        {
            raw = raw[..^1];
        }

        var text = raw.Replace('\r', ' ');
        var contentLen = text.Length;

        var segments = new List<VisualLine.Segment>();
        var pos = 0;
        var lastHeading = 0;
        var lastQuote = false;

        // A block that *is* a done task item reads as closed: its text is struck through and dimmed. Taken from the
        // block's first line, so the wrapped/lazily-continued rows of a done item are struck too — the item
        // closed, not just its first line.
        var done = false;
        for (var line = startLine; line <= endLine; line++)
        {
            var lineStart = doc.GetLineStart(line);
            var lineOffset = lineStart - blockStart; // this line's first column, in block-relative terms
            var lineText = doc.GetText(lineStart, doc.GetLineEnd(line) - lineStart).TrimEnd('\r');
            var headingLevel = HeadingLevel(line);
            lastHeading = headingLevel;
            var inQuote = InQuote(line);
            lastQuote = inQuote;
            var (orderedText, taskText) = ItemMarkers(line, lineStart, lineText);
            var hasTask = taskText.Length > 0;
            if (line == startLine && hasTask && TaskStates.FromMarker(taskText) == TaskState.Done)
            {
                done = true;
            }

            // The inline nodes whose content draws a background chip (inline code, highlight) — their concealed
            // markers hold that chip's side padding open instead of vanishing to zero width.
            var chipPads = ChipPads(line);
            // The text of an inline link [text](url) is tokenized as ordinary content (the engine recurses
            // into it), so to render it like an HTML hyperlink we colour + underline any content that falls
            // inside the link-text span — the gap between the opening "[" and the "](" of each link node.
            var linkSpans = LinkTextSpans(line);

            foreach (var t in _model.LineTokens(line))
            {
                var s = Math.Clamp(t.Start - blockStart, 0, contentLen);
                var en = Math.Clamp(t.End - blockStart, 0, contentLen);
                if (en <= s)
                {
                    continue;
                }

                // A nested item's leading indentation is markup, not content. It lays out as a fixed step per
                // nesting *level* (MarkdownTheme.IndentStep) instead of as the measured width of the spaces
                // the author happened to type — which is what made the same depth of nesting indent a bullet
                // (2 spaces), a number (3) and a task (2 or 6) by three different amounts. Raw-source and
                // markers-revealed views show the literal whitespace, like every other piece of markup.
                if (!_markersRevealed && t.Type == TokenType.ListMarker && s > pos && pos == lineOffset)
                {
                    segments.Add(new VisualLine.Segment(
                        pos, s, DefaultStyle(headingLevel, inQuote), true,
                        DecorationKind.ListIndent, _listLevel(line) * _theme.IndentStep));
                    pos = s;
                }

                if (s > pos)
                {
                    // The gap (including a joined newline) is plain text — unless it falls inside a link's text.
                    var gapStyle = DefaultStyle(headingLevel, inQuote);
                    if (InLinkText(linkSpans, pos + blockStart, s + blockStart))
                    {
                        gapStyle = AsLink(gapStyle);
                    }

                    segments.Add(new VisualLine.Segment(pos, s, Closed(gapStyle, done), false));
                }

                // The decoration helpers index into the *line* text, so keep line-relative offsets for them;
                // the segment itself is placed at block-relative columns.
                var sLine = Math.Clamp(t.Start - lineStart, 0, lineText.Length);
                var enLine = Math.Clamp(t.End - lineStart, 0, lineText.Length);
                var style = StyleFor(t.Type, headingLevel, inQuote);
                var concealed = MarkerConcealed(t);

                if (concealed)
                {
                    // A concealed line-prefix marker may render as a synthetic decoration (bullet, checkbox,
                    // quote bar, rule) that keeps the content's hanging indent; otherwise it just vanishes.
                    var (deco, width) = DecorationFor(t, lineText, sLine, enLine, hasTask, chipPads);

                    // A checkbox carries the state it is in (the box character) as its text; every other
                    // decoration but the ordered number below draws from its kind alone.
                    var decoText = deco == DecorationKind.Checkbox ? BoxMark(taskText) : string.Empty;

                    // An ordered-list marker hides its literal digits and shows a number computed from the
                    // list's structure (items 4 & 5). Raw-source mode shows the literal source digits. A task
                    // marker after it doesn't suppress the number — a numbered task shows both — but the box
                    // is drawn *first*, "☐ 1. step", the way a reader tallies the list. So the two decorations
                    // trade the marker slots they hang on: the list marker's span draws the checkbox and the
                    // task marker's draws the number. Both spans conceal either way, so the source keeps its
                    // "1. [ ] " order and only the drawing order changes.
                    if (orderedText is not null && t.Type is TokenType.ListMarker or TokenType.TaskMarker)
                    {
                        var numberSlot = hasTask ? TokenType.TaskMarker : TokenType.ListMarker;
                        if (t.Type == numberSlot)
                        {
                            decoText = orderedText;
                            deco = DecorationKind.OrderedNumber;
                            width = _measurer.MeasureWidth(decoText, style);
                        }
                        else
                        {
                            (deco, width) = Checkbox(); // a numbered task's list marker leads with the box
                            decoText = BoxMark(taskText);
                        }
                    }

                    segments.Add(new VisualLine.Segment(s, en, style, true, deco, deco == DecorationKind.None ? 0 : width, decoText));
                }
                else
                {
                    // Content inside an inline link's text renders like an HTML hyperlink (coloured + underlined),
                    // keeping any emphasis typeface it already carries.
                    if (InLinkText(linkSpans, t.Start, t.End))
                    {
                        style = AsLink(style);
                    }

                    // Emphasis/strike/highlight content can wrap a construct of another kind (e.g.
                    // ***~~bold~~***): split the content so the inner ~~ markers conceal and the word
                    // carries both the outer emphasis and the inner decoration. Plain content is one segment.
                    AddContentSegments(segments, s, en, text, Closed(style, done), t.Type);
                }

                pos = en;
            }
        }

        if (pos < contentLen)
        {
            segments.Add(new VisualLine.Segment(pos, contentLen, Closed(DefaultStyle(lastHeading, lastQuote), done), false));
        }

        return VisualLine.Build(text, segments, _measurer.MeasureWidth, _wrapWidth);
    }

    /// <summary>The treatment of one content run on a <b>done</b> task item — struck through and dimmed — or
    /// <paramref name="style"/> unchanged when the block isn't one.</summary>
    private RunStyle Closed(RunStyle style, bool done) =>
        done ? _theme.OnDoneTask(style) : style;

    /// <summary>
    /// The two leading-marker facts a list line's decorations need, read in one pass before the token loop: the
    /// ordinal text (<c>"N. "</c>) to draw when <paramref name="line"/> is a <i>numbered</i> item (null when it
    /// isn't one), and the task marker's source text (<c>"[ ] "</c>/<c>"[x] "</c>, empty when the line isn't a
    /// task). Each marker has to know about the other, because on a numbered task they swap the slots their
    /// decorations hang on so the checkbox can lead the number.
    /// </summary>
    private (string? Ordered, string Task) ItemMarkers(int line, int lineStart, string lineText)
    {
        string? ordered = null;
        var sawList = false;
        var task = string.Empty;
        foreach (var t in _model.LineTokens(line))
        {
            var s = Math.Clamp(t.Start - lineStart, 0, lineText.Length);
            var en = Math.Clamp(t.End - lineStart, 0, lineText.Length);
            if (t.Type == TokenType.ListMarker && !sawList)
            {
                sawList = true;
                ordered = _orderedMarkerText(line, lineText, s, en);
            }
            else if (t.Type == TokenType.TaskMarker && task.Length == 0)
            {
                task = lineText[s..en];
            }
        }

        return (ordered, task);
    }

    private int HeadingLevel(int line)
    {
        var tokens = _model.LineTokens(line);
        if (tokens.Count > 0 && tokens[0].Type == TokenType.HeadingMarker && int.TryParse(tokens[0].Meta, out var l))
        {
            return l;
        }

        return 0;
    }

    /// <summary>Whether <paramref name="line"/> is inside a blockquote (so its prose reads as secondary).</summary>
    private bool InQuote(int line)
    {
        var tokens = _model.LineTokens(line);
        return tokens.Count > 0 && tokens[0].Type == TokenType.BlockquoteMarker;
    }

    private RunStyle DefaultStyle(int headingLevel, bool inQuote = false) =>
        StyleFor(TokenType.Text, headingLevel, inQuote);

    /// <summary>
    /// The style for a <paramref name="type"/> run on a line at <paramref name="headingLevel"/>. On a heading
    /// line every run — the concealed <c>#</c> included — takes the heading's size, weight, and (for prose) its
    /// colour, so the whole line sits on one baseline. Inside a blockquote, prose steps down to the quote's
    /// dimmer foreground; code, links, and markers keep the colour that identifies them.
    /// </summary>
    private RunStyle StyleFor(TokenType type, int headingLevel, bool inQuote = false)
    {
        var style = _theme.Resolve(type, headingLevel);
        if (headingLevel > 0)
        {
            return _theme.OnHeading(style, headingLevel, type);
        }

        if (inQuote && type is TokenType.Text or TokenType.Strong or TokenType.Emphasis
            or TokenType.StrongEmphasis or TokenType.Strikethrough)
        {
            style = style with { Foreground = _theme.QuoteForeground };
        }

        return style;
    }

    /// <summary>The absolute text spans of inline links <c>[text](url)</c> on <paramref name="line"/> — the
    /// range between each link node's opening <c>[</c> and its <c>](</c>, i.e. the part a reader clicks.</summary>
    private List<(int Start, int End)> LinkTextSpans(int line)
    {
        var spans = new List<(int, int)>();
        var toks = _model.LineTokens(line);
        foreach (var u in toks)
        {
            if (u.Type != TokenType.LinkUrl)
            {
                continue;
            }

            // Among the markers grouped with this link node, the opening "[" has the smallest start and the
            // "](" is the one ending exactly where the URL begins; the text is what sits between them.
            var openStart = int.MaxValue;
            var openEnd = -1;
            var midStart = -1;
            foreach (var m in toks)
            {
                if (m.Node != u.Node || m.Role != TokenRole.Marker)
                {
                    continue;
                }

                if (m.Start < openStart)
                {
                    openStart = m.Start;
                    openEnd = m.End;
                }

                if (m.End == u.Start)
                {
                    midStart = m.Start;
                }
            }

            if (openEnd >= 0 && midStart > openEnd)
            {
                spans.Add((openEnd, midStart));
            }
        }

        return spans;
    }

    private static bool InLinkText(List<(int Start, int End)> spans, int start, int end)
    {
        foreach (var (s, e) in spans)
        {
            if (start >= s && end <= e)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Add the segments for one inline content token covering block columns <c>[s, en)</c>. Emphasis, strong,
    /// strike, and highlight content can hold a construct of <i>another</i> kind on the inside (the engine
    /// emits such content as one flat token — e.g. <c>***~~bold~~***</c> is StrongEmphasis content
    /// "~~bold~~"). We scan for those nested constructs and split: the inner <c>~~</c>/<c>**</c>/… markers
    /// conceal (zero width, like every other hidden marker), and the inner word carries the base style
    /// <i>combined</i> with the nested one (bold-italic <i>and</i> a strike line). Content with nothing nested
    /// — the common case — stays a single segment, so plain emphasis/strike is unchanged.
    /// </summary>
    private void AddContentSegments(List<VisualLine.Segment> segments, int s, int en, string blockText, RunStyle baseStyle, TokenType type)
    {
        if (type is not (TokenType.Strong or TokenType.Emphasis or TokenType.StrongEmphasis
            or TokenType.Strikethrough or TokenType.Highlight))
        {
            segments.Add(new VisualLine.Segment(s, en, baseStyle, false));
            return;
        }

        var content = blockText[s..en];
        var nested = InlineDelimiters.FindNested(content);
        if (nested.Count == 0)
        {
            segments.Add(new VisualLine.Segment(s, en, baseStyle, false));
            return;
        }

        var p = 0;
        foreach (var n in nested)
        {
            if (n.OpenStart > p)
            {
                segments.Add(new VisualLine.Segment(s + p, s + n.OpenStart, baseStyle, false));
            }

            var innerStart = n.OpenStart + n.MarkerLen;
            segments.Add(new VisualLine.Segment(s + n.OpenStart, s + innerStart, baseStyle, true));         // open marker
            segments.Add(new VisualLine.Segment(s + innerStart, s + n.InnerEnd, CombineNested(baseStyle, n.Delim, n.MarkerLen), false));
            segments.Add(new VisualLine.Segment(s + n.InnerEnd, s + n.InnerEnd + n.MarkerLen, baseStyle, true)); // close marker
            p = n.InnerEnd + n.MarkerLen;
        }

        if (p < content.Length)
        {
            segments.Add(new VisualLine.Segment(s + p, en, baseStyle, false));
        }
    }

    /// <summary>Combine <paramref name="baseStyle"/> with a nested construct's treatment: <c>~~</c> adds the
    /// strike line, <c>==</c> the highlight band, and <c>*</c>/<c>_</c> merge bold/italic into the typeface
    /// (so an emphasised word inside a strike, or struck text inside emphasis, keeps both).</summary>
    private RunStyle CombineNested(RunStyle baseStyle, char delim, int markerLen)
    {
        if (delim == '~')
        {
            return baseStyle with { Decorations = _theme.Resolve(TokenType.Strikethrough).Decorations };
        }

        if (delim == '=')
        {
            return baseStyle with { Background = _theme.Resolve(TokenType.Highlight).Background };
        }

        var addItalic = markerLen is 1 or 3;
        var addBold = markerLen is 2 or 3;
        var tf = baseStyle.Typeface;
        var weight = addBold || tf.Weight == FontWeight.Bold ? FontWeight.Bold : tf.Weight;
        var fontStyle = addItalic || tf.Style == FontStyle.Italic ? FontStyle.Italic : tf.Style;
        return baseStyle with { Typeface = new Typeface(tf.FontFamily, fontStyle, weight) };
    }

    /// <summary>No inline node on this line draws a chip — the common case, shared so a line without one allocates nothing.</summary>
    private static readonly Dictionary<int, double> NoChipPads = [];

    /// <summary>
    /// The inline nodes on <paramref name="line"/> whose <i>content</i> draws a background chip, mapped to the
    /// horizontal padding that chip needs (<see cref="RunStyle.BackgroundPad"/>) — inline code and highlight. A
    /// node's markers are concealed, so this is how a marker learns what its construct's chip needs from it.
    /// </summary>
    private Dictionary<int, double> ChipPads(int line)
    {
        Dictionary<int, double>? pads = null;
        foreach (var t in _model.LineTokens(line))
        {
            if (t.Role != TokenRole.Content || t.Node == 0)
            {
                continue;
            }

            var pad = _theme.Resolve(t.Type).BackgroundPad;
            if (pad > 0)
            {
                (pads ??= [])[t.Node] = pad;
            }
        }

        return pads ?? NoChipPads;
    }

    /// <summary>The synthetic decoration (and its gutter width) that replaces concealed marker <paramref name="t"/>, if any.</summary>
    private (DecorationKind Kind, double Width) DecorationFor(
        Token t, string lineText, int s, int en, bool hasTask, Dictionary<int, double> chipPads)
    {
        var text = lineText[s..en];
        switch (t.Type)
        {
            // The concealed backticks of `code` (or the "==" of a highlight). They draw nothing, but unlike every
            // other hidden marker they don't collapse to zero width: they hold the chip's side padding open, so
            // the chip is drawn into space of its own rather than over the tail of the word before it.
            case TokenType.Marker when t.Node != 0 && chipPads.TryGetValue(t.Node, out var pad):
                return (DecorationKind.ChipPad, pad);

            case TokenType.ListMarker:
                var bullet = text.TrimStart(' ');
                if (bullet.Length is 0 || bullet[0] is not ('-' or '*' or '+'))
                {
                    // An ordered marker: no bullet glyph. Build's slot logic hands this span the number — or,
                    // when the item is also a task, the checkbox that leads it. Either way a number survives
                    // alongside a checkbox, because it says *which* item of the list this is, something the
                    // box can't express: "1. [ ] step" draws both.
                    return (DecorationKind.None, 0);
                }

                // A bulleted task item draws the box alone. The "- " is the bullet the checkbox sits on — it's
                // part of how the checkbox is spelled, not a second marker — so drawing both would show two
                // markers for the one item. (This is the asymmetry with a number, above.)
                return hasTask
                    ? (DecorationKind.None, 0)
                    : (DecorationKind.Bullet, _theme.BulletGutter);

            case TokenType.TaskMarker:
                return Checkbox();

            case TokenType.BlockquoteMarker:
                // One step per '>' (the marker carries its depth in Meta), not the measured width of the source
                // prefix: that barely grows with nesting, so a ">>" quote's text failed to clear the second bar
                // drawn to its left. The bars are drawn on the same step, so the two stay in register.
                var depth = int.TryParse(t.Meta, out var d) ? d : 1;
                return (DecorationKind.QuoteBar, _theme.QuoteGutter(depth));

            case TokenType.HorizontalRule:
                return (DecorationKind.HorizontalRule, 0); // an overlay rule; no advance (the line is otherwise empty)

            default:
                return (DecorationKind.None, 0);
        }
    }

    /// <summary>The checkbox decoration for a task marker spelled <paramref name="text"/> (<c>"[x] "</c>, box
    /// first). One kind for all three states — the state rides along as the decoration's text (the box character),
    /// which the draw path resolves to a colour and a symbol. The gutter is the same width whatever the state, so
    /// changing an item's state never shifts its text sideways.</summary>
    private (DecorationKind Kind, double Width) Checkbox() => (DecorationKind.Checkbox, _checkboxGutter);

    /// <summary>The box character a task marker's source <paramref name="text"/> (<c>"[x] "</c>) holds — what a
    /// <see cref="DecorationKind.Checkbox"/> run carries as its text.</summary>
    private static string BoxMark(string text) =>
        TaskStates.MarkText(TaskStates.FromMarker(text));

    /// <summary>
    /// Whether <paramref name="t"/>'s marker punctuation is hidden (PRD §7.4, revised to full WYSIWYG).
    /// Only <see cref="TokenRole.Marker"/> tokens ever conceal — content is always shown — and in normal
    /// mode every marker conceals regardless of the caret, except whole-line block fences whose row would
    /// blank out if hidden (collapsing their row is deferred, PRD §11). Raw-source mode reveals all.
    /// </summary>
    private bool MarkerConcealed(Token t)
    {
        if (_markersRevealed || t.Role != TokenRole.Marker)
        {
            return false;
        }

        // Whole-line fences (fenced code / front matter / table delimiter) stay visible; everything else
        // — heading/quote/list/task/HR prefixes and all inline emphasis/code/link/escape markers — hides.
        return t.Type is not (TokenType.CodeFence or TokenType.FrontMatterFence or TokenType.TableDelimiter);
    }

    /// <summary>Restyle a run as an inline-link hyperlink: link colour + underline, keeping its typeface/size.</summary>
    private RunStyle AsLink(RunStyle style) =>
        style with { Foreground = _theme.LinkForeground, Decorations = _theme.LinkUnderline };
}
