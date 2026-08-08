using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Services;
using Cellestra.Desktop.Views.Dialogs;
using Puffinote.Components.Commands;
using Puffinote.Components.Controls;
using Puffinote.Components.Controls.Icons;
using Puffinote.Components.Input;
using Puffinote.Components.Overlays;
using Puffinote.Components.Theming;
// The view moved to the Cellestra.Editor namespace; the moved command bar keeps its original
// Cellestra.Desktop.Controls.CommandBar namespace. Alias it to disambiguate from Avalonia's own
// CommandBar control (Icons and Views.Overlays types are already imported below).
using CommandBar = Cellestra.Desktop.Controls.CommandBar.CommandBar;

namespace Cellestra.Editor;

/// <summary>
/// The embeddable spreadsheet editor: the grid plus its formula/command/status chrome, sheet tabs,
/// dialogs, and command catalog, wired to one <see cref="WorkbookSession"/>. The standalone app and any
/// host embed this control and supply file/document operations through <see cref="Host"/> (see
/// <see cref="IEditorHost"/>); the editor itself owns no window, menu, or file dialogs.
/// </summary>
/// <remarks>
/// This root file owns the shared fields, the constructor's wiring, the <see cref="IAppEnvironment"/>
/// core + host file/document operations, the status/chrome sync, and the palette/modal overlay
/// infrastructure. Concern partials carry the rest: <c>.Sheets</c> (add/chart/rename/delete/reset),
/// <c>.Tabs</c> (the sheet-tab strip + drag-reorder + inline rename), <c>.Clipboard</c>
/// (copy/cut/paste/nudge), <c>.FormulaBar</c> (formula bar + name box), <c>.Dialogs</c> (command &amp;
/// dialog launchers + context menus), and <c>.KeyBindings</c> (shortcut registration + gating; the pure
/// gesture rules live in <see cref="KeyGestureRules"/>).
/// </remarks>
public partial class SpreadsheetEditorView : UserControl, IAppEnvironment
{

    /// <summary>The embedder that fulfils file/document operations (New/Open/Save/Import/Export).</summary>
    public IEditorHost? Host { get; set; }

    /// <summary>Raised when the document's name or modified state may have changed (host updates its title/tab).</summary>
    public event EventHandler? DocumentChanged;

    /// <summary>Raised when the document's modified (dirty) state may have changed — part of the embedding
    /// contract so a host shell can show/hide an unsaved-changes indicator on its tab.</summary>
    public event EventHandler? ModifiedChanged;

    /// <summary>Whether the document has unsaved changes (embedding contract).</summary>
    public bool IsModified => Session.IsModified;

    /// <summary>Raised when overlay/modal state changes, so a host can re-gate native-menu commands.</summary>
    public event EventHandler? CommandStateChanged;

    /// <summary>Raised when the command bar is collapsed/expanded, so a host can persist the preference.</summary>
    public event EventHandler? CommandBarCollapsedChanged;

    /// <summary>Raised when the radial-menu configuration changes (via its settings), so a host can persist it.</summary>
    public event EventHandler? RadialConfigChanged;

    /// <summary>Raised when the Preferences dialog is accepted, so the host writes its settings file.</summary>
    public event EventHandler? PreferencesChanged;

    // Gated shortcut commands backing this control's KeyBindings. A command backs off (CanExecute=false)
    // while a modal overlay is open or a focused text input owns the key, so the overlay/editor handles
    // it instead (see RefreshCommandStates).
    private readonly List<RelayCommand> _gatedCommands = [];

    // The rich in-app clipboard (values + formulas + styles) and the exact text we mirrored to the
    // OS clipboard; an in-app paste uses the block only while the OS clipboard still matches.
    private ClipboardBlock? _clipboard;
    private string? _clipboardText;

    private readonly CommandBar? _commandBar;
    private System.DateTime? _lastSavedAt;

    // The one command catalog, built once against this view (which is the IAppEnvironment). Each command
    // closes over this environment (proposal 0001 §5), so the menu bar, command bar, Command Palette, and
    // key bindings all read the same env-bound entries.

