using System.Globalization;
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

/// <summary>
/// The embeddable live-preview editing surface. A custom Avalonia control (not a
/// <c>TextBox</c>) that draws the document from the engine's token stream with the
/// <see cref="MarkdownTheme"/>, owns caret/selection/input, and conceals (dims) syntax markers
/// when the caret is away from their line — the "live preview" feel (PRD §7.3, §7.4).
/// </summary>
/// <remarks>
/// This is an early Phase 3/4 surface: rendering uses <see cref="FormattedText"/> per line and
/// offsets stay 1:1 with the visual model (markers dim rather than collapse), so caret mapping is
/// exact and simple. The surface is line-virtualized (only the visible slice is drawn, via
/// <see cref="ILogicalScrollable"/>) and re-tokenizes incrementally from the first changed line.
/// Full glyph-hiding with an offset↔visual map, IME, and an automation peer are the dedicated later
/// phases (PRD §11). The public API and the engine seam below are stable so that work slots in.
/// </remarks>
public class MarkdownEditor : Avalonia.Controls.Control, ILogicalScrollable
{
    private readonly DocumentBuffer _buffer = new();
    private MarkdownModel _model;
    private MarkdownTheme _theme = MarkdownTheme.Default;
    private IBrush _dimMarker = new SolidColorBrush(Color.FromArgb(0x46, 0x6E, 0x76, 0x81));

    // Virtualized logical-scroll state: only the lines inside the viewport are drawn, and the
    // horizontal extent grows as wide lines are encountered (no whole-document width scan).
    private Vector _offset;
    private Size _viewport;
    private double _extentWidth;
    private IReadOnlyList<SearchMatch> _searchMatches = Array.Empty<SearchMatch>();
    private int _currentMatch = -1;
    private bool _canHorizontallyScroll = true;
    private bool _canVerticallyScroll = true;
    private EventHandler? _scrollInvalidated;

