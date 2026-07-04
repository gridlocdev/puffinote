using Avalonia.Media;
using Typage.Engine.Document;
using Typage.Engine.Markdown;

namespace Typage.Control.Rendering;

/// <summary>
/// The volatile control state a single block layout reads: the document and its token model, the active
/// theme, the block→line index, the two view-mode flags, the wrap width, the checkbox gutter width, and a
/// callback that renders an ordered-list marker's displayed number (kept on the control because it depends
/// on the list's document structure). Passed to <see cref="BlockLayoutBuilder.Build"/> per call so the
/// builder holds no long-lived control references.
/// </summary>
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
        for (var line = startLine; line <= endLine; line++)
        {
            var lineStart = doc.GetLineStart(line);
            var lineText = doc.GetText(lineStart, doc.GetLineEnd(line) - lineStart).TrimEnd('\r');
            var headingLevel = HeadingLevel(line);
            lastHeading = headingLevel;
            var hasTask = _model.LineTokens(line).Any(t => t.Type == TokenType.TaskMarker);
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

                if (s > pos)
                {
                    // The gap (including a joined newline) is plain text — unless it falls inside a link's text.
                    var gapStyle = DefaultStyle(headingLevel);
                    if (InLinkText(linkSpans, pos + blockStart, s + blockStart))
                    {
                        gapStyle = AsLink(gapStyle);
                    }

                    segments.Add(new VisualLine.Segment(pos, s, gapStyle, false));
                }

                // The decoration helpers index into the *line* text, so keep line-relative offsets for them;
                // the segment itself is placed at block-relative columns.
                var sLine = Math.Clamp(t.Start - lineStart, 0, lineText.Length);
                var enLine = Math.Clamp(t.End - lineStart, 0, lineText.Length);
                var style = StyleFor(t.Type, headingLevel);
                var concealed = MarkerConcealed(t);

                if (concealed)
                {
                    // A concealed line-prefix marker may render as a synthetic decoration (bullet, checkbox,
                    // quote bar, rule) that keeps the content's hanging indent; otherwise it just vanishes.
                    var (deco, width) = DecorationFor(t, lineText, sLine, enLine, hasTask, style);
                    var decoText = string.Empty;

                    // An ordered-list marker hides its literal digits and shows a number computed from the
                    // list's structure (items 4 & 5). Raw-source mode shows the literal source digits.
                    if (t.Type == TokenType.ListMarker && !hasTask
                        && _orderedMarkerText(line, lineText, sLine, enLine) is { } ordinalText)
                    {
                        decoText = ordinalText;
                        deco = DecorationKind.OrderedNumber;
                        width = _measurer.MeasureWidth(decoText, style);
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
                    AddContentSegments(segments, s, en, text, style, t.Type);
                }

                pos = en;
            }
        }

        if (pos < contentLen)
        {
            segments.Add(new VisualLine.Segment(pos, contentLen, DefaultStyle(lastHeading), false));
        }

        return VisualLine.Build(text, segments, _measurer.MeasureWidth, _wrapWidth);
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

    private RunStyle DefaultStyle(int headingLevel)
    {
        var style = _theme.Resolve(TokenType.Text);
        return headingLevel > 0 ? style with { FontSize = _theme.HeadingSize(headingLevel), Typeface = HeadingTypeface(style) } : style;
    }

    private RunStyle StyleFor(TokenType type, int headingLevel)
    {
        var style = _theme.Resolve(type, headingLevel);
        // On a heading line, scale everything — including the leading '#' marker — to the heading
        // size so the markers sit at the same size as the heading text on that line.
        if (headingLevel > 0)
        {
            style = style with { FontSize = _theme.HeadingSize(headingLevel) };
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
        var nested = FindNestedConstructs(content);
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

    /// <summary>
    /// The top-level paired delimiter constructs inside <paramref name="content"/> — <c>~~</c>, <c>==</c>, and
    /// <c>*</c>/<c>_</c> emphasis (run 1–3) — using the same tolerance as the engine tokenizer (no leading/
    /// trailing inner whitespace, a close run at least as long as the open). One level deep, which covers
    /// emphasis wrapping a strike/highlight and vice versa; deeper nesting is left as literal text.
    /// </summary>
    private static List<NestedConstruct> FindNestedConstructs(string content)
    {
        var found = new List<NestedConstruct>();
        var i = 0;
        while (i < content.Length)
        {
            var c = content[i];
            if (c is '~' or '=' or '*' or '_')
            {
                var run = DelimRun(content, i, c);
                var count = c is '~' or '=' ? 2 : Math.Min(run, 3);
                var innerStart = i + count;
                if (run >= count && innerStart < content.Length
                    && content[innerStart] != c && !char.IsWhiteSpace(content[innerStart]))
                {
                    var close = FindClose(content, innerStart, c, count);
                    if (close > innerStart)
                    {
                        found.Add(new NestedConstruct(i, close, count, c));
                        i = close + count;
                        continue;
                    }
                }
            }

            i++;
        }

        return found;
    }

    /// <summary>Index of the closing delimiter run (≥ <paramref name="count"/> of <paramref name="c"/>, not
    /// preceded by whitespace) at or after <paramref name="from"/>; -1 if none.</summary>
    private static int FindClose(string content, int from, char c, int count)
    {
        var j = from;
        while (j < content.Length)
        {
            if (content[j] == c && !char.IsWhiteSpace(content[j - 1]))
            {
                if (DelimRun(content, j, c) >= count)
                {
                    return j;
                }

                j += DelimRun(content, j, c);
            }
            else
            {
                j++;
            }
        }

        return -1;
    }

    private static int DelimRun(string s, int i, char c)
    {
        var n = 0;
        while (i + n < s.Length && s[i + n] == c)
        {
            n++;
        }

        return n;
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

    /// <summary>The synthetic decoration (and its gutter width) that replaces concealed marker <paramref name="t"/>, if any.</summary>
    private (DecorationKind Kind, double Width) DecorationFor(Token t, string lineText, int s, int en, bool hasTask, RunStyle style)
    {
        var text = lineText[s..en];
        switch (t.Type)
        {
            case TokenType.ListMarker:
                if (hasTask)
                {
                    return (DecorationKind.None, 0); // the checkbox represents the whole item
                }

                var bullet = text.TrimStart(' ');
                return bullet.Length > 0 && bullet[0] is '-' or '*' or '+'
                    ? (DecorationKind.Bullet, _measurer.MeasureWidth(text, style))
                    : (DecorationKind.None, 0);

            case TokenType.TaskMarker:
                // A fixed gutter (box + gap) regardless of "[ ]" vs "[x]", so the text after the checkbox sits
                // at the same place whether it's checked or not, with a clear space before it.
                var ticked = text.Length > 1 && text[1] is 'x' or 'X';
                return (ticked ? DecorationKind.CheckboxChecked : DecorationKind.CheckboxUnchecked, _checkboxGutter);

            case TokenType.BlockquoteMarker:
                return (DecorationKind.QuoteBar, _measurer.MeasureWidth(text, style));

            case TokenType.HorizontalRule:
                return (DecorationKind.HorizontalRule, 0); // an overlay rule; no advance (the line is otherwise empty)

            default:
                return (DecorationKind.None, 0);
        }
    }

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

    private static Typeface HeadingTypeface(RunStyle baseStyle) =>
        new(baseStyle.Typeface.FontFamily, FontStyle.Normal, FontWeight.Bold);

    /// <summary>Restyle a run as an inline-link hyperlink: link colour + underline, keeping its typeface/size.</summary>
    private RunStyle AsLink(RunStyle style) =>
        style with { Foreground = _theme.LinkForeground, Decorations = _theme.LinkUnderline };

    /// <summary>One nested inline construct found inside another's content: the delimiter, its run length, and
    /// the inner span (all relative to the enclosing content string).</summary>
    private readonly record struct NestedConstruct(int OpenStart, int InnerEnd, int MarkerLen, char Delim);
}