    /// <inheritdoc />
    public AppCommands Commands { get; }

    public SpreadsheetEditorView()
    {
        InitializeComponent();

        Commands = new AppCommands(this);

        Grid.Attach(Session);
        Grid.SelectionChanged += (_, _) => OnSelectionChanged();
        Grid.ContextRequested += OnGridContextRequested;
        Grid.ContextMenuRequested += ShowGridContextMenuAt;
        Grid.RadialCommandActivated += RunRadialCommand;
        Grid.Commands = Commands;
        Grid.RadialCommandEnabled = id => Commands.ById(id) is { } command && command.IsEnabled;

        SheetTabs.PointerPressed += SheetTabsPointerPressed;
        SheetTabs.PointerMoved += SheetTabsPointerMoved;
        SheetTabs.PointerReleased += SheetTabsPointerReleased;
        SheetTabs.PointerCaptureLost += (_, _) => EndTabDragVisual(); // tidy up if the drag is interrupted
        Grid.ClipboardInvalidated += (_, _) => ClearClipboard();
        Grid.EnterPasteRequested += (_, _) => EnterPaste();
        Grid.MoveSelectionRequested += MoveSelection;
        WireSelectionUndo();
        Session.Changed += (_, _) =>
        {
            Grid.Refresh();
            UpdateChrome();
            RefreshChartData();
        };
        Session.StructureChanged += (_, _) =>
        {
            BuildSheetTabs();
            Grid.Refresh();
            UpdateChrome();
            RefreshSheetView();
        };

        FormulaInput.KeyDown += FormulaInputKeyDown;
        FormulaInput.TextChanged += FormulaInputTextChanged;
        FormulaInput.LostFocus += FormulaInputLostFocus;
        Grid.InlineEditTextChanged += OnInlineEditTextChanged;
        NameBox.KeyDown += NameBoxKeyDown;
        // The formula bar's fx button opens the Insert Function picker (conventional behavior), which
        // seeds "=NAME(" and enters edit mode; the inline autocomplete/signature help takes over.
        FxButton.Click += (_, _) => ShowFunctionPicker();
        FormulaExpandButton.Content = _formulaExpandIcon;
        FormulaExpandButton.Click += (_, _) => ToggleFormulaBarExpanded();

        RegisterKeyBindings();
        BuildSheetTabs();

        // The tabs built above can't know the real theme yet — ActualThemeVariant only resolves once this
        // view is attached to a visual tree, and reports the default (light) until then. Recolor when it
        // lands, so the active tab isn't left with a white surface inside a dark shell (and so it follows
        // a theme switch afterwards). Same pattern as SpreadsheetGrid's ApplyTheme.
        ActualThemeVariantChanged += (_, _) => ApplyTabTheme();

        _commandBar = new CommandBar(this);
        _commandBar.CollapsedChanged += (_, _) => CommandBarCollapsedChanged?.Invoke(this, EventArgs.Empty);
        CommandBarHost.Content = _commandBar;

        // The shared overlay host owns show/dismiss + the Tab-trap; the app supplies the blur and the
        // grid-shortcut suspend/restore around it (an open overlay must stop the grid reacting to keys).
        OverlayHost.Opened += (_, _) =>
        {
            RefreshCommandStates();
            SetBackgroundBlur(true);
        };
        OverlayHost.Closed += (_, _) =>
        {
            RefreshCommandStates();
            SetBackgroundBlur(false);
            Grid.Focus();
        };

        // Esc closes any open overlay (command palette / sheet switcher / internal modal) no matter
        // where focus sits inside it. Handled on the tunnelling route so it fires before any inner
        // control (a TextBox, ListBox, default/cancel button) can swallow the key first.
        AddHandler(KeyDownEvent, OnPreviewKeyDownForOverlay, RoutingStrategies.Tunnel);

        // The trackpad half of zoom, over the same seam the ⌘= / ⌘− catalog uses (see PinchZoom). Also
        // tunnelling, and for the same reason: the grid scrolls on the wheel and marks it handled.
        PinchZoom.Attach(this, () => Zoom, zoom => Zoom = zoom);

        UpdateChrome();
        RefreshSheetView();
        OnSelectionChanged();
    }

