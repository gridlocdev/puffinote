using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Cellestra.Core.Data;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Values;
using Cellestra.Desktop.Views.Dialogs;
using NumberFormatter = Cellestra.Core.Styling.NumberFormatter;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.Icons;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Services;
using Cellestra.Desktop.Views.Overlays;

namespace Cellestra.Desktop.Views;

public partial class MainWindow : Window, IAppEnvironment
{
    private readonly SettingsService _settingsService = new();
    private WorkbookSession _session = WorkbookSession.NewBlank();
    private AppSettings _settings;
    private FileLock? _fileLock;
    private bool _fileBusy;

    // The rich in-app clipboard (values + formulas + styles) and the exact text we mirrored to the
    // OS clipboard; an in-app paste uses the block only while the OS clipboard still matches.
    private ClipboardBlock? _clipboard;
    private string? _clipboardText;

    private Controls.CommandBar.CommandBar? _commandBar;
    private System.DateTime? _lastSavedAt;

    public MainWindow()
    {
        InitializeComponent();

        _settings = _settingsService.Load();
        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;

        Grid.Attach(_session);
        Grid.SelectionChanged += (_, _) => OnSelectionChanged();
        Grid.ContextRequested += OnGridContextRequested;

        SheetTabs.PointerPressed += SheetTabsPointerPressed;
        SheetTabs.PointerMoved += SheetTabsPointerMoved;
        SheetTabs.PointerReleased += SheetTabsPointerReleased;
        SheetTabs.PointerCaptureLost += (_, _) => EndTabDragVisual(); // tidy up if the drag is interrupted
        Grid.ClipboardInvalidated += (_, _) => ClearClipboard();
        Grid.EnterPasteRequested += (_, _) => EnterPaste();
        WireSelectionUndo();
        _session.Changed += (_, _) => { Grid.Refresh(); UpdateChrome(); };
        _session.StructureChanged += (_, _) => { BuildSheetTabs(); Grid.Refresh(); UpdateChrome(); };

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
        BuildNativeMenu();
        BuildSheetTabs();

        Grid.Zoom = _settings.Zoom;

        _commandBar = new Controls.CommandBar.CommandBar(this) { Collapsed = _settings.CommandBarCollapsed };
        _commandBar.CollapsedChanged += (_, _) => PersistSettings();
        CommandBarHost.Content = _commandBar;

        Closing += OnWindowClosing;

        // Esc closes any open overlay (command palette / sheet switcher / internal modal) no matter
        // where focus sits inside it. Handled on the tunnelling route so it fires before any inner
        // control (a TextBox, ListBox, default/cancel button) can swallow the key first.
        AddHandler(KeyDownEvent, OnPreviewKeyDownForOverlay, RoutingStrategies.Tunnel);

        UpdateChrome();
        OnSelectionChanged();
    }

    /// <summary>Window-level Esc → dismiss whatever overlay is open: cancel an internal modal (so its
    /// awaiter resolves to the cancel result) or close a palette-style overlay.</summary>
    private void OnPreviewKeyDownForOverlay(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !OverlayHost.IsVisible)
        {
            return;
        }

        if (_activeModal is { } modal)
        {
            modal.CancelFromHost();
        }
        else
        {
            CloseOverlay();
        }

