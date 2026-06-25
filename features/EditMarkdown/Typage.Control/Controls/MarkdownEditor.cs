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

    // Drop-indicator state for Alt+Shift line reorder: while a reorder "session" is active (consecutive
    // moves), faint dotted lines bracket the moved block so it's clear what's being shuffled. Cleared by any
    // other key, click, or edit.
    private bool _reorderActive;
    private int _reorderStartLine;
    private int _reorderEndLine;

    // Per-code-block "copy" buttons, rebuilt each render: each holds its hit rect (control space) and the
    // line range of the code run it copies. _copiedFeedbackLine is the run currently showing "Copied!".
    private readonly List<(Rect Rect, int First, int Last)> _codeCopyButtons = new();
    private int _copiedFeedbackLine = -1;
    private DispatcherTimer? _copyFeedbackTimer;

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

    // Wrapped-layout cache, keyed by *paragraph block* — a maximal run of consecutive plain-paragraph
    // source lines that flow together (their newlines render as spaces, PRD §7.3); every other line is its
    // own singleton block. _blockStartLine[b] is block b's first document line (with a trailing sentinel =
    // LineCount), _blockOfLine[l] is line l's block, and _rowCounts/_rowPrefix hold each block's wrapped
    // row count and running sum. All are rebuilt when the text, theme, or wrap width changes.
    private int[] _blockStartLine = { 0, 0 };
    private int[] _blockOfLine = Array.Empty<int>();
    private int[] _rowCounts = Array.Empty<int>();
    private int[] _rowPrefix = new int[1];
    private bool _layoutDirty = true;
    private double _layoutWidth = double.NaN;

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
            _layoutDirty = true; // line/row counts may have changed → rebuild the wrapped-layout cache
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
            _layoutDirty = true; // line heights/wrapping depend on the font metrics
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

    /// <summary>
    /// When true (the default) long lines soft-wrap to the viewport width and the editor never scrolls
    /// horizontally; when false a line is one row and wide content scrolls sideways (PRD §7.3).
    /// </summary>
    public bool WordWrap
    {
        get => _wordWrap;
        set
        {
            if (_wordWrap == value)
            {
                return;
            }

            _wordWrap = value;
            _layoutDirty = true;
            _extentWidth = 0;
            RaiseScrollInvalidated(EventArgs.Empty);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private bool _wordWrap = true;

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

    public void ToggleHighlight() => Wrap("==", "==");

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
    /// Wrap the selection in <paramref name="left"/>/<paramref name="right"/>. With no selection, wrap the
    /// word the caret is inside (so ⌘B on a bare caret in a word bolds the whole word); if the caret isn't
    /// touching a word, insert the empty pair to type into. Either way the wrapped content is left selected.
    /// </summary>
    public void Wrap(string left, string right)
    {
        var sel = _buffer.Selection;
        int start, length;
        if (!sel.IsEmpty)
        {
            (start, length) = (sel.Start, sel.Length);
        }
        else if (WordAround(sel.Caret) is { } word)
        {
            (start, length) = (word.Start, word.End - word.Start);
        }
        else
        {
            _buffer.ReplaceRange(sel.Caret, 0, left + right);
            _buffer.Selection = Selection.At(sel.Caret + left.Length);
            Focus();
            return;
        }

        var inner = _buffer.Document.GetText(start, length);
        _buffer.ReplaceRange(start, length, left + inner + right);
        _buffer.Selection = new Selection(start + left.Length, start + left.Length + inner.Length);
        Focus();
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

    /// <summary>Wrap the selection as an image <c>![alt](url)</c>, leaving the caret in the URL slot.</summary>
    public void InsertImage()
    {
        var sel = _buffer.Selection;
        var text = sel.IsEmpty ? string.Empty : _buffer.Document.GetText(sel.Start, sel.Length);
        var replacement = $"![{text}]()";
        _buffer.ReplaceRange(sel.Start, sel.Length, replacement);
        _buffer.Selection = Selection.At(sel.Start + replacement.Length - 1); // inside ()
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

    /// <summary>Move the caret line / selected block up one position (Alt+Shift+Up).</summary>
    public void MoveLineUp() => MoveLines(-1);

    /// <summary>Move the caret line / selected block down one position (Alt+Shift+Down).</summary>
    public void MoveLineDown() => MoveLines(1);

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

    /// <summary>Select the paragraph block the caret is inside (Format ▸ Select Paragraph).</summary>
    public void SelectCurrentParagraph() =>
        SelectBlockAt(_buffer.Document.GetPosition(_buffer.Selection.Caret).Line);

    // ----- line-range editing helpers (shared by the blockquote/list toggles) -----

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

    /// <summary>Strip an existing list marker (and its task box) from <paramref name="text"/>, keeping indent and content.</summary>
    private static string StripListMarker(string text, LinePrefix p) =>
        p.HasList ? text[..p.MarkerStart] + text[p.Length..] : text;

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
        EnsureLayout();
        var h = double.IsFinite(availableSize.Height)
            ? availableSize.Height
            : Math.Max(_theme.LineHeight, TotalRows * _theme.LineHeight);
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_viewport != finalSize)
        {
            var widthChanged = Math.Abs(_viewport.Width - finalSize.Width) > 0.5;
            _viewport = finalSize;
            if (widthChanged)
            {
                InvalidateVisual(); // a width change re-wraps the lines
            }

            RaiseScrollInvalidated(EventArgs.Empty);
        }

        return finalSize;
    }

    Size IScrollable.Extent
    {
        get
        {
            EnsureLayout();
            var height = Math.Max(_theme.LineHeight, TotalRows * _theme.LineHeight) + _theme.PaddingTop * 2;
            // When wrapping, the content never exceeds the viewport width, so there is no horizontal scroll.
            var width = _wordWrap ? _viewport.Width : Math.Max(_theme.PaddingLeft * 2 + _extentWidth, _viewport.Width);
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

    // ----- wrapped layout (variable line heights) -----

    /// <summary>The content width lines wrap to (0 disables wrapping, e.g. before the control is arranged).</summary>
    private double WrapWidth()
    {
        if (!_wordWrap)
        {
            return 0;
        }

        var w = _viewport.Width > 0 ? _viewport.Width : Bounds.Width;
        return w > 0 ? Math.Max(40, w - _theme.PaddingLeft * 2) : 0;
    }

    /// <summary>
    /// Rebuild the paragraph-block grouping and per-block row-count cache (with prefix sums) when the text,
    /// theme, or wrap width has changed. Consecutive plain-paragraph lines are merged into one flowing
    /// block; every other line is a singleton. Measurement is cached, so an unchanged block is cheap.
    /// </summary>
    private void EnsureLayout()
    {
        var n = _buffer.Document.LineCount;
        var width = WrapWidth();
        if (!_layoutDirty && _layoutWidth == width && _blockOfLine.Length == n)
        {
            return;
        }

        // Group lines into blocks: a line joins the previous block only if it and the line above are both
        // plain paragraph text and the line above doesn't end in a hard break.
        var starts = new List<int>();
        var ofLine = new int[n];
        for (var l = 0; l < n; l++)
        {
            var joins = l > 0 && IsFlowLine(l) && IsFlowLine(l - 1) && !EndsWithHardBreak(l - 1);
            if (!joins)
            {
                starts.Add(l);
            }

            ofLine[l] = starts.Count - 1;
        }

        _blockStartLine = new int[starts.Count + 1];
        for (var b = 0; b < starts.Count; b++)
        {
            _blockStartLine[b] = starts[b];
        }

        _blockStartLine[starts.Count] = n;
        _blockOfLine = ofLine;

        _rowCounts = new int[starts.Count];
        _rowPrefix = new int[starts.Count + 1];
        for (var b = 0; b < starts.Count; b++)
        {
            _rowCounts[b] = BlockVisual(b).RowCount;
            _rowPrefix[b + 1] = _rowPrefix[b] + _rowCounts[b];
        }

        _layoutWidth = width;
        _layoutDirty = false;
    }

    /// <summary>
    /// Whether <paramref name="line"/> is a plain-paragraph line that flows with its neighbours — non-blank,
    /// ordinary block state (not code/front matter), no leading block/list/quote/heading marker, and not a
    /// table row. Headings, lists, quotes, rules, code, and blank lines all break the flow.
    /// </summary>
    private bool IsFlowLine(int line)
    {
        var doc = _buffer.Document;
        var text = doc.GetLineText(line);
        if (text.Trim().Length == 0)
        {
            return false; // a blank line separates paragraphs
        }

        // A line that starts with indentation is laid out on its own row: the user's leading tabs/spaces
        // are deliberate, so it neither flows into the line above nor absorbs the line below (an indented
        // line under plain prose otherwise merged into it with the newline becoming a space).
        if (text[0] == ' ' || text[0] == '\t')
        {
            return false;
        }

        if (_model.EnteringState(line).Kind != BlockKind.Normal)
        {
            return false; // inside fenced/indented code or front matter
        }

        var toks = _model.LineTokens(line);
        if (toks.Count == 0)
        {
            return false;
        }

        if (toks[0].Type is TokenType.HeadingMarker or TokenType.BlockquoteMarker or TokenType.ListMarker
            or TokenType.TaskMarker or TokenType.HorizontalRule or TokenType.CodeFence or TokenType.CodeText
            or TokenType.FrontMatterFence or TokenType.FrontMatterText or TokenType.TableDelimiter)
        {
            return false;
        }

        // A pipe-table body/header row carries single-'|' Marker tokens (only the table tokenizer emits
        // those); keep it out of paragraph flow.
        foreach (var t in toks)
        {
            if (t.Type == TokenType.Marker && t.Length == 1 && doc[t.Start] == '|')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether <paramref name="line"/> ends in a hard line break (so the next line shouldn't flow into it).</summary>
    private bool EndsWithHardBreak(int line)
    {
        var toks = _model.LineTokens(line);
        return toks.Count > 0 && toks[^1].Type == TokenType.HardBreak;
    }

    /// <summary>The block <paramref name="line"/> belongs to.</summary>
    private int BlockOf(int line)
    {
        EnsureLayout();
        return _blockOfLine[Math.Clamp(line, 0, _blockOfLine.Length - 1)];
    }

    /// <summary>The document offset of <paramref name="block"/>'s first character.</summary>
    private int BlockStartOffset(int block) => _buffer.Document.GetLineStart(_blockStartLine[block]);

    /// <summary>The document offset just past <paramref name="block"/>'s last content character.</summary>
    private int BlockEndOffset(int block) => _buffer.Document.GetLineEnd(_blockStartLine[block + 1] - 1);

    /// <summary>Total number of visual rows across the whole document.</summary>
    private int TotalRows => _rowPrefix.Length > 0 ? _rowPrefix[^1] : 0;

    /// <summary>The top of <paramref name="block"/>'s first row in content space (before padding/offset).</summary>
    private double RowTop(int block) => _rowPrefix[Math.Clamp(block, 0, _rowPrefix.Length - 1)] * _theme.LineHeight;

    /// <summary>The block containing global visual <paramref name="row"/> (binary search over the prefix sums).</summary>
    private int BlockAtRow(int row)
    {
        var n = _rowCounts.Length;
        if (n == 0)
        {
            return 0;
        }

        row = Math.Clamp(row, 0, Math.Max(0, TotalRows - 1));
        int lo = 0, hi = n - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) >> 1;
            if (_rowPrefix[mid] <= row)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

    /// <summary>The caret's block, its start offset, laid-out block, and block-relative column for <paramref name="offset"/>.</summary>
    private (int Block, int BlockStart, VisualLine Visual, int Col) ResolveCaret(int offset)
    {
        EnsureLayout();
        var doc = _buffer.Document;
        var block = BlockOf(doc.GetPosition(offset).Line);
        var blockStart = BlockStartOffset(block);
        var visual = BlockVisual(block);
        var col = Math.Clamp(offset - blockStart, 0, visual.RawLength);
        return (block, blockStart, visual, col);
    }

    // ----- rendering -----

    public override void Render(DrawingContext context)
    {
        var doc = _buffer.Document;
        context.FillRectangle(_theme.Background, new Rect(Bounds.Size));

        EnsureLayout();
        var caret = _buffer.Selection;
        var caretLine = doc.GetPosition(caret.Caret).Line;

        // Draw only the paragraph blocks whose wrapped rows intersect the viewport (virtualization over
        // variable block heights). The viewport height is 0 until the control is arranged in a scroll viewer.
        var lineH = _theme.LineHeight;
        var viewportH = _viewport.Height > 0 ? _viewport.Height : Bounds.Height;
        var contentTop = _offset.Y - _theme.PaddingTop;
        var firstRow = Math.Max(0, (int)(contentTop / lineH));
        var lastRow = (int)((contentTop + viewportH) / lineH) + 1;
        var first = BlockAtRow(firstRow);
        var last = Math.Min(_rowCounts.Length, BlockAtRow(lastRow) + 1);

        // Content is drawn in document space, inset by the padding and shifted by the scroll offset.
        using (context.PushTransform(Matrix.CreateTranslation(_theme.PaddingLeft - _offset.X, _theme.PaddingTop - _offset.Y)))
        {
            DrawPropertiesPanel(context, first, last); // front-matter "Properties" backdrop, under the text

            var maxWidth = _extentWidth;
            for (var block = first; block < last; block++)
            {
                maxWidth = Math.Max(maxWidth, RenderBlock(context, doc, block, caret, caretLine));
            }

            DrawReorderIndicator(context, lineH);
            DrawCodeCopyButtons(context, first, last, lineH);

            // Only relevant when wrapping is off: grow the horizontal extent as wide lines come into view.
            if (!_wordWrap && maxWidth > _extentWidth)
            {
                _extentWidth = maxWidth;
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
    private void DrawPropertiesPanel(DrawingContext context, int firstVisibleBlock, int lastVisibleBlock)
    {
        if (FrontMatterBlock() is not { } fm)
        {
            return;
        }

        var startBlock = BlockOf(fm.Start);
        var endBlock = BlockOf(fm.End);
        if (endBlock < firstVisibleBlock || startBlock >= lastVisibleBlock)
        {
            return;
        }

        var lineH = _theme.LineHeight;
        var viewportW = _viewport.Width > 0 ? _viewport.Width : Bounds.Width;
        var panelRight = Math.Max(220, viewportW - _theme.PaddingLeft * 2);
        var startTop = RowTop(startBlock);
        var endBottom = RowTop(endBlock) + _rowCounts[endBlock] * lineH;
        context.FillRectangle(_theme.PropertiesBackground, new Rect(-8, startTop + 2, panelRight + 8, endBottom - startTop - 4), 6);

        // "Properties" label on the opening-fence row (its source is only "---", so no text overlap).
        var label = GetFormattedText("Properties", _theme.Resolve(TokenType.FrontMatterFence), _dimMarker);
        var labelY = startTop + (lineH - label.Height) / 2;
        context.DrawText(label, new Point(panelRight - label.Width - 6, labelY));
    }

    /// <summary>
    /// Faint dotted lines just above the first and just below the last line of the block being reordered
    /// (Alt+Shift+Up/Down), so it's visually clear what's moving. Drawn in document space inside the
    /// content transform; a no-op unless a reorder session is active.
    /// </summary>
    private void DrawReorderIndicator(DrawingContext context, double lineH)
    {
        if (!_reorderActive)
        {
            return;
        }

        var n = _buffer.Document.LineCount;
        if (_reorderStartLine < 0 || _reorderEndLine >= n || _reorderStartLine > _reorderEndLine)
        {
            _reorderActive = false;
            return;
        }

        var top = RowTop(BlockOf(_reorderStartLine));
        var bottomBlock = BlockOf(_reorderEndLine);
        var bottom = RowTop(bottomBlock) + _rowCounts[bottomBlock] * lineH;
        var pen = new Pen(_dimMarker, 1) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) };
        var left = -6.0;
        var right = ContentRight();
        context.DrawLine(pen, new Point(left, top), new Point(right, top));
        context.DrawLine(pen, new Point(left, bottom), new Point(right, bottom));
    }

    /// <summary>
    /// A "copy" button at the top-right of each code block (fenced or indented) visible in the viewport, plus
    /// a transient "Copied!" label after a click. Drawn in document space; the hit rect is stored in control
    /// space (the inverse of the render transform) so <see cref="OnPointerPressed"/> can test pointer clicks.
    /// </summary>
    private void DrawCodeCopyButtons(DrawingContext context, int firstBlock, int lastBlock, double lineH)
    {
        _codeCopyButtons.Clear();
        var doc = _buffer.Document;
        for (var b = firstBlock; b < lastBlock; b++)
        {
            var line = _blockStartLine[b];
            if (!IsCodeLine(line) || (line > 0 && IsCodeLine(line - 1)))
            {
                continue; // only the first line of each contiguous code run carries the button
            }

            var endLine = line;
            while (endLine + 1 < doc.LineCount && IsCodeLine(endLine + 1))
            {
                endLine++;
            }

            const double w = 24;
            var h = lineH - 8;
            var x = ContentRight() - w - 6;
            var y = RowTop(b) + 4;
            var rect = new Rect(x, y, w, h);
            context.FillRectangle(_theme.CodeBlockBackground, rect, 4);
            DrawCopyGlyph(context, rect);

            if (_copiedFeedbackLine == line)
            {
                var label = GetFormattedText("Copied!", _theme.Resolve(TokenType.CodeText), _theme.LinkForeground);
                context.DrawText(label, new Point(x - label.Width - 6, y + (h - label.Height) / 2));
            }

            // Store the hit rect in control space (undo the PaddingLeft/Top − scroll-offset render transform).
            _codeCopyButtons.Add((new Rect(x + _theme.PaddingLeft - _offset.X, y + _theme.PaddingTop - _offset.Y, w, h), line, endLine));
        }
    }

    /// <summary>Draw a two-overlapping-rectangles "copy" glyph centred in <paramref name="button"/>.</summary>
    private void DrawCopyGlyph(DrawingContext context, Rect button)
    {
        var pen = new Pen(_theme.MarkerForeground, 1.2);
        var cx = button.X + button.Width / 2;
        var cy = button.Y + button.Height / 2;
        const double gw = 8, gh = 9, off = 2;
        var back = new Rect(cx - gw / 2 + off, cy - gh / 2 - off, gw, gh);
        var front = new Rect(cx - gw / 2 - off, cy - gh / 2 + off, gw, gh);
        context.DrawRectangle(_theme.CodeBlockBackground, pen, back, 1.5, 1.5);
        context.DrawRectangle(_theme.CodeBlockBackground, pen, front, 1.5, 1.5);
    }

    /// <summary>The source text of the code run <c>[firstLine, lastLine]</c> to copy: a fenced block's inner
    /// lines (without the <c>```</c> fences), or an indented block's lines dedented by one level.</summary>
    private string CodeBlockText(int firstLine, int lastLine)
    {
        var doc = _buffer.Document;
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

    /// <summary>Copy the code run <c>[first, last]</c> to the clipboard and flash a "Copied!" confirmation.</summary>
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

    /// <summary>The text a code-block copy button would place on the clipboard; for tests.</summary>
    internal string CodeBlockTextForTest(int firstLine, int lastLine) => CodeBlockText(firstLine, lastLine);

    /// <summary>The copy-button hit regions recorded by the last render (line ranges); for tests.</summary>
    internal IReadOnlyList<(int First, int Last)> CodeCopyButtonsForTest() =>
        _codeCopyButtons.ConvertAll(b => (b.First, b.Last));

    private double RenderBlock(DrawingContext context, TextDocument doc, int block, Selection sel, int caretLine)
    {
        var lineH = _theme.LineHeight;
        var blockTop = RowTop(block);
        var startLine = _blockStartLine[block];
        var blockStart = doc.GetLineStart(startLine);
        var visual = BlockVisual(block);

        // A code block (always a singleton block) gets a subtle background panel spanning all its rows.
        if (IsCodeLine(startLine))
        {
            context.FillRectangle(_theme.CodeBlockBackground, new Rect(-8, blockTop, ContentRight() + 16, visual.RowCount * lineH));
        }

        // A horizontal rule has no real text to place a visible caret on, so when the caret is on it
        // (WYSIWYG view, no range selection) highlight the whole rule row in the selection colour to show
        // the rule line itself is the focused / selected line.
        if (IsFocused && ShouldHighlightRuleLine(startLine))
        {
            context.FillRectangle(_theme.SelectionBackground, new Rect(-8, blockTop, ContentRight() + 16, visual.RowCount * lineH));
        }

        // Search-match highlights (under the text), then the selection on top.
        if (_searchMatches.Count > 0)
        {
            DrawSearchMatches(context, blockStart, blockTop, visual);
        }

        if (sel.Length > 0)
        {
            DrawSelection(context, blockStart, blockTop, sel, visual);
        }

        // Draw every visual run on its wrapped row: concealed marker runs vanish (zero width, not drawn);
        // text and decorations advance the cursor exactly as the offset↔visual map measured them.
        foreach (var run in visual.Runs)
        {
            var rowTop = blockTop + run.Row * lineH;
            switch (run.Kind)
            {
                case VisualRunKind.Text:
                    DrawTextRun(context, run, rowTop);
                    break;
                case VisualRunKind.Decoration:
                    DrawDecoration(context, run, rowTop);
                    break;
            }
        }

        // Caret — placed through the map (correct amid hidden markers) on its wrapped row.
        if (BlockOf(caretLine) == block && IsFocused)
        {
            var col = Math.Clamp(sel.Caret - blockStart, 0, visual.RawLength);
            var caretX = visual.ColumnToX(col);
            var h = _theme.BaseFontSize * 1.3;
            var y = blockTop + visual.RowOf(col) * lineH + (lineH - h) / 2;
            context.FillRectangle(_theme.Caret, new Rect(caretX, y, 1.5, h));
        }

        return visual.Width;
    }

    /// <summary>Draw one visible run (content, or a revealed marker — markers carry their own brush in the style).</summary>
    private void DrawTextRun(DrawingContext context, VisualRun run, double lineTop)
    {
        var ft = GetFormattedText(run.Text, run.Style, run.Style.Foreground);
        var y = lineTop + (_theme.LineHeight - ft.Height) / 2;

        if (run.Style.Background is not null)
        {
            context.FillRectangle(run.Style.Background, new Rect(run.X, lineTop + 2, ft.Width, _theme.LineHeight - 4), 3);
        }

        context.DrawText(ft, new Point(run.X, y));
    }

    private void DrawSearchMatches(DrawingContext context, int blockStart, double blockTop, VisualLine visual)
    {
        var lineH = _theme.LineHeight;
        var blockEnd = blockStart + visual.RawLength;
        // Matches are sorted, non-overlapping → both Start and End ascend. Binary-search the first
        // match that could touch this block, then walk forward, so a block only touches its own matches
        // (not all of them) — O(log M + matches-in-block) instead of O(M) per block.
        for (var i = FirstMatchReaching(blockStart); i < _searchMatches.Count; i++)
        {
            var m = _searchMatches[i];
            if (m.Start >= blockEnd)
            {
                break; // all later matches start past this block
            }

            var s = Math.Clamp(m.Start - blockStart, 0, visual.RawLength);
            var e = Math.Clamp(m.End - blockStart, 0, visual.RawLength);
            if (e <= s)
            {
                continue;
            }

            var brush = i == _currentMatch ? _theme.SearchMatchCurrent : _theme.SearchMatch;
            foreach (var (row, x1, x2) in visual.Spans(s, e))
            {
                context.FillRectangle(brush, new Rect(x1, blockTop + row * lineH + 2, x2 - x1, lineH - 4), 2);
            }
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

    private void DrawSelection(DrawingContext context, int blockStart, double blockTop, Selection sel, VisualLine visual)
    {
        var lineH = _theme.LineHeight;
        var contentLen = visual.RawLength;
        var blockEnd = blockStart + contentLen;
        if (sel.Start > blockEnd || sel.End < blockStart)
        {
            return; // selection doesn't touch this block
        }

        var selStartCol = Math.Clamp(sel.Start - blockStart, 0, contentLen);
        var selEndCol = Math.Clamp(sel.End - blockStart, 0, contentLen);
        if (selEndCol <= selStartCol)
        {
            return;
        }

        foreach (var (row, x1, x2) in visual.Spans(selStartCol, selEndCol))
        {
            context.FillRectangle(_theme.SelectionBackground, new Rect(x1, blockTop + row * lineH + 2, x2 - x1, lineH - 4));
        }
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
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var meta = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var ctrl = control || meta; // the platform "command" modifier for the formatting/clipboard shortcuts
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var isMac = OperatingSystem.IsMacOS();
        var wordMod = isMac ? alt : control; // word-wise nav/delete: ⌥ on macOS, Ctrl elsewhere
        var doc = _buffer.Document;
        var caret = _buffer.Selection.Caret;
        var handled = true;

        // Any key other than a continued line move ends the reorder drop-indicator session.
        if (!(alt && shift && e.Key is Key.Up or Key.Down))
        {
            ClearReorderIndicator();
        }

        switch (e.Key)
        {
            // --- deletion (⌘⌫ to line start on macOS, then word-wise) ---
            case Key.Back when isMac && meta: DeleteToRowStart(caret); break;
            case Key.Back when wordMod: DeleteWord(-1); break;
            case Key.Delete when wordMod: DeleteWord(1); break;
            case Key.Back: if (!TryBackspacePair() && !TryVisualBackspace()) { _buffer.Backspace(); } break;
            case Key.Delete: if (!TryVisualDelete()) { _buffer.Delete(); } break;

            case Key.Enter when shift: _buffer.Insert("\n"); break; // plain newline, no list continuation
            case Key.Enter: HandleEnter(); break;
            case Key.Tab: HandleTab(shift); break;

            // --- horizontal navigation: word, then macOS ⌘-line, then by grapheme ---
            case Key.Left when wordMod: MoveCaret(WordBoundary(caret, -1), shift); break;
            case Key.Right when wordMod: MoveCaret(WordBoundary(caret, 1), shift); break;
            case Key.Left when isMac && meta: MoveCaret(RowStartOffset(caret), shift); break;
            case Key.Right when isMac && meta: MoveCaret(RowEndOffset(caret), shift); break;
            case Key.Left: MoveCaret(VisualStep(caret, -1), shift); break;
            case Key.Right: MoveCaret(VisualStep(caret, 1), shift); break;

            // --- vertical navigation: move lines, doc ends (⌘↑/↓ on macOS), else by visual row ---
            case Key.Up when alt && shift: MoveLines(-1); break;
            case Key.Down when alt && shift: MoveLines(1); break;
            case Key.Up when isMac && meta: MoveCaret(0, shift); break;
            case Key.Down when isMac && meta: MoveCaret(doc.Length, shift); break;
            case Key.Up: ArrowByLine(-1, shift); break;
            case Key.Down: ArrowByLine(1, shift); break;
            case Key.PageUp: MoveCaretByLine(-PageRows(), shift); break;
            case Key.PageDown: MoveCaretByLine(PageRows(), shift); break;

            // --- line / document extents ---
            case Key.Home when control: MoveCaret(0, shift); break;        // Ctrl+Home → document start
            case Key.End when control: MoveCaret(doc.Length, shift); break; // Ctrl+End → document end
            case Key.Home: MoveCaret(SmartHome(caret), shift); break;       // content start ⇄ visual-row start
            case Key.End: MoveCaret(RowEndOffset(caret), shift); break;     // end of the visual row

            case Key.Escape when !_buffer.Selection.IsEmpty: MoveCaret(_buffer.Selection.Caret, extend: false); break;

            // Clipboard. (⌘⇧X is strikethrough below — it must be matched before plain ⌘X = Cut.)
            case Key.C when ctrl: _ = CopyAsync(); break;
            case Key.X when ctrl && shift: ToggleStrikethrough(); break;
            case Key.X when ctrl: _ = CutAsync(); break;
            case Key.V when ctrl: _ = PasteAsync(); break;

            // Formatting (the shortcuts a Markdown editor is expected to have).
            case Key.B when ctrl: ToggleBold(); break;
            case Key.I when ctrl: ToggleItalic(); break;
            case Key.E when ctrl: ToggleInlineCode(); break;
            case Key.K when ctrl: InsertLink(); break;
            case Key.L when ctrl: ToggleTask(); break;
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
        var prefix = LinePrefix.Parse(doc.GetLineText(caretLine));

        // Plain text, single caret, not on a list item → a Tab inserts one indent unit (a tab where the
        // line already uses tabs, otherwise spaces).
        if (!shift && !multiLine && sel.IsEmpty && !prefix.HasList)
        {
            _buffer.Insert(IndentUnit(caretLine, caretLine));
            return;
        }

        // Shift+Tab on a single nestable item (list/quote/task) that is already at the outermost level
        // (no indentation left to remove) exits the construct: strip the whole marker prefix, leaving the
        // content as a plain line — so an empty item outdents to an empty line (PRD §7.2 list exit).
        if (shift && !multiLine && (prefix.HasList || prefix.HasQuote) && prefix.Indent == 0)
        {
            var lineStart = doc.GetLineStart(caretLine);
            var caret = sel.Caret;
            _buffer.ReplaceRange(lineStart, prefix.Length, string.Empty);
            _buffer.Selection = Selection.At(Math.Max(lineStart, caret - prefix.Length));
            return;
        }

        IndentLines(startLine, endLine, outdent: shift);
    }

    /// <summary>
    /// The indentation unit to add — a tab when the region (or the line above it) already uses tab
    /// indentation, otherwise <see cref="IndentWidth"/> spaces — so indenting preserves whichever style was
    /// entered in the text.
    /// </summary>
    private string IndentUnit(int startLine, int endLine)
    {
        var doc = _buffer.Document;
        bool StartsWithTab(int l)
        {
            var t = doc.GetLineText(l);
            return t.Length > 0 && t[0] == '\t';
        }

        if (startLine > 0 && StartsWithTab(startLine - 1))
        {
            return "\t";
        }

        for (var l = startLine; l <= endLine; l++)
        {
            if (StartsWithTab(l))
            {
                return "\t";
            }
        }

        return new string(' ', Math.Max(1, IndentWidth));
    }

    /// <summary>Add or remove one indent level on lines <paramref name="startLine"/>..<paramref name="endLine"/> as one edit.</summary>
    private void IndentLines(int startLine, int endLine, bool outdent)
    {
        var doc = _buffer.Document;
        var sel = _buffer.Selection;
        var unit = IndentUnit(startLine, endLine);
        var maxSpaces = Math.Max(1, IndentWidth);
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
                // Remove one indent level: a single leading tab, or up to IndentWidth leading spaces.
                var remove = 0;
                if (text.Length > 0 && text[0] == '\t')
                {
                    remove = 1;
                }
                else
                {
                    while (remove < maxSpaces && remove < text.Length && text[remove] == ' ')
                    {
                        remove++;
                    }
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
                newLines.Add(unit + text);
                delta = unit.Length;
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

    /// <summary>
    /// Move the selected line(s) up (<paramref name="dir"/> = -1) or down (+1), swapping with the adjacent
    /// <b>sibling subtree</b>, as a single undoable edit (Alt+Shift+Up/Down). The moved unit includes any
    /// deeper-indented descendants of the last selected line — so a list item or task carries its sub-items
    /// with it — and it swaps with the neighbouring item at the same indent (skipping that item's own
    /// descendants). The selection travels with the moved block; a no-op at a boundary is silently ignored.
    /// </summary>
    private void MoveLines(int dir)
    {
        var doc = _buffer.Document;
        var sel = _buffer.Selection;
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
        int regionStartLine, regionEndLine, delta;
        var newOrder = new List<string>();
        if (dir < 0)
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
            delta = -BlockChars(doc, p, startLine - 1);
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
            delta = BlockChars(doc, s, swapEnd);
        }

        // The moved subtree's character range, so we can locate it again after it shifts by `delta`.
        var movedStartOffset = doc.GetLineStart(startLine);
        var movedEndOffset = doc.GetLineEnd(endLine);

        var regionStart = doc.GetLineStart(regionStartLine);
        var regionEnd = doc.GetLineEnd(regionEndLine);
        _buffer.ReplaceRange(regionStart, regionEnd - regionStart, string.Join("\n", newOrder));

        // The moved subtree shifted by the swapped sibling's size; carry the selection with it.
        _buffer.Selection = new Selection(sel.Anchor + delta, sel.Caret + delta);
        _goalX = double.NaN;

        // Bracket the moved block with the drop indicator (it shifted by the same `delta` as the selection).
        var moved = _buffer.Document;
        _reorderStartLine = moved.GetPosition(movedStartOffset + delta).Line;
        _reorderEndLine = moved.GetPosition(movedEndOffset + delta).Line;
        _reorderActive = true;
        InvalidateVisual();
    }

    /// <summary>The reorder drop-indicator state (active + the bracketed line range); for tests.</summary>
    internal (bool Active, int Start, int End) ReorderIndicatorForTest() => (_reorderActive, _reorderStartLine, _reorderEndLine);

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

    /// <summary>The kind of list item a line carries (or <see cref="ItemKind.None"/> for a non-list line).</summary>
    private enum ItemKind
    {
        None,
        Bullet,
        Numbered,
        Checkbox,
    }

    /// <summary>
    /// Classify <paramref name="line"/>'s list marker into one of the three list kinds — bulleted, numbered,
    /// or checkbox (a task item is its own kind, distinct from a plain bullet) — or <see cref="ItemKind.None"/>
    /// when the line isn't a list item at all (plain text, a heading, a blockquote, …).
    /// </summary>
    private ItemKind ListItemKindOf(int line)
    {
        var p = LinePrefix.Parse(_buffer.Document.GetLineText(line));
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
    /// The <b>visual indentation</b> of <paramref name="line"/> in columns — its nesting depth. Tabs are
    /// measured on <see cref="IndentWidth"/>-column tab stops (matching the tokenizer's
    /// <c>LeadingWhitespace</c> and how the surface draws a leading tab), so that a child indented with one
    /// tab and a sibling indented with four spaces land at the same depth. Comparing raw character counts
    /// instead would treat mixed tab/space indentation as different levels and split a parent from its child.
    /// </summary>
    private int LineIndent(int line)
    {
        var t = _buffer.Document.GetLineText(line);
        var stop = Math.Max(1, IndentWidth);
        var columns = 0;
        for (var i = 0; i < t.Length && (t[i] == ' ' || t[i] == '\t'); i++)
        {
            columns += t[i] == '\t' ? stop - (columns % stop) : 1;
        }

        return columns;
    }

    /// <summary>Whether <paramref name="line"/> is blank (only whitespace).</summary>
    private bool IsBlankLine(int line) => _buffer.Document.GetLineText(line).Trim().Length == 0;

    /// <summary>The text of lines <c>[from, to]</c> (inclusive).</summary>
    private static List<string> LinesOf(TextDocument doc, int from, int to)
    {
        var list = new List<string>(Math.Max(0, to - from + 1));
        for (var l = from; l <= to; l++)
        {
            list.Add(doc.GetLineText(l));
        }

        return list;
    }

    /// <summary>Total characters of lines <c>[from, to]</c> including each line's trailing newline.</summary>
    private static int BlockChars(TextDocument doc, int from, int to)
    {
        var sum = 0;
        for (var l = from; l <= to; l++)
        {
            sum += (doc.GetLineEnd(l) - doc.GetLineStart(l)) + 1;
        }

        return sum;
    }

    /// <summary>Move the selected line(s) / subtree up/down; for tests.</summary>
    internal void MoveLinesForTest(int dir) => MoveLines(dir);

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
        if (!TryBackspacePair() && !TryVisualBackspace())
        {
            _buffer.Backspace();
        }
    }

    internal void DeleteForTest()
    {
        if (!TryVisualDelete())
        {
            _buffer.Delete();
        }
    }

    private KeyModifiers _modifiers;
    private bool _dragging;
    private double _goalX = double.NaN; // remembered caret x for sticky up/down movement (NaN = recompute)

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ClearReorderIndicator(); // a click ends the reorder session

        // A click on a code block's copy button copies that block and shows the confirmation — handled
        // before caret placement so it doesn't also move the caret.
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var p = e.GetPosition(this);
            foreach (var btn in _codeCopyButtons)
            {
                if (btn.Rect.Contains(p))
                {
                    CopyCodeBlock(btn.First, btn.Last);
                    e.Handled = true;
                    return;
                }
            }
        }

        // Let a right-click through so the context menu can open; only the left button moves the caret.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            base.OnPointerPressed(e);
            return;
        }

        Focus();
        var point = e.GetPosition(this);
        // Resolve the click to a document offset (hit-testing handles padding, scroll, and wrapped rows),
        // then take its document line for word-select / checkbox hit-testing.
        var offset = OffsetAtPoint(point);
        var line = _buffer.Document.GetPosition(offset).Line;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        // A double-click selects the word (or run) under the cursor; a triple-click selects the whole
        // paragraph block (the standard editor escalation).
        if (e.ClickCount == 2)
        {
            SelectWordAt(line, offset);
            e.Handled = true;
            base.OnPointerPressed(e);
            return;
        }

        if (e.ClickCount >= 3)
        {
            SelectBlockAt(line);
            e.Handled = true;
            base.OnPointerPressed(e);
            return;
        }

        // A click on a task-list checkbox toggles it (PRD §7.2) rather than moving the caret — whether
        // it's the raw "[ ]" (caret line) or the drawn checkbox glyph (concealed line).
        if (!shift && TryToggleTaskClick(line, point, offset))
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

    /// <summary>Select the whole paragraph block containing <paramref name="line"/> (triple-click).</summary>
    private void SelectBlockAt(int line)
    {
        var block = BlockOf(line);
        _buffer.Selection = new Selection(BlockStartOffset(block), BlockEndOffset(block));
        _goalX = double.NaN;
    }

    /// <summary>Select the paragraph block at <paramref name="line"/> (triple-click); for tests.</summary>
    internal void SelectBlockForTest(int line) => SelectBlockAt(line);

    /// <summary>Move the caret up/down by <paramref name="delta"/> lines (sticky goal column); for tests.</summary>
    internal void MoveCaretByLineForTest(int delta) => MoveCaretByLine(delta, extend: false);

    /// <summary>Set the caret to <paramref name="offset"/> as a plain click would; for tests.</summary>
    internal void ClickAtForTest(int offset) => MoveCaret(offset, extend: false);

    /// <summary>
    /// Toggle a task checkbox hit by a click on <paramref name="line"/>: first the drawn checkbox glyph
    /// (when the marker is concealed), then the raw <c>[ ]</c> at <paramref name="offset"/> (caret line).
    /// </summary>
    private bool TryToggleTaskClick(int line, Point point, int offset)
    {
        // A task line carries a list marker, so it's always a singleton block (block start == line start).
        var block = BlockOf(line);
        var visual = BlockVisual(block);
        var contentX = point.X - _theme.PaddingLeft + _offset.X;
        var blockStart = BlockStartOffset(block);
        foreach (var run in visual.Runs)
        {
            if (run.Decoration is DecorationKind.CheckboxChecked or DecorationKind.CheckboxUnchecked
                && contentX >= run.X && contentX <= run.X + run.Width)
            {
                return TryToggleTaskAt(line, blockStart + run.Col); // run.Col is the task marker's column
            }
        }

        return TryToggleTaskAt(line, offset);
    }

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

            return ToggleBoxAt(t.Start + 1); // the character between the brackets
        }

        return false;
    }

    /// <summary>Flip the first task checkbox on <paramref name="line"/> (used by ⌘L), if it has one.</summary>
    private bool ToggleTaskOnLine(int line)
    {
        foreach (var t in _model.LineTokens(line))
        {
            if (t.Type == TokenType.TaskMarker)
            {
                return ToggleBoxAt(t.Start + 1);
            }
        }

        return false;
    }

    /// <summary>Flip the <c>[ ]</c>/<c>[x]</c> box character at <paramref name="box"/>, leaving the caret put.</summary>
    private bool ToggleBoxAt(int box)
    {
        if (box < 0 || box >= _buffer.Document.Length)
        {
            return false;
        }

        var replacement = _buffer.Document[box] == ' ' ? "x" : " ";
        var caret = _buffer.Selection;
        _buffer.ReplaceRange(box, 1, replacement);
        _buffer.Selection = caret; // toggling a checkbox shouldn't move the caret
        return true;
    }

    /// <summary>Toggle the task checkbox covering <paramref name="offset"/> on <paramref name="line"/>; for tests.</summary>
    internal bool ToggleTaskForTest(int line, int offset) => TryToggleTaskAt(line, offset);

    /// <summary>Simulate a click at content-space x <paramref name="contentX"/> on a checkbox glyph; for tests.</summary>
    internal bool ToggleTaskClickForTest(int line, double contentX) =>
        TryToggleTaskClick(line, new Point(contentX + _theme.PaddingLeft - _offset.X, 0), _buffer.Document.GetLineStart(line));

    /// <summary>Scroll so the caret stays visible after an edit or navigation (we are the scrollable).</summary>
    private void ScrollCaretIntoView()
    {
        if (_viewport.Width <= 0 || _viewport.Height <= 0)
        {
            return; // not arranged in a viewport yet (e.g. headless construction)
        }

        EnsureLayout();
        var r = ResolveCaret(_buffer.Selection.Caret);
        var x = r.Visual.ColumnToX(r.Col); // through the offset↔visual map, so hidden markers don't skew the scroll
        if (!_wordWrap)
        {
            _extentWidth = Math.Max(_extentWidth, x + 12); // ensure we can scroll to a caret on a wide line
        }

        // Caret position in scroll-content space (which includes the padding), on its wrapped row.
        var px = x + _theme.PaddingLeft;
        var py = RowTop(r.Block) + r.Visual.RowOf(r.Col) * _theme.LineHeight + _theme.PaddingTop;
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

    /// <summary>The laid-out visual line for <paramref name="line"/> (concealment is caret-independent in WYSIWYG mode).</summary>
    private VisualLine LineVisual(int line) => BlockVisual(BlockOf(line));

    /// <summary>
    /// One caret step in <paramref name="dir"/> (+1 right, -1 left) that treats a concealed marker run as
    /// atomic: a raw grapheme step landing strictly inside hidden punctuation keeps going until it reaches
    /// a real stop, so the caret glides over hidden markers like a normal document editor (PRD §7.4).
    /// </summary>
    private int VisualStep(int caret, int dir)
    {
        var doc = _buffer.Document;
        var next = Math.Clamp(dir > 0 ? _buffer.NextCaret(caret) : _buffer.PreviousCaret(caret), 0, doc.Length);
        var r = ResolveCaret(next);
        var guard = 0;
        while (r.Visual.IsInsideConcealed(r.Col) && guard++ <= doc.Length)
        {
            var stepped = Math.Clamp(dir > 0 ? _buffer.NextCaret(next) : _buffer.PreviousCaret(next), 0, doc.Length);
            if (stepped == next)
            {
                break;
            }

            next = stepped;
            r = ResolveCaret(next);
        }

        return next;
    }

    /// <summary>Move the caret atomically in <paramref name="dir"/>; for tests.</summary>
    internal void VisualStepForTest(int dir, bool extend = false) => MoveCaret(VisualStep(_buffer.Selection.Caret, dir), extend);

    /// <summary>
    /// The document offset one word away from <paramref name="offset"/> in <paramref name="dir"/> (+1
    /// right, -1 left): skip a run of whitespace, then a run of like-classed characters (word vs
    /// punctuation), the way Ctrl/⌥ + arrow works in every editor. Word boundaries fall on punctuation-run
    /// edges, which coincide with hidden marker edges, so the result is always a valid caret stop.
    /// </summary>
    private int WordBoundary(int offset, int dir)
    {
        var doc = _buffer.Document;
        var i = Math.Clamp(offset, 0, doc.Length);
        if (dir > 0)
        {
            while (i < doc.Length && char.IsWhiteSpace(doc[i]))
            {
                i++;
            }

            if (i < doc.Length)
            {
                var cls = CharClass(doc[i]);
                while (i < doc.Length && !char.IsWhiteSpace(doc[i]) && CharClass(doc[i]) == cls)
                {
                    i++;
                }
            }
        }
        else
        {
            while (i > 0 && char.IsWhiteSpace(doc[i - 1]))
            {
                i--;
            }

            if (i > 0)
            {
                var cls = CharClass(doc[i - 1]);
                while (i > 0 && !char.IsWhiteSpace(doc[i - 1]) && CharClass(doc[i - 1]) == cls)
                {
                    i--;
                }
            }
        }

        return i;
    }

    /// <summary>Delete from the start of the caret's visual row to the caret (macOS ⌘⌫).</summary>
    private void DeleteToRowStart(int caret)
    {
        if (!_buffer.Selection.IsEmpty)
        {
            _buffer.Insert(string.Empty);
            return;
        }

        var start = RowStartOffset(caret);
        if (start < caret)
        {
            _buffer.ReplaceRange(start, caret - start, string.Empty);
        }
    }

    /// <summary>Delete from the caret to the next word boundary in <paramref name="dir"/> (Ctrl/⌥ + Backspace/Delete).</summary>
    private void DeleteWord(int dir)
    {
        var sel = _buffer.Selection;
        if (!sel.IsEmpty)
        {
            _buffer.Insert(string.Empty); // a selection deletes normally
            return;
        }

        var target = WordBoundary(sel.Caret, dir);
        if (target == sel.Caret)
        {
            return;
        }

        var from = Math.Min(sel.Caret, target);
        _buffer.ReplaceRange(from, Math.Abs(target - sel.Caret), string.Empty);
    }

    /// <summary>The source-column range <c>[start, end)</c> occupied by visual <paramref name="row"/> of <paramref name="visual"/>.</summary>
    private static (int Start, int End) RowColumns(VisualLine visual, int row)
    {
        var start = int.MaxValue;
        var end = 0;
        foreach (var run in visual.Runs)
        {
            if (run.Len > 0 && run.Row == row)
            {
                start = Math.Min(start, run.Col);
                end = Math.Max(end, run.Col + run.Len);
            }
        }

        return start == int.MaxValue ? (0, 0) : (start, end);
    }

    /// <summary>The offset at the start of the caret's wrapped visual row.</summary>
    private int RowStartOffset(int caret)
    {
        var r = ResolveCaret(caret);
        return r.BlockStart + RowColumns(r.Visual, r.Visual.RowOf(r.Col)).Start;
    }

    /// <summary>The offset at the end of the caret's wrapped visual row.</summary>
    private int RowEndOffset(int caret)
    {
        var r = ResolveCaret(caret);
        return r.BlockStart + RowColumns(r.Visual, r.Visual.RowOf(r.Col)).End;
    }

    /// <summary>
    /// Smart Home: jump to the first visible, non-whitespace content on the caret's visual row; when the
    /// caret is already there, jump to the true start of the row instead (the toggle every editor has).
    /// </summary>
    private int SmartHome(int caret)
    {
        var doc = _buffer.Document;
        var r = ResolveCaret(caret);
        var (start, end) = RowColumns(r.Visual, r.Visual.RowOf(r.Col));
        var content = start;
        while (content < end &&
               (r.Visual.IsColumnHidden(content) || char.IsWhiteSpace(doc[r.BlockStart + content])))
        {
            content++;
        }

        var contentOffset = r.BlockStart + content;
        return caret == contentOffset ? r.BlockStart + start : contentOffset;
    }

    /// <summary>The number of visual rows a Page Up/Down moves (a viewport-height of rows, less one for overlap).</summary>
    private int PageRows()
    {
        var h = _viewport.Height > 0 ? _viewport.Height : Bounds.Height;
        return Math.Max(1, (int)(h / _theme.LineHeight) - 1);
    }

    /// <summary>Move the caret to <paramref name="dir"/> word boundary; for tests.</summary>
    internal void WordMoveForTest(int dir, bool extend = false) => MoveCaret(WordBoundary(_buffer.Selection.Caret, dir), extend);

    /// <summary>Delete a word in <paramref name="dir"/>; for tests.</summary>
    internal void DeleteWordForTest(int dir) => DeleteWord(dir);

    /// <summary>Smart-Home the caret; for tests.</summary>
    internal void HomeForTest(bool extend = false) => MoveCaret(SmartHome(_buffer.Selection.Caret), extend);

    /// <summary>Move the caret to the end of its visual row; for tests.</summary>
    internal void EndForTest(bool extend = false) => MoveCaret(RowEndOffset(_buffer.Selection.Caret), extend);

    /// <summary>
    /// Backspace in WYSIWYG mode: delete the previous <b>visible</b> grapheme, stepping over any concealed
    /// marker punctuation between it and the caret so hidden markers stay balanced (backspacing at the end
    /// of a bold word removes a letter, not an invisible <c>*</c>). When only hidden markers precede the
    /// caret on the line, the whole marker prefix is removed instead (un-list / un-quote / un-heading the
    /// line). Returns false at a true line start so the buffer's plain backspace can join lines.
    /// </summary>
    private bool TryVisualBackspace()
    {
        var sel = _buffer.Selection;
        if (!sel.IsEmpty)
        {
            return false; // a selection deletes normally
        }

        var doc = _buffer.Document;
        var r = ResolveCaret(sel.Caret);
        if (r.Col == 0)
        {
            return false; // block start: let plain backspace join with the previous block
        }

        var col = r.Col;
        while (col > 0 && r.Visual.IsColumnHidden(col - 1))
        {
            col--;
        }

        if (col == 0)
        {
            // Only hidden marker punctuation precedes the caret — strip the whole marker prefix.
            _buffer.ReplaceRange(r.BlockStart, r.Col, string.Empty);
            _buffer.Selection = Selection.At(r.BlockStart);
            return true;
        }

        // Grapheme boundaries are taken over the real document text (interior newlines included), so a
        // backspace at a soft-joined line boundary deletes the joining newline and merges the lines.
        var realText = doc.GetText(r.BlockStart, BlockEndOffset(r.Block) - r.BlockStart);
        var start = Grapheme.PreviousBoundary(realText, col);
        _buffer.ReplaceRange(r.BlockStart + start, col - start, string.Empty);
        _buffer.Selection = Selection.At(r.BlockStart + start);
        return true;
    }

    /// <summary>
    /// Forward Delete in WYSIWYG mode: delete the next <b>visible</b> grapheme, stepping over concealed
    /// marker punctuation ahead of the caret. Returns false when only hidden markers (or end-of-line)
    /// follow, letting the buffer's plain delete handle the line join.
    /// </summary>
    private bool TryVisualDelete()
    {
        var sel = _buffer.Selection;
        if (!sel.IsEmpty)
        {
            return false;
        }

        var doc = _buffer.Document;
        var r = ResolveCaret(sel.Caret);
        var col = r.Col;
        while (col < r.Visual.RawLength && r.Visual.IsColumnHidden(col))
        {
            col++;
        }

        if (col >= r.Visual.RawLength)
        {
            return false; // nothing visible ahead in this block: let plain delete join the next block
        }

        var realText = doc.GetText(r.BlockStart, BlockEndOffset(r.Block) - r.BlockStart);
        var end = Grapheme.NextBoundary(realText, col);
        _buffer.ReplaceRange(r.BlockStart + col, end - col, string.Empty);
        _buffer.Selection = Selection.At(r.BlockStart + col);
        return true;
    }

    /// <summary>
    /// Move the caret up/down by <paramref name="delta"/> lines, holding a <b>sticky goal column</b>:
    /// the caret's x is remembered on the first vertical move and reused, so passing through shorter
    /// lines doesn't permanently snap the column inward (the behavior every editor has).
    /// </summary>
    /// <summary>
    /// A bare Up/Down arrow. With a range selection and no Shift, it collapses to the selection's edge —
    /// Up to the top (start), Down to the bottom (end) — rather than stepping a visual row from the active
    /// caret; so e.g. Up right after Select All returns to the top of the selection. With Shift (extend) or
    /// no selection it moves by one visual row as usual.
    /// </summary>
    private void ArrowByLine(int delta, bool extend)
    {
        var sel = _buffer.Selection;
        if (!extend && !sel.IsEmpty)
        {
            MoveCaret(delta < 0 ? sel.Start : sel.End, extend: false);
            return;
        }

        MoveCaretByLine(delta, extend);
    }

    /// <summary>Up/Down arrow with selection-edge collapse; for tests.</summary>
    internal void ArrowByLineForTest(int delta, bool extend = false) => ArrowByLine(delta, extend);

    private void MoveCaretByLine(int delta, bool extend)
    {
        EnsureLayout();
        var r = ResolveCaret(_buffer.Selection.Caret);
        if (double.IsNaN(_goalX))
        {
            _goalX = r.Visual.ColumnToX(r.Col);
        }

        // Move by one wrapped visual row, not a whole document line, so Up/Down step through a long
        // wrapped paragraph (and across joined source lines) the way a normal document editor does.
        var globalRow = _rowPrefix[r.Block] + r.Visual.RowOf(r.Col);
        var targetRow = Math.Clamp(globalRow + delta, 0, Math.Max(0, TotalRows - 1));
        var targetBlock = BlockAtRow(targetRow);
        var localRow = targetRow - _rowPrefix[targetBlock];
        MoveCaret(OffsetAt(targetBlock, localRow, _goalX), extend, keepGoalColumn: true);
    }

    /// <summary>Map a pointer position to the nearest caret offset (accounting for padding, scroll, and wrapped rows).</summary>
    private int OffsetAtPoint(Point point)
    {
        EnsureLayout();
        var contentY = point.Y - _theme.PaddingTop + _offset.Y;
        var contentX = point.X - _theme.PaddingLeft + _offset.X;
        var globalRow = Math.Max(0, (int)(contentY / _theme.LineHeight));
        var block = BlockAtRow(globalRow);
        var localRow = Math.Clamp(globalRow - _rowPrefix[block], 0, Math.Max(0, _rowCounts[block] - 1));
        return OffsetAt(block, localRow, contentX);
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
    /// Build the laid-out <see cref="VisualLine"/> for <paramref name="line"/> under the current
    /// reveal state: tile the line into styled segments (gaps between tokens filled with the default
    /// style), decide which marker segments conceal (PRD §7.4), and hand the result to
    /// <see cref="VisualLine.Build"/> with the cached width measurer. This is the single source of
    /// truth for drawing, caret placement, hit-testing, and selection — they all read the same
    /// offset↔visual map, so hidden markers stay consistent everywhere.
    /// </summary>
    /// <summary>
    /// Lay out one paragraph <paramref name="block"/> as a single (possibly wrapped) <see cref="VisualLine"/>.
    /// A flow block joins its member source lines into one paragraph — the block's text is the contiguous
    /// document range with interior line breaks shown as spaces, so a block column is simply
    /// <c>offset − blockStart</c> and the whole offset↔visual map carries over unchanged. Markers are always
    /// concealed (full WYSIWYG), so the layout is caret-independent.
    /// </summary>
    private VisualLine BlockVisual(int block)
    {
        var doc = _buffer.Document;
        var startLine = _blockStartLine[block];
        var endLine = _blockStartLine[block + 1] - 1;
        var blockStart = doc.GetLineStart(startLine);
        var blockEnd = doc.GetLineEnd(endLine);

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
                    // The gap (including a joined newline) is plain text.
                    segments.Add(new VisualLine.Segment(pos, s, DefaultStyle(headingLevel), false));
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
                    if (t.Type == TokenType.ListMarker && !hasTask && IsOrderedMarker(lineText, sLine, enLine))
                    {
                        decoText = OrderedOrdinal(line) + OrderedDelimiter(lineText, sLine, enLine) + " ";
                        deco = DecorationKind.OrderedNumber;
                        width = MeasureWidth(decoText, style);
                    }

                    segments.Add(new VisualLine.Segment(s, en, style, true, deco, deco == DecorationKind.None ? 0 : width, decoText));
                }
                else
                {
                    segments.Add(new VisualLine.Segment(s, en, style, false));
                }

                pos = en;
            }
        }

        if (pos < contentLen)
        {
            segments.Add(new VisualLine.Segment(pos, contentLen, DefaultStyle(lastHeading), false));
        }

        return VisualLine.Build(text, segments, MeasureWidth, WrapWidth());
    }

    /// <summary>The synthetic decoration (and its gutter width) that replaces concealed marker <paramref name="t"/>, if any.</summary>
    private (DecorationKind Kind, double Width) DecorationFor(Token t, string lineText, int s, int en, bool hasTask, RunStyle style)
    {
        var text = lineText.Substring(s, en - s);
        switch (t.Type)
        {
            case TokenType.ListMarker:
                if (hasTask)
                {
                    return (DecorationKind.None, 0); // the checkbox represents the whole item
                }

                var bullet = text.TrimStart(' ');
                return bullet.Length > 0 && bullet[0] is '-' or '*' or '+'
                    ? (DecorationKind.Bullet, MeasureWidth(text, style))
                    : (DecorationKind.None, 0);

            case TokenType.TaskMarker:
                // A fixed gutter (box + gap) regardless of "[ ]" vs "[x]", so the text after the checkbox sits
                // at the same place whether it's checked or not, with a clear space before it.
                var ticked = text.Length > 1 && text[1] is 'x' or 'X';
                return (ticked ? DecorationKind.CheckboxChecked : DecorationKind.CheckboxUnchecked, CheckboxGutter);

            case TokenType.BlockquoteMarker:
                return (DecorationKind.QuoteBar, MeasureWidth(text, style));

            case TokenType.HorizontalRule:
                return (DecorationKind.HorizontalRule, 0); // an overlay rule; no advance (the line is otherwise empty)

            default:
                return (DecorationKind.None, 0);
        }
    }

    /// <summary>
    /// The ordinal to display for the ordered-list item on <paramref name="line"/>, computed from the
    /// list's structure rather than the literal source digits: walking up, deeper (nested) lines are
    /// skipped, a shallower line ends the level (a sub-list restarts at 1), and a blank or non-ordered
    /// sibling starts a fresh list. So <c>1. 1. 1.</c> renders 1·2·3 and nested levels number themselves.
    /// </summary>
    private int OrderedOrdinal(int line)
    {
        var doc = _buffer.Document;
        var self = LinePrefix.Parse(doc.GetLineText(line));
        if (self.List != ListKind.Ordered)
        {
            return 1;
        }

        var count = 1;
        for (var k = line - 1; k >= 0; k--)
        {
            var text = doc.GetLineText(k).TrimEnd('\r');
            if (text.Trim().Length == 0)
            {
                break; // a blank line starts a fresh list
            }

            var p = LinePrefix.Parse(text);
            if (p.Indent > self.Indent)
            {
                continue; // a deeper nested line belongs to a sub-list (or item continuation) — skip it
            }

            if (p.Indent < self.Indent)
            {
                break; // reached the enclosing (shallower) level — this sub-list starts here
            }

            if (p.Quote != self.Quote || p.List != ListKind.Ordered)
            {
                break; // a same-level sibling that isn't an ordered item ends the run
            }

            count++;
        }

        return count;
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

    /// <summary>Whether <paramref name="line"/> is part of a code block — fenced or indented (for the background panel).</summary>
    private bool IsCodeLine(int line)
    {
        var tokens = _model.LineTokens(line);
        if (tokens.Count > 0)
        {
            // A code row begins with the fence marker or carries code text; the dedent line that ends an
            // indented block tokenizes normally and is correctly excluded here.
            return tokens[0].Type is TokenType.CodeFence or TokenType.CodeText;
        }

        // A blank row inside a fenced block is code until the fence closes. Inside an *indented* block a
        // blank stays code only when it's interior — a later line resumes the indented code. A blank that
        // merely trails the block (nothing more indented follows) is not part of it, so it isn't panelled
        // — this is the empty line your caret sits on while typing below an indented block.
        var entering = _model.EnteringState(line).Kind;
        if (entering == BlockKind.FencedCode)
        {
            return true;
        }

        if (entering == BlockKind.IndentedCode)
        {
            for (var l = line + 1; l < _buffer.Document.LineCount; l++)
            {
                var toks = _model.LineTokens(l);
                if (toks.Count == 0)
                {
                    continue; // another interior blank — keep scanning forward
                }

                return toks[0].Type == TokenType.CodeText; // indented code resumes → this blank is interior
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="line"/> is panelled as code (fenced/indented); for tests.</summary>
    internal bool IsCodeLineForTest(int line) => IsCodeLine(line);

    // Task-checkbox geometry, shared between the gutter width and the drawn glyph so they stay in sync.
    private const double CheckboxLeftPad = 1;   // small inset before the box
    private double CheckboxBoxSize => _theme.BaseFontSize * 0.95;
    private double CheckboxGap => _theme.BaseFontSize * 0.45; // space between the box and the following text

    /// <summary>
    /// The checkbox gutter width — the box plus a fixed gap before the text. Deliberately independent of the
    /// source "[ ]"/"[x]" punctuation, so a checked and an unchecked item indent their text by the same amount.
    /// </summary>
    private double CheckboxGutter => CheckboxLeftPad + CheckboxBoxSize + CheckboxGap;

    /// <summary>The right edge of the text column in content space (for full-width adornments).</summary>
    private double ContentRight()
    {
        var w = _viewport.Width > 0 ? _viewport.Width : Bounds.Width;
        return Math.Max(120, w - _theme.PaddingLeft * 2);
    }

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

    /// <summary>The total visual-row count across the document at the current width; for tests.</summary>
    internal int TotalRowsForTest()
    {
        EnsureLayout();
        return TotalRows;
    }

    /// <summary>
    /// Whether <paramref name="t"/>'s marker punctuation is hidden (PRD §7.4, revised to full WYSIWYG).
    /// Only <see cref="TokenRole.Marker"/> tokens ever conceal — content is always shown — and in normal
    /// mode every marker conceals regardless of the caret, except whole-line block fences whose row would
    /// blank out if hidden (collapsing their row is deferred, PRD §11). Raw-source mode reveals all.
    /// </summary>
    private bool MarkerConcealed(Token t)
    {
        if (_showRawSource || t.Role != TokenRole.Marker)
        {
            return false;
        }

        // Whole-line fences (fenced code / front matter / table delimiter) stay visible; everything else
        // — heading/quote/list/task/HR prefixes and all inline emphasis/code/link/escape markers — hides.
        return t.Type is not (TokenType.CodeFence or TokenType.FrontMatterFence or TokenType.TableDelimiter);
    }

    /// <summary>Draw a synthetic decoration that stands in for a concealed marker (PRD §7.4).</summary>
    /// <summary>Whether <paramref name="line"/> is a horizontal rule (its sole token is the rule marker).</summary>
    private bool IsHorizontalRuleLine(int line)
    {
        var toks = _model.LineTokens(line);
        return toks.Count > 0 && toks[0].Type == TokenType.HorizontalRule;
    }

    /// <summary>
    /// Whether the rule line at <paramref name="line"/> should be drawn with a full-row highlight: the caret
    /// is on it, there is no range selection, and we're in WYSIWYG (non-source) view. (In raw-source view the
    /// rule is plain <c>---</c> text and the usual caret/selection apply.)
    /// </summary>
    internal bool ShouldHighlightRuleLine(int line)
        => !_showRawSource
           && _buffer.Selection.Length == 0
           && IsHorizontalRuleLine(line)
           && _buffer.Document.GetPosition(_buffer.Selection.Caret).Line == line;

    private void DrawDecoration(DrawingContext context, VisualRun run, double lineTop)
    {
        var lineH = _theme.LineHeight;
        var cy = lineTop + lineH / 2;
        switch (run.Decoration)
        {
            case DecorationKind.Bullet:
            {
                var r = Math.Max(2, _theme.BaseFontSize * 0.14);
                context.DrawEllipse(_theme.BulletForeground, null, new Point(run.X + run.Width / 2, cy), r, r);
                break;
            }

            case DecorationKind.CheckboxUnchecked:
            case DecorationKind.CheckboxChecked:
            {
                var size = CheckboxBoxSize;
                var box = new Rect(run.X + CheckboxLeftPad, cy - size / 2, size, size);
                var ticked = run.Decoration == DecorationKind.CheckboxChecked;
                context.DrawRectangle(ticked ? _theme.CheckboxForeground : null, new Pen(_theme.CheckboxForeground, 1.4), box, 3, 3);
                if (ticked)
                {
                    // A check mark in the editor background colour so it reads on the filled box.
                    var pen = new Pen(_theme.Background, 1.6);
                    var a = new Point(box.X + size * 0.24, cy);
                    var b = new Point(box.X + size * 0.43, cy + size * 0.22);
                    var c = new Point(box.X + size * 0.78, cy - size * 0.24);
                    context.DrawLine(pen, a, b);
                    context.DrawLine(pen, b, c);
                }

                break;
            }

            case DecorationKind.OrderedNumber:
            {
                // The computed ordinal (e.g. "3. "), drawn in the bullet colour where the digits would be.
                var ft = GetFormattedText(run.Text, run.Style, _theme.BulletForeground);
                var ny = lineTop + (_theme.LineHeight - ft.Height) / 2;
                context.DrawText(ft, new Point(run.X, ny));
                break;
            }

            case DecorationKind.QuoteBar:
                context.FillRectangle(_theme.QuoteBar, new Rect(run.X + 2, lineTop + 2, 2.5, lineH - 4));
                break;

            case DecorationKind.HorizontalRule:
                context.DrawLine(new Pen(_theme.RuleForeground, _theme.RuleThickness), new Point(run.X, cy), new Point(ContentRight(), cy));
                break;
        }
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
        var ft = new FormattedText(ExpandTabs(text), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
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
        return new FormattedText(ExpandTabs(text), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            style.Typeface, style.FontSize, style.Foreground).Width;
    }

    /// <summary>
    /// A copy of <paramref name="text"/> with each tab expanded to <see cref="IndentWidth"/> spaces, for
    /// measuring and drawing only — the run still covers the source tab as one column, so a tab indents by a
    /// consistent width while the 1:1 offset↔visual map (which indexes the un-expanded run text) is preserved.
    /// </summary>
    private string ExpandTabs(string text) =>
        text.IndexOf('\t') < 0 ? text : text.Replace("\t", new string(' ', Math.Max(1, IndentWidth)));

    private readonly Dictionary<WidthKey, double> _widthCache = new();
    private int _rawMeasures;

    /// <summary>Number of real text-shaping measurements performed (cache misses); for perf tests.</summary>
    internal int RawMeasureCount => _rawMeasures;

    /// <summary>Measure a run with the default text style, exercising the width cache; for perf tests.</summary>
    internal double MeasureWidthForTest(string text) => MeasureWidth(text, _theme.Resolve(TokenType.Text));

    private readonly record struct WidthKey(string Text, FontFamily Family, FontStyle Style, FontWeight Weight, double Size);

    private int OffsetAt(int block, int row, double x)
    {
        // Hit-test against the block's visual map on the clicked wrapped row (concealed markers are
        // zero-width and never a caret stop), so a click resolves to the right document offset even where
        // markers are hidden, the paragraph is joined from several source lines, or the text wraps.
        var visual = BlockVisual(block);
        return BlockStartOffset(block) + visual.XToColumn(row, x);
    }
}
