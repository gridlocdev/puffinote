using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
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
public partial class MarkdownEditor : Avalonia.Controls.Control, ILogicalScrollable, ILayoutHost
{
    public enum CompletedTaskSortMode
    {
        None,
        Top,
        Bottom,
    }

    private readonly record struct TaskSubtree(int StartLine, int EndLine, bool IsComplete);

    private readonly TextMeasurer _measurer; // text measurement + FormattedText/width caches (theme-only dep)
    private readonly LayoutModel _layout; // owns block grouping, row counts, and the VisualLine cache
    private MarkdownModel _model;
    private IBrush _dimMarker = new SolidColorBrush(Color.FromArgb(0x46, 0x6E, 0x76, 0x81));

    // Drop-indicator state for Alt+Shift line reorder: while a reorder "session" is active (consecutive
    // moves), faint dotted lines bracket the moved block so it's clear what's being shuffled. Cleared by any
    // other key, click, or edit.
    private bool _reorderActive;
    private int _reorderStartLine;
    private int _reorderEndLine;

    // Per-code-block "copy" buttons, rebuilt each render: each holds its hit rect (control space) and the
    // line range of the code run it copies. _copiedFeedbackLine is the run currently showing "Copied!".
    private readonly List<(Rect Rect, int First, int Last)> _codeCopyButtons = [];
    private int _copiedFeedbackLine = -1;
    private DispatcherTimer? _copyFeedbackTimer;

    // Virtualized logical-scroll state: only the lines inside the viewport are drawn, and the
    // horizontal extent grows as wide lines are encountered (no whole-document width scan).
    private Vector _offset;
    private Size _viewport;
    private double _extentWidth;
    private IReadOnlyList<SearchMatch> _searchMatches = [];
    private int _currentMatch = -1;
    private EventHandler? _scrollInvalidated;

    public MarkdownEditor()
    {
        Focusable = true;
        _measurer = new TextMeasurer(() => IndentWidth);
        _layout = new LayoutModel(this, _measurer);
        _model = MarkdownModel.Build(Buffer.Document);
        Buffer.TextChanged += (_, _) =>
        {
            // Incremental re-tokenization: reuse the unchanged prefix and re-run only from the first
            // changed line down (PRD §8.2). Equivalent to a full rebuild — see IncrementalTokenizerTests.
            _model = _model.Update(Buffer.Document, Buffer.LastChangedLine, out _);
            _goalX = double.NaN; // an edit moves the caret; the next Up/Down recomputes the goal column
            _layout.MarkDirty(Buffer.LastChangedLine); // reuse blocks above the first changed line
            RaiseScrollInvalidated(EventArgs.Empty); // line count / extent may have changed
            InvalidateVisual();
            TextChanged?.Invoke(this, EventArgs.Empty);
        };
        Buffer.Changed += (_, _) =>
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
        get => Buffer.Document.GetText();
        set => Buffer.Load(value);
    }

    /// <summary>True when the buffer differs from the last-saved snapshot.</summary>
    public bool IsDirty => Buffer.IsDirty;

    /// <summary>Live document statistics for the host's status bar (cached; recomputed on text change only).</summary>
    public DocumentStats Stats => Buffer.Stats;

    /// <summary>The caret position as a 1-based line:column for the status bar.</summary>
    public LinePosition CaretPosition => Buffer.Document.GetPosition(Buffer.Selection.Caret);

    /// <summary>The currently selected text (empty when there is no selection).</summary>
    public string SelectedText
    {
        get
        {
            var sel = Buffer.Selection;
            return sel.IsEmpty ? string.Empty : Buffer.Document.GetText(sel.Start, sel.Length);
        }
    }

    /// <summary>The number of grapheme clusters currently selected (0 when the selection is empty).</summary>
    public int SelectedCharacterCount
    {
        get
        {
            var sel = Buffer.Selection;
            return sel.IsEmpty ? 0 : Grapheme.Count(Buffer.Document.GetText(sel.Start, sel.Length));
        }
    }

    /// <summary>The editable model, exposed for the host (save/load/dirty) and tests.</summary>
    internal DocumentBuffer Buffer { get; } = new();

    public bool CanUndo => Buffer.CanUndo;

    public bool CanRedo => Buffer.CanRedo;

