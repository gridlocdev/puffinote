using Typage.Engine.Editing;
using Xunit;

namespace Typage.Engine.Tests.Editing;

/// <summary>
/// The editing-assist decisions (PRD §7.2): parsing a line's leading structure, choosing what
/// Enter does (continue a list/quote, exit an empty item, auto-indent), and resequencing ordered
/// lists. All headless — the surface is a thin caller over these.
/// </summary>
public class LinePrefixTests
{
    [Theory]
    [InlineData("- item", ListKind.Unordered, '-')]
    [InlineData("* item", ListKind.Unordered, '*')]
    [InlineData("+ item", ListKind.Unordered, '+')]
    public void Parse_UnorderedMarker(string line, ListKind kind, char bullet)
    {
        var p = LinePrefix.Parse(line);
        Assert.Equal(kind, p.List);
        Assert.Equal(bullet, p.Bullet);
        Assert.Equal(2, p.Length);
    }

    [Fact]
    public void Parse_OrderedMarker()
    {
        var p = LinePrefix.Parse("12. text");
        Assert.Equal(ListKind.Ordered, p.List);
        Assert.Equal(12, p.Number);
        Assert.Equal('.', p.Delim);
        Assert.Equal(2, p.DigitCount);
        Assert.Equal(4, p.Length);
    }

    [Fact]
    public void Parse_TaskMarker()
    {
        var p = LinePrefix.Parse("- [x] done");
        Assert.True(p.HasTask);
        Assert.True(p.TaskChecked);
        Assert.Equal(6, p.Length); // "- [x] "
    }

    [Fact]
    public void Parse_BlockquoteAndIndent()
    {
        var p = LinePrefix.Parse("  > > quote");
        Assert.Equal(2, p.Indent);
        Assert.Equal("> > ", p.Quote);
        Assert.False(p.HasList);
    }

    [Fact]
    public void ContinuationPrefix_IncrementsOrdered_AndUnchecksTask()
    {
        Assert.Equal("- ", LinePrefix.Parse("- a").ContinuationPrefix());
        Assert.Equal("3. ", LinePrefix.Parse("2. a").ContinuationPrefix());
        Assert.Equal("- [ ] ", LinePrefix.Parse("- [x] a").ContinuationPrefix());
        Assert.Equal("  > ", LinePrefix.Parse("  > a").ContinuationPrefix());
    }

    [Fact]
    public void ComputeEnter_ContinuesList()
    {
        var r = LinePrefix.ComputeEnter("- apple", caretColumn: 7);
        Assert.Equal(EnterKind.Continue, r.Kind);
        Assert.Equal("\n- ", r.Text);
    }

    [Fact]
    public void ComputeEnter_ExitsEmptyItem()
    {
        var r = LinePrefix.ComputeEnter("- ", caretColumn: 2);
        Assert.Equal(EnterKind.Exit, r.Kind);
        Assert.Equal(0, r.ExitStart);
        Assert.Equal(2, r.ExitLength);
    }

    [Fact]
    public void ComputeEnter_ExitEmptyNestedItem_KeepsOuterQuote()
    {
        var r = LinePrefix.ComputeEnter("> - ", caretColumn: 4);
        Assert.Equal(EnterKind.Exit, r.Kind);
        Assert.Equal(2, r.ExitStart); // drops just the "- ", leaving "> "
        Assert.Equal(2, r.ExitLength);
    }

    [Fact]
    public void ComputeEnter_AutoIndentsPlainLine()
    {
        var r = LinePrefix.ComputeEnter("    code", caretColumn: 8);
        Assert.Equal(EnterKind.Continue, r.Kind);
        Assert.Equal("\n    ", r.Text);
    }

    [Fact]
    public void ComputeEnter_PlainNewlineWhenNoStructure()
    {
        var r = LinePrefix.ComputeEnter("hello", caretColumn: 5);
        Assert.Equal(EnterKind.Plain, r.Kind);
        Assert.Equal("\n", r.Text);
    }

    [Fact]
    public void ComputeEnter_SplittingKeepsContinuationPrefix()
    {
        // Caret in the middle of an item splits it; the tail moves onto a new item.
        var r = LinePrefix.ComputeEnter("- abcdef", caretColumn: 4);
        Assert.Equal(EnterKind.Continue, r.Kind);
        Assert.Equal("\n- ", r.Text);
    }

    [Theory]
    [InlineData("- task", 0)]        // left of a bullet
    [InlineData("- [ ] task", 0)]    // left of a checkbox
    [InlineData("1. task", 0)]       // left of an ordered marker
    [InlineData("  - task", 2)]      // left of a nested bullet (caret at the marker, past the indent)
    public void ComputeEnter_LeftOfAMarker_DoesNotDuplicateIt(string line, int caretColumn)
    {
        // Enter left of a non-empty item's content must not splice another marker in — it's a plain newline.
        var r = LinePrefix.ComputeEnter(line, caretColumn);
        Assert.Equal(EnterKind.Plain, r.Kind);
        Assert.Equal("\n", r.Text);
    }

    [Fact]
    public void Renumber_ResequencesFollowingItems()
    {
        var lines = new[] { "1. a", "1. b", "1. c" };
        var result = OrderedListRenumber.Compute(lines, 0);
        Assert.NotNull(result);
        Assert.Equal(["1. a", "2. b", "3. c"], result!.Value.Lines);
        Assert.Equal(0, result.Value.StartLine);
        Assert.Equal(3, result.Value.EndLineExclusive);
    }

    [Fact]
    public void Renumber_StartsFromGivenLineNumber()
    {
        // Insert "3." between 2 and the old 3; renumber downward from the inserted item.
        var lines = new[] { "1. a", "2. b", "3. inserted", "3. c", "4. d" };
        var result = OrderedListRenumber.Compute(lines, 2);
        Assert.NotNull(result);
        Assert.Equal(["3. inserted", "4. c", "5. d"], result!.Value.Lines);
        Assert.Equal(2, result.Value.StartLine);
    }

    [Fact]
    public void Renumber_StopsAtDifferentLevelOrNonList()
    {
        var lines = new[] { "1. a", "2. b", "text", "1. other" };
        var result = OrderedListRenumber.Compute(lines, 0);
        Assert.Null(result); // already sequential within the run [0,2)
    }

    [Fact]
    public void Renumber_ReturnsNullWhenAlreadySequential()
    {
        var lines = new[] { "1. a", "2. b", "3. c" };
        Assert.Null(OrderedListRenumber.Compute(lines, 0));
    }
}
