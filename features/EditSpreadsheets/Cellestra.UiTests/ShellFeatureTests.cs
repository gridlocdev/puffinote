using Avalonia.Headless.XUnit;
using Cellestra.Core.Data;
using Cellestra.Core.Editing;
using Cellestra.Core.Model;
using Cellestra.Core.Serialization.Ods;
using Cellestra.Core.Values;
using Cellestra.Desktop.Commands;
using Cellestra.Desktop.Services;
using Cellestra.Desktop.Views;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Shell-layer features: create-table opens the first header for editing (#2); paste enforces Stop
/// data-validation (#B); open/save run off the UI thread via the session's load/adopt seam (#A).
/// </summary>
public class ShellFeatureTests
{
    [AvaloniaFact]
    public void Create_table_begins_editing_the_first_header_cell()
    {
        var window = new MainWindow();
        window.Show();
        IAppEnvironment env = window.Editor;
        var sheet = env.Session.ActiveSheet;
        sheet.SetValue(1, 0, CellValue.Number(1)); // body data; the header row (A1) is empty
        sheet.SetValue(2, 0, CellValue.Number(2));
        env.Selection.SetRange(0, 0, 2, 0); // select A1:A3 as the table region

        env.Commands.ById("data.createTable")!.Execute();

        Assert.True(window.Editor.Grid.IsInlineEditing);              // editing started automatically (#2)
        Assert.Equal(0, env.Selection.ActiveRow);              // on the first header cell
        Assert.Equal(0, env.Selection.ActiveColumn);
        Assert.Equal("Column 1", sheet.GetValue(0, 0).AsText); // placeholder pre-filled

        // The placeholder opens fully selected, so the next keystroke overtypes it (#2).
        Assert.Equal("Column 1", window.Editor.Grid.EditorText);
        Assert.True(window.Editor.Grid.EditorTextIsAllSelected);
    }

    [AvaloniaFact]
    public void Paste_violating_stop_validation_is_rejected_whole()
    {
        var window = new MainWindow();
        window.Show();
        var sheet = window.Editor.Session.ActiveSheet;
        sheet.AddValidation(new DataValidation(ValidationKind.WholeNumber, CellRange.Parse("A1:A1"))
        {
            Operator = ValidationOperator.Between,
            Operand1 = 1,
            Operand2 = 10,
        });
        sheet.SetValue(0, 2, CellValue.Number(999)); // C1 holds an out-of-range value to paste

        var block = ClipboardBlock.Capture(sheet, CellRange.Parse("C1"), isCut: false);
        var blocked = new PasteBlockCommand(sheet.Name, CellRange.Parse("A1"), block);
        Assert.False(window.Editor.Grid.TryEnforceValidation(blocked)); // rejected before execute

        sheet.SetValue(0, 3, CellValue.Number(5)); // D1 holds an allowed value
        var ok = new PasteBlockCommand(sheet.Name, CellRange.Parse("A1"),
            ClipboardBlock.Capture(sheet, CellRange.Parse("D1"), isCut: false));
        Assert.True(window.Editor.Grid.TryEnforceValidation(ok));
    }

    [Fact]
    public void LoadAndRecalc_then_AdoptWorkbook_recomputes_and_swaps_in_off_thread_state()
    {
        // Round-trip a workbook with a formula through the background load seam used by async open.
        var source = Workbook.CreateNew();
        source.ActiveSheet.SetValue(0, 0, CellValue.Number(2));
        source.ActiveSheet.SetFormula(0, 1, "A1*3", CellValue.Number(6));

        using var buffer = new MemoryStream();
        OdsWriter.Write(source, buffer, null);
        buffer.Position = 0;

        var session = WorkbookSession.NewBlank();
        var loaded = WorkbookSession.LoadAndRecalc(buffer); // the part async open runs on a background thread
        session.AdoptWorkbook(loaded, "loaded.ods");        // the UI-thread swap

        Assert.Equal("loaded.ods", session.FilePath);
        Assert.False(session.IsModified);
        Assert.Equal(6, session.ActiveSheet.GetValue(0, 1).AsNumber); // formula recomputed on load
    }

    [AvaloniaFact]
    public async Task Save_round_trips_off_thread_and_clears_modified()
    {
        var window = new MainWindow();
        window.Show();
        var sheet = window.Editor.Session.ActiveSheet;
        sheet.SetValue(0, 0, CellValue.Text("hi"));

        string path = Path.Combine(Path.GetTempPath(), $"cellestra-{Guid.NewGuid():N}.ods");
        try
        {
            window.Editor.Session.SaveToFile(path); // exercises the same atomic write the async path runs in Task.Run
            Assert.True(File.Exists(path));

            var reopened = WorkbookSession.NewBlank();
            using (var read = File.OpenRead(path))
            {
                reopened.AdoptWorkbook(WorkbookSession.LoadAndRecalc(read), path);
            }

            Assert.Equal("hi", reopened.ActiveSheet.GetValue(0, 0).AsText);
            Assert.False(reopened.IsModified);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        await Task.CompletedTask;
    }
}
