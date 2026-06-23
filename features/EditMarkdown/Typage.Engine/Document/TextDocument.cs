namespace Typage.Engine.Document;

/// <summary>
/// The document buffer: an <b>immutable</b> piece table (PRD §9.1 — "piece tree / rope
/// with cheap edits and undo snapshots; plain-string document buffers are disallowed").
/// Every edit returns a <i>new</i> <see cref="TextDocument"/> that structurally shares the
/// underlying character buffers with its predecessor, so an undo stack of snapshots costs
/// only an array of piece references per step, never a copy of the text.
/// </summary>
/// <remarks>
/// Offsets are UTF-16 code-unit positions (the same unit Avalonia's text formatting and
/// .NET string indexing use). Caret-legal positions are grapheme-cluster boundaries; see
/// <see cref="Grapheme"/>. Lines are delimited by <c>\n</c>; a preceding <c>\r</c> is kept
/// verbatim as line content so CRLF files round-trip byte-for-byte (PRD §7.1).
/// </remarks>
public sealed class TextDocument
{
    /// <summary>A contiguous run of characters from one source string. Immutable.</summary>
    private readonly record struct Piece(string Source, int Start, int Length);

    private readonly Piece[] _pieces;
    private int[]? _lineStarts; // lazily computed offsets of the first char of each line

    /// <summary>The empty document.</summary>
    public static readonly TextDocument Empty = new(Array.Empty<Piece>(), 0);

    private TextDocument(Piece[] pieces, int length)
    {
        _pieces = pieces;
        Length = length;
    }

    /// <summary>Total length in UTF-16 code units.</summary>
    public int Length { get; }

    /// <summary>Build a document from a string. The string becomes the (shared) original buffer.</summary>
    public static TextDocument FromString(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Empty;
        }

