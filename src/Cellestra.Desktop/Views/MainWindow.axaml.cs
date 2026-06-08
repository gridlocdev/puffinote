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

    // The rich in-app clipboard (values + formulas + styles) and the exact text we mirrored to the
    // OS clipboard; an in-app paste uses the block only while the OS clipboard still matches.
    private ClipboardBlock? _clipboard;
    private string? _clipboardText;

    private Controls.CommandBar.CommandBar? _commandBar;

    public MainWindow()
    {
        InitializeComponent();

        _settings = _settingsService.Load();
        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;

        Grid.Attach(_session);
        Grid.SelectionChanged += (_, _) => OnSelectionChanged();
        Grid.ClipboardInvalidated += (_, _) => ClearClipboard();
        Grid.EnterPasteRequested += (_, _) => EnterPaste();
        WireSelectionUndo();
        _session.Changed += (_, _) => { Grid.Refresh(); UpdateChrome(); };
        _session.StructureChanged += (_, _) => { BuildSheetTabs(); Grid.Refresh(); UpdateChrome(); };

        FormulaInput.KeyDown += FormulaInputKeyDown;
        NameBox.KeyDown += NameBoxKeyDown;
        FxButton.Click += (_, _) => BeginEdit();

        RegisterKeyBindings();
        BuildNativeMenu();
        BuildSheetTabs();

        Grid.Zoom = _settings.Zoom;

        _commandBar = new Controls.CommandBar.CommandBar(this) { Collapsed = _settings.CommandBarCollapsed };
        _commandBar.CollapsedChanged += (_, _) => PersistSettings();
        CommandBarHost.Content = _commandBar;

        Closing += (_, _) =>
        {
            PersistSettings();
            ReleaseFileLock();
        };

        UpdateChrome();
        OnSelectionChanged();
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
        BuildNativeMenu(); // refresh the Open Recent submenu
    }

    /// <summary>The native macOS menu bar — the complete command list (PRD §6.2), all from the one catalog.</summary>
    private void BuildNativeMenu()
    {
        var menu = new NativeMenu();
        var file = Submenu("File", "file.new", "file.open", "file.save", "file.saveAs", null, "file.importCsv", "file.exportCsv");
        file.Menu!.Items.Insert(2, BuildOpenRecent());
        menu.Items.Add(file);
        menu.Items.Add(Submenu("Edit", "edit.undo", "edit.redo", null, "edit.cut", "edit.copy", "edit.paste", "edit.delete", "edit.clearFormats", "edit.clearAll", "edit.selectAll", null, "edit.find", "edit.findReplace"));
        menu.Items.Add(Submenu("Format", "format.cells", null, "format.bold", "format.italic", "format.underline", null,
            "format.alignLeft", "format.alignCenter", "format.alignRight", "format.wrap", null,
            "format.currency", "format.percent", "format.date", null, "format.merge"));
        menu.Items.Add(Submenu("Insert", "insert.rowsAbove", "insert.columnsLeft", "insert.sheet"));
        menu.Items.Add(Submenu("Data", "data.sortAsc", "data.sortDesc", "data.customSort", null,
            "data.toggleFilter", "data.clearFilter", null, "data.createTable", "data.removeDuplicates",
            "data.textToColumns", "data.validation", null, "formula.recalc"));
        menu.Items.Add(Submenu("View", "view.commandPalette", "view.goToSheet", null, "view.zoomIn", "view.zoomOut", "view.actualSize", null, "view.freezePanes"));
        menu.Items.Add(Submenu("Help", "help.shortcuts"));
        NativeMenu.SetMenu(this, menu);
    }

    private NativeMenuItem BuildOpenRecent()
    {
        var submenu = new NativeMenu();
        if (_settings.RecentFiles.Count == 0)
        {
            submenu.Items.Add(new NativeMenuItem("(none)") { IsEnabled = false });
        }
        else
        {
            foreach (string path in _settings.RecentFiles)
            {
                string captured = path;
                submenu.Items.Add(new NativeMenuItem(Path.GetFileName(path))
                {
                    Command = new RelayCommand(() => _ = OpenPathAsync(captured)),
                });
            }
        }

        return new NativeMenuItem("Open Recent") { Menu = submenu };
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
                    Command = new RelayCommand(() => command.Execute(this), () => command.IsEnabled(this)),
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
        UpdateChrome();
    }

    public void BeginEdit() => Grid.BeginEdit(null);

    /// <summary>Narrate a bar action in the status line; <see cref="UpdateStatus"/> restores the selection stats on the next move.</summary>
    public void Narrate(string message) => StatusStats.Text = message;

    public Task NewWorkbookAsync()
    {
        ReleaseFileLock();
        _session = WorkbookSession.NewBlank();
        RewireSession();
        return Task.CompletedTask;
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
        try
        {
            var existingLock = FileLock.Inspect(path);

            await using (var stream = File.OpenRead(path))
            {
                _session.LoadFrom(stream, path);
            }

            ReleaseFileLock();
            _fileLock = FileLock.TryAcquire(path);

            Grid.Attach(_session);
            BuildSheetTabs();
            RememberRecentFile(path);
            UpdateChrome();

            if (existingLock is not null)
            {
                string who = string.IsNullOrWhiteSpace(existingLock.User) ? "another application" : existingLock.User;
                string host = string.IsNullOrWhiteSpace(existingLock.Host) ? string.Empty : $" on {existingLock.Host}";
                Title = $"Cellestra — {Path.GetFileName(path)} (open in {who}{host}; changes may conflict)";
            }
            else if (_session.LoadRecalcMismatches > 0)
            {
                Title = $"Cellestra — {Path.GetFileName(path)} ({_session.LoadRecalcMismatches} cell(s) recomputed differently than the saved file)";
            }
        }
        catch (Exception ex)
        {
            Title = $"Cellestra — could not open: {ex.Message}";
        }
    }

    /// <summary>Remove our document lock, if we hold one.</summary>
    private void ReleaseFileLock()
    {
        _fileLock?.Dispose();
        _fileLock = null;
    }

    public async Task SaveWorkbookAsync()
    {
        if (_session.FilePath is null)
        {
            await SaveWorkbookAsAsync();
            return;
        }

        _session.SaveToFile(_session.FilePath);
        RememberRecentFile(_session.FilePath);
        UpdateChrome();
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
        _session.SaveToFile(path);

        // We now own this file on disk — take its lock so other editors are warned.
        if (_fileLock?.DocumentPath != Path.GetFullPath(path))
        {
            ReleaseFileLock();
            _fileLock = FileLock.TryAcquire(path);
        }

        RememberRecentFile(path);
        UpdateChrome();
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

        string text = await File.ReadAllTextAsync(files[0].Path.LocalPath);
        var workbook = Cellestra.Core.Serialization.Csv.CsvReader.ReadToWorkbook(text);
        ReleaseFileLock();
        _session = new WorkbookSession(workbook);
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
            _session.Execute(command);
            if (block.IsCut)
            {
                ClearClipboard(); // a cut is a move, consumed by its first paste
            }

            if (command.ShapeMismatch)
            {
                Title = "Cellestra — paste area is a different shape than the copied cells";
            }

            Grid.Refresh();
            return;
        }

        if (!string.IsNullOrEmpty(text))
        {
            PasteTsv(text);
        }
    }

    public void ShowCommandPalette()
    {
        var items = AppCommands.All
            .Where(c => c.IsEnabled(this))
            .Select(c => new PaletteItem(c.Label, c.Icon, c.Description, c.ShortcutText, () => c.Execute(this)))
            .ToList();
        ShowPalette("Type a command — bold, currency, sort…", items);
    }

    public void ShowSheetSwitcher()
    {
        var items = _session.Workbook.Worksheets
            .Select(s => new PaletteItem(s.Name, "list", "Worksheet", string.Empty, () => { _session.SetActiveSheet(s); BuildSheetTabs(); Grid.Refresh(); }))
            .ToList();
        ShowPalette("Go to sheet", items);
    }

    public void AddSheet()
    {
        _session.AddSheet();
        BuildSheetTabs();
        Grid.Refresh();
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
        var spec = await new SortDialog(sheet, range, header).ShowDialog<SortSpec?>(this);
        if (spec is { Levels.Count: > 0 })
        {
            _session.Execute(DataEditCommands.Sort(sheet.Name, range, spec));
            Refresh();
        }
    }

    public async void ShowRemoveDuplicatesDialog()
    {
        var (sheet, range, header) = AppCommands.DataTarget(this);
        var result = await new RemoveDuplicatesDialog(sheet, range, header).ShowDialog<RemoveDuplicatesDialog.Result?>(this);
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
        var delimiter = await new TextToColumnsDialog().ShowDialog<char?>(this);
        if (delimiter is { } d)
        {
            _session.Execute(DataEditCommands.TextToColumns(sheet, source, d));
            Refresh();
        }
    }

    public async void ShowDataValidationDialog()
    {
        var sheet = _session.ActiveSheet;
        var rule = await new DataValidationDialog(Selection.Range).ShowDialog<DataValidation?>(this);
        if (rule is not null)
        {
            _session.Execute(DataEditCommands.SetValidation(sheet.Name, rule));
            Refresh();
        }
    }

    private FindReplaceDialog? _findReplace;

    public void ShowFindReplace(bool replace)
    {
        if (_findReplace is not null)
        {
            _findReplace.Activate();
            return;
        }

        _findReplace = new FindReplaceDialog(this, replace);
        _findReplace.Closed += (_, _) => _findReplace = null;
        _findReplace.Show(this);
    }

    public void ToggleFreezePanes()
    {
        Grid.ToggleFreezePanes();
        Refresh();
    }

    public void ShowKeyboardShortcuts() => _ = new KeyboardShortcutsDialog().ShowDialog(this);

    public void ShowMessage(string heading, string message) => _ = new MessageDialog(heading, message).ShowDialog(this);

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

        var transform = await new FormatCellsDialog(current).ShowDialog<System.Func<Cellestra.Core.Styling.CellStyle, Cellestra.Core.Styling.CellStyle>?>(this);
        if (transform is not null)
        {
            _session.Execute(EditCommands.ApplyStyle(_session.Workbook, sheet.Name, Selection.Range, transform, "Format Cells"));
            Refresh();
        }
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
        var address = new CellAddress(Selection.ActiveColumn, Selection.ActiveRow);
        NameBox.Text = address.ToString();

        var cell = _session.ActiveSheet.Find(Selection.ActiveRow, Selection.ActiveColumn);
        FormulaInput.Text = cell is null ? string.Empty : (cell.IsFormula ? "=" + cell.Formula : cell.Value.ToInvariantString());

        UpdateStatus();
        _commandBar?.RefreshState();
    }

    private void UpdateChrome()
    {
        string name = _session.FilePath is null ? "Untitled" : Path.GetFileName(_session.FilePath);
        Title = $"{(_session.IsModified ? "● " : string.Empty)}{name} — Cellestra";
        StatusZoom.Text = $"{Grid.Zoom * 100:0}%";
        StatusCalc.Text = _session.Workbook.Settings.CalculationMode == CalculationMode.Manual ? "Manual" : "Auto";
        _commandBar?.RefreshState();
    }

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
            var button = new Button
            {
                Content = sheet.Name,
                Padding = new Thickness(10, 2),
                Background = active ? ThemedSurface : Brushes.Transparent,
                FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal,
            };
            var captured = sheet;
            button.Click += (_, _) => { _session.SetActiveSheet(captured); BuildSheetTabs(); Grid.Refresh(); OnSelectionChanged(); };
            SheetTabs.Children.Add(button);
        }

        var add = new Button { Content = "＋", Padding = new Thickness(8, 2) };
        add.Click += (_, _) => AddSheet();
        SheetTabs.Children.Add(add);
    }

    // ---- Formula bar / Name box ------------------------------------------

    private void FormulaInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            _session.Execute(EditCommands.SetInput(
                _session.Workbook, _session.ActiveSheet.Name, Selection.ActiveRow, Selection.ActiveColumn,
                FormulaInput.Text ?? string.Empty, _session.Workbook.Settings.EffectiveLocale));
            Selection.MoveTo(Selection.ActiveRow + 1, Selection.ActiveColumn);
            Grid.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
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
            Grid.Focus();
        }
        else if (_session.Workbook.FindNamedRange(text) is { } named)
        {
            Selection.SetRange(named.Target.Top, named.Target.Left, named.Target.Bottom, named.Target.Right);
            Grid.Focus();
        }

        e.Handled = true;
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
            _session.Execute(new PasteBlockCommand(_session.ActiveSheet.Name, Selection.Range, block));
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

        _session.Execute(EditCommands.SetBlock(_session.ActiveSheet.Name, Selection.ActiveRow, Selection.ActiveColumn, values));
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
                // selects the palette text, not all cells).
                Command = new RelayCommand(() => captured.Execute(this), () => !IsModalOpen && captured.IsEnabled(this)),
            });
        }
    }

    // ---- Palette overlay --------------------------------------------------

    private sealed record PaletteItem(string Label, string? Icon, string Description, string Shortcut, Action Run);

    private static readonly IBrush PaletteMuted = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

    private void ShowPalette(string placeholder, IReadOnlyList<PaletteItem> items)
    {
        OverlayHost.Children.Clear();

        var input = new TextBox { PlaceholderText = placeholder, Margin = new Thickness(0, 0, 0, 6) };
        var list = new ListBox
        {
            MaxHeight = 340,
            ItemTemplate = new FuncDataTemplate<PaletteItem>((item, _) => BuildPaletteRow(item)),
        };

        void Populate(string query)
        {
            var filtered = items
                .Select(i => (Item: i, Score: FuzzyMatcher.Score(i.Label, query)))
                .Where(x => x.Score >= 0)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Item)
                .ToList();
            list.ItemsSource = filtered;
            if (filtered.Count > 0)
            {
                list.SelectedIndex = 0;
            }
        }

        Populate(string.Empty);

        void Run()
        {
            if (list.SelectedItem is PaletteItem chosen)
            {
                CloseOverlay();
                chosen.Run();
            }
        }

        input.TextChanged += (_, _) => Populate(input.Text ?? string.Empty);
        input.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape: CloseOverlay(); break;
                case Key.Return: Run(); break;
                case Key.Down: list.SelectedIndex = Math.Min(list.SelectedIndex + 1, (list.ItemCount) - 1); break;
                case Key.Up: list.SelectedIndex = Math.Max(list.SelectedIndex - 1, 0); break;
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
        OverlayHost.PointerPressed += OverlayBackgroundPressed;
        input.Focus();
    }

    /// <summary>One palette row: optional icon, the command name, its description beneath, and the shortcut (PRD §6.7).</summary>
    private static Control BuildPaletteRow(PaletteItem? item)
    {
        // The ListBox recycles containers while scrolling and may build the template with a null
        // item; render nothing rather than dereferencing it.
        if (item is null)
        {
            return new Control();
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
        if (ReferenceEquals(e.Source, OverlayHost))
        {
            CloseOverlay();
        }
    }

    private void CloseOverlay()
    {
        OverlayHost.PointerPressed -= OverlayBackgroundPressed;
        OverlayHost.Children.Clear();
        OverlayHost.IsVisible = false;
        Grid.Focus();
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
