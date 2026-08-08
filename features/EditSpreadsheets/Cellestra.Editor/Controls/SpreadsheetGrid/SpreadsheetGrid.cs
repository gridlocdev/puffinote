using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Reactive;
using Cellestra.Core.Model;
using Cellestra.Core.Styling;
using Cellestra.Desktop.Services;

namespace Cellestra.Desktop.Controls.SpreadsheetGrid;

/// <summary>
/// The custom-drawn, virtualized spreadsheet grid (ARCHITECTURE §6). Reads cell data directly
/// from the active <see cref="Worksheet"/> — no per-cell view models — and draws only the
/// visible cells. Handles selection, keyboard navigation, scrolling, and inline editing.
/// </summary>
/// <remarks>
/// This root file owns the shared fields, the constructor, theme/attach plumbing, visible-band
/// layout (<see cref="BuildVisible"/>), and <see cref="ArrangeOverride"/>. The concern partials
/// carry the rest: <c>.Rendering</c> (the draw path), <c>.Pointer</c> (hit-test + pointer input),
/// <c>.Keyboard</c> (navigation + text input), <c>.Editor</c> (inline editing), and <c>.Layers</c>
/// (the nested <see cref="Surface"/> / <see cref="EditorDecorationLayer"/> draw children) — plus the
/// pre-existing feature partials (<c>.Chrome</c>, <c>.Scroll</c>, <c>.Fill</c>, …).
/// </remarks>
public sealed partial class SpreadsheetGrid : Panel
{
    public const double HeaderWidth = 46;
    public const double HeaderHeight = 22;

    // Inset around cell text so glyphs have a little breathing room from the gridlines. The same
    // values drive both the drawn text (see DrawCellContent/DrawHashes) and the inline editor, so
    // committing an edit never shifts the text. CellPadX is the per-side horizontal inset; CellPadY
    // the per-side vertical inset.
    private const double CellPadX = 5;
    private const double CellPadY = 2;

    private const double EditorPadX = CellPadX;
    private const double EditorPadY = CellPadY;

    private readonly TextBox _editor = new()
    {
        IsVisible = false,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(EditorPadX, EditorPadY, EditorPadX, 0),
        Background = Brushes.White,
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top, // sit at the top of the cell, like the drawn text

        // ⇧↩ puts a line break in the cell, so the editor has to hold one. Return itself never reaches
        // the TextBox — EditorPreviewKeyDown decides it in the tunnel phase — so accepting returns buys
        // the rendering and caret behaviour of a multi-line field without giving up the commit key.
        AcceptsReturn = true,
    };

    // A TextBox can't render underline/strikethrough, so a thin overlay above the editor draws those
    // decorations itself — using the same text metrics the grid uses for cells, so they line up.
    private readonly EditorDecorationLayer _editorDecorations;

    private readonly Surface _surface;
    private WorkbookSession? _session;

    // Scroll position is an anchor cell (_scrollRow/_scrollColumn — the first scrollable row/column)
    // plus a sub-cell pixel offset (_scrollRowOffset/_scrollColumnOffset) for how far that anchor is
    // scrolled past the top/left fold. The offset is what makes scrolling glide instead of snapping to
    // whole cells: the anchor is drawn partially clipped and advances/retreats as the offset crosses a
    // cell boundary (see NormalizeVerticalScroll/NormalizeHorizontalScroll). Offsets are in post-zoom
    // device pixels and kept in [0, size-of-anchor).
    // Mutated directly across the partials (++/+=/=); the internal ScrollRow/ColumnForTest accessors
    // only expose them read-only, so these are not auto-property candidates despite IDE0032.
#pragma warning disable IDE0032 // Use auto property
    private int _scrollRow;
    private int _scrollColumn;
#pragma warning restore IDE0032
    private bool _selecting;
    private int? _tabOriginColumn; // the column a Tab sequence began at, so Enter returns to it (#19)

    private readonly List<(int Index, double Offset, double Size)> _visibleColumns = [];
    private readonly List<(int Index, double Offset, double Size)> _visibleRows = [];

    private GridPalette _palette = GridPalette.For(Avalonia.Styling.ThemeVariant.Light);

