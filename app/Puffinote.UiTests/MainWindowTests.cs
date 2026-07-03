using Avalonia.Headless.XUnit;
using Puffinote.Desktop.Shell;
using Puffinote.Desktop.Views;
using Xunit;

namespace Puffinote.UiTests;

public sealed class MainWindowTests
{
    [AvaloniaFact]
    public void Window_shows_and_opening_a_notebook_updates_the_title()
    {
        var dir = Directory.CreateTempSubdirectory("puffinote-uitest");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "notes.md"), "# hello");
            Directory.CreateDirectory(Path.Combine(dir.FullName, "sub"));

            var window = new MainWindow(new NotebookService());
            window.Show(); // exercises the full layout/render pass under null drawing

            Assert.Equal("Puffinote", window.Title);

            window.OpenNotebook(dir.FullName);

            Assert.Equal($"Puffinote — {dir.Name}", window.Title);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
