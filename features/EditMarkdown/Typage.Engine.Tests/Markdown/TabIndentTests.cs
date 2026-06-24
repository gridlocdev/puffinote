using Typage.Engine.Editing;
using Typage.Engine.Markdown;
using Xunit;
using static Typage.Engine.Tests.Markdown.TokenizerTestHelpers;

namespace Typage.Engine.Tests.Markdown;

/// <summary>
/// Tabs count as indentation everywhere spaces do (a tab is one char, four columns). Tab-indented list
/// items / tasks are recognized, tab-indented lines open code blocks, and editing assists preserve the
/// literal tab rather than converting it to spaces.
/// </summary>
public class TabIndentTests
{
    [Fact]
    public void TabIndentedListItem_IsRecognized()
    {
        // After a list line (so it isn't a fresh-block indented-code position), a tab-nested item is a list.
        Assert.True(HasType("- a\n\t- b", TokenType.ListMarker));
    }

    [Fact]
    public void TabIndentedTask_IsRecognized()
    {
        Assert.True(HasType("- a\n\t- [ ] b", TokenType.TaskMarker));
    }

    [Fact]
    public void SingleLeadingTab_AtAFreshPosition_IsIndentedCode()
    {
        // A tab is four columns, so like four spaces it opens an indented code block after a blank line.
        Assert.Equal(new[] { "\tcode" }, TextsOfType("x\n\n\tcode\nprose", TokenType.CodeText));
    }

    [Fact]
    public void LinePrefix_ParsesATabIndent()
    {
        var p = LinePrefix.Parse("\t- [ ] todo");
        Assert.Equal(ListKind.Unordered, p.List);
        Assert.True(p.HasTask);
        Assert.Equal(1, p.Indent);          // one indentation character
        Assert.Equal("\t", p.IndentText);   // captured verbatim
    }

    [Fact]
    public void Continuation_PreservesTheTabIndent()
    {
        Assert.Equal("\t- ", LinePrefix.Parse("\t- item").ContinuationPrefix());
        Assert.Equal("\t- [ ] ", LinePrefix.Parse("\t- [ ] item").ContinuationPrefix());
        Assert.Equal("\t2. ", LinePrefix.Parse("\t1. item").ContinuationPrefix());
    }

    [Fact]
    public void Enter_OnATabIndentedItem_ContinuesWithTheTab()
    {
        var r = LinePrefix.ComputeEnter("\t- item", caretColumn: "\t- item".Length);
        Assert.Equal(EnterKind.Continue, r.Kind);
        Assert.Equal("\n\t- ", r.Text);
    }

    [Fact]
    public void Enter_OnATabIndentedParagraph_CarriesTheTabDown()
    {
        var line = "\tplain text";
        var r = LinePrefix.ComputeEnter(line, caretColumn: line.Length);
        Assert.Equal(EnterKind.Continue, r.Kind);
        Assert.Equal("\n\t", r.Text);
    }

    [Fact]
    public void NestedOrderedList_WithTabs_RenumbersByLevel()
    {
        // Two tab-nested ordered items written "1." / "1." resequence to 1, 2 at their (tab) level.
        var lines = new[] { "\t1. a", "\t1. b" };
        var result = OrderedListRenumber.Compute(lines, 0);
        Assert.NotNull(result);
        Assert.Equal(new[] { "\t1. a", "\t2. b" }, result!.Value.Lines);
    }
}
