using Cellestra.Core.Styling;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Services;

namespace Cellestra.Desktop.Commands;

/// <summary>
/// The UI operations a command may invoke. Implemented by the main window so the
/// <see cref="AppCommands"/> catalog stays free of view references and remains testable.
/// </summary>
public interface IAppEnvironment
{
    /// <summary>
    /// The one command catalog, built once against this environment (proposal 0001 §5). Every surface —
    /// menu bar, command bar, Command Palette, key bindings — reads its env-bound <see cref="AppCommand"/>s
    /// from here, so they can never drift apart.
    /// </summary>
    AppCommands Commands { get; }

    WorkbookSession Session { get; }

    GridSelection Selection { get; }

    /// <summary>Re-render the grid and the chrome after a model change.</summary>
    void Refresh();

    /// <summary>Scroll the active cell into view (e.g. after jumping to a Find result).</summary>
    void RevealActiveCell();

    /// <summary>Show a brief status-line message narrating an action (cleared on the next selection change).</summary>
    void Narrate(string message);

    /// <summary>Begin inline editing of the active cell.</summary>
    void BeginEdit();

    /// <summary>
    /// Begin inline editing of the active cell with its existing content selected, so the next key
    /// overtypes it (used when a new table's first header cell opens for naming).
    /// </summary>
    void BeginEditSelectAll();

    Task NewWorkbookAsync();

    Task OpenWorkbookAsync();

    Task SaveWorkbookAsync();

    Task SaveWorkbookAsAsync();

    Task ImportCsvAsync();

    Task ExportCsvAsync();

    void Copy();

    void Cut();

    void Paste();

    /// <summary>Offer Paste Special (values / formats / formulas / all) and paste the chosen facet.</summary>
    void PasteSpecial();

    void ShowCommandPalette();

    void ShowSheetSwitcher();

    void AddSheet();

    /// <summary>Add a new chart sheet, seeding its data from the current selection when it looks tabular.</summary>
    void AddChartSheet();

    /// <summary>Rename the active worksheet (prompts for the new name).</summary>
    void RenameActiveSheet();

    /// <summary>Insert a copy of the active worksheet (data or chart sheet) right after it, and select it.</summary>
    void DuplicateActiveSheet();

    /// <summary>Delete the active worksheet (confirms first; refuses to delete the last one).</summary>
    void DeleteActiveSheet();

    /// <summary>Clear every cell and its formatting on the active sheet (confirms above the §8.7 threshold; undoable).</summary>
    void ResetSheetData();

    /// <summary>
    /// The grid's zoom factor, on the family-wide <see cref="Puffinote.Components.Theming.ZoomScale"/> ladder.
    /// Setting it clamps to the ladder's bounds and repaints the chrome that reports it.
    /// </summary>
    double Zoom { get; set; }

    /// <summary>
    /// How much of the adaptive command bar shows (PRD §6.3). A command sets it, the same way the bar's own
    /// chevrons do — which is what lets the palette and the menu bar reach the hidden state, where there is
    /// no chevron left to click.
    /// </summary>
    Puffinote.Components.Controls.CommandBar.CommandBarVisibility CommandBarVisibility { get; set; }

    void ToggleFilter();

    void ShowSortDialog();

    /// <summary>Open the unified Sort &amp; Filter dialog over the active data region.</summary>
    void ShowSortFilterDialog();

    void ShowRemoveDuplicatesDialog();

    void ShowTextToColumnsDialog();

    void ShowDataValidationDialog();

    /// <summary>Toggle whether the table under the active cell styles its first column as a header column.</summary>
    void ToggleTableHeaderColumn();

    /// <summary>The table the active cell sits in, or null when it isn't inside one (for menu state).</summary>
    Cellestra.Core.Data.Table? ActiveTable { get; }

    void ShowFindReplace(bool replace);

    void ShowFormatCellsDialog();

    // ---- Insert menu (function picker / named ranges / hyperlinks) ---------

    /// <summary>Open the Insert Function picker; choosing a function seeds <c>=NAME(</c> and enters edit mode.</summary>
    void ShowFunctionPicker();

    /// <summary>Open the Named Range manager (create a name for the selection, list, and delete names).</summary>
    void ShowNamedRangeManager();

    /// <summary>Open the Insert/Edit Hyperlink dialog for the active cell (set, change, or remove its link).</summary>
    void ShowHyperlinkDialog();

    /// <summary>Apply a style transform to the selection as one undoable edit (used by the bar's drawers).</summary>
    void ApplyCellStyle(Func<CellStyle, CellStyle> transform, string label);

    /// <summary>Preview a style transform over the selection without committing it; null clears the preview.</summary>
    void PreviewCellStyle(Func<CellStyle, CellStyle>? transform);

    /// <summary>Grow columns <paramref name="left"/>…<paramref name="right"/> to fit their content if they clip (never shrink).</summary>
    void AutofitColumns(int left, int right);

    /// <summary>Size columns <paramref name="left"/>…<paramref name="right"/> to their content exactly — narrowing as well as widening.</summary>
    void FitColumns(int left, int right);

    void ToggleFreezePanes();

    /// <summary>Display the keyboard shortcuts reference.</summary>
    void ShowKeyboardShortcuts();

    /// <summary>Open the radial command menu's settings (segment count, labels, and per-segment bindings).</summary>
    void ShowRadialSettings();

    /// <summary>Open Preferences — the options the host keeps in Cellestra's settings file.</summary>
    void ShowPreferences();

    /// <summary>Show the About screen (app name, pronunciation, description, version, license).</summary>
    void ShowAbout();

    /// <summary>Show a non-fatal message dialog: a plain-language heading and what to do about it.</summary>
    void ShowMessage(string heading, string message);

    /// <summary>True when a text field (cell editor, formula bar, name box, or dialog) holds keyboard focus.</summary>
    bool IsTextInputFocused { get; }

    /// <summary>True while a modal overlay (Command Palette or sheet switcher) is open over the grid.</summary>
    bool IsModalOpen { get; }
}