    /// <summary>Give the grid keyboard focus (the host window calls this after it becomes active, so the
    /// keyboard works without a priming click — Avalonia doesn't reliably focus a freshly-shown window).</summary>
    public void FocusGrid() => Grid.Focus();

    /// <summary>
    /// The grid's zoom factor (the family-wide <see cref="Puffinote.Components.Theming.ZoomScale"/> ladder);
    /// the host persists and restores it across sessions. The grid clamps; setting it repaints the status
    /// bar's percentage, so the number the reader sees can't lag the size they're looking at.
    /// </summary>
    public double Zoom
    {
        get => Grid.Zoom;
        set
        {
            Grid.Zoom = value;
            UpdateChrome();
        }
    }

    /// <summary>Whether the adaptive command bar is collapsed to its slim handle; the host persists it.</summary>
    public bool CommandBarCollapsed
    {
        get => _commandBar?.Collapsed ?? false; set => _commandBar?.Collapsed = value;
    }

    /// <summary>The press-and-hold radial menu's bindings; pushed in from settings and edited via its dialog.</summary>
    public RadialMenuConfig RadialConfig
    {
        get => Grid.RadialConfig;
        set => Grid.RadialConfig = value;
    }

    /// <summary>Run a command a radial wedge was released over (it is already known to be enabled).</summary>
    private void RunRadialCommand(string commandId)
    {
        if (Commands.ById(commandId) is { } command && command.IsEnabled)
        {
            command.Execute();
        }
    }

