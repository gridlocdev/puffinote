using Avalonia.Headless.XUnit;
using Typage.Control.Controls;
using Xunit;

namespace Typage.UiTests;

/// <summary>
/// EnsureLayout reuses the block layout above the first edited line (mirroring the incremental tokenizer)
/// instead of re-measuring every block on each keystroke. These prove the incremental result is identical
/// to a from-scratch rebuild — block grouping, per-block row counts (via the block-top prefix sums), and
/// the total row count must all match after any sequence of edits.
/// </summary>
public class IncrementalLayoutTests
{
    private const string Seed =
        "# Title\n\npara one two three four\n\n- a\n- b\n  - nested\n\n```\ncode line\nmore code\n```\n\n1. x\n2. y\n3. z\n\n> quote\n\nlast para";

    [AvaloniaFact]
    public void IncrementalLayout_MatchesFullRebuild_AcrossRandomEdits()
    {
        var rng = new System.Random(0xBEEF);
        var editor = new MarkdownEditor { Text = Seed }; // one editor, edited in place → exercises prefix reuse

        for (var step = 0; step < 600; step++)
        {
            var doc = editor.Buffer.Document;
            var len = doc.Length;
            var offset = rng.Next(0, len + 1);

            switch (rng.Next(3))
            {
                case 0:
                    editor.Buffer.ReplaceRange(offset, 0, RandomChunk(rng)); // insert
                    break;
                case 1:
                    editor.Buffer.ReplaceRange(offset, rng.Next(0, System.Math.Min(5, len - offset) + 1), string.Empty); // delete
                    break;
                default:
                    editor.Buffer.ReplaceRange(offset, rng.Next(0, System.Math.Min(5, len - offset) + 1), RandomChunk(rng)); // replace
                    break;
            }

            AssertLayoutMatchesFullRebuild(editor, step);
        }
    }

    [AvaloniaFact]
    public void IncrementalLayout_HandlesStructureChangingEdits()
    {
        // Targeted cascades: toggling a code fence, splitting/merging paragraphs, and inserting a list item
        // (which renumbers following ordered items) must all match a full rebuild even with prefix reuse.
        var editor = new MarkdownEditor { Text = "para a\n\npara b\n\n1. one\n2. two\n2. three" };

        editor.Buffer.ReplaceRange(0, 0, "```\n"); // open a fence at the very top — cascades to the whole doc
        AssertLayoutMatchesFullRebuild(editor, 0);

        var len = editor.Buffer.Document.Length;
        editor.Buffer.ReplaceRange(len, 0, "\n```"); // close it
        AssertLayoutMatchesFullRebuild(editor, 1);

        editor.Buffer.ReplaceRange(editor.Buffer.Document.Length, 0, "\n0. zero"); // add a list item at the end
        AssertLayoutMatchesFullRebuild(editor, 2);
    }

    [AvaloniaFact]
    public void IncrementalLayout_AccumulatesDirtyLineAcrossEditsBeforeALayout()
    {
        // A compound operation makes several edits before the next layout. The reused prefix must start above
        // the *earliest* of them, so editing a high line then a low one (no layout between) still rebuilds the
        // low one. No ForTest/render call happens between the edits, so EnsureLayout sees both.
        var editor = new MarkdownEditor { Text = "one\n\ntwo\n\nthree\n\nfour" };
        editor.Buffer.ReplaceRange(editor.Buffer.Document.Length, 0, " edited"); // line 6 ("four")
        editor.Buffer.ReplaceRange(0, 0, "ZERO\n\n");                            // now line 0 — the earlier line
        AssertLayoutMatchesFullRebuild(editor, 0);
    }

    [AvaloniaFact]
    public void IncrementalLayout_TypingThenDeletingOnABlankLine_RestoresTheOriginalLayout()
    {
        var seed = "\n\n\n\n\n";
        var editor = new MarkdownEditor { Text = seed };
        editor.Buffer.Selection = Typage.Engine.Document.Selection.At(2);

        editor.TypeForTest('t');
        editor.BackspaceForTest();

        Assert.Equal(seed, editor.Text);
        AssertLayoutMatchesFullRebuild(editor, 0);
    }

    private static void AssertLayoutMatchesFullRebuild(MarkdownEditor edited, int step)
    {
        var fresh = new MarkdownEditor { Text = edited.Text }; // a new editor full-rebuilds its layout

        Assert.Equal(fresh.TotalRowsForTest(), edited.TotalRowsForTest());

        var lines = edited.Buffer.Document.LineCount;
        Assert.Equal(fresh.Buffer.Document.LineCount, lines);
        for (var line = 0; line < lines; line++)
        {
            Assert.True(
                fresh.BlockOfLineForTest(line) == edited.BlockOfLineForTest(line),
                $"step {step}: block grouping diverged at line {line}");
            Assert.True(
                fresh.BlockTopRowForTest(line) == edited.BlockTopRowForTest(line),
                $"step {step}: block-top row diverged at line {line}");
        }
    }

    private static string RandomChunk(System.Random rng)
    {
        var n = rng.Next(0, 5);
        var chars = new char[n];
        for (var i = 0; i < n; i++)
        {
            chars[i] = rng.Next(8) switch
            {
                0 => '\n',
                1 => '\n',
                2 => '#',
                3 => '-',
                4 => '`',
                5 => '*',
                _ => (char)('a' + rng.Next(4)),
            };
        }

        return new string(chars);
    }
}
