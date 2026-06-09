using System;
using System.Threading.Tasks;
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

    Task NewWorkbookAsync();

    Task OpenWorkbookAsync();

    Task SaveWorkbookAsync();

    Task SaveWorkbookAsAsync();

    Task ImportCsvAsync();

    Task ExportCsvAsync();

    void Copy();

    void Cut();

    void Paste();

    void ShowCommandPalette();

    void ShowSheetSwitcher();

    void AddSheet();

    /// <summary>Rename the active worksheet (prompts for the new name).</summary>
    void RenameActiveSheet();

    /// <summary>Delete the active worksheet (confirms first; refuses to delete the last one).</summary>
    void DeleteActiveSheet();

    void ZoomBy(double delta);

    void ResetZoom();

    void ToggleFilter();

    void ShowSortDialog();

    void ShowRemoveDuplicatesDialog();

    void ShowTextToColumnsDialog();

    void ShowDataValidationDialog();

    void ShowFindReplace(bool replace);

    void ShowFormatCellsDialog();

    /// <summary>Apply a style transform to the selection as one undoable edit (used by the bar's drawers).</summary>
    void ApplyCellStyle(Func<CellStyle, CellStyle> transform, string label);

    /// <summary>Preview a style transform over the selection without committing it; null clears the preview.</summary>
    void PreviewCellStyle(Func<CellStyle, CellStyle>? transform);

    /// <summary>Grow columns <paramref name="left"/>…<paramref name="right"/> to fit their content if they clip (never shrink).</summary>
    void AutofitColumns(int left, int right);

    void ToggleFreezePanes();

    /// <summary>Display the keyboard shortcuts reference.</summary>
    void ShowKeyboardShortcuts();

    /// <summary>Show the About screen (app name, pronunciation, description, version, license).</summary>
    void ShowAbout();

    /// <summary>Show a non-fatal message dialog: a plain-language heading and what to do about it.</summary>
    void ShowMessage(string heading, string message);

    /// <summary>True when a text field (cell editor, formula bar, name box, or dialog) holds keyboard focus.</summary>
    bool IsTextInputFocused { get; }

    /// <summary>True while a modal overlay (Command Palette or sheet switcher) is open over the grid.</summary>
    bool IsModalOpen { get; }
}