    /// <summary>
    /// Open the radial-menu settings dialog, then adopt and persist any change. Reached from the command
    /// catalog (and so the Command Palette / menu) — see <c>view.radialSettings</c>.
    /// </summary>
    public async void ShowRadialSettings()
    {
        var updated = await ShowModalAsync(new RadialSettingsDialog(RadialConfig, Commands));
        if (updated is not null)
        {
            RadialConfig = updated;
            RadialConfigChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Open the Preferences dialog over the options the host keeps in Cellestra's <c>settings.json</c>,
    /// then apply and persist any change. Reached from the command catalog — see <c>app.preferences</c>.
    /// <para>
    /// It covers the file's <em>preferences</em>, not its session state: the recent-files list and the
    /// remembered window size are records of what you did, and the window size in particular is rewritten
    /// from the real window on close, so a value typed here could only ever be discarded. The radial
    /// menu's bindings have an editor of their own (View ▸ Radial Menu Settings…) that a row in a form
    /// couldn't improve on — the overlay host shows one modal at a time, so a nested one isn't offered.
    /// </para>
    /// </summary>
    public async void ShowPreferences()
    {
        var form = new SettingsEditorView(
        [
            new SettingsSection("View",
            [
                new NumberSetting("Zoom", () => Zoom * 100, v => Zoom = v / 100, 25, 400, step: 10,
                    hint: "Percent. ⌘+ and ⌘− change it while you work; this is where it starts."),
                new ToggleSetting("Collapse the command bar", () => CommandBarCollapsed, v => CommandBarCollapsed = v,
                    "Fold the toolbar to its slim handle, for more grid."),
            ]),
            new SettingsSection("Radial menu",
            [
                new NumberSetting("Segments", () => RadialConfig.SegmentCount,
                    v => RadialConfig = RadialConfig with { SegmentCount = (int)v },
                    RadialMenuConfig.MinSegments, RadialMenuConfig.MaxSegments,
                    hint: "How many wedges the hold-right-click menu is divided into. View ▸ Radial Menu Settings… binds a command to each."),
            ]),
        ]);

        if (!await ShowModalAsync(new FormDialog("Preferences", form)))
        {
            return; // Cancel / Esc — leave settings untouched
        }

        form.Commit();
        PreferencesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Esc → dismiss whatever overlay is open: cancel an internal modal (so its
    /// awaiter resolves to the cancel result) or close a palette-style overlay.</summary>
    private void OnPreviewKeyDownForOverlay(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && OverlayHost.TryCancel())
        {
            e.Handled = true;
        }
    }

    // ---- IAppEnvironment --------------------------------------------------

    public WorkbookSession Session { get; private set; } = WorkbookSession.NewBlank();

    public GridSelection Selection => Grid.Selection;

    public void Refresh()
    {
        Grid.Refresh();
        SyncFormulaBar(); // the active cell's value/formula may have changed, or the selection moved programmatically
        UpdateChrome();
    }

    public void BeginEdit() => Grid.BeginEdit(null);

    public void BeginEditSelectAll() => Grid.BeginEditSelectAll();

    public void RevealActiveCell() => Grid.ScrollToActive();

    /// <summary>Narrate the last action in the status line's left indicator (a quick reference, per the mockup).</summary>
    public void Narrate(string message) => StatusAction.Text = message;

    // ---- File / document operations (delegated to the embedding Host) -----
    //
    // The catalog commands (file.new/open/save/…) call these through IAppEnvironment; the editor itself
    // owns no file dialogs, disk access, lock, or recent-files list. The Host (the standalone window, or
    // an embedding app) performs the actual operation and drives the document back in via AdoptLoaded /
    // ReplaceSession / MarkSavedNow.

    public Task NewWorkbookAsync() => Host?.NewWorkbookAsync() ?? Task.CompletedTask;

    public Task OpenWorkbookAsync() => Host?.OpenWorkbookAsync() ?? Task.CompletedTask;

    public Task SaveWorkbookAsync() => Host?.SaveWorkbookAsync() ?? Task.CompletedTask;

    public Task SaveWorkbookAsAsync() => Host?.SaveWorkbookAsAsync() ?? Task.CompletedTask;

    public Task ImportCsvAsync() => Host?.ImportCsvAsync() ?? Task.CompletedTask;

    public Task ExportCsvAsync() => Host?.ExportCsvAsync() ?? Task.CompletedTask;

    /// <summary>
    /// Adopt a workbook the host loaded from disk: swap it into the session, reattach the grid, and
    /// rebuild the tabs/chrome. A freshly-opened file is on disk in a saved state, so this records a
    /// saved baseline. The host owns the file lock, recent-files list, and any open-time warnings.
    /// </summary>
    public void AdoptLoaded(WorkbookSession.LoadedWorkbook loaded, string? path)
    {
        Session.AdoptWorkbook(loaded, path);
        _lastSavedAt = DateTime.Now;
        Grid.Attach(Session);
        BuildSheetTabs();
        UpdateChrome();
    }

    /// <summary>
    /// Replace the editor's document with a different session (a new blank workbook or a CSV import),
    /// rewiring the grid/chrome/undo to it. The new document has no on-disk saved baseline.
    /// </summary>
    public void ReplaceSession(WorkbookSession session)
    {
        Session = session;
        _lastSavedAt = null;
        RewireSession();
    }

    /// <summary>Record that the document was just saved (the host wrote it to disk), so the status line
    /// reads "Saved HH:mm" until the next edit.</summary>
    public void MarkSavedNow()
    {
        _lastSavedAt = DateTime.Now;
        UpdateChrome();
        Saved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised each time the document reaches disk, however it got there (⌘S, Save As, or a host
    /// shell's stream save). A host can use it to acknowledge the save in its own chrome.</summary>
    public event EventHandler? Saved;

    // ---- Embedding contract (stream-based load/save for a host shell) -----
    //
    // These let a host (e.g. a notes-app shell driving the editor from a file tree) open and save an
    // .ods document by stream, without the catalog commands or file dialogs. The standalone app uses
    // the richer IEditorHost path (lock, recent files, busy overlay); both ultimately go through the
    // same WorkbookSession serialization.

    /// <summary>
    /// Load an OpenDocument Spreadsheet from <paramref name="content"/> and show it, associating it with
    /// <paramref name="path"/> (used for the title/Save target; may be null for an unsaved stream). The
    /// deserialize + initial recalc run off the UI thread so a large document doesn't freeze the host.
    /// </summary>
    public async Task LoadAsync(Stream content, string? path)
    {
        var loaded = await Task.Run(() => WorkbookSession.LoadAndRecalc(content));
        AdoptLoaded(loaded, path);
    }

    /// <summary>
    /// Serialize the current document as .ods bytes into <paramref name="target"/> and mark it saved.
    /// The snapshot is taken on the UI thread (no edit can race it); the host owns where the stream goes.
    /// </summary>
    public async Task SaveAsync(Stream target)
    {
        byte[] image = Session.SerializeToBytes();
        await target.WriteAsync(image);
        if (Session.FilePath is { } path)
        {
            Session.MarkSaved(path);
        }

        MarkSavedNow();
    }

    /// <summary>A text field has focus when the focused element is a <see cref="TextBox"/> (cell editor, formula bar, name box, or a dialog field).</summary>
    public bool IsTextInputFocused => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;

    /// <summary>The OS clipboard from the hosting top level (window or embedding shell), or null when this
    /// control isn't attached to one yet (so copy/paste no-op rather than throwing).</summary>
    private Avalonia.Input.Platform.IClipboard? Clipboard => TopLevel.GetTopLevel(this)?.Clipboard;

    /// <summary>The Command Palette / sheet switcher overlay is modal: while it's up, the grid behind it must ignore command shortcuts.</summary>
    public bool IsModalOpen => OverlayHost.IsVisible;

    /// <summary>An opaque surface color for floating chrome (palette box, active tab) that follows the light/dark theme so themed text stays legible.</summary>
    private IBrush ThemedSurface => ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
        ? new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B))
        : Brushes.White;

    // ---- Chrome -----------------------------------------------------------

    private void RewireSession()
    {
        Session.Changed += (_, _) =>
        {
            Grid.Refresh();
            UpdateChrome();
            RefreshChartData();
        };
        Session.StructureChanged += (_, _) =>
        {
            BuildSheetTabs();
            Grid.Refresh();
            UpdateChrome();
            RefreshSheetView();
        };
        WireSelectionUndo();
        Grid.Attach(Session);
        BuildSheetTabs();
        UpdateChrome();
        RefreshSheetView();
        OnSelectionChanged();
    }

    /// <summary>Let the session capture/restore the grid selection so undo returns there (#33).</summary>
    private void WireSelectionUndo()
    {
        Session.SelectionProvider = () => new SelectionMemento(
            Selection.Top, Selection.Left, Selection.Bottom, Selection.Right,
            Selection.ActiveRow, Selection.ActiveColumn);
        Session.SelectionRestorer = m =>
            Selection.Restore(m.Top, m.Left, m.Bottom, m.Right, m.ActiveRow, m.ActiveColumn);
    }

    private void OnSelectionChanged()
    {
        SyncFormulaBar();
        UpdateStatus();
        Narrate($"Selected {new CellAddress(Selection.ActiveColumn, Selection.ActiveRow)}");
        _commandBar?.RefreshState();
    }

    private void UpdateChrome()
    {
        StatusZoom.Text = ZoomScale.Format(Grid.Zoom);
        StatusCalc.Text = Session.Workbook.Settings.CalculationMode == CalculationMode.Manual ? "Manual" : "Auto";
        StatusSaved.Text = Session.IsModified ? "Edited"
            : _lastSavedAt is { } saved ? $"Saved {saved:HH:mm}" : "Not saved";
        _commandBar?.RefreshState();

        // The window title / host tab follows the document name + dirty state, which the host renders;
        // notify it rather than touching a Window.Title this control doesn't own.
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        ModifiedChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateStatus()
    {
        var sheet = Session.ActiveSheet;
        StatusCell.Text = new CellAddress(Selection.ActiveColumn, Selection.ActiveRow).ToString();

        // Aggregate over the union of all selected ranges, deduped so overlaps aren't double-counted.
        int count = 0;
        int numericCount = 0;
        double sum = 0;
        var seen = new HashSet<(int, int)>();
        foreach (var range in Selection.Ranges)
        {
            // A whole-row/column selection spans the entire sheet; only its populated cells matter.
            foreach (var (row, column, _) in sheet.EnumerateCells())
            {
                if (!range.Contains(row, column) || !seen.Add((row, column)))
                {
                    continue;
                }

                var value = sheet.GetValue(row, column);
                if (value.IsEmpty)
                {
                    continue;
                }

                count++;
                if (value.IsNumber)
                {
                    numericCount++;
                    sum += value.AsNumber;
                }
            }
        }

        if (numericCount > 0)
        {
            StatusStats.Text = $"Count: {count}    Sum: {sum:0.######}    Average: {sum / numericCount:0.######}";
        }
        else
        {
            StatusStats.Text = count > 0 ? $"Count: {count}" : string.Empty;
        }
    }

    // ---- Palette overlay --------------------------------------------------

    /// <summary>One palette entry: an icon + command name + description + shortcut that runs an action.</summary>
    private sealed record PaletteItem(string Label, string? Icon, string Description, string Shortcut, Action Run, string Category = "");

    private void ShowPalette(string placeholder, IReadOnlyList<PaletteItem> items) =>
        ShowPalette(placeholder, [.. items.Select(i => new PaletteEntry(i.Label, i.Run, i.Description, i.Shortcut, i.Icon, i.Category))]);

    /// <summary>
    /// Open <paramref name="entries"/> in the centred overlay card. This is the keyboard palette's home: a
    /// shortcut is fired from a neutral, centred gaze, so the results meet the eye in the middle of the window.
    /// The search pill's click drops its list from the pill instead — see <see cref="PaletteFlyout"/>.
    /// </summary>
    private void ShowPalette(string placeholder, IReadOnlyList<PaletteEntry> entries, IReadOnlyList<string>? recent = null)
    {
        var palette = new PaletteListView(LucideIcon.PaletteGlyph)
        {
            Placeholder = placeholder,
        };
        palette.SetItems(entries, recent);
        palette.Cancelled += (_, _) => OverlayHost.Close();
        palette.Chosen += entry =>
        {
            OverlayHost.Close();
            entry.Run();
        };

        var box = new Border
        {
            Width = 520,
            Background = ThemedSurface,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 80, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, OffsetY = 4, Color = Color.FromArgb(0x40, 0, 0, 0) }),
            Child = palette, // the shared list view traps Tab inside itself
        };

        // The shared host shows the card over the backdrop and (via Opened/Closed) blurs the grid and
        // suspends its shortcuts; a backdrop click or Esc closes it.
        OverlayHost.ShowContent(box);
        // Defer focus until the overlay's template is realized, otherwise focusing the just-shown input
        // can silently fail and the user has to click into it first.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => palette.SearchBox.Focus());
    }

    // ---- Internal modal host ---------------------------------------------

    /// <summary>
    /// Blur the grid/chrome behind an overlay (PRD task: "add blur to the background above any modal").
    /// Applied to <see cref="ContentRoot"/> only, so the overlay's own content stays sharp. Wired to the
    /// shared <see cref="OverlayHost"/>'s Opened/Closed in the constructor.
    /// </summary>
    private void SetBackgroundBlur(bool on) =>
        ContentRoot.Effect = on ? new BlurEffect { Radius = 8 } : null;

    /// <summary>
    /// Host an internal modal dialog over the (blurred) grid in the shared overlay host and await its
    /// result (internal modals instead of separate OS windows). Esc / backdrop cancel. Public so a host
    /// can present its own dialogs (e.g. the unsaved-changes prompt) in the editor's overlay.
    /// </summary>
    public Task<TResult?> ShowModalAsync<TResult>(ModalDialog<TResult> dialog) =>
        OverlayHost.ShowModalAsync(dialog);

}