    public SpreadsheetGrid()
    {
        Focusable = true;
        _surface = new Surface(this);
        _editorDecorations = new EditorDecorationLayer(this);
        Children.Add(_surface);            // draws the grid
        Children.Add(_editor);             // floats above for inline editing
        Children.Add(_editorDecorations);  // draws the editor's underline/strikethrough, above the editor
        InitScrollBars();              // overlay scroll bars along the right/bottom edges (task 3)
        InitChrome();                  // popup layer for filter/validation dropdowns
        InitSignatureHelp();           // function argument hint while typing a formula
        InitRadial();                  // press-and-hold radial command menu (added last → on top)
        ApplyTheme();                  // canvas/text colors follow the system light/dark theme
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        Selection.Changed += (_, _) => Redraw();
        // Point-mode clicks must be caught in the tunnel phase, before the focus class handler moves
        // focus to the grid and blurs the inline editor: that blur would commit the formula before
        // the click could insert a reference into it (#17 / task 1).
        AddHandler(PointerPressedEvent, PointModePreviewPressed, RoutingStrategies.Tunnel);
        _editor.KeyDown += EditorKeyDown;
        // Arrow keys must be caught in the tunnel phase: the TextBox's own (bubbling) handler would
        // otherwise consume them for caret movement before point mode could poke at a cell.
        _editor.AddHandler(KeyDownEvent, EditorPreviewKeyDown, RoutingStrategies.Tunnel);
        // Commit on blur, but don't pull focus back to the grid: the editor is losing focus precisely
        // because it's moving elsewhere (e.g. the user clicked the formula bar). Re-grabbing it here is
        // a reentrant focus change *during* that transfer, which leaves Avalonia's focus manager pointing
        // at the grid while the new control still shows as focused — so a later click can't dislodge it
        // and the formula bar keeps its focus state. Let the focus go where it was headed.
        _editor.LostFocus += (_, _) =>
        {
            // Avalonia moves focus on a pointer press *before* routing the press event, so the blur
            // fires ahead of any pointer handler. When focus is moving to the grid itself during a
            // formula edit, the press that caused it is a point-mode reference click (task 1): defer
            // the commit to PointModePreviewPressed, which inserts the reference (and keeps editing)
            // or commits explicitly if the press wasn't an operand position. Any other blur (the
            // formula bar, another control) commits here as before.
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            if (IsFormulaEdit() && ReferenceEquals(focused, this))
            {
                return;
            }

            CommitEdit(moveRow: 0, moveColumn: 0, refocus: false);
        };
        _editor.TextChanged += (_, _) =>
        {
            // Compare by value (not a bool flag) so a TextChanged that arrives after SetEditorText
            // returns still counts as our own edit, not typing. Two kinds of "programmatic" change
            // are distinguished: a grid self-edit (point mode, autocomplete, F4) still drives the
            // formula bar, while an echo coming FROM the bar must not bounce back.
            bool programmatic = _editor.Text == _programmaticText;
            bool barEcho = programmatic && _barEcho;
            _programmaticText = null;
            _barEcho = false;
            if (!programmatic)
            {
                ResetPointMode();  // typing ends ref replacement (#17)
                OfferCompletion(); // type-ahead from the column (#20)
            }

            Redraw(); // keep live reference highlights in step with the formula (#18)
            InvalidateArrange(); // grow the editor rightwards as the text widens (Google Sheets feel)
            UpdateSignatureHelp(); // refresh the argument hint as the formula changes

            // Task-9 hook: notify external listeners (the formula bar) of edits so they can mirror
            // the inline editor live. User typing AND grid self-edits (point-mode references,
            // autocomplete, F4) update the bar; only the echo coming FROM the bar (SetInlineEditorText)
            // is suppressed, to avoid an echo loop.
            if (!barEcho)
            {
                InlineEditTextChanged?.Invoke(_editor.Text ?? string.Empty);
            }
        };
        // The argument hint also tracks caret movement (arrows / clicks within the formula).
        _editor.GetObservable(TextBox.CaretIndexProperty).Subscribe(new AnonymousObserver<int>(_ => UpdateSignatureHelp()));
    }

    private void Redraw()
    {
        _surface.InvalidateVisual();
        _editorDecorations.InvalidateVisual();
    }

    /// <summary>Re-resolve the theme palette and repaint; the inline editor matches the canvas so editing reads in either theme.</summary>
    private void ApplyTheme()
    {
        _palette = GridPalette.For(ActualThemeVariant);
        Background = _palette.Canvas;
        _editor.Background = _palette.Canvas;
        _editor.Foreground = _palette.DefaultText;
        Redraw();
    }

    public GridSelection Selection { get; } = new();

    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Raised by ⌥⇧+Arrow: move the current selection's contents one cell in the given direction
    /// (rowDelta, columnDelta). The host performs the move (and the reference fix-ups) since the
    /// cut/move machinery lives there, mirroring <see cref="EnterPasteRequested"/>.
    /// </summary>
    public event Action<int, int>? MoveSelectionRequested;

