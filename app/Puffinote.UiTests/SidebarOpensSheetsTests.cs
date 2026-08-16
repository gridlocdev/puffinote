using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cellestra.Desktop.Services;
using Cellestra.Editor;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Xunit;

namespace Puffinote.UiTests;

/// <summary>
/// Clicking a spreadsheet in the sidebar opens <em>the spreadsheet editor</em> — end to end, from the row
/// under the pointer through the tree, the sidebar, the shell's open, the factory's routing and Cellestra's
/// own load, in both panels that show the tree. The pieces were each covered on their own (the factory routes
/// <c>.ods</c> to a <c>SheetPane</c>; a click activates a row); the chain between them was not, which is
/// precisely where a sidebar change can break opening a file without a single existing test noticing.
/// </summary>
public sealed class SidebarOpensSheetsTests
{
    private static void Click(Window window, string name)
    {
        var row = Row(window, name);
        var centre = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static ListBoxItem Row(Window window, string name) =>
        window.GetVisualDescendants()
            .OfType<ListBoxItem>()
            .Single(i => i.GetVisualDescendants().OfType<Control>().Any(c => AutomationProperties.GetName(c) == name));

    [AvaloniaFact]
    public async Task Clicking_an_ods_row_opens_the_sheet_editor()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-ods-click");
        try
        {
            var ods = Path.Combine(dir.FullName, "book.ods");
            File.WriteAllBytes(ods, WorkbookSession.NewBlank().SerializeToBytes());

            var window = new MainWindow(new NotebookService()) { Width = 900, Height = 600 };
            window.Show();
            window.OpenNotebook(dir.FullName);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var row = Row(window, "book.ods");
            var centre = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            // The open is fire-and-forget from the click handler; give it a turn to finish.
            await Task.Delay(200);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ods, window.ActiveTabPathForTest);
            Assert.IsType<SpreadsheetEditorView>(window.ActiveEditorViewForTest);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Clicking_an_ods_in_a_subfolder_opens_the_sheet_editor()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-ods-sub");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir.FullName, "Books"));
            var ods = Path.Combine(dir.FullName, "Books", "book.ods");
            File.WriteAllBytes(ods, WorkbookSession.NewBlank().SerializeToBytes());

            var window = new MainWindow(new NotebookService()) { Width = 900, Height = 600 };
            window.Show();
            window.OpenNotebook(dir.FullName);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // Open the folder, then click the sheet inside it.
            Click(window, "Books");
            Click(window, "book.ods");
            await Task.Delay(200);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ods, window.ActiveTabPathForTest);
            Assert.IsType<SpreadsheetEditorView>(window.ActiveEditorViewForTest);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Clicking_an_ods_found_in_the_Filter_panel_opens_the_sheet_editor()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-ods-filter");
        try
        {
            var ods = Path.Combine(dir.FullName, "book.ods");
            File.WriteAllBytes(ods, WorkbookSession.NewBlank().SerializeToBytes());

            var window = new MainWindow(new NotebookService()) { Width = 900, Height = 600 };
            window.Show();
            window.OpenNotebook(dir.FullName);
            Dispatcher.UIThread.RunJobs();

            window.SidebarForTest.Activate("filter");
            var panel = window.SidebarForTest.FilterPanelForTest;
            panel.QueryForTest = "ods";
            await panel.ApplyFilterNowForTest();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Click(window, "book.ods");
            await Task.Delay(200);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ods, window.ActiveTabPathForTest);
            Assert.IsType<SpreadsheetEditorView>(window.ActiveEditorViewForTest);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Opening_an_ods_directly_opens_the_sheet_editor()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-ods-direct");
        try
        {
            var ods = Path.Combine(dir.FullName, "book.ods");
            File.WriteAllBytes(ods, WorkbookSession.NewBlank().SerializeToBytes());

            var window = new MainWindow(new NotebookService());
            window.Show();
            window.OpenNotebook(dir.FullName);
            await window.OpenFileForTest(ods);

            Assert.Equal(ods, window.ActiveTabPathForTest);
            Assert.IsType<SpreadsheetEditorView>(window.ActiveEditorViewForTest);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
