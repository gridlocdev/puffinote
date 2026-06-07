using System;
using System.IO;
using System.Linq;
using Cellestra.Core.Values;
using Cellestra.Desktop.Services;
using Xunit;

namespace Cellestra.UiTests;

public class AtomicSaveTests
{
    [Fact]
    public void SaveToFile_round_trips_and_leaves_no_temp_file()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cellestra-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "book.ods");
            var session = WorkbookSession.NewBlank();
            session.ActiveSheet.SetValue(0, 0, CellValue.Number(42));

            session.SaveToFile(path);

            Assert.True(File.Exists(path));
            Assert.Equal(path, session.FilePath);
            Assert.False(session.IsModified);
            Assert.Empty(TempFiles(dir));

            using var read = File.OpenRead(path);
            var reopened = WorkbookSession.NewBlank();
            reopened.LoadFrom(read, path);
            Assert.Equal(42, reopened.ActiveSheet.GetValue(0, 0).AsNumber);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SaveToFile_preserves_the_original_and_cleans_up_when_the_rename_fails()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cellestra-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // The target path is an existing *directory*, so the final rename throws.
            string target = Path.Combine(dir, "target");
            Directory.CreateDirectory(target);

            var session = WorkbookSession.NewBlank();
            Assert.ThrowsAny<Exception>(() => session.SaveToFile(target));

            Assert.True(Directory.Exists(target)); // original untouched
            Assert.Empty(TempFiles(dir));          // temp file cleaned up
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string[] TempFiles(string dir) =>
        Directory.GetFiles(dir).Where(f => Path.GetFileName(f).Contains(".tmp", StringComparison.Ordinal)).ToArray();
}