        return new TextDocument(new[] { new Piece(text, 0, text.Length) }, text.Length);
    }

    /// <summary>The full document text, materialized.</summary>
    public string GetText()
    {
        if (Length == 0)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(Length);
        foreach (var p in _pieces)
        {
            sb.Append(p.Source, p.Start, p.Length);
        }

        return sb.ToString();
    }

    /// <summary>The substring <c>[start, start+length)</c>.</summary>
    public string GetText(int start, int length)
    {
        if (length == 0)
        {
            return string.Empty;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (start + length > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Range extends past the end of the document.");
        }

        var sb = new System.Text.StringBuilder(length);
        var consumed = 0;
        var remainingToSkip = start;
        var remainingToTake = length;
        foreach (var p in _pieces)
        {
            if (remainingToTake == 0)
            {
                break;
            }

            if (remainingToSkip >= p.Length)
            {
                remainingToSkip -= p.Length;
                continue;
            }

            var from = p.Start + remainingToSkip;
            var avail = p.Length - remainingToSkip;
            remainingToSkip = 0;
            var take = Math.Min(avail, remainingToTake);
            sb.Append(p.Source, from, take);
            remainingToTake -= take;
            consumed += take;
        }

        _ = consumed;
        return sb.ToString();
    }

    /// <summary>The character at <paramref name="index"/>.</summary>
    public char this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var pos = index;
            foreach (var p in _pieces)
            {
                if (pos < p.Length)
                {
                    return p.Source[p.Start + pos];
                }

                pos -= p.Length;
            }

            throw new ArgumentOutOfRangeException(nameof(index)); // unreachable
        }
    }

    /// <summary>Insert <paramref name="text"/> at <paramref name="offset"/>, returning a new document.</summary>
    public TextDocument Insert(int offset, string text)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (offset > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (string.IsNullOrEmpty(text))
        {
            return this;
        }

        var inserted = new Piece(text, 0, text.Length);
        var rebuilt = SpliceAt(offset, 0, new[] { inserted });
        return new TextDocument(rebuilt, Length + text.Length);
    }

    /// <summary>Delete <paramref name="length"/> code units at <paramref name="offset"/>.</summary>
    public TextDocument Delete(int offset, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (offset + length > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (length == 0)
        {
            return this;
        }

        var rebuilt = SpliceAt(offset, length, Array.Empty<Piece>());
        return new TextDocument(rebuilt, Length - length);
    }

    /// <summary>Delete <paramref name="length"/> at <paramref name="offset"/> then insert <paramref name="text"/>.</summary>
    public TextDocument Replace(int offset, int length, string text)
    {
        if (length == 0)
        {
            return Insert(offset, text);
        }

        if (string.IsNullOrEmpty(text))
        {
            return Delete(offset, length);
        }

        var rebuilt = SpliceAt(offset, length, new[] { new Piece(text, 0, text.Length) });
        return new TextDocument(rebuilt, Length - length + text.Length);
    }

    /// <summary>
    /// Replace the piece span covering <c>[offset, offset+deleteLength)</c> with
    /// <paramref name="insertPieces"/>, splitting boundary pieces as needed.
    /// </summary>
    private Piece[] SpliceAt(int offset, int deleteLength, Piece[] insertPieces)
    {
        var head = new List<Piece>();
        var tail = new List<Piece>();
        var deleteEnd = offset + deleteLength;
        var pos = 0;
        foreach (var p in _pieces)
        {
            var pieceStart = pos;
            var pieceEnd = pos + p.Length;
            pos = pieceEnd;

            // Part of this piece before the deleted span -> head.
            if (pieceStart < offset)
            {
                var keep = Math.Min(p.Length, offset - pieceStart);
                head.Add(new Piece(p.Source, p.Start, keep));
            }

            // Part of this piece after the deleted span -> tail.
            if (pieceEnd > deleteEnd)
            {
                var skip = Math.Max(0, deleteEnd - pieceStart);
                tail.Add(new Piece(p.Source, p.Start + skip, p.Length - skip));
            }
        }

        var result = new List<Piece>(head.Count + insertPieces.Length + tail.Count);
        result.AddRange(head);
        result.AddRange(insertPieces);
        result.AddRange(tail);
        return result.ToArray();
    }

    // ----- Line / offset services -----

    private int[] LineStarts => _lineStarts ??= ComputeLineStarts();

    private int[] ComputeLineStarts()
    {
        var starts = new List<int> { 0 };
        var text = GetText();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return starts.ToArray();
    }

    /// <summary>Number of lines. An empty document has one (empty) line.</summary>
    public int LineCount => LineStarts.Length;

    /// <summary>Offset of the first character of <paramref name="line"/> (0-based).</summary>
    public int GetLineStart(int line)
    {
        if ((uint)line >= (uint)LineCount)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        return LineStarts[line];
    }

    /// <summary>Offset just past the last content character of <paramref name="line"/>, excluding the trailing <c>\n</c>.</summary>
    public int GetLineEnd(int line)
    {
        var start = GetLineStart(line);
        if (line + 1 < LineCount)
        {
            return LineStarts[line + 1] - 1; // drop the '\n'
        }

        return Length;
    }

    /// <summary>The text of <paramref name="line"/>, excluding its trailing <c>\n</c> (a trailing <c>\r</c> is kept).</summary>
    public string GetLineText(int line)
    {
        var start = GetLineStart(line);
        return GetText(start, GetLineEnd(line) - start);
    }

    /// <summary>Map an absolute offset to a 0-based line/column position.</summary>
    public LinePosition GetPosition(int offset)
    {
        if ((uint)offset > (uint)Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var starts = LineStarts;
        // Binary search for the greatest line start <= offset.
        var lo = 0;
        var hi = starts.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) >> 1;
            if (starts[mid] <= offset)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return new LinePosition(lo, offset - starts[lo]);
    }

    /// <summary>Map a 0-based line/column position to an absolute offset (clamped to the line).</summary>
    public int GetOffset(int line, int column)
    {
        var start = GetLineStart(line);
        var end = GetLineEnd(line);
        return Math.Clamp(start + Math.Max(0, column), start, end);
    }
}