        e.Handled = true;
    }

    /// <summary>Set once the user has answered the unsaved-changes prompt and chosen to close, so the
    /// programmatic re-close we trigger skips the prompt instead of looping.</summary>
    private bool _forceClose;

    /// <summary>
    /// On close/quit with unsaved changes, cancel the close and ask Save / Don't Save / Cancel (PRD §8.7).
    /// On Save (succeeded) or Don't Save we re-close the window for real; on Cancel we stay open. A clean
    /// (unmodified) document closes immediately.
    /// </summary>
    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_forceClose && _session.IsModified)
        {
            e.Cancel = true; // hold the window open until the user decides
            if (await ConfirmDiscardChangesAsync())
            {
                _forceClose = true;
                Close(); // re-enter Closing, this time allowed straight through
            }

            return; // cancelled, or we'll finish teardown on the forced re-close
        }

        PersistSettings();
        ReleaseFileLock();
    }

    private void PersistSettings()
    {
        _settings = _settings with
        {
            CommandBarCollapsed = _commandBar?.Collapsed ?? false,
            Zoom = Grid.Zoom,
            WindowWidth = Width,
            WindowHeight = Height,
        };
        _settingsService.Save(_settings);
    }

    private void RememberRecentFile(string path)
    {
        _settings = _settings.WithRecentFile(path);
        _settingsService.Save(_settings);
        RefreshOpenRecent(); // refresh the Open Recent submenu in place (don't rebuild the whole menu — see #17a)
    }

    /// <summary>The "Open Recent" submenu item, kept so its contents refresh in place without a full menu rebuild.</summary>
    private NativeMenuItem? _openRecentItem;

    /// <summary>The native macOS menu bar — the complete command list (PRD §6.2), all from the one catalog.</summary>
    private void BuildNativeMenu()
    {
        var menu = new NativeMenu();

        // Application menu (next to the Apple menu on macOS): the standard "About Cellestra" item lives
        // here per PRD §6.2. Avalonia merges this first submenu into the native application menu.
        menu.Items.Add(Submenu("Cellestra", "help.about"));

        var file = Submenu("File", "file.new", "file.open", "file.save", "file.saveAs", null, "file.importCsv", "file.exportCsv");
        _openRecentItem = BuildOpenRecent();
        file.Menu!.Items.Insert(2, _openRecentItem);
        menu.Items.Add(file);
        var edit = Submenu("Edit", "edit.undo", "edit.redo", null, "edit.cut", "edit.copy", "edit.paste", "edit.pasteSpecial",
            "edit.delete", "edit.clearFormats", "edit.clearAll", "sheet.resetData", "edit.selectAll", null,
            "edit.find", "edit.findReplace", null, "edit.autoSum");
        edit.Menu!.Items.Add(Submenu("Fill", "fill.down", "fill.right")); // Edit ▸ Fill ▸ Down / Right (PRD §6.2)
        menu.Items.Add(edit);
        var format = Submenu("Format", "format.cells", null, "format.bold", "format.italic", "format.underline", null,
            "format.alignLeft", "format.alignCenter", "format.alignRight", "format.wrap", null,
            "format.currency", "format.percent", "format.date", null, "format.merge");
        format.Menu!.Items.Add(Submenu("Borders", "format.borderAll", "format.borderOutline",
            "format.borderTop", "format.borderBottom", "format.borderLeft", "format.borderRight", null, "format.borderNone"));
        menu.Items.Add(format);
        menu.Items.Add(Submenu("Insert", "insert.rowsAbove", "insert.columnsLeft", "edit.deleteRows", "edit.deleteColumns", null,
            "insert.sheet", "data.createTable", "insert.function", "insert.namedRange", "insert.hyperlink", null, "sheet.rename", "sheet.delete"));
        menu.Items.Add(Submenu("Data", "data.sortAsc", "data.sortDesc", "data.customSort", null,
            "data.toggleFilter", "data.clearFilter", null, "data.createTable", "data.removeDuplicates",
            "data.textToColumns", "data.validation", null, "formula.recalc"));
        menu.Items.Add(Submenu("View", "view.commandPalette", "view.goToSheet", null, "view.zoomIn", "view.zoomOut", "view.actualSize", null, "view.freezePanes"));
        menu.Items.Add(Submenu("Help", "help.shortcuts", null, "help.about"));
        NativeMenu.SetMenu(this, menu);
    }

    private NativeMenuItem BuildOpenRecent()
    {
        var item = new NativeMenuItem("Open Recent") { Menu = new NativeMenu() };
        PopulateOpenRecent(item.Menu);
        return item;
    }

    /// <summary>Fill an Open Recent submenu's items from the current recent-files list.</summary>
    private void PopulateOpenRecent(NativeMenu submenu)
    {
        submenu.Items.Clear();
        if (_settings.RecentFiles.Count == 0)
        {
            submenu.Items.Add(new NativeMenuItem("(none)") { IsEnabled = false });
            return;
        }

        foreach (string path in _settings.RecentFiles)
        {
            string captured = path;
            submenu.Items.Add(new NativeMenuItem(Path.GetFileName(path))
            {
                Command = new RelayCommand(() => _ = OpenPathAsync(captured)),
            });
        }
    }

    /// <summary>
    /// Refresh the Open Recent submenu's items <em>in place</em> after opening/saving a file. Rebuilding and
    /// re-<see cref="NativeMenu.SetMenu"/>-ting the whole menu while it's mounted is what threw
    /// "the menu being updated does not match" on macOS (#17a); mutating the existing submenu's items avoids it.
    /// </summary>
    private void RefreshOpenRecent()
    {
        if (_openRecentItem?.Menu is { } submenu)
        {
            PopulateOpenRecent(submenu);
        }
    }

    private NativeMenuItem Submenu(string header, params string?[] commandIds)
    {
        var submenu = new NativeMenu();
        foreach (string? id in commandIds)
        {
            if (id is null)
            {
                submenu.Items.Add(new NativeMenuItemSeparator());
                continue;
            }

            if (AppCommands.ById(id) is { } command)
            {
                submenu.Items.Add(new NativeMenuItem(command.Label)
                {
                    Gesture = command.Shortcut,
                    // Mirror the in-window KeyBinding's guard (RegisterKeyBindings): a native menu item's
                    // Gesture becomes a macOS key equivalent that the system dispatches globally, ahead of
                    // the focused control. Without the same back-off, a plain-key command (Delete → Clear
                    // Contents) fires while the formula bar or inline cell editor is focused — wiping the
                    // whole cell instead of forward-deleting one character. Yielding here lets the key fall
                    // through to the focused TextBox so it edits in place (#8).
                    Command = new RelayCommand(
                        () => command.Execute(this),
                        () => !IsModalOpen && !BlockedByTextInput(command.Shortcut) && command.IsEnabled(this)),
                });
            }
        }

        return new NativeMenuItem(header) { Menu = submenu };
    }

    // ---- IAppEnvironment --------------------------------------------------

    public WorkbookSession Session => _session;

    public GridSelection Selection => Grid.Selection;

    public void Refresh()
    {
        Grid.Refresh();
        SyncFormulaBar(); // the active cell's value/formula may have changed, or the selection moved programmatically
        UpdateChrome();
    }

    public void BeginEdit() => Grid.BeginEdit(null);

    public void RevealActiveCell() => Grid.ScrollToActive();

    /// <summary>Narrate the last action in the status line's left indicator (a quick reference, per the mockup).</summary>
    public void Narrate(string message) => StatusAction.Text = message;

    /// <summary>The user's answer to a "you have unsaved changes" prompt (PRD §8.7).</summary>
    internal enum SaveChoice
    {
        Save,
        DontSave,
        Cancel,
    }

    /// <summary>
    /// Decide whether a discard-the-document action (close/quit, New, Open, Import) may proceed, given
    /// whether the workbook is modified and — only when it is — the user's <see cref="SaveChoice"/>.
    /// Pure so the close/open/new gating logic is unit-testable without a window (AGENTS.md: focus/pointer
    /// flows are hard headlessly). Returns true to proceed (discarding), false to stay put.
    /// </summary>
    internal static bool ShouldProceedAfterPrompt(bool isModified, SaveChoice choice) =>
        !isModified || choice != SaveChoice.Cancel;

    /// <summary>
    /// Gate a document-replacing action on the save-changes prompt (PRD §8.7). If nothing is modified it
    /// proceeds silently; otherwise it asks Save / Don't Save / Cancel, saving first on Save. Returns
    /// true when the caller may discard the current document, false when the user cancelled.
    /// </summary>
    private async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (!_session.IsModified)
        {
            return true;
        }

        var choice = await ShowModalAsync(new SaveChangesDialog(DocumentDisplayName()));
        if (choice == SaveChoice.Save)
        {
            await SaveWorkbookAsync();
            // If the save was cancelled (no path chosen in Save As) or failed, the document is still
            // modified — don't discard it.
            return !_session.IsModified;
        }

        return ShouldProceedAfterPrompt(isModified: true, choice);
    }

    /// <summary>The document's display name for prompts (file name, or "Untitled" for a new workbook).</summary>
    private string DocumentDisplayName() =>
        _session.FilePath is null ? "Untitled" : Path.GetFileName(_session.FilePath);

    public async Task NewWorkbookAsync()
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return; // user cancelled out of the save-changes prompt
        }

        ReleaseFileLock();
        _session = WorkbookSession.NewBlank();
        _lastSavedAt = null;
        RewireSession();
    }

    public async Task OpenWorkbookAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Spreadsheet",
            AllowMultiple = false,
            FileTypeFilter = new[] { OdsFileType },
        });

        if (files.Count == 0)
        {
            return;
        }

        await OpenPathAsync(files[0].Path.LocalPath);
    }

    public async Task OpenPathAsync(string path)
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return; // user cancelled out of the save-changes prompt; keep the current document
        }

        if (BeginFileOperation($"Opening {Path.GetFileName(path)}…") is not { } busy)
        {
            return; // another file operation is already running
        }

        using (busy)
        {
            try
            {
                var existingLock = FileLock.Inspect(path);

                // Deserialize + initial recalc are the heavy part — run them off the UI thread so a
                // large open never freezes the window (PRD §8.6 / threading rule), then adopt on the UI
                // thread where the model is touched by rendering.
                var loaded = await Task.Run(() =>
                {
                    using var stream = File.OpenRead(path);
                    return WorkbookSession.LoadAndRecalc(stream);
                });

                _session.AdoptWorkbook(loaded, path);

                ReleaseFileLock();
                _fileLock = FileLock.TryAcquire(path);

                // A freshly-opened, unmodified file is on disk in a saved state — record a baseline so the
                // status bar reads "Saved …" rather than "Not saved" until the user edits something.
                _lastSavedAt = System.DateTime.Now;

                Grid.Attach(_session);
                BuildSheetTabs();
                RememberRecentFile(path);
                UpdateChrome();

                // The title stays the document name (#15); transient open conditions are narrated in the
                // status line so they don't hijack the title bar.
                if (existingLock is not null)
                {
                    string who = string.IsNullOrWhiteSpace(existingLock.User) ? "another application" : existingLock.User;
                    string host = string.IsNullOrWhiteSpace(existingLock.Host) ? string.Empty : $" on {existingLock.Host}";
                    Narrate($"Open in {who}{host}; changes may conflict");
                }
                else if (_session.LoadRecalcMismatches > 0)
                {
                    Narrate($"{_session.LoadRecalcMismatches} cell(s) recomputed differently than the saved file");
                }
            }
            catch (Exception ex)
            {
                // A failed open shouldn't permanently rename the title bar (#17a) — explain it in a dialog
                // and leave the title showing whatever document is still open.
                ShowMessage("Couldn't open the file", ex.Message);
            }
        }
    }

    /// <summary>Remove our document lock, if we hold one.</summary>
    private void ReleaseFileLock()
    {
        _fileLock?.Dispose();
        _fileLock = null;
    }

    /// <summary>
    /// Show the blocking busy overlay for a file open/save that runs off the UI thread, and guard
    /// against a second file operation starting while one is in flight (#A). Returns null when one is
    /// already running (the caller should bail); otherwise dispose the result (via <c>using</c>) to
    /// hide the overlay and clear the guard, even on error.
    /// </summary>
    private IDisposable? BeginFileOperation(string text)
    {
        if (_fileBusy)
        {
            return null;
        }

        _fileBusy = true;
        BusyText.Text = text;
        BusyOverlay.IsVisible = true;
        return new FileOperationScope(this);
    }

    private sealed class FileOperationScope(MainWindow owner) : IDisposable
    {
        public void Dispose()
        {
            owner._fileBusy = false;
            owner.BusyOverlay.IsVisible = false;
        }
    }

    public async Task SaveWorkbookAsync()
    {
        if (_session.FilePath is null)
        {
            await SaveWorkbookAsAsync();
            return;
        }

        if (BeginFileOperation($"Saving {DocumentDisplayName()}…") is not { } busy)
        {
            return; // another file operation is already running
        }

        using (busy)
        {
            string filePath = _session.FilePath;
            try
            {
                await Task.Run(() => _session.SaveToFile(filePath)); // atomic write off the UI thread
            }
            catch (Exception ex)
            {
                // A failed save must not crash the app — surface it like OpenPathAsync does for opens.
                ShowMessage("Couldn't save the file", ex.Message);
                return;
            }

            _lastSavedAt = System.DateTime.Now;
            RememberRecentFile(filePath);
            UpdateChrome();
        }
    }

    public async Task SaveWorkbookAsAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Spreadsheet",
            DefaultExtension = "ods",
            SuggestedFileName = "Untitled.ods",
            FileTypeChoices = new[] { OdsFileType },
        });

        if (file is null)
        {
            return;
        }

        string path = file.Path.LocalPath;
        if (BeginFileOperation($"Saving {Path.GetFileName(path)}…") is not { } busy)
        {
            return; // another file operation is already running
        }

        using (busy)
        {
            try
            {
                await Task.Run(() => _session.SaveToFile(path)); // atomic write off the UI thread
            }
            catch (Exception ex)
            {
                // A failed save must not crash the app — surface it like OpenPathAsync does for opens.
                ShowMessage("Couldn't save the file", ex.Message);
                return;
            }

            _lastSavedAt = System.DateTime.Now;

            // We now own this file on disk — take its lock so other editors are warned.
            if (_fileLock?.DocumentPath != Path.GetFullPath(path))
            {
                ReleaseFileLock();
                _fileLock = FileLock.TryAcquire(path);
            }

            RememberRecentFile(path);
            UpdateChrome();
        }
    }

    public async Task ImportCsvAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import CSV",
            AllowMultiple = false,
            FileTypeFilter = new[] { CsvFileType },
        });

        if (files.Count == 0)
        {
            return;
        }

        if (!await ConfirmDiscardChangesAsync())
        {
            return; // user cancelled out of the save-changes prompt; keep the current document
        }

        string text = await File.ReadAllTextAsync(files[0].Path.LocalPath);
        var workbook = Cellestra.Core.Serialization.Csv.CsvReader.ReadToWorkbook(text);
        ReleaseFileLock();
        _session = new WorkbookSession(workbook);
        _lastSavedAt = null;
        RewireSession();
    }

    public async Task ExportCsvAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export CSV",
            DefaultExtension = "csv",
            SuggestedFileName = _session.ActiveSheet.Name + ".csv",
            FileTypeChoices = new[] { CsvFileType },
        });

        if (file is null)
        {
            return;
        }

        string csv = Cellestra.Core.Serialization.Csv.CsvWriter.Write(_session.Workbook, _session.ActiveSheet);
        await File.WriteAllTextAsync(file.Path.LocalPath, csv);
    }

    public void Copy() => CopyToClipboard(cut: false);

    public void Cut() => CopyToClipboard(cut: true);

    public async void Paste()
    {
        string? text = Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;

        // Prefer the rich in-app block while the OS clipboard still holds exactly what we put there
        // (so an external copy in between falls back to plain text).
        if (_clipboard is { } block && text == _clipboardText)
        {
            var command = new PasteBlockCommand(_session.ActiveSheet.Name, Selection.Range, block);
            if (!Grid.TryEnforceValidation(command)) // reject the whole paste on a Stop-rule violation (#B)
            {
                return;
            }

            _session.Execute(command);
            if (block.IsCut)
            {
                ClearClipboard(); // a cut is a move, consumed by its first paste
            }

            if (command.ShapeMismatch)
            {
                Narrate("Paste area is a different shape than the copied cells");
            }

            Grid.Refresh();
            return;
        }

        if (!string.IsNullOrEmpty(text))
        {
            PasteTsv(text);
        }
    }

    /// <summary>
    /// Paste Special (PRD §4 / §6.2): offer the four modes — Values / Formats / Formulas / All — in
    /// the palette overlay, then paste the chosen facet. The narrowed modes need the rich in-app block
    /// (values+formulas+styles); a plain external clipboard has only text, so we fall back to an
    /// ordinary TSV value paste and say so.
    /// </summary>
    public async void PasteSpecial()
    {
        string? text = Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;

        // Only the rich in-app block carries formulas and styles; without it there's nothing special
        // to choose between, so paste the external text as values.
        if (_clipboard is not { } || text != _clipboardText)
        {
            if (!string.IsNullOrEmpty(text))
            {
                PasteTsv(text);
                Narrate("Pasted values (external clipboard has no formats or formulas)");
            }

            return;
        }

        var items = new[]
        {
            PasteSpecialItem("Paste Values", "Computed values only — no formulas, keep formatting", PasteMode.Values),
            PasteSpecialItem("Paste Formats", "Formatting only — leave the values in place", PasteMode.Formats),
            PasteSpecialItem("Paste Formulas", "Formulas (and their results) — keep formatting", PasteMode.Formulas),
            PasteSpecialItem("Paste All", "Values, formulas, and formatting", PasteMode.All),
        };
        ShowPalette("Paste Special — values, formats, formulas…", items);
    }

    private PaletteItem PasteSpecialItem(string label, string description, PasteMode mode) =>
        new(label, "clipboard-paste", description, string.Empty, () => PasteWithMode(mode), "Edit");

    /// <summary>Paste the in-app block at the current selection in the given <paramref name="mode"/>.</summary>
    private void PasteWithMode(PasteMode mode)
    {
        if (_clipboard is not { } block)
        {
            return; // the clipboard cleared (e.g. an edit) between opening the chooser and picking
        }

        var command = new PasteBlockCommand(_session.ActiveSheet.Name, Selection.Range, block, mode);
        if (!Grid.TryEnforceValidation(command)) // reject the whole paste on a Stop-rule violation (#B)
        {
            return;
        }

        _session.Execute(command);
        if (block.IsCut)
        {
            ClearClipboard(); // a cut is a move, consumed by its first paste
        }

        if (command.ShapeMismatch)
        {
            Narrate("Paste area is a different shape than the copied cells");
        }

        Grid.Refresh();
    }

    public void ShowCommandPalette()
    {
        var items = AppCommands.All
            .Where(c => c.IsEnabled(this))
            .Select(c => new PaletteItem(c.Label, c.Icon, c.Description, c.ShortcutText, () => c.Execute(this), CommandCategory(c.Id)))
            .ToList();
        ShowPalette("Type a command — bold, currency, sort…", items);
    }

    /// <summary>Friendly category name for a command, derived from its id prefix, used as the palette's section header.</summary>
    private static string CommandCategory(string id)
    {
        string prefix = id.Contains('.', StringComparison.Ordinal) ? id[..id.IndexOf('.', StringComparison.Ordinal)] : id;
        return prefix switch
        {
            "file" => "File",
            "edit" => "Edit",
            "format" => "Format",
            "insert" => "Insert",
            "sheet" => "Insert",
            "data" => "Data",
            "formula" => "Formulas",
            "view" => "View",
            "help" => "Help",
            _ => "Other",
        };
    }

    public void ShowSheetSwitcher()
    {
        var items = _session.Workbook.Worksheets
            .Select(s => new PaletteItem(s.Name, "list", "Worksheet", string.Empty, () => { _session.SetActiveSheet(s); BuildSheetTabs(); Grid.Refresh(); OnSelectionChanged(); }, "Sheets"))
            .ToList();
        ShowPalette("Go to sheet", items);
    }

    public void AddSheet()
    {
        _session.AddSheet();
        BuildSheetTabs();
        // The new sheet is empty and active: reset the active cell to A1 and resync the chrome so the
        // formula bar reflects the new empty cell instead of stale text from the previous sheet (#7).
        Selection.MoveTo(0, 0);
        Grid.Refresh();
        OnSelectionChanged();
    }

    public async void RenameActiveSheet()
    {
        var sheet = _session.ActiveSheet;
        string? name = await ShowModalAsync(new Dialogs.TextPromptDialog("Rename sheet", "Name", sheet.Name));
        if (name is null || name == sheet.Name)
        {
            return;
        }

        CommitSheetRename(sheet, name);
    }

    /// <summary>Apply a sheet rename through the validating session path, narrating success or surfacing the error.</summary>
    /// <returns>True when the rename succeeded.</returns>
    private bool CommitSheetRename(Worksheet sheet, string name)
    {
        try
        {
            _session.RenameSheet(sheet, name); // raises StructureChanged → rebuilds tabs/chrome
            OnSelectionChanged();
            Narrate($"Renamed sheet to {name}");
            return true;
        }
        catch (Exception ex)
        {
            ShowMessage("Couldn't rename the sheet", ex.Message);
            return false;
        }
    }

    public async void DeleteActiveSheet()
    {
        if (_session.Workbook.Worksheets.Count <= 1)
        {
            ShowMessage("Can't delete the last sheet", "A workbook must keep at least one worksheet.");
            return;
        }

        var sheet = _session.ActiveSheet;
        bool confirmed = await ShowModalAsync(new ConfirmDialog("Delete sheet?",
            $"Delete “{sheet.Name}” and all its data? This can’t be undone.", "Delete"));
        if (confirmed)
        {
            _session.DeleteSheet(sheet); // raises StructureChanged → rebuilds tabs/chrome
            OnSelectionChanged();
            Narrate($"Deleted sheet {sheet.Name}");
        }
    }

    public async void ResetSheetData()
    {
        var sheet = _session.ActiveSheet;
        var used = sheet.GetUsedRange();
        if (used is null)
        {
            Narrate("Sheet is already empty");
            return; // nothing to clear
        }

        // Confirm only above the §8.7 threshold (>100 non-empty cells); below it, undo covers a mistake.
        int nonEmpty = CountNonEmpty(sheet);
        if (nonEmpty > 100)
        {
            bool confirmed = await ShowModalAsync(new ConfirmDialog("Reset sheet data?",
                $"Clear all data and formatting from “{sheet.Name}” ({nonEmpty} cells)? You can undo this.", "Reset"));
            if (!confirmed)
            {
                return;
            }
        }

        // ClearAll wipes value, formula, style, and hyperlink over the range; ResetSheetLayout adds
        // the sheet-level state ClearAll can't see — row/column fills, hidden flags, and any active
        // AutoFilter/sort — so a reset truly clears background colours and filters. One undoable step.
        _session.Execute(EditCommands.Composite("Reset Sheet Data", new IEditCommand[]
        {
            EditCommands.ClearAll(sheet.Name, used.Value),
            DataEditCommands.ResetSheetLayout(sheet.Name),
        }));
        Selection.MoveTo(0, 0);
        Refresh();
        Narrate($"Reset {sheet.Name}");
    }

    /// <summary>Number of cells on the sheet that hold a value or formula (the §8.7 "non-empty" count).</summary>
    private static int CountNonEmpty(Worksheet sheet)
    {
        int count = 0;
        foreach (var (_, _, cell) in sheet.EnumerateCells())
        {
            if (!cell.Value.IsEmpty || cell.IsFormula)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Build a right-click menu from catalog command ids (null = separator); each row shows its icon, label, and shortcut.</summary>
    private MenuFlyout BuildCommandFlyout(params string?[] ids)
    {
        var flyout = new MenuFlyout();
        foreach (string? id in ids)
        {
            if (id is null)
            {
                flyout.Items.Add(new Separator());
                continue;
            }

            if (AppCommands.ById(id) is { } command)
            {
                flyout.Items.Add(new MenuItem
                {
                    Header = command.Label,
                    InputGesture = command.Shortcut,
                    Icon = command.Icon is null ? null : new LucideIcon { IconKey = command.Icon, Size = 15 },
                    Command = new RelayCommand(() => command.Execute(this), () => command.IsEnabled(this)),
                });
            }
        }

        return flyout;
    }

    private void OnGridContextRequested(object? sender, Avalonia.Input.ContextRequestedEventArgs e)
    {
        var target = e.TryGetPosition(Grid, out var point)
            ? Grid.SelectForContext(point)
            : Controls.SpreadsheetGrid.SpreadsheetGrid.ContextTarget.Cell;

        MenuFlyout? flyout = target switch
        {
            Controls.SpreadsheetGrid.SpreadsheetGrid.ContextTarget.None => null,
            Controls.SpreadsheetGrid.SpreadsheetGrid.ContextTarget.ColumnHeader => BuildCommandFlyout(
                "edit.cut", "edit.copy", "edit.paste", null,
                "insert.columnsLeft", "edit.deleteColumns", null,
                "edit.delete", "edit.clearFormats"),
            Controls.SpreadsheetGrid.SpreadsheetGrid.ContextTarget.RowHeader => BuildCommandFlyout(
                "edit.cut", "edit.copy", "edit.paste", null,
                "insert.rowsAbove", "edit.deleteRows", null,
                "edit.delete", "edit.clearFormats"),
            _ => BuildCommandFlyout(
                "edit.cut", "edit.copy", "edit.paste", null,
                "insert.rowsAbove", "insert.columnsLeft", null,
                "edit.delete", "edit.clearFormats", "edit.clearAll", null,
                "format.cells"),
        };

        if (flyout is null)
        {
            return; // right-clicked the header corner — nothing to offer
        }

        flyout.ShowAt(Grid, showAtPointer: true);
        e.Handled = true;
    }

    public void ZoomBy(double delta)
    {
        Grid.Zoom += delta;
        UpdateChrome();
    }

    public void ResetZoom()
    {
        Grid.Zoom = 1.0;
        UpdateChrome();
    }

    public void ToggleFilter()
    {
        var (sheet, range, _) = AppCommands.DataTarget(this);
        if (sheet.ActiveFilter is not null)
        {
            _session.Execute(DataEditCommands.ClearFilter(sheet.Name, sheet.ActiveFilter.Value));
            sheet.ActiveFilter = null;
            // Also drop the conditions and sort so they don't linger and resurrect when the filter
            // is turned back on.
            sheet.ActiveFilterSpec = null;
            sheet.ActiveSort = null;
        }
        else
        {
            sheet.ActiveFilter = range;
        }

        Refresh();
    }

    public async void ShowSortDialog()
    {
        var (sheet, range, header) = AppCommands.DataTarget(this);
        var spec = await ShowModalAsync(new SortDialog(sheet, range, header));
        if (spec is { Levels.Count: > 0 })
        {
            _session.Execute(DataEditCommands.Sort(sheet.Name, range, spec));
            // Persist the sort so it round-trips like the dropdown quick-sort, and remember the sorted
            // range as the active filter region if one isn't already set.
            sheet.ActiveSort = spec;
            sheet.ActiveFilter ??= range;
            Refresh();
        }
    }

    public async void ShowRemoveDuplicatesDialog()
    {
        var (sheet, range, header) = AppCommands.DataTarget(this);
        var result = await ShowModalAsync(new RemoveDuplicatesDialog(sheet, range, header));
        if (result is not null)
        {
            _session.Execute(DataEditCommands.RemoveDuplicates(sheet.Name, range, result.KeyColumns, result.HasHeader, out var removedCount));
            Refresh();

            int removed = removedCount();
            int remaining = range.RowCount - (result.HasHeader ? 1 : 0) - removed;
            ShowMessage("Remove Duplicates", RemoveDuplicatesSummary(removed, remaining));
        }
    }

    /// <summary>The result line after Remove Duplicates: how many were removed and how many unique rows remain.</summary>
    internal static string RemoveDuplicatesSummary(int removed, int remaining) => removed == 0
        ? $"No duplicates found — all {remaining} {(remaining == 1 ? "row is" : "rows are")} unique."
        : $"Removed {removed} duplicate {(removed == 1 ? "row" : "rows")}; {remaining} unique {(remaining == 1 ? "row remains" : "rows remain")}.";

    public async void ShowTextToColumnsDialog()
    {
        var sheet = _session.ActiveSheet;
        var selection = Selection.Range;
        var source = new CellRange(new CellAddress(selection.Left, selection.Top), new CellAddress(selection.Left, selection.Bottom));
        var delimiter = await ShowModalAsync(new TextToColumnsDialog());
        if (delimiter is { } d)
        {
            // The split writes the extra fields into the columns to the right of the source; warn before
            // clobbering anything already there (PRD §8.14).
            if (HasDataToRight(sheet, source))
            {
                bool confirmed = await ShowModalAsync(new ConfirmDialog(
                    "Overwrite data to the right?",
                    "This may overwrite data to the right. Continue?", "Continue"));
                if (!confirmed)
                {
                    return;
                }
            }

            _session.Execute(DataEditCommands.TextToColumns(sheet, source, d));
            Refresh();
        }
    }

    /// <summary>
    /// True when any cell immediately right of <paramref name="source"/>'s single column — within its row
    /// range — holds a value or formula, so a Text-to-Columns split there would overwrite data (PRD §8.14).
    /// Conservatively scans only the column directly to the right (the first the split would fill).
    /// </summary>
    internal static bool HasDataToRight(Worksheet sheet, CellRange source)
    {
        int rightColumn = source.Left + 1;
        if (rightColumn >= SheetDimensions.MaxColumns)
        {
            return false;
        }

        for (int row = source.Top; row <= source.Bottom; row++)
        {
            var cell = sheet.Find(row, rightColumn);
            if (cell is not null && (!cell.Value.IsEmpty || cell.IsFormula))
            {
                return true;
            }
        }

        return false;
    }

    public async void ShowDataValidationDialog()
    {
        var sheet = _session.ActiveSheet;
        var rule = await ShowModalAsync(new DataValidationDialog(Selection.Range));
        if (rule is not null)
        {
            _session.Execute(DataEditCommands.SetValidation(sheet.Name, rule));
            Refresh();
        }
    }

    public void ShowFindReplace(bool replace) => _ = ShowModalAsync(new FindReplaceDialog(this, replace));

    public void ToggleFreezePanes()
    {
        Grid.ToggleFreezePanes();
        Refresh();
    }

    public void ShowKeyboardShortcuts() => _ = ShowModalAsync(new KeyboardShortcutsDialog());

    public void ShowAbout() => _ = ShowModalAsync(new AboutDialog());

    public void ShowMessage(string heading, string message) => _ = ShowModalAsync(new MessageDialog(heading, message));

    /// <summary>A text field has focus when the focused element is a <see cref="TextBox"/> (cell editor, formula bar, name box, or a dialog field).</summary>
    public bool IsTextInputFocused => FocusManager?.GetFocusedElement() is TextBox;

    /// <summary>The Command Palette / sheet switcher overlay is modal: while it's up, the grid behind it must ignore command shortcuts.</summary>
    public bool IsModalOpen => OverlayHost.IsVisible;

    /// <summary>An opaque surface color for floating chrome (palette box, active tab) that follows the light/dark theme so themed text stays legible.</summary>
    private IBrush ThemedSurface => ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
        ? new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B))
        : Brushes.White;

    public async void ShowFormatCellsDialog()
    {
        var sheet = _session.ActiveSheet;
        var active = sheet.Find(Selection.ActiveRow, Selection.ActiveColumn);
        var current = _session.Workbook.Styles.Get(active?.StyleId ?? Cellestra.Core.Styling.StylePool.DefaultId);

        var transform = await ShowModalAsync(new FormatCellsDialog(current, DistinctSelectionStyles(sheet)));
        if (transform is not null)
        {
            _session.Execute(EditCommands.ApplyStyle(_session.Workbook, sheet.Name, Selection.Range, transform, "Format Cells"));
            Refresh();
        }
    }

    // ---- Insert ▸ Function… / Named Range… / Hyperlink… -------------------

    /// <summary>
    /// Insert Function picker (PRD §4 Formulas). Lists every function from the one
    /// <see cref="Cellestra.Core.Formulas.Functions.FunctionRegistry"/>; choosing one seeds
    /// <c>=NAME(</c> into the active cell and enters edit mode with the caret after the open paren,
    /// ready for arguments — the inline autocomplete/signature help then takes over as usual.
    /// </summary>
    public async void ShowFunctionPicker()
    {
        var meta = await ShowModalAsync(new FunctionPickerDialog());
        if (meta is not null)
        {
            // Seed "=NAME(" so the existing autocomplete/signature help kicks in for the arguments;
            // BeginEdit places the caret at the end of the seed (right after the paren).
            Grid.BeginEdit($"={meta.Name}(");
        }
    }

    /// <summary>
    /// Named Range manager (PRD §4 Formulas). Defaults the Target to the current selection (scoped to
    /// the active sheet), lets the user add/delete names, and refreshes the grid and Name Box if
    /// anything changed (a new/removed name affects formula resolution and the Name Box jump list).
    /// </summary>
    public async void ShowNamedRangeManager()
    {
        var selection = Selection.Range.WithSheet(_session.ActiveSheet.Name);
        bool changed = await ShowModalAsync(new NamedRangeDialog(_session.Workbook, selection));
        if (changed)
        {
            _session.RecalculateAll();
            Refresh();
        }
    }

    /// <summary>
    /// Insert/edit Hyperlink on the active cell (PRD §4 Editing). Sets, edits, or clears
    /// <see cref="Cell.Hyperlink"/> through the undoable <see cref="EditCommands.SetHyperlink"/>; when
    /// the cell is empty and the user supplied display text, that text is set in the same undoable step.
    /// </summary>
    public async void ShowHyperlinkDialog()
    {
        var sheet = _session.ActiveSheet;
        int row = Selection.ActiveRow, column = Selection.ActiveColumn;
        var cell = sheet.Find(row, column);
        var value = sheet.GetValue(row, column);
        bool cellIsEmpty = value.IsEmpty && (cell is null || !cell.IsFormula);
        string displayValue = cellIsEmpty ? string.Empty : value.ToInvariantString();

        var outcome = await ShowModalAsync(new HyperlinkDialog(cell?.Hyperlink, displayValue, cellIsEmpty));
        if (outcome is null)
        {
            return; // cancelled — no change
        }

        var commands = new List<IEditCommand>();
        if (outcome.DisplayText is { } text)
        {
            commands.Add(EditCommands.SetInput(_session.Workbook, sheet.Name, row, column, text, _session.Workbook.Settings.EffectiveLocale));
        }

        commands.Add(EditCommands.SetHyperlink(_session.Workbook, sheet.Name, row, column, outcome.Link));
        _session.Execute(commands.Count == 1 ? commands[0] : EditCommands.Composite("Set Hyperlink", commands));
        Refresh();
    }

    /// <summary>
    /// The distinct styles across the current selection, so the Format Cells dialog can show a mixed
    /// selection as "(varies)" rather than flattening everything to the active cell. Walks only stored
    /// cells (bounded), and adds the default style when the selection also covers blank cells.
    /// </summary>
    private IReadOnlyCollection<Cellestra.Core.Styling.CellStyle> DistinctSelectionStyles(Worksheet sheet)
    {
        var ids = new HashSet<int>();
        long stored = 0;
        foreach (var (row, column, cell) in sheet.EnumerateCells())
        {
            if (Selection.Ranges.Any(r => r.Contains(row, column)))
            {
                ids.Add(cell.StyleId);
                stored++;
            }
        }

        long area = Selection.Ranges.Sum(r => (long)(r.Bottom - r.Top + 1) * (r.Right - r.Left + 1));
        if (area > stored) // the selection includes at least one blank (default-styled) cell
        {
            ids.Add(Cellestra.Core.Styling.StylePool.DefaultId);
        }

        return ids.Select(id => _session.Workbook.Styles.Get(id)).ToList();
    }

    public void ApplyCellStyle(System.Func<Cellestra.Core.Styling.CellStyle, Cellestra.Core.Styling.CellStyle> transform, string label)
    {
        Grid.ClearStylePreview();
        _session.Execute(EditCommands.ApplyStyle(_session.Workbook, _session.ActiveSheet.Name, Selection.Range, transform, label));
        Refresh();
    }

    public void PreviewCellStyle(System.Func<Cellestra.Core.Styling.CellStyle, Cellestra.Core.Styling.CellStyle>? transform)
    {
        if (transform is null)
        {
            Grid.ClearStylePreview();
        }
        else
        {
            Grid.SetStylePreview(Selection.Range, transform);
        }
    }

    public void AutofitColumns(int left, int right) => Grid.GrowColumnsToFit(left, right);

    // ---- Chrome -----------------------------------------------------------

    private void RewireSession()
    {
        _session.Changed += (_, _) => { Grid.Refresh(); UpdateChrome(); };
        _session.StructureChanged += (_, _) => { BuildSheetTabs(); Grid.Refresh(); UpdateChrome(); };
        WireSelectionUndo();
        Grid.Attach(_session);
        BuildSheetTabs();
        UpdateChrome();
        OnSelectionChanged();
    }

    /// <summary>Let the session capture/restore the grid selection so undo returns there (#33).</summary>
    private void WireSelectionUndo()
    {
        _session.SelectionProvider = () => new SelectionMemento(
            Selection.Top, Selection.Left, Selection.Bottom, Selection.Right,
            Selection.ActiveRow, Selection.ActiveColumn);
        _session.SelectionRestorer = m =>
            Selection.Restore(m.Top, m.Left, m.Bottom, m.Right, m.ActiveRow, m.ActiveColumn);
    }

    private void OnSelectionChanged()
    {
        SyncFormulaBar();
        UpdateStatus();
        Narrate($"Selected {new CellAddress(Selection.ActiveColumn, Selection.ActiveRow)}");
        _commandBar?.RefreshState();
    }

    /// <summary>Mirror the active cell into the Name Box and formula input (its value/formula may have changed).</summary>
    private void SyncFormulaBar()
    {
        // Grid.ActiveCellName reports a selected merge as its whole span (e.g. B2:C3); fall back to the
        // single active address for ordinary cells.
        NameBox.Text = Grid.ActiveCellName;
        var cell = _session.ActiveSheet.Find(Selection.ActiveRow, Selection.ActiveColumn);
        _syncingFormulaBar = true; // a programmatic refresh, not the user typing — don't echo into the cell
        FormulaInput.Text = cell is null ? string.Empty : (cell.IsFormula ? "=" + cell.Formula : cell.Value.ToInvariantString());
        _syncingFormulaBar = false;
    }

    // Guards the two-way formula-bar ↔ inline-editor mirror (task 9) against feedback loops.
    private bool _syncingFormulaBar;

    /// <summary>Cell → formula bar: reflect live keystrokes in the inline editor up into the formula input.</summary>
    private void OnInlineEditTextChanged(string text)
    {
        if (_syncingFormulaBar)
        {
            return;
        }

        _syncingFormulaBar = true;
        int caret = FormulaInput.CaretIndex;
        FormulaInput.Text = text;
        FormulaInput.CaretIndex = Math.Min(caret, text.Length);
        _syncingFormulaBar = false;
    }

    /// <summary>Formula bar → cell: mirror typing in the formula input into the inline editor ("vice versa").</summary>
    private void FormulaInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        // Only when the user is actually typing in the bar — not during a programmatic SyncFormulaBar
        // refresh or an echo from the cell side (which would otherwise begin a spurious edit).
        if (_syncingFormulaBar || !FormulaInput.IsFocused)
        {
            return;
        }

        _syncingFormulaBar = true;
        Grid.SetInlineEditorText(FormulaInput.Text ?? string.Empty);
        _syncingFormulaBar = false;
    }

    private void UpdateChrome()
    {
        Title = DocumentTitle();
        StatusZoom.Text = $"{Grid.Zoom * 100:0}%";
        StatusCalc.Text = _session.Workbook.Settings.CalculationMode == CalculationMode.Manual ? "Manual" : "Auto";
        StatusSaved.Text = _session.IsModified ? "Edited"
            : _lastSavedAt is { } saved ? $"Saved {saved:HH:mm}" : "Not saved";
        _commandBar?.RefreshState();
    }

    /// <summary>
    /// The window title: just the document's file name (e.g. <c>Budget.ods</c>, or <c>Untitled</c> for a
    /// new workbook), with a leading ● when there are unsaved changes. No app-name suffix, and transient
    /// errors/status never overwrite it — those surface in the status line instead (#15/#16/#17a).
    /// </summary>
    internal static string ComposeTitle(string? filePath, bool modified)
    {
        string name = filePath is null ? "Untitled" : Path.GetFileName(filePath);
        return $"{(modified ? "● " : string.Empty)}{name}";
    }

    private string DocumentTitle() => ComposeTitle(_session.FilePath, _session.IsModified);

    private void UpdateStatus()
    {
        var sheet = _session.ActiveSheet;
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

    private void BuildSheetTabs()
    {
        SheetTabs.Children.Clear();
        foreach (var sheet in _session.Workbook.Worksheets)
        {
            bool active = ReferenceEquals(sheet, _session.ActiveSheet);
            // Tabs are Borders (not Buttons) so pointer events bubble to the strip for click-to-switch
            // and drag-to-reorder; the active/click/drag logic lives in the SheetTabs* handlers.
            var tab = new Border
            {
                Padding = new Thickness(10, 2),
                Background = active ? ThemedSurface : Brushes.Transparent,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Child = new TextBlock
                {
                    Text = sheet.Name,
                    FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var captured = sheet;
            tab.ContextRequested += (_, e) =>
            {
                // Right-clicking a tab activates it, then offers sheet actions (incl. delete — there's
                // no other way to remove a sheet). Anchor on the stable strip, since activating the
                // sheet rebuilds the tab elements.
                _session.SetActiveSheet(captured);
                Grid.Refresh();
                OnSelectionChanged();
                BuildCommandFlyout("sheet.rename", "insert.sheet", null, "sheet.delete").ShowAt(SheetTabs, showAtPointer: true);
                e.Handled = true;
            };
            // Double-click renames inline — but that's driven from SheetTabsPointerPressed (ClickCount==2),
            // not a per-tab DoubleTapped handler: the first click activates the sheet, which rebuilds the
            // whole strip and orphans this Border before its gesture recognizer could see the second click.
            SheetTabs.Children.Add(tab);
        }

        var add = new Button { Content = "＋", Padding = new Thickness(8, 2) };
        add.Click += (_, _) => AddSheet();
        SheetTabs.Children.Add(add);
    }

    /// <summary>True while an inline sheet-tab rename editor is open, so the strip drag/click logic backs off.</summary>
    private bool _inlineRenaming;

    /// <summary>
    /// Swap a sheet tab's label for an inline editable <see cref="TextBox"/> (task: double-click renames a
    /// sheet inline, like cell editing). Commits on Enter/blur through the validating rename path, cancels
    /// on Esc, and restores the tab either way.
    /// </summary>
    private void BeginInlineSheetRename(Border tab, Worksheet sheet)
    {
        if (_inlineRenaming)
        {
            return;
        }

        // Editing implies focusing this sheet. But SetActiveSheet raises StructureChanged, which runs
        // BuildSheetTabs and rebuilds the strip — orphaning the `tab` we were handed. So switch FIRST
        // (only when needed), then re-find the freshly-built tab Border to host the editor.
        if (!ReferenceEquals(_session.ActiveSheet, sheet))
        {
            _session.SetActiveSheet(sheet); // rebuilds the strip via StructureChanged
            Grid.Refresh();
            OnSelectionChanged();

            int index = _session.Workbook.IndexOf(sheet);
            if (index < 0 || index >= SheetTabs.Children.Count || SheetTabs.Children[index] is not Border rebuilt)
            {
                return; // strip changed out from under us; nothing safe to edit
            }

            tab = rebuilt;
        }

        _inlineRenaming = true;

        var editor = new TextBox
        {
            Text = sheet.Name,
            MinWidth = 60,
            Padding = new Thickness(2, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Avalonia.Automation.AutomationProperties.SetName(editor, "Sheet name");

        bool finished = false;

        void Finish(bool commit)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            _inlineRenaming = false;

            string name = (editor.Text ?? string.Empty).Trim();
            if (commit && name.Length > 0 && name != sheet.Name)
            {
                // A failed rename (duplicate/invalid) surfaces a message; rebuild the strip to drop the editor.
                if (!CommitSheetRename(sheet, name))
                {
                    BuildSheetTabs();
                }

                // Success raises StructureChanged → BuildSheetTabs already ran.
            }
            else
            {
                BuildSheetTabs(); // restore the original label
            }
        }

        editor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Finish(commit: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Finish(commit: false);
                e.Handled = true;
            }
        };
        editor.LostFocus += (_, _) => Finish(commit: true);

        tab.Child = editor;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            editor.Focus();
            editor.SelectAll();
        });
    }

    // ---- Sheet tab click / drag-to-reorder (handled at the strip level) ---

    private int _tabDragFrom = -1;
    private double _tabDragStartX;
    private bool _tabDragging;
    private Worksheet? _tabDragSheet;

    /// <summary>Index of the worksheet tab under <paramref name="point"/> (in strip coordinates), or -1 (e.g. the ＋ button).</summary>
    private int TabIndexAt(Point point)
    {
        int sheets = _session.Workbook.Worksheets.Count;
        for (int i = 0; i < sheets && i < SheetTabs.Children.Count; i++)
        {
            if (SheetTabs.Children[i].Bounds.Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    private void SheetTabsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_inlineRenaming)
        {
            return; // an inline rename editor owns the strip; don't start a drag/click gesture
        }

        if (!e.GetCurrentPoint(SheetTabs).Properties.IsLeftButtonPressed)
        {
            return; // right-click is handled per-tab via ContextRequested
        }

        var point = e.GetPosition(SheetTabs);
        int index = TabIndexAt(point);
        if (index < 0)
        {
            return; // the ＋ button has its own Click
        }

        // Double-click anywhere on a tab → rename it inline (like editing a cell). Detected here, at
        // the strip level, because the first click already rebuilt the strip via activation, so a
        // per-tab DoubleTapped never fires. BeginInlineSheetRename re-finds the live tab by index.
        if (e.ClickCount == 2 && SheetTabs.Children[index] is Border doubleClicked)
        {
            _tabDragFrom = -1; // cancel any pending click/drag gesture from the first press
            BeginInlineSheetRename(doubleClicked, _session.Workbook.Worksheets[index]);
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        // Record the gesture but DON'T activate yet — activating rebuilds the strip, which would
        // invalidate the tab bounds the release needs to compute the drop target. Activation (and any
        // reorder) happens on release, against the still-laid-out tabs.
        _tabDragFrom = index;
        _tabDragSheet = _session.Workbook.Worksheets[index];
        _tabDragStartX = point.X;
        _tabDragging = false;
        e.Pointer.Capture(SheetTabs);
    }

    private void SheetTabsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_tabDragFrom < 0 || !e.GetCurrentPoint(SheetTabs).Properties.IsLeftButtonPressed)
        {
            return;
        }

        double x = e.GetPosition(SheetTabs).X;
        if (!_tabDragging && System.Math.Abs(x - _tabDragStartX) > 5)
        {
            // Past the threshold this is a drag, not a click: lift the grabbed tab and show a grabbing
            // cursor (drag-and-drop best practice — make the grabbed item and drop point obvious).
            _tabDragging = true;
            if (_tabDragFrom < SheetTabs.Children.Count)
            {
                SheetTabs.Children[_tabDragFrom].Opacity = 0.4;
            }

            SetTabCursors(_grabbingCursor);
        }

        if (_tabDragging)
        {
            UpdateTabDropIndicator(x);
        }
    }

    private void SheetTabsPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_tabDragFrom >= 0 && _tabDragSheet is { } dragged)
        {
            if (_tabDragging)
            {
                // Bounds are valid here (no rebuild happened during the gesture).
                int target = TabDropSlot(e.GetPosition(SheetTabs).X);
                if (target > _tabDragFrom)
                {
                    target--; // account for removing the dragged tab before re-inserting
                }

                if (target != _tabDragFrom)
                {
                    _session.MoveSheet(_tabDragFrom, target);
                }
            }

            _session.SetActiveSheet(dragged); // click-to-switch / activate the dragged tab (rebuilds the strip)
            Grid.Refresh();
            OnSelectionChanged();
        }

        EndTabDragVisual(); // clears opacity/cursor/marker; the rebuilt strip already starts clean
        e.Pointer.Capture(null);
    }

    // Reordering is horizontal, so the move cursor reads naturally as "I'm carrying this tab."
    private readonly Avalonia.Input.Cursor _grabbingCursor = new(Avalonia.Input.StandardCursorType.DragMove);
    private readonly Avalonia.Input.Cursor _tabHandCursor = new(Avalonia.Input.StandardCursorType.Hand);

    /// <summary>The insertion slot (0..count) the pointer at <paramref name="x"/> would drop into.</summary>
    private int TabDropSlot(double x)
    {
        int sheets = _session.Workbook.Worksheets.Count;
        int slot = 0;
        for (int i = 0; i < sheets && i < SheetTabs.Children.Count; i++)
        {
            if (x > SheetTabs.Children[i].Bounds.Center.X)
            {
                slot++;
            }
        }

        return slot;
    }

    /// <summary>Place the green insertion marker in the gap where the dragged tab would land.</summary>
    private void UpdateTabDropIndicator(double x)
    {
        int sheets = _session.Workbook.Worksheets.Count;
        if (sheets == 0 || SheetTabs.Children.Count == 0)
        {
            return;
        }

        int slot = TabDropSlot(x);
        double edge = slot == 0
            ? SheetTabs.Children[0].Bounds.Left
            : slot < sheets && slot < SheetTabs.Children.Count
                ? SheetTabs.Children[slot].Bounds.Left
                : SheetTabs.Children[sheets - 1].Bounds.Right + 1;

        TabDropIndicator.Margin = new Thickness(System.Math.Max(0, edge - 1), 0, 0, 0);
        TabDropIndicator.IsVisible = true;
    }

    private void SetTabCursors(Avalonia.Input.Cursor cursor)
    {
        int sheets = _session.Workbook.Worksheets.Count;
        for (int i = 0; i < sheets && i < SheetTabs.Children.Count; i++)
        {
            SheetTabs.Children[i].Cursor = cursor;
        }
    }

    /// <summary>Clear all drag affordances — opacity, cursor, drop marker — and reset the gesture state.</summary>
    private void EndTabDragVisual()
    {
        TabDropIndicator.IsVisible = false;
        if (_tabDragging)
        {
            if (_tabDragFrom >= 0 && _tabDragFrom < SheetTabs.Children.Count)
            {
                SheetTabs.Children[_tabDragFrom].Opacity = 1.0;
            }

            SetTabCursors(_tabHandCursor);
        }

        _tabDragFrom = -1;
        _tabDragSheet = null;
        _tabDragging = false;
    }

    // ---- Formula bar / Name box ------------------------------------------

    private bool _formulaBarExpanded;

    // The expand/collapse chevron, drawn as a crisp Lucide glyph (the old "⌄"/"⌃" text glyphs were
    // clipped by the button's fixed width). Its IconKey flips with _formulaBarExpanded.
    private readonly Controls.Icons.LucideIcon _formulaExpandIcon = new() { IconKey = "chevron-down", Size = 14 };

    /// <summary>
    /// Toggle the formula bar between one line and a taller multi-line area for editing long formulas
    /// (task: formula-bar expand/collapse). Expanded, the input grows to several wrapped lines.
    /// </summary>
    private void ToggleFormulaBarExpanded()
    {
        _formulaBarExpanded = !_formulaBarExpanded;
        if (_formulaBarExpanded)
        {
            FormulaInput.MinHeight = 84;
            FormulaInput.TextWrapping = TextWrapping.Wrap;
            FormulaInput.VerticalContentAlignment = VerticalAlignment.Top;
            _formulaExpandIcon.IconKey = "chevron-up";
            ToolTip.SetTip(FormulaExpandButton, "Collapse formula bar");
        }
        else
        {
            FormulaInput.MinHeight = 0;
            FormulaInput.ClearValue(TextBox.HeightProperty);
            FormulaInput.TextWrapping = TextWrapping.NoWrap;
            FormulaInput.VerticalContentAlignment = VerticalAlignment.Center;
            _formulaExpandIcon.IconKey = "chevron-down";
            ToolTip.SetTip(FormulaExpandButton, "Expand formula bar");
        }
    }

    private void FormulaInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            CommitFormulaBar(moveColumn: 0, moveRow: 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            // Commit-and-move like the inline editor: Tab steps right, Shift+Tab left (task 15a).
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            CommitFormulaBar(moveColumn: shift ? -1 : 1, moveRow: 0);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (Grid.IsInlineEditing)
            {
                Grid.CancelInlineEdit();
            }

            OnSelectionChanged();
            Grid.Focus();
        }
        else if (e.Key == Key.F4)
        {
            var (text, caret) = Cellestra.Core.Formulas.References.ReferenceCycler.Cycle(
                FormulaInput.Text ?? string.Empty, FormulaInput.CaretIndex);
            FormulaInput.Text = text;
            FormulaInput.CaretIndex = caret;
            e.Handled = true;
        }
        else if (e.Key == Key.Delete &&
                 (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control | KeyModifiers.Alt)) == 0)
        {
            // Forward-delete the character (or selection) to the right of the caret. Handled here so the
            // global "Clear Contents" shortcut (also Delete) can't preempt it and wipe the whole cell.
            DeleteForwardInFormulaBar();
            e.Handled = true;
        }
    }

    /// <summary>Delete the selection (if any) or the single character to the right of the caret in the formula input.</summary>
    private void DeleteForwardInFormulaBar()
    {
        string text = FormulaInput.Text ?? string.Empty;
        int start = Math.Min(FormulaInput.SelectionStart, FormulaInput.SelectionEnd);
        int end = Math.Max(FormulaInput.SelectionStart, FormulaInput.SelectionEnd);

        if (start != end)
        {
            FormulaInput.Text = text.Remove(start, end - start);
            FormulaInput.CaretIndex = start;
        }
        else if (start < text.Length)
        {
            FormulaInput.Text = text.Remove(start, 1);
            FormulaInput.CaretIndex = start;
        }
    }

    /// <summary>
    /// Commit what's in the formula bar through the grid's one validated path (so data validation runs
    /// either way), then move the selection. Whether or not a mirrored inline edit is already open, we go
    /// through <see cref="SpreadsheetGrid.SpreadsheetGrid.CommitInlineEdit"/> — when none is open we first
    /// push the bar's text in as an edit so the same validate-then-SetInput lifecycle applies (task 15b).
    /// </summary>
    private void CommitFormulaBar(int moveColumn, int moveRow)
    {
        if (!Grid.IsInlineEditing)
        {
            // Mirror the bar's text into a (focus-less) inline edit so the commit runs validation rather
            // than calling EditCommands.SetInput directly and bypassing it.
            Grid.SetInlineEditorText(FormulaInput.Text ?? string.Empty);
        }

        Grid.CommitInlineEdit(moveRow, moveColumn);
        Grid.Focus();
    }

    /// <summary>
    /// Clicking away from the formula bar (to a non-grid surface) leaves a mirrored inline edit open and
    /// uncommitted; commit it on blur so the typed value isn't silently lost (task 15).
    /// </summary>
    private void FormulaInputLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Collapse any selection/caret so the bar doesn't keep showing a highlighted I-beam once focus
        // returns to a cell — Avalonia leaves the selection brush drawn on an unfocused TextBox (#5).
        FormulaInput.SelectionStart = FormulaInput.SelectionEnd = 0;
        FormulaInput.CaretIndex = 0;

        if (_syncingFormulaBar)
        {
            return; // a programmatic refresh moved focus, not the user committing
        }

        if (Grid.IsInlineEditing)
        {
            Grid.CommitInlineEdit(0, 0); // commit in place, no selection move
        }
    }

    private void NameBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Return)
        {
            return;
        }

        string text = NameBox.Text ?? string.Empty;
        if (CellAddress.TryParse(text, out var address))
        {
            Selection.MoveTo(address.Row, address.Column);
            JumpFromNameBox();
        }
        else if (_session.Workbook.FindNamedRange(text) is { } named)
        {
            Selection.SetRange(named.Target.Top, named.Target.Left, named.Target.Bottom, named.Target.Right);
            JumpFromNameBox();
        }
        else
        {
            // Unrecognized input: don't silently swallow it — tell the user, and restore the box to the
            // current address so it doesn't keep showing the bad text.
            Narrate($"No cell or named range “{text.Trim()}”");
            SyncFormulaBar();
        }

        e.Handled = true;
    }

    /// <summary>Finish a Name Box jump: scroll the new selection into view, refresh the chrome, focus the grid.</summary>
    private void JumpFromNameBox()
    {
        RevealActiveCell();
        OnSelectionChanged();
        Refresh();
        Grid.Focus();
    }

    // ---- Clipboard --------------------------------------------------------

    private async void CopyToClipboard(bool cut)
    {
        var sheet = _session.ActiveSheet;
        var range = Selection.Range;
        var culture = _session.Workbook.Settings.EffectiveLocale;
        var sb = new StringBuilder();

        for (int row = range.Top; row <= range.Bottom; row++)
        {
            for (int column = range.Left; column <= range.Right; column++)
            {
                if (column > range.Left)
                {
                    sb.Append('\t');
                }

                var cell = sheet.Find(row, column);
                if (cell is not null && !cell.Value.IsEmpty)
                {
                    sb.Append(NumberFormatter.Format(cell.Value, _session.Workbook.Styles.Get(cell.StyleId).NumberFormat, culture));
                }
            }

            sb.Append('\n');
        }

        string tsv = sb.ToString();
        _clipboard = ClipboardBlock.Capture(sheet, range, cut);
        _clipboardText = tsv;
        Grid.ShowMarquee(range, cut, sheet.Name);

        if (Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(tsv);
        }

        // A cut no longer clears immediately — it is a deferred move that its first paste performs,
        // so an unused cut (Esc / new copy) leaves the data in place.
    }

    private void ClearClipboard()
    {
        _clipboard = null;
        _clipboardText = null;
        Grid.ShowMarquee(null, false, null);
    }

    /// <summary>Return with a marquee active pastes once at the active cell and consumes the clipboard (#3).</summary>
    private void EnterPaste()
    {
        if (_clipboard is { } block)
        {
            var command = new PasteBlockCommand(_session.ActiveSheet.Name, Selection.Range, block);
            if (!Grid.TryEnforceValidation(command)) // reject the whole paste on a Stop-rule violation (#B)
            {
                return;
            }

            _session.Execute(command);
            ClearClipboard();
            Grid.Refresh();
        }
    }

    private void PasteTsv(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
        int rows = lines.Length;
        int columns = lines.Max(l => l.Split('\t').Length);
        var values = new CellValue[rows, columns];
        var culture = _session.Workbook.Settings.EffectiveLocale;

        for (int r = 0; r < rows; r++)
        {
            var fields = lines[r].Split('\t');
            for (int c = 0; c < columns; c++)
            {
                string field = c < fields.Length ? fields[c] : string.Empty;
                values[r, c] = field.Length == 0
                    ? CellValue.Empty
                    : (double.TryParse(field, NumberStyles.Any, culture, out double n) ? CellValue.Number(n) : CellValue.Text(field));
            }
        }

        // Reject the whole paste if any landed value violates a Stop rule (#B). TSV values are never
        // formulas, so every projected write is a literal value.
        int top = Selection.ActiveRow;
        int left = Selection.ActiveColumn;
        var incoming = new List<(int, int, CellValue, bool)>(rows * columns);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                incoming.Add((top + r, left + c, values[r, c], false));
            }
        }

        if (!Grid.TryEnforceValidation(incoming))
        {
            return;
        }

        _session.Execute(EditCommands.SetBlock(_session.ActiveSheet.Name, top, left, values));
        Grid.Refresh();
    }

    // ---- Key bindings -----------------------------------------------------

    private void RegisterKeyBindings()
    {
        foreach (var command in AppCommands.All)
        {
            if (command.Shortcut is null)
            {
                continue;
            }

            var captured = command;
            KeyBindings.Add(new KeyBinding
            {
                Gesture = command.Shortcut,
                // While a modal overlay (Command Palette / sheet switcher) is open, the grid behind it
                // must not react to command shortcuts — only the overlay's own keys apply. So every
                // global binding backs off, letting the focused overlay field handle the key (e.g. ⌘A
                // selects the palette text, not all cells). Likewise, a plain-key gesture (no ⌘/⌃, e.g.
                // Delete) must not fire its global cell command while a text input is focused — there it
                // is ordinary in-textbox editing, not "clear the cell" (#8).
                Command = new RelayCommand(
                    () => captured.Execute(this),
                    () => !IsModalOpen && !BlockedByTextInput(captured.Shortcut) && captured.IsEnabled(this)),
            });
        }
    }

    /// <summary>
    /// True when a global binding must yield to a focused text input (#8): the gesture is a plain key
    /// (no ⌘/⌃) — like Delete or Backspace — and a <see cref="TextBox"/> (cell editor, formula bar, name
    /// box, dialog field) holds focus, so the key does ordinary in-textbox editing instead of firing the
    /// cell command (e.g. ClearContents). Modifier shortcuts (⌘C, ⌘V, ⌘Z…) still apply, and the textbox's
    /// own handlers take precedence over them anyway.
    /// </summary>
    internal static bool ShouldYieldToTextInput(KeyGesture? gesture, bool textInputFocused)
    {
        if (gesture is null || !textInputFocused)
        {
            return false;
        }

        return (gesture.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) == 0;
    }

    // An active inline edit counts the same as a focused text field: while a cell is being edited
    // (in the cell or mirrored from the formula bar), a plain key like Delete is in-editor text
    // editing, not "clear the cell". This explicit flag is steadier than focus detection alone and,
    // because the gate lives in CanExecute, it makes the binding/native-menu hotkey inert so the key
    // flows through to the editor — Avalonia evaluates these gestures before the focused element ever
    // sees the KeyDown, so e.Handled in the editor can't claw it back after the fact.
    private bool BlockedByTextInput(KeyGesture? gesture) =>
        ShouldYieldToTextInput(gesture, IsTextInputFocused || Grid.IsInlineEditing);

    // ---- Palette overlay --------------------------------------------------

    /// <summary>
    /// One palette row. A normal item carries an action; a <see cref="IsHeader"/> row is a non-selectable
    /// category divider (PRD §6.7 screenshot showed results grouped by category, not a flat list).
    /// </summary>
    private sealed record PaletteItem(string Label, string? Icon, string Description, string Shortcut, Action Run, string Category = "", bool IsHeader = false)
    {
        public static PaletteItem Header(string category) =>
            new(category, null, string.Empty, string.Empty, static () => { }, category, IsHeader: true);
    }

    private static readonly IBrush PaletteMuted = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

    private void ShowPalette(string placeholder, IReadOnlyList<PaletteItem> items)
    {
        OverlayHost.Children.Clear();
        _activeModal = null; // a palette isn't a result-returning modal; clear any stale handle

        var input = new TextBox { PlaceholderText = placeholder, Margin = new Thickness(0, 0, 0, 6) };
        var list = new ListBox
        {
            MaxHeight = 340,
            ItemTemplate = new FuncDataTemplate<PaletteItem>((item, _) => BuildPaletteRow(item)),
        };

        void Populate(string query)
        {
            // Score and rank, then divide the results under their category section headers (File, Edit,
            // Format, Data, View…) instead of one flat list. Within a category, keep the fuzzy order.
            var matches = items
                .Where(i => !i.IsHeader)
                .Select(i => (Item: i, Score: FuzzyMatcher.Score(i.Label, query)))
                .Where(x => x.Score >= 0)
                .OrderByDescending(x => x.Score)
                .ToList();

            var grouped = new List<PaletteItem>();
            foreach (var group in matches.GroupBy(x => x.Item.Category))
            {
                if (!string.IsNullOrEmpty(group.Key))
                {
                    grouped.Add(PaletteItem.Header(group.Key));
                }

                grouped.AddRange(group.Select(x => x.Item));
            }

            list.ItemsSource = grouped;
            list.SelectedIndex = FirstSelectableIndex(grouped, 0, +1);
        }

        Populate(string.Empty);

        void Run()
        {
            if (list.SelectedItem is PaletteItem { IsHeader: false } chosen)
            {
                CloseOverlay();
                chosen.Run();
            }
        }

        // Step over category-header rows so arrow keys land only on runnable commands.
        void Move(int direction)
        {
            if (list.ItemsSource is not IReadOnlyList<PaletteItem> rows || rows.Count == 0)
            {
                return;
            }

            int next = FirstSelectableIndex(rows, list.SelectedIndex + direction, direction);
            if (next >= 0)
            {
                list.SelectedIndex = next;
            }
        }

        input.TextChanged += (_, _) => Populate(input.Text ?? string.Empty);
        input.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape: CloseOverlay(); break;
                case Key.Return: Run(); break;
                case Key.Down: Move(+1); break;
                case Key.Up: Move(-1); break;
            }
        };
        list.DoubleTapped += (_, _) => Run();

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
            Child = new StackPanel { Children = { input, list } },
        };

        OverlayHost.Children.Add(box);
        OverlayHost.IsVisible = true;
        SetBackgroundBlur(true); // blur the grid behind the palette too (consistent with internal modals)
        OverlayHost.PointerPressed += OverlayBackgroundPressed;
        input.Focus();
    }

    /// <summary>The first selectable (non-header) row index at or after <paramref name="start"/> stepping by <paramref name="step"/>, or -1.</summary>
    private static int FirstSelectableIndex(IReadOnlyList<PaletteItem> rows, int start, int step)
    {
        for (int i = Math.Max(0, start); i >= 0 && i < rows.Count; i += step)
        {
            if (!rows[i].IsHeader)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>One palette row: a category divider, or an icon + command name + description + shortcut (PRD §6.7).</summary>
    private static Control BuildPaletteRow(PaletteItem? item)
    {
        // The ListBox recycles containers while scrolling and may build the template with a null
        // item; render nothing rather than dereferencing it.
        if (item is null)
        {
            return new Control();
        }

        if (item.IsHeader)
        {
            // A non-interactive category section header (e.g. "Format"), dividing the results.
            return new TextBlock
            {
                Text = item.Label.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = PaletteMuted,
                Margin = new Thickness(2, 8, 0, 2),
            };
        }

        var dock = new DockPanel { LastChildFill = true };

        if (!string.IsNullOrEmpty(item.Shortcut))
        {
            var shortcut = new TextBlock
            {
                Text = item.Shortcut,
                Foreground = PaletteMuted,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            DockPanel.SetDock(shortcut, Dock.Right);
            dock.Children.Add(shortcut);
        }

        if (!string.IsNullOrEmpty(item.Icon))
        {
            var icon = new LucideIcon { IconKey = item.Icon, Size = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 11, 0) };
            DockPanel.SetDock(icon, Dock.Left);
            dock.Children.Add(icon);
        }

        dock.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = item.Label, FontWeight = FontWeight.Medium },
                new TextBlock { Text = item.Description, Foreground = PaletteMuted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis },
            },
        });

        return dock;
    }

    private void OverlayBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, OverlayHost))
        {
            return;
        }

        // Clicking the dimmed backdrop dismisses: cancel an internal modal (so its awaiter resolves
        // to the cancel result), or just close a palette-style overlay.
        if (_activeModal is { } modal)
        {
            modal.CancelFromHost();
        }
        else
        {
            CloseOverlay();
        }
    }

    private void CloseOverlay()
    {
        OverlayHost.PointerPressed -= OverlayBackgroundPressed;
        OverlayHost.Children.Clear();
        OverlayHost.IsVisible = false;
        _activeModal = null;
        SetBackgroundBlur(false);
        Grid.Focus();
    }

    // ---- Internal modal host ---------------------------------------------

    /// <summary>The currently shown internal modal dialog, if any (so its backdrop click cancels it).</summary>
    private Overlays.IModalDialog? _activeModal;

    /// <summary>
    /// Blur the grid/chrome behind a modal (PRD task: "add blur to the background above any modal").
    /// Applied to <see cref="ContentRoot"/> only, so the overlay's own content stays sharp.
    /// </summary>
    private void SetBackgroundBlur(bool on) =>
        ContentRoot.Effect = on ? new BlurEffect { Radius = 8 } : null;

    /// <summary>
    /// Host an internal modal dialog over the (blurred) grid, like the command palette, and await its
    /// result (PRD task: internal modals instead of separate OS windows). Esc / backdrop cancel.
    /// </summary>
    private Task<TResult?> ShowModalAsync<TResult>(Overlays.ModalDialog<TResult> dialog)
    {
        OverlayHost.Children.Clear();
        _activeModal = dialog;

        var chrome = BuildModalChrome(dialog, dialog.Title);
        OverlayHost.Children.Add(chrome);
        OverlayHost.IsVisible = true;
        SetBackgroundBlur(true);
        OverlayHost.PointerPressed += OverlayBackgroundPressed;

        dialog.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeModal, dialog))
            {
                CloseOverlay();
            }
        };

        // Focus the dialog so Esc reaches it even before an inner field claims focus.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => dialog.Focus());
        return dialog.Result;
    }

    /// <summary>Wrap a modal dialog's body in the same floating "card" chrome the palette uses (title + shadow).</summary>
    private Border BuildModalChrome(Control body, string title)
    {
        var stack = new StackPanel { Spacing = 10 };
        if (!string.IsNullOrEmpty(title))
        {
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.SemiBold,
                FontSize = 14,
            });
        }

        stack.Children.Add(body);

        return new Border
        {
            MaxWidth = 560,
            MaxHeight = 640,
            Background = ThemedSurface,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, OffsetY = 4, Color = Color.FromArgb(0x40, 0, 0, 0) }),
            Child = stack,
        };
    }

    private static readonly FilePickerFileType OdsFileType = new("OpenDocument Spreadsheet")
    {
        Patterns = new[] { "*.ods" },
        MimeTypes = new[] { "application/vnd.oasis.opendocument.spreadsheet" },
    };

    private static readonly FilePickerFileType CsvFileType = new("CSV")
    {
        Patterns = new[] { "*.csv" },
        MimeTypes = new[] { "text/csv" },
    };
}

