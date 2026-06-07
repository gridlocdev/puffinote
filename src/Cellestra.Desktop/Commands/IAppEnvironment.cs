using System.Threading.Tasks;
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

    void ToggleRibbon();

    void ShowCommandPalette();

    void ShowSheetSwitcher();

    void AddSheet();

    void ZoomBy(double delta);

    void ResetZoom();

    void ToggleFilter();

    void ShowSortDialog();

    void ShowRemoveDuplicatesDialog();

    void ShowTextToColumnsDialog();

    void ShowDataValidationDialog();

    void ShowFindReplace(bool replace);

    void ShowFormatCellsDialog();
}
