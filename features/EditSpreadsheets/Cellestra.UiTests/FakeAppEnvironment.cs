using Cellestra.Core.Data;
using Cellestra.Core.Styling;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Controls.SpreadsheetGrid;
using Cellestra.Desktop.Services;
using Puffinote.Components.Theming;

namespace Cellestra.UiTests;

/// <summary>
/// A no-op <see cref="IAppEnvironment"/> for <b>metadata-only</b> catalog tests. Building
/// <c>new AppCommands(new FakeAppEnvironment())</c> only captures this environment in each command's
/// closures — it is never invoked — so the catalog's env-independent metadata (id, title, description,
/// icon, shortcut) can be asserted without standing up a whole <c>MainWindow</c>. Any member that a test
/// actually calls would throw, which is the point: reach for a real window (<c>window.Editor</c>) for
/// behaviour tests, and this for pure metadata.
/// </summary>
internal sealed class FakeAppEnvironment : IAppEnvironment
{
    private static NotSupportedException Nope([System.Runtime.CompilerServices.CallerMemberName] string? member = null) =>
        new($"FakeAppEnvironment is metadata-only; '{member}' must not be invoked (use a real MainWindow for behaviour).");

    public AppCommands Commands => throw Nope();
    public WorkbookSession Session => throw Nope();
    public GridSelection Selection => throw Nope();
    public Table? ActiveTable => throw Nope();
    public bool IsTextInputFocused => throw Nope();
    public bool IsModalOpen => throw Nope();

    public void Refresh() => throw Nope();
    public void RevealActiveCell() => throw Nope();
    public void Narrate(string message) => throw Nope();
    public void BeginEdit() => throw Nope();
    public void BeginEditSelectAll() => throw Nope();
    public Task NewWorkbookAsync() => throw Nope();
    public Task OpenWorkbookAsync() => throw Nope();
    public Task SaveWorkbookAsync() => throw Nope();
    public Task SaveWorkbookAsAsync() => throw Nope();
    public Task ImportCsvAsync() => throw Nope();
    public Task ExportCsvAsync() => throw Nope();
    public void Copy() => throw Nope();
    public void Cut() => throw Nope();
    public void Paste() => throw Nope();
    public void PasteSpecial() => throw Nope();
    public void ShowCommandPalette() => throw Nope();
    public void ShowSheetSwitcher() => throw Nope();
    public void AddSheet() => throw Nope();
    public void AddChartSheet() => throw Nope();
    public void RenameActiveSheet() => throw Nope();
    public void DuplicateActiveSheet() => throw Nope();
    public void DeleteActiveSheet() => throw Nope();
    public void ResetSheetData() => throw Nope();
    public double Zoom { get; set; } = ZoomScale.Default;
    public Puffinote.Components.Controls.CommandBar.CommandBarVisibility CommandBarVisibility { get; set; }
    public void ToggleFilter() => throw Nope();
    public void ShowSortDialog() => throw Nope();
    public void ShowSortFilterDialog() => throw Nope();
    public void ShowRemoveDuplicatesDialog() => throw Nope();
    public void ShowTextToColumnsDialog() => throw Nope();
    public void ShowDataValidationDialog() => throw Nope();
    public void ToggleTableHeaderColumn() => throw Nope();
    public void ShowFindReplace(bool replace) => throw Nope();
    public void ShowFormatCellsDialog() => throw Nope();
    public void ShowRowHeightDialog() => throw Nope();
    public void ShowColumnWidthDialog() => throw Nope();
    public void ShowFunctionPicker() => throw Nope();
    public void ShowNamedRangeManager() => throw Nope();
    public void ShowHyperlinkDialog() => throw Nope();
    public void ApplyCellStyle(Func<CellStyle, CellStyle> transform, string label) => throw Nope();
    public void PreviewCellStyle(Func<CellStyle, CellStyle>? transform) => throw Nope();
    public void AutofitColumns(int left, int right) => throw Nope();

    public void FitColumns(int left, int right) => throw Nope();
    public void ToggleFreezePanes() => throw Nope();
    public void RunCleanUp(bool wholeWorkbook) => throw Nope();
    public void ShowKeyboardShortcuts() => throw Nope();
    public void ShowRadialSettings() => throw Nope();
    public void ShowPreferences() => throw Nope();
    public void ShowAbout() => throw Nope();
    public void ShowMessage(string heading, string message) => throw Nope();

    /// <summary>A ready-made catalog bound to a fake environment — for metadata-only assertions.</summary>
    public static AppCommands Catalog { get; } = new(new FakeAppEnvironment());
}
