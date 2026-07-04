using Cellestra.Editor;
using Xunit;

namespace Cellestra.UiTests;

/// <summary>The Remove Duplicates result message reports counts with correct singular/plural wording.</summary>
public class RemoveDuplicatesSummaryTests
{
    [Fact]
    public void Reports_removed_and_remaining_counts()
    {
        Assert.Equal("Removed 3 duplicate rows; 5 unique rows remain.",
            SpreadsheetEditorView.RemoveDuplicatesSummary(removed: 3, remaining: 5));
    }

    [Fact]
    public void Uses_singular_wording_for_one()
    {
        Assert.Equal("Removed 1 duplicate row; 1 unique row remains.",
            SpreadsheetEditorView.RemoveDuplicatesSummary(removed: 1, remaining: 1));
    }

    [Fact]
    public void Reports_when_nothing_was_removed()
    {
        Assert.Equal("No duplicates found — all 4 rows are unique.",
            SpreadsheetEditorView.RemoveDuplicatesSummary(removed: 0, remaining: 4));
    }
}
