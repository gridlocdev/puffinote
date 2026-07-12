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
                // Three states, not a bit — so the export draws the same box the editor does rather than an
                // <input type=checkbox>, which can only say done or not (PRD §8.3.2). The item's text is wrapped
                // so a done item can be struck through without the rule crossing its box.
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

    /// <summary>The CSS class that carries a task item's state (see <see cref="DefaultCss"/>).</summary>
    private static string TaskClass(TaskState state) => state switch
    {
        TaskState.InProgress => "t-prog",
        TaskState.Done => "t-done",
        _ => "t-not",
    };

    /// <summary>
    /// The symbol drawn inside a task item's box, as inline SVG in <c>currentColor</c> (the box, its outline, and
    /// its fill come from the CSS). Not-started draws nothing — a resting box is an empty square.
    ///
    /// <para>These are the export's copy of the shapes the editing surface draws (<c>Typage.Control</c>'s
    /// <c>TaskGlyphs</c>): each surface owns its own presentation — the engine has no UI dependency and the
    /// control has no HTML — so the two are written from the same vocabulary, not shared code. The design box is
    /// the checkbox itself, so the path data and the stroke widths here are <b>character-for-character</b> the
    /// ones <c>TaskGlyphs</c> uses. Keep them in step.</para>
    /// </summary>
    private static string TaskGlyph(TaskState state)
    {
        var path = state switch
        {
            // Half-drawn work, said with half a check.
            TaskState.InProgress => """<path d="M6.7 12 L17.3 12" stroke="currentColor" stroke-width="2.8" stroke-linecap="round"/>""",
            TaskState.Done => """<path d="M6.5 12.6 L10.2 16.3 L17.6 7.9" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/>""",
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

        /* Task states (PRD §8.3.2). One accent, single-hue: the dash and the check share it, so "under way" and
           "done" read as two points on one path. A resting box is a bare outline in the quiet border colour. The
           svg is sized to the whole box, so its two paths land at the same proportions the editor draws them at. */
        :root { --t-border:#B1B8C2; --t-accent:#216BE0; --t-check:#fff; --t-off:#6E7781; }
        @media (prefers-color-scheme: dark) {
          :root { --t-border:#5A6272; --t-accent:#3B82F6; --t-check:#fff; --t-off:#9AA3B5; }
        }
        li.task { list-style: none; margin-left: -1.35em; }
        li.task > .tbox { position: relative; display: inline-block; box-sizing: border-box;
                          width: 1.15em; height: 1.15em; margin-right: 0.45em; vertical-align: -0.22em;
                          border: 1.5px solid var(--t-border); border-radius: 0.32em; }
        li.task > .tbox svg { position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%);
                              width: 1.15em; height: 1.15em; display: block; }
        li.t-prog > .tbox { color: var(--t-accent); }
        li.t-done > .tbox { background: var(--t-accent); border-color: var(--t-accent); color: var(--t-check); }
        li.t-done > .ttext { color: var(--t-off); text-decoration: line-through; }
        """;
}