    /// <summary>
    /// The active cell's address as text for the Name Box — the full merged range (e.g. <c>A1:B2</c>)
    /// when the active cell sits inside a merge, otherwise the single address (e.g. <c>A1</c>). The
    /// host's formula bar should display this rather than building the address from the raw
    /// active row/column, so a selected merge reads as its whole span.
    /// </summary>
    public string ActiveCellName
    {
        get
        {
            if (Sheet?.FindMerge(Selection.ActiveRow, Selection.ActiveColumn) is { } merge)
            {
                return merge.Range.ToString();
            }

            return new CellAddress(Selection.ActiveColumn, Selection.ActiveRow).ToString();
        }
    }

    /// <summary>
    /// When true (a modal overlay is open above the grid), the grid ignores keyboard and text input so
    /// navigation, typing and editing don't leak to the hidden grid behind the modal. The event is left
    /// unhandled so it still reaches the overlay. Set by the window when it shows/hides an overlay.
    /// </summary>
    public bool InputBlocked { get; set; }

    // The modifier chord from the most recent key press; consulted in OnTextInput so a Cmd/Ctrl combo
    // never types into a cell. KeyDown always precedes the text input it produces, so this stays current.
    private KeyModifiers _lastKeyModifiers;

    public double Zoom
    {
        get; set
        {
            field = Math.Clamp(value, Cellestra.Core.Defaults.MinZoom, Cellestra.Core.Defaults.MaxZoom);
            Redraw();
        }
    } = 1.0;

    public void Attach(WorkbookSession session)
    {
        _session = session;
        _scrollRow = 0;
        _scrollColumn = 0;
        ScrollRowOffsetForTest = 0;
        ScrollColumnOffsetForTest = 0;
        Selection.MoveTo(0, 0);
        WireFormatPainter(session); // paintbrush cursor + drag-to-paint follow the session's armed stamp
        Redraw();
        InvalidateArrange(); // size the scroll bars to the attached sheet
    }

    public void Refresh()
    {
        // A formatting command (bold, colour, alignment, number format…) can run while a cell is being
        // edited — the toolbar buttons don't steal focus. Re-skin the live editor so the text you're
        // typing immediately takes on the new style, matching how the cell will look once committed.
        if (IsInlineEditing && _session is not null)
        {
            ApplyEditorFont(Sheet?.Find(Selection.ActiveRow, Selection.ActiveColumn));
        }

        Redraw();
        InvalidateArrange(); // an edit may have changed the content extent — resize the scroll bars
    }

    private Worksheet? Sheet => _session?.ActiveSheet;

    private CultureInfo Culture => _session?.Workbook.Settings.EffectiveLocale ?? CultureInfo.CurrentCulture;

    // ---- Layout -----------------------------------------------------------

    private double _frozenRight = HeaderWidth;
    private double _frozenBottom = HeaderHeight;