    /// <summary>The active theme (presentation seam — swap to re-style without touching the engine).</summary>
    public MarkdownTheme EditorTheme
    {
        get; set
        {
            field = value;
            UpdateDerivedBrushes();
            _measurer.Invalidate(); // cached widths/glyph runs assumed the old theme's fonts/brushes
            _extentWidth = 0;    // fonts changed — let the horizontal extent regrow as lines redraw
            _layout.InvalidateAll(); // line heights/wrapping depend on the font metrics — every block re-measures
            RaiseScrollInvalidated(EventArgs.Empty);
            InvalidateMeasure();
            InvalidateVisual();
        }
    } = MarkdownTheme.Default;

    /// <summary>The faded marker brush is derived from the theme's marker color so it tracks light/dark/HC.</summary>
    private void UpdateDerivedBrushes()
    {
        if (EditorTheme.MarkerForeground is ISolidColorBrush b)
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
    /// <summary>
    /// How the editing surface presents the document (PRD §7.4). <see cref="ViewMode.Rich"/> is the WYSIWYG
    /// live preview (markers concealed, full formatting); <see cref="ViewMode.Formatted"/> keeps the
    /// formatting but reveals the raw Markdown markers; <see cref="ViewMode.RawText"/> shows the plain
    /// monospace source with no concealment, styling, or decorations — read exactly as it will be saved.
    /// </summary>
    public enum ViewMode
    {
        /// <summary>WYSIWYG live preview: markers concealed, full formatting.</summary>
        Rich,

        /// <summary>Formatted, but with the raw Markdown markers revealed (the classic "raw source" view).</summary>
        Formatted,

        /// <summary>Plain monospace source text: no concealment, no styling, no decorations.</summary>
        RawText,
    }

    /// <summary>The active presentation mode (PRD §7.4). Defaults to the WYSIWYG <see cref="ViewMode.Rich"/>.</summary>
    public ViewMode View
    {
        get; set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            // RawText ungroups paragraphs and un-collapses blanks, and concealment/styling differ by mode, so
            // re-group fully and drop the cached visuals.
            _layout.InvalidateAll();
            _extentWidth = 0;
            RaiseScrollInvalidated(EventArgs.Empty);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Legacy toggle kept for existing callers: reveals or conceals the syntax markers. Reading it is true
    /// in both <see cref="ViewMode.Formatted"/> and <see cref="ViewMode.RawText"/>; setting it swaps between
    /// <see cref="ViewMode.Rich"/> and <see cref="ViewMode.Formatted"/>.
    /// </summary>
    public bool ShowRawSource
    {
        get => View != ViewMode.Rich;
        set => View = value ? ViewMode.Formatted : ViewMode.Rich;
    }

    /// <summary>Markers are revealed (not concealed) in both the Formatted and RawText views.</summary>
    private bool MarkersRevealed => View != ViewMode.Rich;

    /// <summary>The plain monospace source view: no concealment, styling, decorations, or paragraph flow.</summary>
    private bool RawTextMode => View == ViewMode.RawText;

    /// <summary>
    /// The left/right text inset from the editor edge, in device pixels, applied symmetrically to both
    /// sides (Settings ▸ padding). When null the theme's own <see cref="MarkdownTheme.PaddingLeft"/> is
    /// used. Changing it re-wraps every line, since the wrap width is the viewport minus this padding.
    /// </summary>
    public double? HorizontalPadding
    {
        get; set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            _layout.InvalidateAll(); // wrap width depends on padding — every block re-measures
            _extentWidth = 0;
            RaiseScrollInvalidated(EventArgs.Empty);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>The effective left/right inset: the <see cref="HorizontalPadding"/> override or the theme's default.</summary>
    private double PaddingLeft => HorizontalPadding ?? EditorTheme.PaddingLeft;

    /// <summary>
    /// The top/bottom text inset from the editor edge, in device pixels, applied symmetrically to both ends
    /// (Settings ▸ padding). When null the theme's own <see cref="MarkdownTheme.PaddingTop"/> is used. Unlike
    /// the horizontal inset this doesn't affect wrapping, only the scroll extent and where content is drawn.
    /// </summary>
    public double? VerticalPadding
    {
        get; set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            RaiseScrollInvalidated(EventArgs.Empty); // the scroll extent grows/shrinks by the new inset
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>The effective top/bottom inset: the <see cref="VerticalPadding"/> override or the theme's default.</summary>
    private double PaddingTop => VerticalPadding ?? EditorTheme.PaddingTop;

    /// <summary>
    /// When enabled, toggling a task checkbox keeps its sibling checkbox list sorted by completion state.
    /// A checked item only moves when every nested task in its subtree is also complete.
    /// </summary>
    public CompletedTaskSortMode CompletedTaskSorting { get; set; }

    /// <summary>
    /// When true (the default) long lines soft-wrap to the viewport width and the editor never scrolls
    /// horizontally; when false a line is one row and wide content scrolls sideways (PRD §7.3).
    /// </summary>
    public bool WordWrap
    {
        get; set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            _layout.InvalidateAll(); // wrapping changes for every block
            _extentWidth = 0;
            RaiseScrollInvalidated(EventArgs.Empty);
            InvalidateMeasure();
            InvalidateVisual();
        }
    } = true;

    public void MarkSaved() => Buffer.MarkSaved();

    /// <summary>Replace the document but leave it marked dirty (used for crash recovery, PRD §9.4).</summary>
    public void LoadDirty(string text) => Buffer.Load(text, markSaved: false);

    public void Undo() => Buffer.Undo();

    public void Redo() => Buffer.Redo();

    // ----- formatting commands (toolbar / Format menu — PRD §7.5) -----

    public void ToggleBold() => ToggleStarEmphasis(bold: true);

    public void ToggleItalic() => ToggleStarEmphasis(bold: false);

    public void ToggleInlineCode() => ToggleEmphasis("`", "`", TokenType.InlineCode);

    public void ToggleStrikethrough() => ToggleEmphasis("~~", "~~", TokenType.Strikethrough);

    public void ToggleHighlight() => ToggleEmphasis("==", "==", TokenType.Highlight);

    /// <summary>
    /// Insert a link <c>[text](url)</c>. With a selection it becomes the link text; with no selection the
    /// word the caret is inside is consumed as the text. Any link/image already inside the target is reduced
    /// to its visible text first (the outer link wins, the inner construct is removed). The caret lands in
    /// the empty URL slot — or in the empty text slot, when there was nothing to consume, so it stays visible.
    /// </summary>
    public void InsertLink() => InsertReference(image: false);

    /// <summary>
    /// The text a link inserted right now would use as its visible label: the selection, or the word under a
    /// bare caret (empty when neither). The shell reads this to prefill the "display text" field of its link
    /// dialog (⌘K) so wrapping an existing word keeps it.
    /// </summary>
    public string SuggestedLinkText() => LinkTarget().Text;

    /// <summary>One inline link/image on a line: its full source span (markers + url) and its visible-text span.</summary>
    private readonly record struct InlineLinkSpan(int FullStart, int FullEnd, int TextStart, int TextEnd);

    /// <summary>
    /// Insert an image <c>![alt](url)</c> with an empty URL — the bare-bones path used when no file is
    /// chosen. Like <see cref="InsertLink"/>, it consumes the selection/word as alt text and reduces any
    /// inner link/image to its text. The shell normally routes image insertion through a file picker and
    /// calls <see cref="InsertImageReference"/> with the chosen path instead.
    /// </summary>
    public void InsertImage() => InsertReference(image: true);

    /// <summary>Move the caret line / selected block up one position (Alt+Shift+Up).</summary>
    public void MoveLineUp() => MoveLines(-1);

    /// <summary>Move the caret line / selected block down one position (Alt+Shift+Down).</summary>
    public void MoveLineDown() => MoveLines(1);

    /// <summary>Select the paragraph block the caret is inside (Format ▸ Select Paragraph).</summary>
    public void SelectCurrentParagraph() =>
        SelectBlockAt(Buffer.Document.GetPosition(Buffer.Selection.Caret).Line);

    // ----- line-range editing helpers (shared by the blockquote/list toggles) -----

    /// <summary>Strip an existing list marker (and its task box) from <paramref name="text"/>, keeping indent and content.</summary>
    private static string StripListMarker(string text, LinePrefix p) =>
        p.HasList ? text[..p.MarkerStart] + text[p.Length..] : text;

    /// <summary>Select the whole document.</summary>
    public void SelectAll() => Buffer.Selection = new Selection(0, Buffer.Document.Length);

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
        _searchMatches = [];
        _currentMatch = -1;
        InvalidateVisual();
    }

    /// <summary>Select <c>[start, start+length)</c> and scroll it into view (used by find navigation).</summary>
    public void Select(int start, int length)
    {
        var len = Buffer.Document.Length;
        start = Math.Clamp(start, 0, len);
        var end = Math.Clamp(start + length, 0, len);
        Buffer.Selection = new Selection(start, end);
    }

    /// <summary>Replace <c>[start, start+length)</c> with <paramref name="replacement"/> (used by find &amp; replace).</summary>
    public void Replace(int start, int length, string replacement) => Buffer.ReplaceRange(start, length, replacement);

    // ----- clipboard (PRD §7.2 — copy yields Markdown source) -----
    //
    // The continuation after a clipboard await can resume off the UI thread, and the document edit
    // it performs raises events that call InvalidateVisual()/scroll — which Avalonia rejects from a
    // non-UI thread (that was the "paste crashes the app" bug). So every edit here is marshalled back
    // to the UI thread, and clipboard calls are guarded so a clipboard failure degrades to a no-op.

    Size IScrollable.Extent
    {
        get
        {
            EnsureLayout();
            var height = Math.Max(EditorTheme.LineHeight, TotalRows * EditorTheme.LineHeight) + PaddingTop * 2;
            // When wrapping, the content never exceeds the viewport width, so there is no horizontal scroll.
            var width = WordWrap ? _viewport.Width : Math.Max(PaddingLeft * 2 + _extentWidth, _viewport.Width);
            return new Size(width, height);
        }
    }

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
    public bool CanHorizontallyScroll { get; set; } = true;

    /// <summary>Whether vertical scrolling is enabled (set by the hosting scroll viewer).</summary>
    public bool CanVerticallyScroll { get; set; } = true;

    bool ILogicalScrollable.IsLogicalScrollEnabled => true;

    Size ILogicalScrollable.ScrollSize => new(EditorTheme.BaseFontSize, EditorTheme.LineHeight);

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

    // ----- layout forwarders — the wrapped-layout state and grouping/measurement algorithm live in
    // LayoutModel; these keep the control's many call sites unchanged and expose the model to ILayoutHost. -----
    private void EnsureLayout() => _layout.EnsureLayout();
    private int BlockOf(int line) => _layout.BlockOf(line);
    private int BlockAtRow(int row) => _layout.BlockAtRow(row);
    private (int Block, int BlockStart, VisualLine Visual, int Col) ResolveCaret(int offset) => _layout.ResolveCaret(offset);
    private VisualLine BlockVisual(int block) => _layout.BlockVisual(block);
    private int BlockStartOffset(int block) => _layout.BlockStartOffset(block);
    private int BlockEndOffset(int block) => _layout.BlockEndOffset(block);
    private int TotalRows => _layout.TotalRows;
    private double RowTop(int block) => _layout.RowTop(block);
    private int[] _rowCounts => _layout.RowCounts;
    private int[] _rowPrefix => _layout.RowPrefix;
    private int[] _blockStartLine => _layout.BlockStartLine;

    // ILayoutHost — the live state LayoutModel reads to group and measure blocks.
    TextDocument ILayoutHost.Document => Buffer.Document;
    MarkdownModel ILayoutHost.Model => _model;
    MarkdownTheme ILayoutHost.Theme => EditorTheme;
    double ILayoutHost.WrapWidth => WrapWidth();
    bool ILayoutHost.RawTextMode => RawTextMode;
    bool ILayoutHost.MarkersRevealed => MarkersRevealed;
    double ILayoutHost.CheckboxGutter => CheckboxGutter;
    bool ILayoutHost.IsCollapsedBlankLine(int line) => IsCollapsedBlankLine(line);
    string? ILayoutHost.OrderedMarkerText(int line, string lineText, int sLine, int enLine)
        => OrderedMarkerText(line, lineText, sLine, enLine);

    /// <summary>The detected front-matter block range; for tests.</summary>
    internal (int Start, int End)? FrontMatterBlockForTest() => FrontMatterBlock();

    /// <summary>The text a code-block copy button would place on the clipboard; for tests.</summary>
    internal string CodeBlockTextForTest(int firstLine, int lastLine) => CodeBlockText(firstLine, lastLine);

    /// <summary>The copy-button hit regions recorded by the last render (line ranges); for tests.</summary>
    internal IReadOnlyList<(int First, int Last)> CodeCopyButtonsForTest() =>
        _codeCopyButtons.ConvertAll(b => (b.First, b.Last));

    /// <summary>The pairs auto-inserted/closed and used to wrap a selection (opener → closer).</summary>
    private static readonly IReadOnlyDictionary<char, char> AutoPairs = new Dictionary<char, char>
    {
        ['('] = ')',
        ['['] = ']',
        ['`'] = '`',
        ['*'] = '*',
        ['_'] = '_',
    };

    /// <summary>The reorder drop-indicator state (active + the bracketed line range); for tests.</summary>
    internal (bool Active, int Start, int End) ReorderIndicatorForTest() => (_reorderActive, _reorderStartLine, _reorderEndLine);

    /// <summary>The kind of list item a line carries (or <see cref="ItemKind.None"/> for a non-list line).</summary>
    private enum ItemKind
    {
        None,
        Bullet,
        Numbered,
        Checkbox,
    }

    /// <summary>Whether <paramref name="line"/> is blank (only whitespace).</summary>
    private bool IsBlankLine(int line) => Buffer.Document.GetLineText(line).Trim().Length == 0;

    /// <summary>Whether the caret <paramref name="offset"/> sits on a collapsed blank line (not a valid stop).</summary>
    private bool IsCollapsedOffset(int offset) =>
        IsCollapsedBlankLine(Buffer.Document.GetPosition(offset).Line);

    /// <summary>Move the selected line(s) / subtree up/down; for tests.</summary>
    internal void MoveLinesForTest(int dir) => MoveLines(dir);

    // Test hooks: drive the same editing-assist paths the key handlers use, without a focused window.
    internal void EnterForTest() => HandleEnter();

    /// <summary>Shift+Enter: a plain newline in the document's own line ending, no list continuation.</summary>
    internal void ShiftEnterForTest() => Buffer.Insert(DetectNewline(Buffer.Document));

    internal void TabForTest(bool shift) => HandleTab(shift);

    private KeyModifiers _modifiers;
    private bool _dragging;
    private double _goalX = double.NaN; // remembered caret x for sticky up/down movement (NaN = recompute)

    private static int CharClass(char c) =>
        char.IsLetterOrDigit(c) || c == '_' ? 0 : char.IsWhiteSpace(c) ? 1 : 2;

    /// <summary>Select the word/run at <paramref name="offset"/> on <paramref name="line"/>; for tests.</summary>
    internal void SelectWordForTest(int line, int offset) => SelectWordAt(line, offset);

    /// <summary>Select the paragraph block at <paramref name="line"/> (triple-click); for tests.</summary>
    internal void SelectBlockForTest(int line) => SelectBlockAt(line);

    /// <summary>Move the caret up/down by <paramref name="delta"/> lines (sticky goal column); for tests.</summary>
    internal void MoveCaretByLineForTest(int delta) => MoveCaretByLine(delta, extend: false);

    /// <summary>Set the caret to <paramref name="offset"/> as a plain click would; for tests.</summary>
    internal void ClickAtForTest(int offset) => MoveCaret(offset, extend: false);

    /// <summary>Toggle the task checkbox covering <paramref name="offset"/> on <paramref name="line"/>; for tests.</summary>
    internal bool ToggleTaskForTest(int line, int offset) => TryToggleTaskAt(line, offset);

    /// <summary>Simulate a click at content-space x <paramref name="contentX"/> on a checkbox glyph; for tests.</summary>
    internal bool ToggleTaskClickForTest(int line, double contentX) =>
        TryToggleTaskClick(line, new Point(contentX + PaddingLeft - _offset.X, 0), Buffer.Document.GetLineStart(line));

    /// <summary>The laid-out visual line for <paramref name="line"/> (concealment is caret-independent in WYSIWYG mode).</summary>
    private VisualLine LineVisual(int line) => BlockVisual(BlockOf(line));

    /// <summary>Whether <paramref name="col"/> sits strictly between two hidden characters (a zero-width
    /// junction of concealed/decoration runs): not next to any visible glyph and not a line boundary, so it
    /// is visually indistinguishable from its neighbours and not a useful caret stop.</summary>
    private static bool IsBetweenHidden(VisualLine visual, int col) =>
        col > 0 && col < visual.RawLength && visual.IsColumnHidden(col - 1) && visual.IsColumnHidden(col);

    /// <summary>Move the caret atomically in <paramref name="dir"/>; for tests.</summary>
    internal void VisualStepForTest(int dir, bool extend = false) => MoveCaret(VisualStep(Buffer.Selection.Caret, dir), extend);

    /// <summary>Move the caret to <paramref name="dir"/> word boundary; for tests.</summary>
    internal void WordMoveForTest(int dir, bool extend = false) => MoveCaret(WordBoundary(Buffer.Selection.Caret, dir), extend);

    /// <summary>Delete a word in <paramref name="dir"/>; for tests.</summary>
    internal void DeleteWordForTest(int dir) => DeleteWord(dir);

    /// <summary>Smart-Home the caret; for tests.</summary>
    internal void HomeForTest(bool extend = false) => MoveCaret(SmartHome(Buffer.Selection.Caret), extend);

    /// <summary>Move the caret to the end of its visual row; for tests.</summary>
    internal void EndForTest(bool extend = false) => MoveCaret(RowEndOffset(Buffer.Selection.Caret), extend);

    /// <summary>Up/Down arrow with selection-edge collapse; for tests.</summary>
    internal void ArrowByLineForTest(int delta, bool extend = false) => ArrowByLine(delta, extend);

    /// <summary>A list line's marker column and how its number's digit count changed when renumbered (for caret tracking).</summary>
    private readonly record struct RenumberInfo(int MarkerStart, int DigitDelta);

    /// <summary>Whether <paramref name="line"/> is panelled as code (fenced/indented); for tests.</summary>
    internal bool IsCodeLineForTest(int line) => IsCodeLine(line);

    // Task-checkbox geometry, shared between the gutter width and the drawn glyph so they stay in sync.
    private const double CheckboxLeftPad = 1;   // small inset before the box
    private double CheckboxBoxSize => EditorTheme.BaseFontSize * 0.95;
    private double CheckboxGap => EditorTheme.BaseFontSize * 0.45; // space between the box and the following text

    /// <summary>
    /// The checkbox gutter width — the box plus a fixed gap before the text. Deliberately independent of the
    /// source "[ ]"/"[x]" punctuation, so a checked and an unchecked item indent their text by the same amount.
    /// </summary>
    private double CheckboxGutter => CheckboxLeftPad + CheckboxBoxSize + CheckboxGap;

    /// <summary>The laid-out visual line (its paragraph block) for <paramref name="line"/>; for concealment + offset-map tests.</summary>
    internal VisualLine BuildVisualLineForTest(int line) => BlockVisual(BlockOf(line));

    /// <summary>Document offset the visual line resolves <paramref name="x"/> to on <paramref name="line"/>'s block (hit-test); for tests.</summary>
    internal int OffsetAtForTest(int line, double x) => OffsetAt(BlockOf(line), 0, x);

    /// <summary>The number of wrapped visual rows <paramref name="line"/>'s block occupies at the current width; for tests.</summary>
    internal int RowCountForTest(int line) => LineVisual(line).RowCount;

    /// <summary>The paragraph-block index <paramref name="line"/> belongs to; for tests.</summary>
    internal int BlockOfLineForTest(int line) => BlockOf(line);

    /// <summary>The source-content length (columns) of <paramref name="line"/>'s block; for tests.</summary>
    internal int BlockLengthForTest(int line) => LineVisual(line).RawLength;

    /// <summary>Number of <see cref="FormattedText"/> objects built (cache misses); for perf tests.</summary>
    internal int FormattedBuildCount => _measurer.FormattedBuildCount;

    /// <summary>Number of real text-shaping measurements performed (cache misses); for perf tests.</summary>
    internal int RawMeasureCount => _measurer.RawMeasureCount;

    /// <summary>Measure a run with the default text style, exercising the width cache; for perf tests.</summary>
    internal double MeasureWidthForTest(string text) => _measurer.MeasureWidth(text, EditorTheme.Resolve(TokenType.Text));

}
