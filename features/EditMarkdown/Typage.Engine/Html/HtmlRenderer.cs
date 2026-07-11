using System.Text;
using Typage.Engine.Document;
using Typage.Engine.Markdown;

namespace Typage.Engine.Html;

/// <summary>
/// Renders a document to a self-contained HTML string (PRD §7.8 — "Export to HTML … fully
/// in-house … in scope for v1"). Pure and dependency-free: it walks the same
/// <see cref="MarkdownModel"/> token stream the live-preview surface uses, so what the CLI
/// exports matches what the editor shows. No external CSS, no network.
/// </summary>
public static class HtmlRenderer
{
    /// <summary>Render a complete, styled, self-contained HTML document.</summary>
    public static string RenderDocument(TextDocument document, string? title = null)
    {
        var body = RenderBody(document);
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n");
        sb.Append("<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<title>").Append(Escape(title ?? "Document")).Append("</title>\n");
        sb.Append("<style>\n").Append(DefaultCss).Append("\n</style>\n");
        sb.Append("</head>\n<body>\n");
        sb.Append(body);
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>Render just the body markup (no <c>&lt;html&gt;</c> wrapper) — handy for tests and embedding.</summary>
    public static string RenderBody(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var model = MarkdownModel.Build(document);
        var sb = new StringBuilder();
        var line = 0;
        var n = document.LineCount;
        while (line < n)
        {
            line = RenderBlock(document, model, line, sb);
        }

        return sb.ToString();
    }

    /// <summary>Render the block beginning at <paramref name="line"/>; return the next unconsumed line.</summary>
    private static int RenderBlock(TextDocument doc, MarkdownModel model, int line, StringBuilder sb)
    {
        var first = FirstToken(model, line);

        if (IsBlank(doc, line))
        {
            return line + 1;
        }

        // A setext underline is markup for the heading above it, which has already been rendered. It carries no
        // content of its own, so it emits nothing — otherwise it would close as an empty paragraph.
        if (first is { Type: TokenType.SetextUnderline })
        {
            return line + 1;
        }

        if (first is { Type: TokenType.FrontMatterFence })
        {
            return RenderFrontMatter(doc, model, line, sb);
        }

        if (first is { Type: TokenType.CodeFence })
        {
            return RenderCodeBlock(doc, model, line, sb);
        }

        if (first is { Type: TokenType.HeadingMarker })
        {
            var level = int.TryParse(first.Value.Meta, out var l) ? l : 1;
            sb.Append("<h").Append(level).Append('>');
            RenderInline(doc, model.LineTokens(line), sb);
            sb.Append("</h").Append(level).Append(">\n");
            return line + 1;
        }

        if (first is { Type: TokenType.HorizontalRule })
        {
            sb.Append("<hr>\n");
            return line + 1;
        }

        if (first is { Type: TokenType.BlockquoteMarker })
        {
            return RenderBlockquote(doc, model, line, sb);
        }

        if (first is { Type: TokenType.ListMarker })
        {
            return RenderList(doc, model, line, sb);
        }

        if (IsTableStart(doc, model, line))
        {
            return RenderTable(doc, model, line, sb);
        }

        return RenderParagraph(doc, model, line, sb);
    }

    private static int RenderFrontMatter(TextDocument doc, MarkdownModel model, int line, StringBuilder sb)
    {
        // Rendered as a collapsed "Properties" block (PRD §7.4).
        var n = doc.LineCount;
        var i = line + 1;
        var yaml = new StringBuilder();
        while (i < n && FirstToken(model, i) is not { Type: TokenType.FrontMatterFence })
        {
            yaml.Append(Escape(doc.GetLineText(i).TrimEnd('\r'))).Append('\n');
            i++;
        }

        if (i < n)
        {
            i++; // consume the closing fence
        }

        sb.Append("<details class=\"properties\"><summary>Properties</summary><pre>");
        sb.Append(yaml);
        sb.Append("</pre></details>\n");
        return i;
    }

    private static int RenderCodeBlock(TextDocument doc, MarkdownModel model, int line, StringBuilder sb)
    {
        var lang = FirstToken(model, line)?.Meta;
        var n = doc.LineCount;
        var i = line + 1;
        var code = new StringBuilder();
        while (i < n && model.EnteringState(i).Kind == BlockKind.FencedCode)
        {
            code.Append(Escape(doc.GetLineText(i).TrimEnd('\r'))).Append('\n');
            i++;
        }

        if (i < n && FirstToken(model, i) is { Type: TokenType.CodeFence })
        {
            i++; // consume the closing fence
        }

        sb.Append("<pre><code");
        if (!string.IsNullOrEmpty(lang))
        {
            sb.Append(" class=\"language-").Append(Escape(lang)).Append('"');
        }

        sb.Append('>').Append(code).Append("</code></pre>\n");
        return i;
    }

    private static int RenderBlockquote(TextDocument doc, MarkdownModel model, int line, StringBuilder sb)
        => RenderQuoteLevel(doc, model, line, depth: 1, sb);

    /// <summary>
    /// Render one blockquote nesting level, mirroring <see cref="RenderListLevel"/>: a line at this depth is a
    /// paragraph of this quote, a deeper-quoted run opens a nested <c>&lt;blockquote&gt;</c> inside it, and a
    /// shallower line returns to the caller. Returns the line after the quote.
    /// </summary>
    private static int RenderQuoteLevel(TextDocument doc, MarkdownModel model, int line, int depth, StringBuilder sb)
    {
        var n = doc.LineCount;
        var i = line;
        sb.Append("<blockquote>\n");
        while (i < n)
        {
            var d = QuoteDepth(model, i);
            if (d < depth)
            {
                break; // the quote ended, or stepped back out to an enclosing level
            }

            if (d > depth)
            {
                i = RenderQuoteLevel(doc, model, i, depth + 1, sb);
                continue;
            }

            sb.Append("<p>");
            RenderInline(doc, model.LineTokens(i), sb);
            sb.Append("</p>\n");
            i++;
        }

        sb.Append("</blockquote>\n");
        return i;
    }

    /// <summary>The blockquote nesting depth of <paramref name="line"/> (the number of <c>&gt;</c>), or 0 if it isn't a quote.</summary>
    private static int QuoteDepth(MarkdownModel model, int line) =>
        FirstToken(model, line) is { Type: TokenType.BlockquoteMarker } t && int.TryParse(t.Meta, out var d) ? d : 0;

    private static int RenderList(TextDocument doc, MarkdownModel model, int line, StringBuilder sb)
        => RenderListLevel(doc, model, line, ListIndent(doc, model, line), sb);

    /// <summary>
    /// Render one list level: items at <paramref name="levelIndent"/> are siblings, a deeper-indented
    /// run becomes a nested list inside the preceding item, and a shallower item returns to the caller.
    /// A single blank line between items keeps them in the same list (so ordered numbering continues).
    /// </summary>
    private static int RenderListLevel(TextDocument doc, MarkdownModel model, int line, int levelIndent, StringBuilder sb)
    {
        var n = doc.LineCount;
        var ordered = IsOrdered(doc, model, line);
        var start = ordered ? StartNumber(doc, model, line) : 1;
        sb.Append(ordered ? "<ol" : "<ul");
        if (ordered && start != 1)
        {
            sb.Append(" start=\"").Append(start).Append('"');
        }

        sb.Append(">\n");

        var i = line;
        while (i < n)
        {
            if (IsBlank(doc, i))
            {
                // Skip a run of blank lines if a list item at this level or deeper follows (loose list).
                var j = i;
                while (j < n && IsBlank(doc, j))
                {
                    j++;
                }

                if (j < n && IsListLine(model, j) && ListIndent(doc, model, j) >= levelIndent)
                {
                    i = j;
                    continue;
                }

                break;
            }

            if (!IsListLine(model, i))
            {
                break;
            }

            var indent = ListIndent(doc, model, i);
            if (indent < levelIndent)
            {
                break; // belongs to an outer list
            }

            if (indent > levelIndent)
            {
                break; // safety: a deeper item with no preceding sibling — let the caller handle it
            }

            var tokens = model.LineTokens(i);
            var hasTask = tokens.Count > 1 && tokens[1].Type == TokenType.TaskMarker;
            if (hasTask)
            {
                // Six states, not a bit — so the export draws the same coloured lamp the editor does rather than
                // an <input type=checkbox>, which can only say done or not (PRD §8.3.2). The item's text is
                // wrapped so a resolved item can be struck through without the rule crossing its lamp.
                var state = TaskStates.FromMark(tokens[1].Meta is { Length: > 0 } m ? m[0] : ' ');
                sb.Append("<li class=\"task ").Append(TaskClass(state)).Append("\"><span class=\"tbox\" role=\"img\" aria-label=\"");
                sb.Append(TaskStates.Label(state)).Append("\">").Append(TaskGlyph(state)).Append("</span><span class=\"ttext\">");
            }
            else
            {
                sb.Append("<li>");
            }

            RenderInline(doc, tokens, sb);
            if (hasTask)
            {
                sb.Append("</span>");
            }

            i++;

            // A deeper-indented run immediately below is a nested list inside this item.
            if (i < n && IsListLine(model, i) && ListIndent(doc, model, i) > levelIndent)
            {
                sb.Append('\n');
                i = RenderListLevel(doc, model, i, ListIndent(doc, model, i), sb);
            }

            sb.Append("</li>\n");
        }

        sb.Append(ordered ? "</ol>\n" : "</ul>\n");
        return i;
    }

    private static bool IsListLine(MarkdownModel model, int line) =>
        FirstToken(model, line) is { Type: TokenType.ListMarker };

    private static int ListIndent(TextDocument doc, MarkdownModel model, int line)
    {
        var first = FirstToken(model, line);
        return first is null ? 0 : first.Value.Start - doc.GetLineStart(line);
    }

    private static int StartNumber(TextDocument doc, MarkdownModel model, int line)
    {
        var first = FirstToken(model, line);
        if (first is null)
        {
            return 1;
        }

        var marker = doc.GetText(first.Value.Start, first.Value.Length);
        var digits = new string([.. marker.TakeWhile(char.IsAsciiDigit)]);
        return int.TryParse(digits, out var value) ? value : 1;
    }

    private static int RenderTable(TextDocument doc, MarkdownModel model, int line, StringBuilder sb)
    {
        var n = doc.LineCount;
        sb.Append("<table>\n<thead>\n");
        RenderTableRow(doc, model.LineTokens(line), "th", sb);
        sb.Append("</thead>\n");
        var i = line + 2; // skip header + delimiter
        var hasBody = i < n && HasTableTokens(model, i);
        if (hasBody)
        {
            sb.Append("<tbody>\n");
            while (i < n && HasTableTokens(model, i))
            {
                RenderTableRow(doc, model.LineTokens(i), "td", sb);
                i++;
            }

            sb.Append("</tbody>\n");
        }

        sb.Append("</table>\n");
        return i;
    }

    private static void RenderTableRow(TextDocument doc, IReadOnlyList<Token> tokens, string cellTag, StringBuilder sb)
    {
        sb.Append("<tr>");
        // Split the row's tokens into cells at the '|' markers, then render each cell's inline run.
        var cellStart = 0;
        for (var i = 0; i <= tokens.Count; i++)
        {
            var isPipe = i < tokens.Count && tokens[i].Type == TokenType.Marker
                && tokens[i].Length == 1 && doc.GetText(tokens[i].Start, 1) == "|";
            if (isPipe || i == tokens.Count)
            {
                if (i > cellStart || (isPipe && cellStart != 0))
                {
                    var cell = new StringBuilder();
                    RenderInline(doc, tokens, cellStart, i, cell);
                    var content = cell.ToString().Trim();
                    if (content.Length > 0 || (i != tokens.Count && cellStart != 0))
                    {
                        sb.Append('<').Append(cellTag).Append('>').Append(content)
                          .Append("</").Append(cellTag).Append('>');
                    }
                }

                cellStart = i + 1;
            }
        }

        sb.Append("</tr>\n");
    }

    private static int RenderParagraph(TextDocument doc, MarkdownModel model, int line, StringBuilder sb)
    {
        var n = doc.LineCount;
        sb.Append("<p>");
        var i = line;
        var firstLine = true;
        var prevHardBreak = false;
        while (i < n && !IsBlank(doc, i) && IsParagraphLine(doc, model, i))
        {
            if (!firstLine)
            {
                // A hard break at the end of the previous line becomes a <br>; otherwise a soft join.
                sb.Append(prevHardBreak ? "<br>\n" : "\n");
            }

            RenderInline(doc, model.LineTokens(i), sb);
            prevHardBreak = EndsWithHardBreak(model, i);
            firstLine = false;
            i++;
        }

        sb.Append("</p>\n");
        return i;
    }

    // ----- inline rendering (pairs link/image structure out of the flat token stream) -----

    private static void RenderInline(TextDocument doc, IReadOnlyList<Token> tokens, StringBuilder sb)
        => RenderInline(doc, tokens, 0, tokens.Count, sb);

    private static void RenderInline(TextDocument doc, IReadOnlyList<Token> tokens, int from, int to, StringBuilder sb)
    {
        var i = from;
        while (i < to)
        {
            var t = tokens[i];
            if (t.Type == TokenType.Marker)
            {
                var text = doc.GetText(t.Start, t.Length);
                if (text == "[")
                {
                    i = RenderLink(doc, tokens, i, to, sb);
                    continue;
                }

                if (text == "![")
                {
                    i = RenderImage(tokens, i, to, sb);
                    continue;
                }

                i++; // emphasis/code/escape/autolink/wiki/embed punctuation — concealed in HTML
                continue;
            }

            if (t.Role == TokenRole.Marker)
            {
                i++; // block markers (heading #, list bullet, quote >, task box) aren't rendered as text
                continue;
            }

            RenderContentToken(doc, t, sb);
            i++;
        }
    }

    private static int RenderLink(TextDocument doc, IReadOnlyList<Token> tokens, int i, int to, StringBuilder sb)
    {
        // tokens: "[" inner... "](" LinkUrl ")"
        var depth = 1;
        var j = i + 1;
        while (j < to)
        {
            if (tokens[j].Type == TokenType.Marker)
            {
                var m = doc.GetText(tokens[j].Start, tokens[j].Length);
                if (m is "[" or "![")
                {
                    depth++;
                }
                else if (m == "](")
                {
                    depth--;
                    if (depth == 0)
                    {
                        break;
                    }
                }
            }

            j++;
        }

        if (j + 1 >= to)
        {
            return to; // malformed (shouldn't happen given the tokenizer)
        }

        var href = tokens[j + 1].Meta ?? string.Empty;
        sb.Append("<a href=\"").Append(EscapeAttr(href)).Append("\">");
        RenderInline(doc, tokens, i + 1, j, sb);
        sb.Append("</a>");
        return j + 3; // past LinkUrl and ")"
    }

    private static int RenderImage(IReadOnlyList<Token> tokens, int i, int to, StringBuilder sb)
    {
        // tokens: "![" ImageAlt "](" ImageUrl ")"
        if (i + 4 >= to)
        {
            return to;
        }

        var alt = tokens[i + 1].Meta ?? string.Empty;
        var src = tokens[i + 3].Meta ?? string.Empty;
        sb.Append("<img src=\"").Append(EscapeAttr(src)).Append("\" alt=\"").Append(EscapeAttr(alt)).Append("\">");
        return i + 5;
    }

    private static void RenderContentToken(TextDocument doc, Token t, StringBuilder sb)
    {
        var text = doc.GetText(t.Start, t.Length);
        switch (t.Type)
        {
            case TokenType.Emphasis:
                sb.Append("<em>").Append(Escape(text)).Append("</em>");
                break;
            case TokenType.Strong:
                sb.Append("<strong>").Append(Escape(text)).Append("</strong>");
                break;
            case TokenType.StrongEmphasis:
                sb.Append("<strong><em>").Append(Escape(text)).Append("</em></strong>");
                break;
            case TokenType.Strikethrough:
                sb.Append("<del>").Append(Escape(text)).Append("</del>");
                break;
            case TokenType.Highlight:
                sb.Append("<mark>").Append(Escape(text)).Append("</mark>");
                break;
            case TokenType.InlineCode:
                sb.Append("<code>").Append(Escape(text)).Append("</code>");
                break;
            case TokenType.Autolink:
                var url = t.Meta ?? text;
                sb.Append("<a href=\"").Append(EscapeAttr(url)).Append("\">").Append(Escape(text)).Append("</a>");
                break;
            case TokenType.WikiLink:
                sb.Append("<span class=\"wikilink\">").Append(Escape(text)).Append("</span>");
                break;
            case TokenType.Embed:
                sb.Append("<span class=\"embed\">").Append(Escape(text)).Append("</span>");
                break;
            case TokenType.Escape:
                sb.Append(Escape(text));
                break;
            default:
                sb.Append(Escape(text));
                break;
        }
    }

    // ----- line classification helpers -----

    private static Token? FirstToken(MarkdownModel model, int line)
    {
        var tokens = model.LineTokens(line);
        return tokens.Count > 0 ? tokens[0] : null;
    }

    private static bool IsBlank(TextDocument doc, int line) => doc.GetLineText(line).TrimEnd('\r').Length == 0;

    private static bool EndsWithHardBreak(MarkdownModel model, int line)
    {
        var tokens = model.LineTokens(line);
        return tokens.Count > 0 && tokens[^1].Type == TokenType.HardBreak;
    }

    private static bool IsParagraphLine(TextDocument doc, MarkdownModel model, int line)
    {
        var first = FirstToken(model, line);
        if (first is null)
        {
            return false;
        }

        return first.Value.Type is not (TokenType.HeadingMarker or TokenType.BlockquoteMarker
            or TokenType.ListMarker or TokenType.HorizontalRule or TokenType.CodeFence
            or TokenType.FrontMatterFence or TokenType.TableDelimiter or TokenType.SetextUnderline)
            && !IsTableStart(doc, model, line);
    }

    private static bool IsOrdered(TextDocument doc, MarkdownModel model, int line)
    {
        var first = FirstToken(model, line);
        if (first is null)
        {
            return false;
        }

        var marker = doc.GetText(first.Value.Start, first.Value.Length);
        return marker.Length > 0 && char.IsAsciiDigit(marker[0]);
    }

    private static bool IsTableStart(TextDocument doc, MarkdownModel model, int line)
    {
        if (line + 1 >= doc.LineCount)
        {
            return false;
        }

        return HasTableTokens(model, line) && FirstToken(model, line + 1) is { Type: TokenType.TableDelimiter };
    }

    private static bool HasTableTokens(MarkdownModel model, int line)
    {
        foreach (var t in model.LineTokens(line))
        {
            if (t.Type is TokenType.Marker or TokenType.TableDelimiter)
            {
                return true;
            }
        }

        return false;
    }

    // ----- escaping -----

    internal static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    private static string EscapeAttr(string s)
    {
        return Escape(s).Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    /// <summary>The CSS class that carries a task item's state colour (see <see cref="DefaultCss"/>).</summary>
    private static string TaskClass(TaskState state) => state switch
    {
        TaskState.InProgress => "t-prog",
        TaskState.Blocked => "t-block",
        TaskState.Deferred => "t-defer",
        TaskState.Done => "t-done",
        TaskState.Cancelled => "t-cancel",
        _ => "t-not",
    };

    /// <summary>
    /// The symbol drawn inside a task item's circular lamp, as inline SVG in <c>currentColor</c> (the lamp's ring
    /// and tint come from the CSS). Not-started draws nothing — an un-lit lamp is an empty ring.
    ///
    /// <para>These are the export's copy of the shapes the editing surface draws (<c>Typage.Control</c>'s
    /// <c>TaskGlyphs</c>): each surface owns its own presentation — the engine has no UI dependency and the
    /// control has no HTML — so the two are written from the same six-symbol vocabulary, not shared code. Keep
    /// them in step.</para>
    /// </summary>
    private static string TaskGlyph(TaskState state)
    {
        var path = state switch
        {
            // A disc filled to its meridian: work is half-done, and the shape says so at a glance.
            TaskState.InProgress => """<path d="M12 3a9 9 0 0 1 0 18Z" fill="currentColor"/>""",
            TaskState.Blocked => """<path d="M12 5.5v8" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"/><circle cx="12" cy="18" r="1.5" fill="currentColor"/>""",
            TaskState.Deferred => """<path d="M6 6l5 6-5 6M12 6l5 6-5 6" stroke="currentColor" stroke-width="2.3" stroke-linecap="round" stroke-linejoin="round"/>""",
            TaskState.Done => """<path d="M5.5 12.5 10 17l8.5-10" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"/>""",
            TaskState.Cancelled => """<path d="M7 7l10 10M17 7 7 17" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"/>""",
            _ => string.Empty,
        };

        return path.Length == 0 ? string.Empty : $"""<svg viewBox="0 0 24 24" fill="none" aria-hidden="true">{path}</svg>""";
    }

    private const string DefaultCss = """
        :root { color-scheme: light dark; }
        body { max-width: 44rem; margin: 2rem auto; padding: 0 1rem;
               font: 16px/1.6 -apple-system, "Segoe UI", system-ui, sans-serif; }
        h1, h2, h3, h4, h5, h6 { line-height: 1.25; }
        code { font-family: ui-monospace, "SF Mono", Menlo, monospace; font-size: 0.9em;
               background: rgba(127,127,127,0.15); padding: 0.1em 0.3em; border-radius: 4px; }
        pre { background: rgba(127,127,127,0.12); padding: 1rem; border-radius: 8px; overflow:auto; }
        pre code { background: none; padding: 0; }
        blockquote { border-left: 3px solid rgba(127,127,127,0.5); margin: 0; padding: 0 1rem; color: #666; }
        mark { background: #fdf6b2; }
        table { border-collapse: collapse; }
        th, td { border: 1px solid rgba(127,127,127,0.4); padding: 0.4em 0.7em; }
        .properties { color: #888; font-size: 0.85em; }
        img { max-width: 100%; }

        /* Task states (PRD §8.3.2). Two complementary pairs anchor the six hues: done/cancelled are the
           resolved ends (finished vs. abandoned), in-progress/blocked the in-flight ones (flowing vs. stuck);
           deferred is a cool violet (dormant, not dead) and not-started stays achromatic — the zero point. */
        :root { --t-not:#6B7383; --t-prog:#1668D6; --t-block:#B26205;
                --t-defer:#6D3FD1; --t-done:#12855A; --t-cancel:#C3352E; --t-off:#6E7781; }
        @media (prefers-color-scheme: dark) {
          :root { --t-not:#8B94A7; --t-prog:#3E9BFF; --t-block:#F49A38;
                  --t-defer:#A184F7; --t-done:#33C77D; --t-cancel:#E1564F; --t-off:#9AA3B5; }
        }
        li.task { list-style: none; margin-left: -1.35em; }
        li.task > .tbox { display: inline-flex; align-items: center; justify-content: center;
                          width: 1.15em; height: 1.15em; margin-right: 0.45em; vertical-align: -0.22em;
                          border: 1.5px solid currentColor; border-radius: 50%;
                          background: rgba(127,127,127,0.10); color: var(--t-not); }
        li.task > .tbox svg { width: 0.72em; height: 0.72em; display: block; }
        li.t-not > .tbox { background: none; opacity: 0.75; }  /* an un-lit lamp: no hue, no glow */
        li.t-prog > .tbox { color: var(--t-prog); }
        li.t-block > .tbox { color: var(--t-block); }
        li.t-defer > .tbox { color: var(--t-defer); }
        li.t-done > .tbox { color: var(--t-done); }
        li.t-cancel > .tbox { color: var(--t-cancel); }
        li.t-done > .ttext, li.t-cancel > .ttext { color: var(--t-off); text-decoration: line-through; }
        """;
}