    private void BuildVisible(Size bounds)
    {
        _visibleColumns.Clear();
        _visibleRows.Clear();
        var sheet = Sheet;
        if (sheet is null)
        {
            return;
        }

        RebuildRowFontMap(sheet); // so EffectiveRowHeight can grow rows to fit their tallest font

        // Re-settle the anchor/offset against the current sizes (zoom, row growth, resize can shift them)
        // so the partially-scrolled top row/column is always the right one before we lay anything out.
        NormalizeVerticalScroll(sheet);
        NormalizeHorizontalScroll(sheet);

        int frozenColumns = sheet.FrozenColumns;
        int frozenRows = sheet.FrozenRows;

        // A scrollable row/column scrolled partway under a frozen band would bleed into it (the headers
        // only cover the header strips, not the frozen-pane band). So on a frozen axis the draw snaps to
        // a whole-cell boundary — the anchor still advances smoothly as the accumulated offset crosses a
        // cell, but nothing is drawn half-clipped under the frozen rows/columns. Axes without a freeze
        // keep full sub-cell smoothness.
        double rowDrawOffset = frozenRows > 0 ? 0 : ScrollRowOffsetForTest;
        double colDrawOffset = frozenColumns > 0 ? 0 : ScrollColumnOffsetForTest;

        // Frozen columns are always shown, pinned just right of the row header.
        double x = HeaderWidth;
        for (int col = 0; col < frozenColumns && x < bounds.Width; col++)
        {
            if (sheet.IsColumnHidden(col))
            {
                continue;
            }

            double w = EffectiveColumnWidth(sheet, col) * Zoom;
            _visibleColumns.Add((col, x, w));
            x += w;
        }

        _frozenRight = x;
        // The first scrollable column is shifted left by its sub-cell offset, so it can be drawn
        // partially clipped behind the frozen columns / row header (which repaint over the overhang).
        x = _frozenRight - colDrawOffset;
        for (int col = Math.Max(_scrollColumn, frozenColumns); col < SheetDimensions.MaxColumns && x < bounds.Width; col++)
        {
            if (sheet.IsColumnHidden(col))
            {
                continue;
            }

            double w = EffectiveColumnWidth(sheet, col) * Zoom;
            _visibleColumns.Add((col, x, w));
            x += w;
        }

        double y = HeaderHeight;
        for (int row = 0; row < frozenRows && y < bounds.Height; row++)
        {
            if (sheet.IsRowHidden(row))
            {
                continue;
            }

            double h = EffectiveRowHeight(sheet, row) * Zoom;
            _visibleRows.Add((row, y, h));
            y += h;
        }

        _frozenBottom = y;
        // The first scrollable row is shifted up by its sub-cell offset, so it can be drawn partially
        // clipped behind the frozen rows / column header (which repaint over the overhang).
        y = _frozenBottom - rowDrawOffset;
        for (int row = Math.Max(_scrollRow, frozenRows); row < SheetDimensions.MaxRows && y < bounds.Height; row++)
        {
            if (sheet.IsRowHidden(row))
            {
                continue;
            }

            double h = EffectiveRowHeight(sheet, row) * Zoom;
            _visibleRows.Add((row, y, h));
            y += h;
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _surface.Arrange(new Rect(finalSize));
        _popupLayer.Arrange(new Rect(finalSize));
        _radialOverlay.Arrange(new Rect(finalSize));
        BuildVisible(finalSize);
        if (IsInlineEditing && TryGetActiveCellRect(out var rect))
        {
            rect = ExpandedEditorRect(rect, finalSize); // grow rightward over neighbours as the text widens
            // Measure the editor at the size it is about to occupy, not at the grid's full width (which
            // is what Panel.MeasureOverride hands it). A TextBox lays its text out against the width it
            // was *measured* with, so without this a wrapping editor folded its lines at the grid's width
            // and then got arranged at the column's — the caret and the visible line breaks landed
            // nowhere near where the committed cell draws them.
            _editor.Measure(rect.Size);
            _editor.Arrange(rect);
            _editorDecorations.Arrange(rect); // the decoration overlay tracks the editor exactly
            ArrangeSignatureTip(rect, finalSize);
        }
        else
        {
            _editor.Arrange(new Rect(0, 0, 0, 0));
            _editorDecorations.Arrange(new Rect(0, 0, 0, 0));
            _signatureTip.Arrange(new Rect(0, 0, 0, 0));
        }

        LayoutScrollBars(finalSize);
        return finalSize;
    }

    /// <summary>Place the argument hint just above the cell editor, dropping below it when there's no room.</summary>
    private void ArrangeSignatureTip(Rect editor, Size finalSize)
    {
        if (!_signatureTip.IsVisible)
        {
            _signatureTip.Arrange(new Rect(0, 0, 0, 0));
            return;
        }

        _signatureTip.Measure(finalSize);
        var size = _signatureTip.DesiredSize;
        double x = Math.Max(0, Math.Min(editor.X, finalSize.Width - size.Width));
        double y = editor.Y - size.Height - 2;
        if (y < HeaderHeight)
        {
            y = editor.Bottom + 2; // no room above — show below the cell
        }

        _signatureTip.Arrange(new Rect(x, y, size.Width, size.Height));
    }

    private SpreadsheetGridAutomationPeer? _automationPeer;

    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        _automationPeer = new SpreadsheetGridAutomationPeer(this);

    /// <summary>The active cell's address and displayed value, for the accessibility peer (#C).</summary>
    public string ActiveCellDescription
    {
        get
        {
            string address = ActiveCellName;
            var cell = Sheet?.Find(Selection.ActiveRow, Selection.ActiveColumn);
            if (cell is null || cell.Value.IsEmpty)
            {
                return $"{address}: empty";
            }

            var style = ResolveStyle(Selection.ActiveRow, Selection.ActiveColumn);
            return $"{address}: {NumberFormatter.Format(cell.Value, style.NumberFormat, Culture)}";
        }
    }

    private void RaiseSelectionChanged()
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        _automationPeer?.AnnounceActiveCell(ActiveCellDescription); // read the new active cell to a screen reader
    }
}
