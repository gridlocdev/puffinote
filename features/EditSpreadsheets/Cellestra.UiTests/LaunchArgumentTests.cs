using Cellestra.Desktop;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>
/// Launching with a document — the double-click / "Open With" path for the `.ods` association the
/// macOS bundle claims. macOS passes the document as the first argument; anything that isn't an
/// existing file must leave the app on a blank workbook rather than erroring before it has painted.
/// </summary>
public class LaunchArgumentTests
{
    [Fact]
    public void A_file_passed_on_the_command_line_is_the_document_to_open()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cellestra-launch-{Guid.NewGuid():N}.ods");
        File.WriteAllBytes(path, []);
        try
        {
            Assert.Equal(path, App.DocumentToOpen([path]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Only_the_first_argument_is_opened()
    {
        string first = Path.Combine(Path.GetTempPath(), $"cellestra-launch-{Guid.NewGuid():N}.ods");
        string second = Path.Combine(Path.GetTempPath(), $"cellestra-launch-{Guid.NewGuid():N}.ods");
        File.WriteAllBytes(first, []);
        File.WriteAllBytes(second, []);
        try
        {
            Assert.Equal(first, App.DocumentToOpen([first, second]));
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public void No_arguments_means_a_blank_workbook()
    {
        Assert.Null(App.DocumentToOpen([]));
        Assert.Null(App.DocumentToOpen(null));
    }

    [Fact]
    public void A_path_that_does_not_exist_is_ignored()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"cellestra-missing-{Guid.NewGuid():N}.ods");
        Assert.Null(App.DocumentToOpen([missing]));
    }

    [Fact]
    public void A_directory_is_ignored()
    {
        // Cellestra edits a file, not a folder — dropping a directory on it must not try to load one.
        Assert.Null(App.DocumentToOpen([Path.GetTempPath()]));
    }
}
