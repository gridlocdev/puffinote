namespace Puffinote.Text.Document;

/// <summary>
/// A caret-and-selection: an <see cref="Anchor"/> (where selection started) and a moving
/// <see cref="Caret"/>. When the two coincide the selection is empty (a plain caret).
/// Offsets are document code-unit positions.
/// </summary>
public readonly record struct Selection(int Anchor, int Caret)
{
    /// <summary>A collapsed caret at <paramref name="offset"/>.</summary>
    public static Selection At(int offset) => new(offset, offset);

    /// <summary>Lower bound of the selected range.</summary>
    public int Start => Math.Min(Anchor, Caret);

    /// <summary>Upper bound of the selected range.</summary>
    public int End => Math.Max(Anchor, Caret);

    /// <summary>Number of code units selected.</summary>
    public int Length => End - Start;

    /// <summary>True when nothing is selected (anchor == caret).</summary>
    public bool IsEmpty => Anchor == Caret;

    /// <summary>True when <paramref name="offset"/> lies within (or on the boundary of) the range.</summary>
    public bool Contains(int offset) => offset >= Start && offset <= End;

    /// <summary>True when this selection overlaps <c>[start, start+length)</c> (boundary touching counts).</summary>
    public bool Intersects(int start, int length) => Start <= start + length && End >= start;
}
