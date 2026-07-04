using Typage.Engine.Document;
using Typage.Engine.Markdown;
using Xunit;

namespace Typage.Engine.Tests.Markdown;

/// <summary>
/// The incremental update (PRD §8.2) must always agree with a full rebuild — re-tokenizing
/// only the affected tail is an optimization, never a behavior change. These tests apply an
/// edit, run <see cref="MarkdownModel.Update"/>, and assert the result is token-for-token
/// identical to <see cref="MarkdownModel.Build"/> on the new document.
/// </summary>
public class IncrementalTokenizerTests
{
    private static void AssertIncrementalMatchesFull(string before, int editOffset, int deleteLen, string insert)
    {
        var doc1 = TextDocument.FromString(before);
        var model1 = MarkdownModel.Build(doc1);

        var firstChangedLine = doc1.GetPosition(editOffset).Line;
        var doc2 = doc1.Replace(editOffset, deleteLen, insert);

        var incremental = model1.Update(doc2, firstChangedLine, out _);
        var full = MarkdownModel.Build(doc2);

        var inc = incremental.AllTokens().ToList();
        var fl = full.AllTokens().ToList();
        Assert.Equal(fl.Count, inc.Count);
        for (var i = 0; i < fl.Count; i++)
        {
            Assert.Equal(fl[i], inc[i]);
        }
    }

    [Fact]
    public void EditWithinParagraph()
    {
        AssertIncrementalMatchesFull("hello world\nsecond line\nthird", 6, 5, "there");
    }

    [Fact]
    public void TypingBoldMarkers()
    {
        AssertIncrementalMatchesFull("make this bold\nnext", 0, 0, "**");
    }

    [Fact]
    public void InsertingNewline_ShiftsLines()
    {
        AssertIncrementalMatchesFull("one two three\nfour", 3, 1, "\n");
    }

    [Fact]
    public void OpeningFenceAtTop_FlipsRestToCode()
    {
        // The O(n) worst case: an opening fence at the top turns everything below into code.
        AssertIncrementalMatchesFull("para one\npara **two**\npara three", 0, 0, "```\n");
    }

    [Fact]
    public void DeletingClosingFence_ExtendsCode()
    {
        AssertIncrementalMatchesFull("```\ncode\n```\nafter", 9, 3, "");
    }

    [Fact]
    public void EditInsideTable()
    {
        AssertIncrementalMatchesFull("| A | B |\n| - | - |\n| 1 | 2 |", 24, 1, "9");
    }

    [Fact]
    public void EditAboveTable_AffectsRecognition()
    {
        AssertIncrementalMatchesFull("intro\n| A | B |\n| - | - |\n| 1 | 2 |", 0, 5, "# Title");
    }

    [Fact]
    public void Checkpoints_AllowResumeFromAnyLine()
    {
        var doc = TextDocument.FromString("# H\n\n```\ncode\n```\n\n- list\n- items\n\npara");
        var model = MarkdownModel.Build(doc);
        // Every line records the block state it was entered with (the checkpoint cache),
        // so a viewport can resume from the nearest line above without re-scanning the top.
        Assert.Equal(doc.LineCount, model.LineCount);
        for (var i = 0; i < model.LineCount; i++)
        {
            _ = model.EnteringState(i); // present for every line
        }

        Assert.Equal(BlockKind.FencedCode, model.EnteringState(3).Kind);
    }
}
