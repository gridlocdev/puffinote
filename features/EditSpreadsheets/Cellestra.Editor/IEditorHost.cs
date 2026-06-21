using System.Threading.Tasks;

namespace Cellestra.Editor;

/// <summary>
/// The file/document-shell operations a <see cref="SpreadsheetEditorView"/> delegates to its embedder.
/// The standalone <c>Cellestra.Desktop</c> window implements these with native file dialogs, a document
/// lock, and a recent-files menu; a host application (e.g. a notes-app shell driving the editor from a
/// file tree) implements them against its own document model. These are the only members the editor
/// hands outward — everything else (editing, sheets, formatting, dialogs) lives in the view itself.
/// </summary>
public interface IEditorHost
{
    /// <summary>Start a new, empty workbook (prompting to save the current one if it's modified).</summary>
    Task NewWorkbookAsync();

    /// <summary>Open a workbook the user chooses (file picker in the standalone app).</summary>
    Task OpenWorkbookAsync();

    /// <summary>Save the current document, prompting for a location when it has none.</summary>
    Task SaveWorkbookAsync();

    /// <summary>Save the current document to a new location chosen by the user.</summary>
    Task SaveWorkbookAsAsync();

    /// <summary>Import a CSV file as a new workbook.</summary>
    Task ImportCsvAsync();

    /// <summary>Export the active sheet to a CSV file.</summary>
    Task ExportCsvAsync();
}
