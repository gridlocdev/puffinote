using System.Linq;
using Typage.Engine.Editing;
using Typage.Engine.Markdown;
using Xunit;

namespace Typage.Engine.Tests.Markdown;

/// <summary>
/// Validates the surface's incremental-tokenization wiring: maintaining a <see cref="MarkdownModel"/>
/// by calling <see cref="MarkdownModel.Update"/> with <see cref="DocumentBuffer.LastChangedLine"/>
/// after each edit must always equal a full rebuild — including across multi-line inserts, a fence
/// typed at the top (the O(n) worst case), and undo/redo.
/// </summary>
public class IncrementalEditingTests
{
    [Fact]
    public void BufferDrivenIncrementalUpdates_MatchFullRebuild()
    {
        var buffer = new DocumentBuffer(string.Empty);
        var model = MarkdownModel.Build(buffer.Document);

        void Apply(Action edit)
        {
            edit();
            model = model.Update(buffer.Document, buffer.LastChangedLine, out _);
            AssertSameTokens(MarkdownModel.Build(buffer.Document), model);
        }

        Apply(() => buffer.Insert("# Title"));
        Apply(() => buffer.Insert("\n\nA paragraph with **bold** and `code`."));
        Apply(() => buffer.Insert("\n\n- item one\n- item two"));

        // Edit in the middle of the document.
        buffer.Selection = Engine.Document.Selection.At(2);
        Apply(() => buffer.Insert("# "));

        // The worst case: a fence opener at the very top flips everything below into code.
        buffer.Selection = Engine.Document.Selection.At(0);
        Apply(() => buffer.Insert("```\n"));

        // Undo and redo must keep the incremental model correct too.
        Apply(() => buffer.Undo());
        Apply(() => buffer.Redo());
        Apply(() => buffer.Undo());
    }

    private static void AssertSameTokens(MarkdownModel expected, MarkdownModel actual)
    {
        var e = expected.AllTokens().ToList();
        var a = actual.AllTokens().ToList();
        Assert.Equal(e.Count, a.Count);
        for (var i = 0; i < e.Count; i++)
        {
            Assert.Equal(e[i], a[i]);
        }
    }
}
