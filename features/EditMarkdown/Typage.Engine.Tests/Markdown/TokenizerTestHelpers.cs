using Puffinote.Text.Document;
using Typage.Engine.Markdown;

namespace Typage.Engine.Tests.Markdown;

/// <summary>Shared helpers for asserting against the tokenizer output.</summary>
internal static class TokenizerTestHelpers
{
    public static (TextDocument Doc, MarkdownModel Model) Tokenize(string markdown)
    {
        var doc = TextDocument.FromString(markdown);
        return (doc, MarkdownModel.Build(doc));
    }

    /// <summary>Every token of <paramref name="type"/>, as its source text.</summary>
    public static List<string> TextsOfType(string markdown, TokenType type)
    {
        var (doc, model) = Tokenize(markdown);
        var result = new List<string>();
        foreach (var t in model.AllTokens())
        {
            if (t.Type == type)
            {
                result.Add(doc.GetText(t.Start, t.Length));
            }
        }

        return result;
    }

    /// <summary>The distinct token types found in the document, in first-seen order.</summary>
    public static List<TokenType> TypesIn(string markdown)
    {
        var (_, model) = Tokenize(markdown);
        var seen = new List<TokenType>();
        foreach (var t in model.AllTokens())
        {
            if (!seen.Contains(t.Type))
            {
                seen.Add(t.Type);
            }
        }

        return seen;
    }

    public static bool HasType(string markdown, TokenType type) => TypesIn(markdown).Contains(type);
}
