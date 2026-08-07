using Puffinote.Text.Document;

namespace Puffinote.Text.Editing;

/// <summary>
/// One reversible change to the document: replace <see cref="Removed"/> at
/// <see cref="Start"/> with <see cref="Inserted"/>. Both the removed and inserted text are
/// captured so the edit can be applied <i>and</i> undone against the immutable
/// <see cref="TextDocument"/> without consulting anything else (ARCHITECTURE — the model
/// mutation lives in the edit; <see cref="EditHistory"/> owns the stacks).
/// </summary>
public readonly record struct TextEdit(int Start, string Removed, string Inserted)
{
    /// <summary>Length of the span this edit removes.</summary>
    public int OldLength => Removed.Length;

    /// <summary>Length of the text this edit inserts.</summary>
    public int NewLength => Inserted.Length;

    /// <summary>Caret offset just past the inserted text (the natural post-edit caret).</summary>
    public int CaretAfter => Start + Inserted.Length;

    /// <summary>Build the edit that replaces <c>[start, start+length)</c> with <paramref name="inserted"/>.</summary>
    public static TextEdit Replace(TextDocument document, int start, int length, string inserted)
    {
        ArgumentNullException.ThrowIfNull(document);
        var removed = length == 0 ? string.Empty : document.GetText(start, length);
        return new TextEdit(start, removed, inserted ?? string.Empty);
    }

    /// <summary>Apply this edit, returning the new document.</summary>
    public TextDocument Apply(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Replace(Start, Removed.Length, Inserted);
    }

    /// <summary>The inverse edit, which undoes this one.</summary>
    public TextEdit Invert() => new(Start, Inserted, Removed);

    /// <summary>
    /// True when <paramref name="next"/> can fold into this edit as one undo step — used to
    /// coalesce a run of single-character typing (PRD §7.2: "typing runs coalesce"). Only pure
    /// insertions that append exactly where this one ended, with no intervening selection, merge.
    /// </summary>
    public bool CanCoalesceWith(TextEdit next)
    {
        var bothPureInsert = Removed.Length == 0 && next.Removed.Length == 0;
        var adjacent = next.Start == CaretAfter;
        // A newline ends a typing run on either side: don't fold an insert that ends a line,
        // nor one that begins a new one.
        var nextNotNewline = next.Inserted.Length > 0 && next.Inserted[^1] != '\n';
        var thisNotNewline = Inserted.Length > 0 && Inserted[^1] != '\n';
        return bothPureInsert && adjacent && nextNotNewline && thisNotNewline;
    }

    /// <summary>Fold <paramref name="next"/> into this edit (only valid when <see cref="CanCoalesceWith"/> is true).</summary>
    public TextEdit CoalesceWith(TextEdit next) => new(Start, Removed, Inserted + next.Inserted);
}