    public MarkdownEditor()
    {
        Focusable = true;
        _model = MarkdownModel.Build(_buffer.Document);
        _buffer.TextChanged += (_, _) =>
        {
            // Incremental re-tokenization: reuse the unchanged prefix and re-run only from the first
            // changed line down (PRD §8.2). Equivalent to a full rebuild — see IncrementalTokenizerTests.
            _model = _model.Update(_buffer.Document, _buffer.LastChangedLine, out _);
            _goalX = double.NaN; // an edit moves the caret; the next Up/Down recomputes the goal column
            RaiseScrollInvalidated(EventArgs.Empty); // line count / extent may have changed
            InvalidateVisual();
            TextChanged?.Invoke(this, EventArgs.Empty);
        };
        _buffer.Changed += (_, _) =>
        {
            InvalidateVisual();
            ScrollCaretIntoView();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
        ContextMenu = BuildContextMenu();
        UpdateDerivedBrushes();
    }

    /// <summary>Raised after the document text changes.</summary>
    public event EventHandler? TextChanged;

    /// <summary>Raised after the caret or selection changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The full document text. Setting it replaces the document and clears undo history.</summary>
    public string Text
    {
        get => _buffer.Document.GetText();
        set => _buffer.Load(value);
    }

    /// <summary>True when the buffer differs from the last-saved snapshot.</summary>
    public bool IsDirty => _buffer.IsDirty;

    /// <summary>Live document statistics for the host's status bar (cached; recomputed on text change only).</summary>
    public DocumentStats Stats => _buffer.Stats;

    /// <summary>The caret position as a 1-based line:column for the status bar.</summary>
    public LinePosition CaretPosition => _buffer.Document.GetPosition(_buffer.Selection.Caret);

    /// <summary>The currently selected text (empty when there is no selection).</summary>
    public string SelectedText
    {
        get
        {
            var sel = _buffer.Selection;
            return sel.IsEmpty ? string.Empty : _buffer.Document.GetText(sel.Start, sel.Length);
        }
    }

    /// <summary>The number of grapheme clusters currently selected (0 when the selection is empty).</summary>
    public int SelectedCharacterCount
    {
        get
        {
            var sel = _buffer.Selection;
            return sel.IsEmpty ? 0 : Grapheme.Count(_buffer.Document.GetText(sel.Start, sel.Length));
        }
    }

    /// <summary>The editable model, exposed for the host (save/load/dirty) and tests.</summary>
    internal DocumentBuffer Buffer => _buffer;

    public bool CanUndo => _buffer.CanUndo;

    public bool CanRedo => _buffer.CanRedo;

    /// <summary>The active theme (presentation seam — swap to re-style without touching the engine).</summary>
    public MarkdownTheme EditorTheme
    {
        get => _theme;
        set
        {
            _theme = value;
            UpdateDerivedBrushes();
            _widthCache.Clear(); // cached widths assume the old fonts/sizes
            _formattedCache.Clear(); // cached glyph runs were built with the old theme's brushes/fonts
            _extentWidth = 0;    // fonts changed — let the horizontal extent regrow as lines redraw
            RaiseScrollInvalidated(EventArgs.Empty);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>The faded marker brush is derived from the theme's marker color so it tracks light/dark/HC.</summary>
    private void UpdateDerivedBrushes()
    {
        if (_theme.MarkerForeground is ISolidColorBrush b)
        {
            _dimMarker = new SolidColorBrush(b.Color, 0.32);
        }
    }

    /// <summary>Resolves embed targets to controls; defaults to <see cref="NullEmbedResolver"/> (embeds show as source).</summary>
    public IEmbedResolver EmbedResolver { get; set; } = NullEmbedResolver.Instance;

    /// <summary>Number of spaces inserted by Tab (PRD §7.2 — default 4, configurable in Settings).</summary>
    public int IndentWidth { get; set; } = 4;

    /// <summary>
    /// When true the surface shows the raw Markdown source: syntax markers are always revealed at full
    /// strength instead of dimming away from the caret (PRD §7.4 raw-source view). A per-document
    /// session toggle whose default comes from Settings.
    /// </summary>
    public bool ShowRawSource
    {
        get => _showRawSource;
        set
        {
            if (_showRawSource == value)
            {
                return;
            }

            _showRawSource = value;
            InvalidateVisual();
        }
    }

    private bool _showRawSource;

    public void MarkSaved() => _buffer.MarkSaved();

    /// <summary>Replace the document but leave it marked dirty (used for crash recovery, PRD §9.4).</summary>
    public void LoadDirty(string text) => _buffer.Load(text, markSaved: false);

    public void Undo() => _buffer.Undo();

    public void Redo() => _buffer.Redo();

    // ----- formatting commands (toolbar / Format menu — PRD §7.5) -----

    public void ToggleBold() => Wrap("**", "**");

    public void ToggleItalic() => Wrap("*", "*");

    public void ToggleInlineCode() => Wrap("`", "`");

    public void ToggleStrikethrough() => Wrap("~~", "~~");

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

    /// <summary>Wrap the selection in <paramref name="left"/>/<paramref name="right"/>, or insert the pair at the caret.</summary>
    public void Wrap(string left, string right)
    {
        var sel = _buffer.Selection;
        if (sel.IsEmpty)
        {
            _buffer.ReplaceRange(sel.Caret, 0, left + right);
            _buffer.Selection = Selection.At(sel.Caret + left.Length);
        }
        else
        {
            var inner = _buffer.Document.GetText(sel.Start, sel.Length);
            _buffer.ReplaceRange(sel.Start, sel.Length, left + inner + right);
            _buffer.Selection = new Selection(sel.Start + left.Length, sel.Start + left.Length + inner.Length);
        }

        Focus();
    }

    /// <summary>Wrap the selection as a link <c>[text](url)</c>, leaving the caret in the URL slot.</summary>
    public void InsertLink()
    {
        var sel = _buffer.Selection;
        var text = sel.IsEmpty ? string.Empty : _buffer.Document.GetText(sel.Start, sel.Length);
        var replacement = $"[{text}]()";
        _buffer.ReplaceRange(sel.Start, sel.Length, replacement);
        _buffer.Selection = Selection.At(sel.Start + replacement.Length - 1); // inside ()
        Focus();
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

    /// <summary>Select the whole document.</summary>
    public void SelectAll() => _buffer.Selection = new Selection(0, _buffer.Document.Length);

    // ----- find / replace support (the find bar lives in the shell) -----

    /// <summary>Highlight the given search matches, marking <paramref name="current"/> as the active one.</summary>
    public void SetSearchMatches(IReadOnlyList<SearchMatch> matches, int current)
    {
        _searchMatches = matches;
        _currentMatch = current;
        InvalidateVisual();
    }

    public void ClearSearchMatches()
    {
        _searchMatches = Array.Empty<SearchMatch>();
        _currentMatch = -1;
        InvalidateVisual();
    }

    /// <summary>Select <c>[start, start+length)</c> and scroll it into view (used by find navigation).</summary>
    public void Select(int start, int length)
    {
        var len = _buffer.Document.Length;
        start = Math.Clamp(start, 0, len);
        var end = Math.Clamp(start + length, 0, len);
        _buffer.Selection = new Selection(start, end);
    }

    /// <summary>Replace <c>[start, start+length)</c> with <paramref name="replacement"/> (used by find &amp; replace).</summary>
    public void Replace(int start, int length, string replacement) => _buffer.ReplaceRange(start, length, replacement);

    // ----- clipboard (PRD §7.2 — copy yields Markdown source) -----
    //
    // The continuation after a clipboard await can resume off the UI thread, and the document edit
    // it performs raises events that call InvalidateVisual()/scroll — which Avalonia rejects from a
    // non-UI thread (that was the "paste crashes the app" bug). So every edit here is marshalled back
    // to the UI thread, and clipboard calls are guarded so a clipboard failure degrades to a no-op.

    public async Task CopyAsync()
    {
        var sel = _buffer.Selection;
        if (sel.IsEmpty || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        var text = _buffer.Document.GetText(sel.Start, sel.Length); // read before the await (UI thread)
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
        if (_buffer.Selection.IsEmpty)
        {
            return;
        }

        await CopyAsync();
        RunOnUiThread(() => _buffer.Insert(string.Empty)); // delete the selection on the UI thread
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
            RunOnUiThread(() => _buffer.Insert(text));
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

    private ContextMenu BuildContextMenu()
    {
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        var menu = new ContextMenu();
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
        menu.Items.Add(Item("Link…", InsertLink));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Undo", () => _buffer.Undo()));
        menu.Items.Add(Item("Redo", () => _buffer.Redo()));
        return menu;
    }

    // ----- layout & logical scrolling -----

    protected override Size MeasureOverride(Size availableSize)
    {
        // Under logical scrolling the presenter constrains us to the viewport; report that. When a
        // dimension is unconstrained (not in a scroll viewer), fall back to the content size.
        var w = double.IsFinite(availableSize.Width) ? availableSize.Width : _extentWidth;
        var h = double.IsFinite(availableSize.Height)
            ? availableSize.Height
            : Math.Max(_theme.LineHeight, _buffer.Document.LineCount * _theme.LineHeight);
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_viewport != finalSize)
        {
            _viewport = finalSize;
            RaiseScrollInvalidated(EventArgs.Empty);
        }

        return finalSize;
    }

    Size IScrollable.Extent => new(
        Math.Max(_theme.PaddingLeft * 2 + _extentWidth, _viewport.Width),
        Math.Max(_theme.LineHeight, _buffer.Document.LineCount * _theme.LineHeight) + _theme.PaddingTop * 2);

    Size IScrollable.Viewport => _viewport;

    Vector IScrollable.Offset
    {
        get => _offset;
        set
        {
            var clamped = ClampOffset(value);
            if (clamped != _offset)
            {
                _offset = clamped;
                InvalidateVisual();
            }
        }
    }

    /// <summary>Whether horizontal scrolling is enabled (set by the hosting scroll viewer).</summary>
    public bool CanHorizontallyScroll
    {
        get => _canHorizontallyScroll;
        set => _canHorizontallyScroll = value;
    }

    /// <summary>Whether vertical scrolling is enabled (set by the hosting scroll viewer).</summary>
    public bool CanVerticallyScroll
    {
        get => _canVerticallyScroll;
        set => _canVerticallyScroll = value;
    }

    bool ILogicalScrollable.IsLogicalScrollEnabled => true;

    Size ILogicalScrollable.ScrollSize => new(_theme.BaseFontSize, _theme.LineHeight);

    Size ILogicalScrollable.PageScrollSize => new(_viewport.Width, _viewport.Height);

    event EventHandler? ILogicalScrollable.ScrollInvalidated
    {
        add => _scrollInvalidated += value;
        remove => _scrollInvalidated -= value;
    }

    bool ILogicalScrollable.BringIntoView(Avalonia.Controls.Control target, Rect targetRect) => false;

    Avalonia.Controls.Control? ILogicalScrollable.GetControlInDirection(NavigationDirection direction, Avalonia.Controls.Control? from) => null;

    void ILogicalScrollable.RaiseScrollInvalidated(EventArgs e) => RaiseScrollInvalidated(e);

    private void RaiseScrollInvalidated(EventArgs e) => _scrollInvalidated?.Invoke(this, e);

    private Vector ClampOffset(Vector value)
    {
        var extent = ((IScrollable)this).Extent;
        var maxX = Math.Max(0, extent.Width - _viewport.Width);
        var maxY = Math.Max(0, extent.Height - _viewport.Height);
        return new Vector(Math.Clamp(value.X, 0, maxX), Math.Clamp(value.Y, 0, maxY));
    }

    // ----- rendering -----

    public override void Render(DrawingContext context)
    {
        var doc = _buffer.Document;
        context.FillRectangle(_theme.Background, new Rect(Bounds.Size));

        var caret = _buffer.Selection;
        var caretLine = doc.GetPosition(caret.Caret).Line;

        // Draw only the lines intersecting the viewport (virtualization), translated by the scroll
        // offset. The viewport height is 0 until the control is arranged in a scroll viewer.
        var lineH = _theme.LineHeight;
        var viewportH = _viewport.Height > 0 ? _viewport.Height : Bounds.Height;
        var contentTop = _offset.Y - _theme.PaddingTop;
        var first = Math.Max(0, (int)(contentTop / lineH));
        var last = Math.Min(doc.LineCount, (int)((contentTop + viewportH) / lineH) + 1);

        // Content is drawn in document space, inset by the padding and shifted by the scroll offset.
        using (context.PushTransform(Matrix.CreateTranslation(_theme.PaddingLeft - _offset.X, _theme.PaddingTop - _offset.Y)))
        {
            DrawPropertiesPanel(context, first, last); // front-matter "Properties" backdrop, under the text

            var maxWidth = _extentWidth;
            for (var line = first; line < last; line++)
            {
                maxWidth = Math.Max(maxWidth, RenderLine(context, doc, line, caret, caretLine));
            }

            if (maxWidth > _extentWidth)
            {
                _extentWidth = maxWidth; // grow the horizontal extent as wide lines come into view
                // Notify the scroll viewer AFTER this render pass — invalidating a visual (the scroll
                // bar) during render throws ("Visual was invalidated during the render pass").
                Dispatcher.UIThread.Post(() => RaiseScrollInvalidated(EventArgs.Empty));
            }
        }
    }

    /// <summary>
    /// The leading front-matter block (PRD §7.4): line 0's opening <c>---</c> through its closing
    /// fence, or null when the document has none. Front matter exists only when a closing fence is
    /// present (a lone <c>---</c> tokenizes as a horizontal rule), so a found opening implies a close.
    /// </summary>
    private (int Start, int End)? FrontMatterBlock()
    {
        var doc = _buffer.Document;
        if (doc.LineCount == 0)
        {
            return null;
        }

        var first = _model.LineTokens(0);
        if (first.Count == 0 || first[0].Type != TokenType.FrontMatterFence)
        {
            return null;
        }

        for (var line = 1; line < doc.LineCount; line++)
        {
            var tokens = _model.LineTokens(line);
            if (tokens.Count > 0 && tokens[0].Type == TokenType.FrontMatterFence)
            {
                return (0, line);
            }
        }

        return null;
    }

    /// <summary>The detected front-matter block range; for tests.</summary>
    internal (int Start, int End)? FrontMatterBlockForTest() => FrontMatterBlock();

    /// <summary>
    /// Draw the "Properties" panel behind the front-matter block: a tinted backdrop spanning its
    /// rows plus a right-aligned label on the opening-fence row. Drawn under the text and without
    /// changing any glyph layout, so caret/selection/hit-testing stay exact (offsets remain 1:1).
    /// True folding of the block belongs with the §7.4 concealment work (the offset↔visual map).
    /// </summary>
    private void DrawPropertiesPanel(DrawingContext context, int firstVisible, int lastVisible)
    {
        if (FrontMatterBlock() is not { } block || block.End < firstVisible || block.Start >= lastVisible)
        {
            return;
        }

        var lineH = _theme.LineHeight;
        var viewportW = _viewport.Width > 0 ? _viewport.Width : Bounds.Width;
        var panelRight = Math.Max(220, viewportW - _theme.PaddingLeft * 2);
        var top = block.Start * lineH + 2;
        var height = (block.End - block.Start + 1) * lineH - 4;
        context.FillRectangle(_theme.PropertiesBackground, new Rect(-8, top, panelRight + 8, height), 6);

        // "Properties" label on the opening-fence row (its source is only "---", so no text overlap).
        var label = GetFormattedText("Properties", _theme.Resolve(TokenType.FrontMatterFence), _dimMarker);
        var labelY = block.Start * lineH + (lineH - label.Height) / 2;
        context.DrawText(label, new Point(panelRight - label.Width - 6, labelY));
    }

    private double RenderLine(DrawingContext context, TextDocument doc, int line, Selection sel, int caretLine)
    {
        var lineTop = line * _theme.LineHeight;
        var lineStart = doc.GetLineStart(line);
        var lineEnd = doc.GetLineEnd(line);
        var lineText = doc.GetText(lineStart, lineEnd - lineStart).TrimEnd('\r');
        var contentLen = lineText.Length;

        var headingLevel = HeadingLevel(line);
        var revealMarkers = _showRawSource || caretLine == line || sel.Intersects(lineStart, Math.Max(1, lineEnd - lineStart));

        // Search-match highlights (under the text), then the selection on top.
        if (_searchMatches.Count > 0)
        {
            DrawSearchMatches(context, line, lineStart, contentLen, lineText, headingLevel);
        }

        if (sel.Length > 0)
        {
            DrawSelection(context, line, lineStart, contentLen, sel, lineText);
        }

        // Tile the line with styled segments (filling gaps between tokens with default style).
        var x = 0d;
        var caretX = double.NaN;
        var caretCol = sel.Caret - lineStart;

        foreach (var (from, to, style) in StyledSegments(line, contentLen, headingLevel))
        {
            x += DrawSegment(context, lineText, from, to, lineTop, style, revealMarkers, ref caretX, caretCol, x);
        }

        // Caret.
        if (caretLine == line && IsFocused)
        {
            if (double.IsNaN(caretX))
            {
                caretX = caretCol <= 0 ? 0 : x;
            }

            var h = _theme.BaseFontSize * 1.3;
            var y = lineTop + (_theme.LineHeight - h) / 2;
            context.FillRectangle(_theme.Caret, new Rect(caretX, y, 1.5, h));
        }

        return x; // total content width of this line (drives the horizontal extent)
    }

    /// <summary>Draw one styled run; returns its advance width and records the caret X if it lands inside.</summary>
    private double DrawSegment(
        DrawingContext context, string lineText, int from, int to, double lineTop, RunStyle style,
        bool revealMarkers, ref double caretX, int caretCol, double x)
    {
        var text = lineText.Substring(from, to - from);
        var brush = style.IsMarker ? (revealMarkers ? _theme.MarkerForeground : _dimMarker) : style.Foreground;
        var ft = GetFormattedText(text, style, brush);
        var y = lineTop + (_theme.LineHeight - ft.Height) / 2;

        if (style.Background is not null)
        {
            context.FillRectangle(style.Background, new Rect(x, lineTop + 2, ft.Width, _theme.LineHeight - 4), 3);
        }

        context.DrawText(ft, new Point(x, y));

        // Caret X if it falls within this run (measured with trailing whitespace included).
        if (caretCol >= from && caretCol <= to && double.IsNaN(caretX))
        {
            caretX = x + MeasureWidth(lineText.Substring(from, caretCol - from), style);
        }

        return MeasureWidth(text, style); // advance includes trailing whitespace, unlike ft.Width
    }

    private void DrawSearchMatches(DrawingContext context, int line, int lineStart, int contentLen, string lineText, int headingLevel)
    {
        var lineEnd = lineStart + contentLen;
        // Matches are sorted, non-overlapping → both Start and End ascend. Binary-search the first
        // match that could touch this line, then walk forward, so a line only touches its own matches
        // (not all of them) — O(log M + matches-on-line) instead of O(M) per line.
        for (var i = FirstMatchReaching(lineStart); i < _searchMatches.Count; i++)
        {
            var m = _searchMatches[i];
            if (m.Start >= lineEnd)
            {
                break; // all later matches start past this line
            }

            var s = Math.Clamp(m.Start - lineStart, 0, contentLen);
            var e = Math.Clamp(m.End - lineStart, 0, contentLen);
            if (e <= s)
            {
                continue;
            }

            var x1 = ColumnX(line, contentLen, headingLevel, lineText, s);
            var x2 = ColumnX(line, contentLen, headingLevel, lineText, e);
            var brush = i == _currentMatch ? _theme.SearchMatchCurrent : _theme.SearchMatch;
            context.FillRectangle(brush, new Rect(x1, line * _theme.LineHeight + 2, x2 - x1, _theme.LineHeight - 4), 2);
        }
    }

    /// <summary>Index of the first match whose end is past <paramref name="lineStart"/> (lower bound).</summary>
    internal int FirstMatchReaching(int lineStart)
    {
        int lo = 0, hi = _searchMatches.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (_searchMatches[mid].End <= lineStart)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private void DrawSelection(DrawingContext context, int line, int lineStart, int contentLen, Selection sel, string lineText)
    {
        var lineEnd = lineStart + contentLen;
        if (sel.Start > lineEnd || sel.End < lineStart)
        {
            return; // selection doesn't touch this line
        }

        var selStartCol = Math.Clamp(sel.Start - lineStart, 0, contentLen);
        var selEndCol = Math.Clamp(sel.End - lineStart, 0, contentLen);
        if (selEndCol <= selStartCol)
        {
            return;
        }

        var headingLevel = HeadingLevel(line);
        var x1 = ColumnX(line, contentLen, headingLevel, lineText, selStartCol);
        var x2 = ColumnX(line, contentLen, headingLevel, lineText, selEndCol);
        context.FillRectangle(_theme.SelectionBackground, new Rect(x1, line * _theme.LineHeight + 2, x2 - x1, _theme.LineHeight - 4));
    }

    // ----- input -----

    protected override void OnTextInput(TextInputEventArgs e)
    {
        // A Cmd/Ctrl+key combination is a command, not text — don't type the letter. (Ctrl+Alt is
        // left alone so AltGr characters still work.)
        var command = _modifiers.HasFlag(KeyModifiers.Meta)
            || (_modifiers.HasFlag(KeyModifiers.Control) && !_modifiers.HasFlag(KeyModifiers.Alt));

        if (!command && !string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text[0]))
        {
            if (e.Text.Length != 1 || !TryAutoPair(e.Text[0]))
            {
                _buffer.Insert(e.Text);
            }
        }

        e.Handled = true;
        base.OnTextInput(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        _modifiers = e.KeyModifiers;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var doc = _buffer.Document;
        var caret = _buffer.Selection.Caret;
        var handled = true;

        switch (e.Key)
        {
            case Key.Back: if (!TryBackspacePair()) { _buffer.Backspace(); } break;
            case Key.Delete: _buffer.Delete(); break;
            case Key.Enter: HandleEnter(); break;
            case Key.Tab: HandleTab(shift); break;
            case Key.Left: MoveCaret(_buffer.PreviousCaret(caret), shift); break;
            case Key.Right: MoveCaret(_buffer.NextCaret(caret), shift); break;
            case Key.Up: MoveCaretByLine(-1, shift); break;
            case Key.Down: MoveCaretByLine(1, shift); break;
            case Key.Home: MoveCaret(doc.GetLineStart(doc.GetPosition(caret).Line), shift); break;
            case Key.End: MoveCaret(doc.GetLineEnd(doc.GetPosition(caret).Line), shift); break;

            // Clipboard.
            case Key.C when ctrl: _ = CopyAsync(); break;
            case Key.X when ctrl: _ = CutAsync(); break;
            case Key.V when ctrl: _ = PasteAsync(); break;

            // Formatting (the shortcuts a Markdown editor is expected to have).
            case Key.B when ctrl: ToggleBold(); break;
            case Key.I when ctrl: ToggleItalic(); break;
            case Key.E when ctrl: ToggleInlineCode(); break;
            case Key.K when ctrl: InsertLink(); break;
            case Key.D0 when ctrl: SetParagraph(); break;
            case >= Key.D1 and <= Key.D6 when ctrl: ApplyHeading(e.Key - Key.D1 + 1); break;

            case Key.A when ctrl: SelectAll(); break;
            case Key.Z when ctrl && shift: _buffer.Redo(); break;
            case Key.Z when ctrl: _buffer.Undo(); break;
            case Key.Y when ctrl: _buffer.Redo(); break;
            default: handled = false; break;
        }

        e.Handled = handled;
        base.OnKeyDown(e);
    }

    // ----- editing assists (PRD §7.2): list/quote continuation, indent, auto-pairing -----

    /// <summary>The pairs auto-inserted/closed and used to wrap a selection (opener → closer).</summary>
    private static readonly IReadOnlyDictionary<char, char> AutoPairs = new Dictionary<char, char>
    {
        ['('] = ')',
        ['['] = ']',
        ['`'] = '`',
        ['*'] = '*',
        ['_'] = '_',
    };

    /// <summary>Enter: continue or exit a list/blockquote, auto-indent, or insert a plain newline (PRD §7.2).</summary>
    private void HandleEnter()
    {
        var sel = _buffer.Selection;
        if (!sel.IsEmpty)
        {
            _buffer.Insert("\n"); // a selection is replaced by the newline; no continuation
            return;
        }

        var doc = _buffer.Document;
        var line = doc.GetPosition(sel.Caret).Line;
        var lineStart = doc.GetLineStart(line);
        var lineText = doc.GetLineText(line);
        var result = LinePrefix.ComputeEnter(lineText, sel.Caret - lineStart);

        switch (result.Kind)
        {
            case EnterKind.Exit:
                _buffer.ReplaceRange(lineStart + result.ExitStart, result.ExitLength, string.Empty);
                break;
            case EnterKind.Continue:
                _buffer.ReplaceRange(sel.Caret, 0, result.Text);
                RenumberOrderedFrom(line + 1); // resequence following items if we split an ordered list
                break;
            default:
                _buffer.Insert("\n");
                break;
        }
    }

    /// <summary>Tab/Shift+Tab: indent or outdent list items / selected lines; otherwise insert spaces.</summary>
    private void HandleTab(bool shift)
    {
        var sel = _buffer.Selection;
        var doc = _buffer.Document;
        var startLine = doc.GetPosition(sel.Start).Line;
        var endLine = doc.GetPosition(sel.End).Line;
        var caretLine = doc.GetPosition(sel.Caret).Line;
        var multiLine = endLine > startLine;
        var onList = LinePrefix.Parse(doc.GetLineText(caretLine)).HasList;

        // Plain text, single caret, not on a list item → a Tab just inserts spaces (the old behavior).
        if (!shift && !multiLine && sel.IsEmpty && !onList)
        {
            _buffer.Insert(new string(' ', Math.Max(1, IndentWidth)));
            return;
        }

        IndentLines(startLine, endLine, outdent: shift);
    }

    /// <summary>Add or remove one indent level on lines <paramref name="startLine"/>..<paramref name="endLine"/> as one edit.</summary>
    private void IndentLines(int startLine, int endLine, bool outdent)
    {
        var doc = _buffer.Document;
        var sel = _buffer.Selection;
        var indent = new string(' ', Math.Max(1, IndentWidth));
        var regionStart = doc.GetLineStart(startLine);
        var regionEnd = doc.GetLineEnd(endLine);

        var newLines = new List<string>();
        var startDelta = 0;
        var totalDelta = 0;
        for (var l = startLine; l <= endLine; l++)
        {
            var text = doc.GetLineText(l);
            int delta;
            if (outdent)
            {
                var remove = 0;
                while (remove < indent.Length && remove < text.Length && text[remove] == ' ')
                {
                    remove++;
                }

                newLines.Add(text[remove..]);
                delta = -remove;
            }
            else if (text.Length == 0 && startLine != endLine)
            {
                newLines.Add(text); // don't indent blank lines inside a multi-line block
                delta = 0;
            }
            else
            {
                newLines.Add(indent + text);
                delta = indent.Length;
            }

            if (l == startLine)
            {
                startDelta = delta;
            }

            totalDelta += delta;
        }

        _buffer.ReplaceRange(regionStart, regionEnd - regionStart, string.Join("\n", newLines));
        var newStart = Math.Max(regionStart, sel.Start + startDelta);
        var newEnd = Math.Max(newStart, sel.End + totalDelta);
        _buffer.Selection = new Selection(sel.Anchor <= sel.Caret ? newStart : newEnd, sel.Anchor <= sel.Caret ? newEnd : newStart);
    }

    /// <summary>Resequence the ordered-list run starting at <paramref name="fromLine"/> as one edit (PRD §7.2).</summary>
    private void RenumberOrderedFrom(int fromLine)
    {
        var doc = _buffer.Document;
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
        var caret = _buffer.Selection;
        var lastLine = fromLine + result.EndLineExclusive - 1;
        var regionStart = doc.GetLineStart(fromLine);
        var regionEnd = doc.GetLineEnd(lastLine);
        _buffer.ReplaceRange(regionStart, regionEnd - regionStart, string.Join("\n", result.Lines));
        _buffer.Selection = caret;
    }

    /// <summary>Wrap a selection in a pair, type through a closer, or auto-close a bracket. Returns true if it consumed the key.</summary>
    private bool TryAutoPair(char c)
    {
        var sel = _buffer.Selection;
        var doc = _buffer.Document;

        // Wrap a non-empty selection in the typed pair, keeping the original text selected inside it.
        if (!sel.IsEmpty && AutoPairs.TryGetValue(c, out var closeWrap))
        {
            var inner = doc.GetText(sel.Start, sel.Length);
            _buffer.ReplaceRange(sel.Start, sel.Length, c + inner + closeWrap);
            _buffer.Selection = new Selection(sel.Start + 1, sel.Start + 1 + inner.Length);
            return true;
        }

        if (!sel.IsEmpty)
        {
            return false;
        }

        var caret = sel.Caret;
        var next = caret < doc.Length ? doc[caret] : '\0';

        // Type-through: typing a ')'/']' already sitting under the caret just steps over it.
        if ((c == ')' || c == ']') && next == c)
        {
            _buffer.Selection = Selection.At(caret + 1);
            return true;
        }

        // Auto-close a bracket/backtick when the caret is at a boundary (end of line, whitespace, or a
        // closer). Emphasis (* and _) is deliberately not auto-closed — too noisy mid-word.
        if ((c == '(' || c == '[' || c == '`')
            && (next is '\0' or '\r' or '\n' or ')' or ']' or '}' || char.IsWhiteSpace(next)))
        {
            _buffer.ReplaceRange(caret, 0, $"{c}{AutoPairs[c]}");
            _buffer.Selection = Selection.At(caret + 1);
            return true;
        }

        return false;
    }

    /// <summary>Backspace between an empty auto-pair (e.g. <c>(|)</c>) removes both halves. Returns true if it did.</summary>
    private bool TryBackspacePair()
    {
        var sel = _buffer.Selection;
        var doc = _buffer.Document;
        if (!sel.IsEmpty || sel.Caret == 0 || sel.Caret >= doc.Length)
        {
            return false;
        }

        var prev = doc[sel.Caret - 1];
        var next = doc[sel.Caret];
        if (prev is '(' or '[' or '`' && AutoPairs.TryGetValue(prev, out var close) && next == close)
        {
            _buffer.ReplaceRange(sel.Caret - 1, 2, string.Empty);
            return true;
        }

        return false;
    }

    // Test hooks: drive the same editing-assist paths the key handlers use, without a focused window.
    internal void EnterForTest() => HandleEnter();

    internal void TabForTest(bool shift) => HandleTab(shift);

    internal void TypeForTest(char c)
    {
        if (c < ' ' || !TryAutoPair(c))
        {
            _buffer.Insert(c.ToString());
        }
    }

    internal void BackspaceForTest()
    {
        if (!TryBackspacePair())
        {
            _buffer.Backspace();
        }
    }

    private KeyModifiers _modifiers;
    private bool _dragging;
    private double _goalX = double.NaN; // remembered caret x for sticky up/down movement (NaN = recompute)

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // Let a right-click through so the context menu can open; only the left button moves the caret.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            base.OnPointerPressed(e);
            return;
        }

        Focus();
        var point = e.GetPosition(this);
        // The viewport is inset by the padding and translated by the scroll offset; map back.
        var line = Math.Clamp((int)((point.Y - _theme.PaddingTop + _offset.Y) / _theme.LineHeight), 0, _buffer.Document.LineCount - 1);
        var offset = OffsetAtPoint(point);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        // A double-click selects the word (or run) under the cursor.
        if (e.ClickCount == 2)
        {
            SelectWordAt(line, offset);
            e.Handled = true;
            base.OnPointerPressed(e);
            return;
        }

        // A click on a task-list checkbox toggles it (PRD §7.2) rather than moving the caret.
        if (!shift && TryToggleTaskAt(line, offset))
        {
            e.Handled = true;
            base.OnPointerPressed(e);
            return;
        }

        MoveCaret(offset, shift);
        _dragging = true; // begin a drag-select; OnPointerMoved extends the selection
        e.Pointer.Capture(this);
        e.Handled = true;
        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_dragging && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            MoveCaret(OffsetAtPoint(e.GetPosition(this)), extend: true); // hold the anchor, move the caret
            e.Handled = true;
        }

        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        base.OnPointerReleased(e);
    }

    /// <summary>
    /// Select the run under <paramref name="offset"/> on <paramref name="line"/>: a word (letters /
    /// digits / underscore), a whitespace run, or a punctuation run — the standard double-click word
    /// select. Snaps the selection ends to grapheme boundaries so it never splits a cluster.
    /// </summary>
    private void SelectWordAt(int line, int offset)
    {
        var doc = _buffer.Document;
        var lineStart = doc.GetLineStart(line);
        var text = doc.GetText(lineStart, doc.GetLineEnd(line) - lineStart).TrimEnd('\r');
        if (text.Length == 0)
        {
            MoveCaret(lineStart, extend: false);
            return;
        }

        var col = Math.Clamp(offset - lineStart, 0, text.Length);
        var probe = col < text.Length ? col : col - 1;
        var cls = CharClass(text[probe]);

        var start = probe;
        while (start > 0 && CharClass(text[start - 1]) == cls)
        {
            start--;
        }

        var end = probe + 1;
        while (end < text.Length && CharClass(text[end]) == cls)
        {
            end++;
        }

        // Snap to grapheme boundaries (a surrogate pair / combining sequence stays whole).
        start = Grapheme.PreviousBoundary(text, Math.Min(start + 1, text.Length));
        end = Grapheme.NextBoundary(text, Math.Max(0, end - 1));
        _buffer.Selection = new Selection(lineStart + start, lineStart + end);
        _goalX = double.NaN;
    }

    private static int CharClass(char c) =>
        char.IsLetterOrDigit(c) || c == '_' ? 0 : char.IsWhiteSpace(c) ? 1 : 2;

    /// <summary>Select the word/run at <paramref name="offset"/> on <paramref name="line"/>; for tests.</summary>
    internal void SelectWordForTest(int line, int offset) => SelectWordAt(line, offset);

    /// <summary>Move the caret up/down by <paramref name="delta"/> lines (sticky goal column); for tests.</summary>
    internal void MoveCaretByLineForTest(int delta) => MoveCaretByLine(delta, extend: false);

    /// <summary>Set the caret to <paramref name="offset"/> as a plain click would; for tests.</summary>
    internal void ClickAtForTest(int offset) => MoveCaret(offset, extend: false);

    /// <summary>
    /// If <paramref name="offset"/> lands on a task-list checkbox on <paramref name="line"/>, flip it
    /// between <c>[ ]</c> and <c>[x]</c> (leaving the caret where it was) and return true (PRD §7.2).
    /// </summary>
    private bool TryToggleTaskAt(int line, int offset)
    {
        foreach (var t in _model.LineTokens(line))
        {
            if (t.Type != TokenType.TaskMarker || offset < t.Start || offset > t.Start + 3)
            {
                continue;
            }

            var box = t.Start + 1; // the character between the brackets
            if (box >= _buffer.Document.Length)
            {
                return false;
            }

            var replacement = _buffer.Document[box] == ' ' ? "x" : " ";
            var caret = _buffer.Selection;
            _buffer.ReplaceRange(box, 1, replacement);
            _buffer.Selection = caret; // a checkbox click shouldn't move the caret
            return true;
        }

        return false;
    }

    /// <summary>Toggle the task checkbox covering <paramref name="offset"/> on <paramref name="line"/>; for tests.</summary>
    internal bool ToggleTaskForTest(int line, int offset) => TryToggleTaskAt(line, offset);

    /// <summary>Scroll so the caret stays visible after an edit or navigation (we are the scrollable).</summary>
    private void ScrollCaretIntoView()
    {
        if (_viewport.Width <= 0 || _viewport.Height <= 0)
        {
            return; // not arranged in a viewport yet (e.g. headless construction)
        }

        var doc = _buffer.Document;
        var pos = doc.GetPosition(_buffer.Selection.Caret);
        var lineStart = doc.GetLineStart(pos.Line);
        var text = doc.GetText(lineStart, doc.GetLineEnd(pos.Line) - lineStart).TrimEnd('\r');
        var x = ColumnX(pos.Line, text.Length, HeadingLevel(pos.Line), text, Math.Min(pos.Column, text.Length));
        _extentWidth = Math.Max(_extentWidth, x + 12); // ensure we can scroll to a caret on a wide line

        // Caret position in scroll-content space (which includes the padding).
        var px = x + _theme.PaddingLeft;
        var py = pos.Line * _theme.LineHeight + _theme.PaddingTop;
        const double margin = 8;

        var newY = _offset.Y;
        if (py < _offset.Y)
        {
            newY = py;
        }
        else if (py + _theme.LineHeight > _offset.Y + _viewport.Height)
        {
            newY = py + _theme.LineHeight - _viewport.Height;
        }

        var newX = _offset.X;
        if (px < _offset.X + margin)
        {
            newX = Math.Max(0, px - margin);
        }
        else if (px + margin > _offset.X + _viewport.Width)
        {
            newX = px + margin - _viewport.Width;
        }

        var target = ClampOffset(new Vector(newX, newY));
        if (target != _offset)
        {
            _offset = target;
            InvalidateVisual();
            RaiseScrollInvalidated(EventArgs.Empty);
        }
    }

    private void MoveCaret(int newCaret, bool extend, bool keepGoalColumn = false)
    {
        newCaret = Math.Clamp(newCaret, 0, _buffer.Document.Length);
        var anchor = extend ? _buffer.Selection.Anchor : newCaret;
        _buffer.Selection = new Selection(anchor, newCaret);
        if (!keepGoalColumn)
        {
            _goalX = double.NaN; // a horizontal move resets the remembered column
        }
    }

    /// <summary>
    /// Move the caret up/down by <paramref name="delta"/> lines, holding a <b>sticky goal column</b>:
    /// the caret's x is remembered on the first vertical move and reused, so passing through shorter
    /// lines doesn't permanently snap the column inward (the behavior every editor has).
    /// </summary>
    private void MoveCaretByLine(int delta, bool extend)
    {
        var doc = _buffer.Document;
        var pos = doc.GetPosition(_buffer.Selection.Caret);
        if (double.IsNaN(_goalX))
        {
            _goalX = CaretX(pos);
        }

        var targetLine = Math.Clamp(pos.Line + delta, 0, doc.LineCount - 1);
        MoveCaret(OffsetAt(targetLine, _goalX), extend, keepGoalColumn: true);
    }

    /// <summary>The x-offset of the caret at <paramref name="pos"/>, measured with each run's real font.</summary>
    private double CaretX(LinePosition pos)
    {
        var doc = _buffer.Document;
        var lineStart = doc.GetLineStart(pos.Line);
        var text = doc.GetText(lineStart, doc.GetLineEnd(pos.Line) - lineStart).TrimEnd('\r');
        return ColumnX(pos.Line, text.Length, HeadingLevel(pos.Line), text, Math.Min(pos.Column, text.Length));
    }

    /// <summary>Map a pointer position to the nearest caret offset (accounting for padding + scroll).</summary>
    private int OffsetAtPoint(Point point)
    {
        var contentY = point.Y - _theme.PaddingTop + _offset.Y;
        var contentX = point.X - _theme.PaddingLeft + _offset.X;
        var line = Math.Clamp((int)(contentY / _theme.LineHeight), 0, _buffer.Document.LineCount - 1);
        return OffsetAt(line, contentX);
    }

    // ----- helpers -----

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

    private static Typeface HeadingTypeface(RunStyle baseStyle) =>
        new(baseStyle.Typeface.FontFamily, FontStyle.Normal, FontWeight.Bold);

    /// <summary>
    /// The full styled tiling of a line: every token span resolved to a <see cref="RunStyle"/>,
    /// with the gaps between tokens (e.g. leading indent) filled by the default style. The single
    /// source of truth used by both rendering and width measurement, so the caret, the selection
    /// rectangle, and mouse hit-testing all agree with the drawn glyphs even on styled lines.
    /// </summary>
    private IEnumerable<(int From, int To, RunStyle Style)> StyledSegments(int line, int contentLen, int headingLevel)
    {
        var lineStart = _buffer.Document.GetLineStart(line);
        var pos = 0;
        foreach (var t in _model.LineTokens(line))
        {
            var s = Math.Clamp(t.Start - lineStart, 0, contentLen);
            var en = Math.Clamp(t.End - lineStart, 0, contentLen);
            if (en <= s)
            {
                continue;
            }

            if (s > pos)
            {
                yield return (pos, s, DefaultStyle(headingLevel));
            }

            yield return (s, en, StyleFor(t.Type, headingLevel));
            pos = en;
        }

        if (pos < contentLen)
        {
            yield return (pos, contentLen, DefaultStyle(headingLevel));
        }
    }

    /// <summary>The x-offset of column <paramref name="col"/> on <paramref name="line"/>, measured with each run's real font.</summary>
    private double ColumnX(int line, int contentLen, int headingLevel, string lineText, int col)
    {
        if (col <= 0)
        {
            return 0;
        }

        var x = 0d;
        foreach (var (from, to, style) in StyledSegments(line, contentLen, headingLevel))
        {
            if (col <= from)
            {
                break;
            }

            var end = Math.Min(col, to);
            x += MeasureWidth(lineText.Substring(from, end - from), style);
            if (col <= to)
            {
                break;
            }
        }

        return x;
    }

    /// <summary>
    /// The advance width of <paramref name="text"/> in <paramref name="style"/>, <b>including any
    /// trailing whitespace</b>. <see cref="FormattedText.Width"/> omits trailing whitespace, which
    /// made a run ending in a space (e.g. a heading marker <c>"## "</c> or a word before emphasis)
    /// advance too little so the next run drew on top of the space — the missing-space bug. Appending
    /// a non-space sentinel makes those spaces interior (and thus measured) and is then subtracted out.
    /// </summary>
    private double MeasureWidth(string text, RunStyle style)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        // Width is fully determined by (text, font family/style/weight, size). Cache it so caret
        // moves, clicks, selection, and per-frame advances stop reconstructing/shaping FormattedText
        // for runs they've already measured (the cache is cleared when the theme/fonts change).
        var tf = style.Typeface;
        var key = new WidthKey(text, tf.FontFamily, tf.Style, tf.Weight, style.FontSize);
        if (_widthCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var width = char.IsWhiteSpace(text[^1])
            ? RawWidth(text + ".", style) - RawWidth(".", style)
            : RawWidth(text, style);

        if (_widthCache.Count >= 16384)
        {
            _widthCache.Clear(); // bound the cache; it rebuilds on demand
        }

        _widthCache[key] = width;
        return width;
    }

    /// <summary>
    /// The drawable <see cref="FormattedText"/> for a run, cached on its full visual identity (text,
    /// font, size, brush, decorations). The draw path builds one <see cref="FormattedText"/> per
    /// visible run per frame; for a static viewport every run is identical frame to frame, so caching
    /// the objects removes the last per-frame allocation/shaping (the cache is invalidated on a theme
    /// change, and a marker run is rebuilt only when it flips between revealed and dimmed).
    /// </summary>
    private FormattedText GetFormattedText(string text, RunStyle style, IBrush brush)
    {
        var key = new FormattedKey(text, style.Typeface, style.FontSize, brush, style.Decorations);
        if (_formattedCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        _formattedBuilds++; // counts FormattedText construction (cache misses); used by perf tests
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            style.Typeface, style.FontSize, brush);
        if (style.Decorations is not null)
        {
            ft.SetTextDecorations(style.Decorations);
        }

        if (_formattedCache.Count >= 8192)
        {
            _formattedCache.Clear(); // bound the cache; it rebuilds on demand
        }

        _formattedCache[key] = ft;
        return ft;
    }

    private readonly Dictionary<FormattedKey, FormattedText> _formattedCache = new();
    private int _formattedBuilds;

    private readonly record struct FormattedKey(string Text, Typeface Typeface, double Size, IBrush Brush, TextDecorationCollection? Decorations);

    /// <summary>Number of <see cref="FormattedText"/> objects built (cache misses); for perf tests.</summary>
    internal int FormattedBuildCount => _formattedBuilds;

    /// <summary>Fetch a run's <see cref="FormattedText"/> through the cache, exercising it; for perf tests.</summary>
    internal void GetFormattedTextForTest(string text)
    {
        var style = _theme.Resolve(TokenType.Text);
        GetFormattedText(text, style, style.Foreground);
    }

    private double RawWidth(string text, RunStyle style)
    {
        _rawMeasures++; // counts actual FormattedText shaping (cache misses) — used by perf tests
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            style.Typeface, style.FontSize, style.Foreground).Width;
    }

    private readonly Dictionary<WidthKey, double> _widthCache = new();
    private int _rawMeasures;

    /// <summary>Number of real text-shaping measurements performed (cache misses); for perf tests.</summary>
    internal int RawMeasureCount => _rawMeasures;

    /// <summary>Measure a run with the default text style, exercising the width cache; for perf tests.</summary>
    internal double MeasureWidthForTest(string text) => MeasureWidth(text, _theme.Resolve(TokenType.Text));

    private readonly record struct WidthKey(string Text, FontFamily Family, FontStyle Style, FontWeight Weight, double Size);

    private int OffsetAt(int line, double x)
    {
        var doc = _buffer.Document;
        var lineStart = doc.GetLineStart(line);
        var lineEnd = doc.GetLineEnd(line);
        var text = doc.GetText(lineStart, lineEnd - lineStart).TrimEnd('\r');
        var headingLevel = HeadingLevel(line);
        // Walk grapheme boundaries, picking the one whose styled prefix width is closest to x.
        var best = 0;
        var bestDist = double.MaxValue;
        var col = 0;
        while (true)
        {
            var w = ColumnX(line, text.Length, headingLevel, text, col);
            var d = Math.Abs(w - x);
            if (d < bestDist)
            {
                bestDist = d;
                best = col;
            }

            if (col >= text.Length)
            {
                break;
            }

            col = Grapheme.NextBoundary(text, col);
        }

        return lineStart + best;
    }
}
