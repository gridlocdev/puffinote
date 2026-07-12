using Typage.Engine.Markdown;
using Xunit;
using static Typage.Engine.Tests.Markdown.TokenizerTestHelpers;

namespace Typage.Engine.Tests.Markdown;

public class MarkdownModelTests
{
    [Theory]
    [InlineData("# H1", "1")]
    [InlineData("### H3", "3")]
    [InlineData("###### H6", "6")]
    public void AtxHeading_EmitsMarkerWithLevel(string md, string level)
    {
        var (_, model) = Tokenize(md);
        var marker = Assert.Single(model.AllTokens(), t => t.Type == TokenType.HeadingMarker);
        Assert.Equal(level, marker.Meta);
    }

    [Fact]
    public void SevenHashes_IsNotAHeading()
    {
        Assert.False(HasType("####### too many", TokenType.HeadingMarker));
    }

    [Fact]
    public void Blockquote_EmitsMarker()
    {
        Assert.Equal(new[] { "> " }, TextsOfType("> quoted", TokenType.BlockquoteMarker));
    }

    [Fact]
    public void UnorderedList_EmitsMarker()
    {
        Assert.Equal(new[] { "- " }, TextsOfType("- item", TokenType.ListMarker));
    }

    [Fact]
    public void OrderedList_EmitsMarker()
    {
        Assert.Equal(new[] { "1. ", "2. " }, TextsOfType("1. a\n2. b", TokenType.ListMarker));
    }

    [Fact]
    public void TaskList_EmitsTaskMarker()
    {
        Assert.Equal(new[] { "[ ] ", "[x] " }, TextsOfType("- [ ] todo\n- [x] done", TokenType.TaskMarker));
    }

    [Fact]
    public void TaskList_ReadsAllThreeStates_AndCarriesTheStateInTheMarkersMeta()
    {
        // The dialect's task vocabulary (PRD §8.3.2): one character between the brackets, three meanings. The
        // marker carries the state it spells, so a consumer reads it off the token instead of re-parsing the box.
        var (_, model) = Tokenize("- [ ] a\n- [/] b\n- [x] c");
        var states = Enumerable.Range(0, 3)
            .Select(line => model.LineTokens(line).Single(t => t.Type == TokenType.TaskMarker))
            .Select(t => TaskStates.FromMark(t.Meta![0]))
            .ToArray();

        Assert.Equal([TaskState.NotStarted, TaskState.InProgress, TaskState.Done], states);
    }

    [Fact]
    public void TaskList_UppercaseX_IsDone_AndNormalizesInMeta()
    {
        var (_, model) = Tokenize("- [X] done");
        var marker = model.LineTokens(0).Single(t => t.Type == TokenType.TaskMarker);
        Assert.Equal("x", marker.Meta); // the source keeps its "[X]"; the *state* is canonical
        Assert.Equal(TaskState.Done, TaskStates.FromMark(marker.Meta![0]));
    }

    [Fact]
    public void AnUnknownBoxCharacter_IsNotATaskMarker()
    {
        // Tolerant tokenizing: "[a]" is not in the vocabulary, so the brackets stay literal text rather than
        // becoming a box in some fourth, undefined state.
        Assert.False(HasType("- [a] not a task", TokenType.TaskMarker));
        Assert.False(HasType("- [] not a task", TokenType.TaskMarker));

        // The characters the dialect used to admit are ordinary text again — a file that spells them keeps them,
        // and reads them back as the literal "[?]" the author typed rather than as a box we no longer draw.
        Assert.False(HasType("- [?] blocked", TokenType.TaskMarker));
        Assert.False(HasType("- [>] deferred", TokenType.TaskMarker));
        Assert.False(HasType("- [-] cancelled", TokenType.TaskMarker));
    }

    [Fact]
    public void HorizontalRule()
    {
        Assert.True(HasType("---", TokenType.HorizontalRule));
        Assert.True(HasType("***", TokenType.HorizontalRule));
        Assert.True(HasType("___", TokenType.HorizontalRule));
    }

    [Fact]
    public void FencedCode_BodyIsCodeText_AndCarriesState()
    {
        var md = "```python\nx = 1\ny = 2\n```";
        var (_, model) = Tokenize(md);
        Assert.Equal(new[] { "x = 1", "y = 2" }, TextsOfType(md, TokenType.CodeText));
        // Line 1 and 2 are entered inside the fence; line 0 (the opener) is Normal.
        Assert.Equal(BlockKind.Normal, model.EnteringState(0).Kind);
        Assert.Equal(BlockKind.FencedCode, model.EnteringState(1).Kind);
        Assert.Equal(BlockKind.FencedCode, model.EnteringState(2).Kind);
    }

    [Fact]
    public void FencedCode_CapturesLanguage()
    {
        var marker = System.Linq.Enumerable.First(
            Tokenize("```rust\ncode\n```").Model.AllTokens(),
            t => t.Type == TokenType.CodeFence);
        Assert.Equal("rust", marker.Meta);
    }

    [Fact]
    public void FencedCode_DoesNotTokenizeMarkdownInside()
    {
        // The '**' inside a code block must NOT become Strong.
        Assert.False(HasType("```\n**not bold**\n```", TokenType.Strong));
    }

    [Fact]
    public void UnterminatedFence_ExtendsToEof()
    {
        var md = "```\nstill code\nmore code";
        Assert.Equal(new[] { "still code", "more code" }, TextsOfType(md, TokenType.CodeText));
    }

    [Fact]
    public void FrontMatter_OnlyAtTop()
    {
        var (_, model) = Tokenize("---\ntitle: Hi\n---\n# Body");
        Assert.Equal(BlockKind.FrontMatter, model.EnteringState(1).Kind);
        Assert.True(HasType("---\ntitle: Hi\n---\n# Body", TokenType.FrontMatterText));
    }

    [Fact]
    public void DashesNotAtTop_AreHorizontalRule_NotFrontMatter()
    {
        Assert.False(HasType("text\n---\nmore", TokenType.FrontMatterFence));
    }

    [Fact]
    public void Table_IsRecognized()
    {
        var md = "| A | B |\n| - | - |\n| 1 | 2 |";
        Assert.True(HasType(md, TokenType.TableDelimiter));
        // Pipe markers present on the header row.
        Assert.Contains(Tokenize(md).Model.LineTokens(0), t => t.Type == TokenType.Marker);
    }

    [Fact]
    public void PipesWithoutDelimiter_AreNotATable()
    {
        Assert.False(HasType("a | b | c\njust text", TokenType.TableDelimiter));
    }
}