/// <summary>
/// The "you have unsaved changes" prompt (PRD §8.7): Save / Don't Save / Cancel, rendered as an
/// internal modal like the other dialogs. Esc and a backdrop click both resolve to
/// <see cref="MainWindow.SaveChoice.Cancel"/> — the safe, non-destructive default.
/// </summary>
internal sealed class SaveChangesDialog : Overlays.ModalDialog<MainWindow.SaveChoice>
{
    public SaveChangesDialog(string documentName)
    {
        Title = "Unsaved changes";

        var save = new Button { Content = "Save", IsDefault = true };
        save.Click += (_, _) => Close(MainWindow.SaveChoice.Save);
        var dontSave = new Button { Content = "Don’t Save" };
        dontSave.Click += (_, _) => Close(MainWindow.SaveChoice.DontSave);
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(MainWindow.SaveChoice.Cancel);

        DialogContent = new StackPanel
        {
            Width = 380,
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = $"Do you want to save the changes you made to “{documentName}”? Your changes will be lost if you don’t save them.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { dontSave, cancel, save },
                },
            },
        };
    }

    // Esc / backdrop click must mean "stay" (don't lose data), not the enum's default of Save.
    protected override MainWindow.SaveChoice CancelResult => MainWindow.SaveChoice.Cancel;
}
