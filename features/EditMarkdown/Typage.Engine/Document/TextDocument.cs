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
    /// <summary>
    /// A contiguous run of characters, either from the immutable original buffer or (when
    /// <see cref="FromAdd"/>) the shared append buffer. Immutable.
    /// </summary>
    private readonly record struct Piece(bool FromAdd, int Start, int Length);

    /// <summary>
    /// The append-only insert buffer, shared by reference across one document's edit lineage so consecutive
    /// insertions land in one contiguous run and coalesce into a single piece (rather than ~2-3 new pieces per
    /// keystroke). Append-only — content at <c>[0, Length)</c> never changes — so every existing piece's range
    /// stays valid as the buffer grows, preserving snapshot immutability.
    /// </summary>
    private sealed class AddBuffer
    {
        private char[] _chars = new char[64];

        public int Length { get; private set; }

        public int Append(string text)
        {
            var start = Length;
            if (Length + text.Length > _chars.Length)
            {
                Array.Resize(ref _chars, Math.Max(Length + text.Length, _chars.Length * 2));
            }

            text.CopyTo(0, _chars, Length, text.Length);
            Length += text.Length;
            return start;
        }

        public char this[int i] => _chars[i];

        public void AppendTo(System.Text.StringBuilder sb, int start, int length) => sb.Append(_chars, start, length);
    }

    private static readonly AddBuffer EmptyAdd = new(); // never appended to (first edit allocates a fresh buffer)

    private readonly string _original; // the immutable original buffer (shared by reference, never copied)
    private readonly AddBuffer _add;   // the lineage's append buffer (inserts live here)
    private readonly Piece[] _pieces;
    private int[]? _lineStarts;   // lazily computed offsets of the first char of each line

    /// <summary>The empty document.</summary>
    public static readonly TextDocument Empty = new(string.Empty, EmptyAdd, [], 0);

    private TextDocument(string original, AddBuffer add, Piece[] pieces, int length, int[]? lineStarts = null)
    {
        _original = original;
        _add = add;
        _pieces = pieces;
        Length = length;
        _lineStarts = lineStarts;
    }

    /// <summary>The append buffer to insert into: the lineage's existing one, or a fresh one when none has been
    /// started yet (so the never-appended static <see cref="EmptyAdd"/> is never grown).</summary>
    private AddBuffer InsertBuffer() => _add.Length > 0 ? _add : new AddBuffer();

    /// <summary>Total length in UTF-16 code units.</summary>
    public int Length { get; }

    /// <summary>Number of pieces in the table; for tests (coalescing keeps this small under sequential edits).</summary>
    internal int PieceCount => _pieces.Length;

    /// <summary>Build a document from a string. The string becomes the (shared) original buffer.</summary>
    public static TextDocument FromString(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Empty;
        }

        return new TextDocument(text, EmptyAdd, [new Piece(false, 0, text.Length)], text.Length);
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
            AppendPiece(sb, p, p.Start, p.Length);
        }

        return sb.ToString();
    }

    /// <summary>Append the sub-run <c>[from, from+take)</c> of <paramref name="p"/> to <paramref name="sb"/>.</summary>
    private void AppendPiece(System.Text.StringBuilder sb, Piece p, int from, int take)
    {
        if (p.FromAdd)
        {
            _add.AppendTo(sb, from, take);
        }
        else
        {
            sb.Append(_original, from, take);
        }
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
        var i = PieceIndexAt(start);
        var remaining = length;
        var posInPiece = start - PieceOffsets[i]; // offset within the first piece to start at
        for (; i < _pieces.Length && remaining > 0; i++)
        {
            var p = _pieces[i];
            var take = Math.Min(p.Length - posInPiece, remaining);
            AppendPiece(sb, p, p.Start + posInPiece, take);
            remaining -= take;
            posInPiece = 0; // every later piece is consumed from its beginning
        }

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

            var i = PieceIndexAt(index);
            var p = _pieces[i];
            var pos = p.Start + (index - PieceOffsets[i]);
            return p.FromAdd ? _add[pos] : _original[pos];
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

        var add = InsertBuffer();
        var inserted = new Piece(true, add.Append(text), text.Length);
        var rebuilt = SpliceAt(offset, 0, [inserted]);
        return new TextDocument(_original, add, rebuilt, Length + text.Length, PatchedLineStarts(offset, 0, text));
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

        var rebuilt = SpliceAt(offset, length, []);
        return new TextDocument(_original, _add, rebuilt, Length - length, PatchedLineStarts(offset, length, string.Empty));
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

        var add = InsertBuffer();
        var rebuilt = SpliceAt(offset, length, [new Piece(true, add.Append(text), text.Length)]);
        return new TextDocument(_original, add, rebuilt, Length - length + text.Length, PatchedLineStarts(offset, length, text));
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
                head.Add(new Piece(p.FromAdd, p.Start, keep));
            }

            // Part of this piece after the deleted span -> tail.
            if (pieceEnd > deleteEnd)
            {
                var skip = Math.Max(0, deleteEnd - pieceStart);
                tail.Add(new Piece(p.FromAdd, p.Start + skip, p.Length - skip));
            }
        }

        var result = new List<Piece>(head.Count + insertPieces.Length + tail.Count);

        void AddCoalescing(Piece p)
        {
            if (p.Length == 0)
            {
                return;
            }

            // Merge with the previous piece when they are the same buffer and physically adjacent — so
            // consecutive insertions (which append contiguously to the add buffer) collapse to one piece.
            if (result.Count > 0)
            {
                var last = result[^1];
                if (last.FromAdd == p.FromAdd && last.Start + last.Length == p.Start)
                {
                    result[^1] = last with { Length = last.Length + p.Length };
                    return;
                }
            }

            result.Add(p);
        }

        foreach (var p in head)
        {
            AddCoalescing(p);
        }

        foreach (var p in insertPieces)
        {
            AddCoalescing(p);
        }

        foreach (var p in tail)
        {
            AddCoalescing(p);
        }

        return [.. result];
    }

    // ----- Piece index (binary search by absolute offset) -----

    /// <summary>Cumulative start offset of each piece (entry <c>i</c> = total length of pieces before <c>i</c>;
    /// the final entry is <see cref="Length"/>), so a piece can be found by offset in O(log pieces).</summary>
    private int[] PieceOffsets
    {
        get
        {
            if (field is not null)
            {
                return field;
            }

            var offsets = new int[_pieces.Length + 1];
            for (var i = 0; i < _pieces.Length; i++)
            {
                offsets[i + 1] = offsets[i] + _pieces[i].Length;
            }

            return field = offsets;
        }
    }

    /// <summary>Index of the piece containing absolute <paramref name="offset"/> — the greatest <c>i</c> with
    /// <c>PieceOffsets[i] &lt;= offset</c>. Requires <c>0 &lt;= offset &lt; Length</c> (so at least one piece).</summary>
    private int PieceIndexAt(int offset)
    {
        var offsets = PieceOffsets;
        var lo = 0;
        var hi = _pieces.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) >> 1;
            if (offsets[mid] <= offset)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

    // ----- Line / offset services -----

    private int[] LineStarts => _lineStarts ??= ComputeLineStarts();

    /// <summary>
    /// Derive the new document's line starts from this (pre-edit) document's, given a replace of
    /// <c>[offset, offset+deleteLength)</c> with <paramref name="insertText"/> — without re-scanning the
    /// whole document. Returns <c>null</c> if our own starts aren't cached yet (then the new document
    /// computes them lazily, exactly as before). Line starts before the edit are kept verbatim, the
    /// inserted text is scanned for its own newlines, and starts after the edit shift by the length delta.
    /// </summary>
    private int[]? PatchedLineStarts(int offset, int deleteLength, string insertText)
    {
        var old = _lineStarts;
        if (old is null)
        {
            return null;
        }

        var delta = insertText.Length - deleteLength;

        // Kept prefix: every old start at or before the edit point (its preceding '\n' is untouched).
        var keep = 0;
        while (keep < old.Length && old[keep] <= offset)
        {
            keep++;
        }

        // Tail: old starts strictly past the deleted region (whose preceding '\n' survives), shifted by delta.
        // A start at exactly offset+deleteLength is dropped — its '\n' sat on the last deleted character.
        var tailFrom = keep;
        var deleteEnd = offset + deleteLength;
        while (tailFrom < old.Length && old[tailFrom] <= deleteEnd)
        {
            tailFrom++;
        }

        var inserted = 0;
        for (var i = 0; i < insertText.Length; i++)
        {
            if (insertText[i] == '\n')
            {
                inserted++;
            }
        }

        var result = new int[keep + inserted + (old.Length - tailFrom)];
        Array.Copy(old, 0, result, 0, keep);

        var w = keep;
        for (var i = 0; i < insertText.Length; i++)
        {
            if (insertText[i] == '\n')
            {
                result[w++] = offset + i + 1;
            }
        }

        for (var j = tailFrom; j < old.Length; j++)
        {
            result[w++] = old[j] + delta;
        }

        return result;
    }

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

        return [.. starts];
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
        _ = GetLineStart(line);
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
