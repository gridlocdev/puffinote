import type { LinePosition } from './selection.ts';

/** A run of characters from the original buffer, or from the shared append buffer when `fromAdd`. */
interface Piece {
  readonly fromAdd: boolean;
  readonly start: number;
  readonly length: number;
}

/**
 * The append-only insert buffer, shared by reference across one document's edit lineage. A
 * run of typing lands in one contiguous stretch of it, so it becomes one piece instead of two
 * or three new pieces per keystroke. Content at `[0, length)` never changes, so every existing
 * piece stays valid as the buffer grows.
 *
 * The text is held in chunks of at most `chunkSize` code units; a larger single insert, such as a
 * paste, is a chunk of its own. Only the last chunk grows, so a slice re-flattens at most one small
 * chunk, however much was typed or pasted before it.
 */
class AddBuffer {
  private static readonly chunkSize = 16 * 1024;
  private readonly chunks: string[] = [];
  /** The buffer offset of each chunk's first code unit. */
  private readonly starts: number[] = [];
  length = 0;

  append(text: string): number {
    const start = this.length;
    const last = this.chunks.length - 1;
    if (last >= 0 && this.chunks[last]!.length + text.length <= AddBuffer.chunkSize) {
      this.chunks[last] += text;
    } else {
      this.chunks.push(text);
      this.starts.push(start);
    }
    this.length += text.length;
    return start;
  }

  /** The text at `[from, from + take)`. */
  slice(from: number, take: number): string {
    let i = this.chunkAt(from);
    let pos = from - this.starts[i]!;
    let remaining = take;
    let out = '';
    while (remaining > 0) {
      const chunk = this.chunks[i]!;
      const n = Math.min(chunk.length - pos, remaining);
      out += chunk.slice(pos, pos + n);
      remaining -= n;
      pos = 0;
      i++;
    }
    return out;
  }

  charAt(index: number): string {
    const i = this.chunkAt(index);
    return this.chunks[i]!.charAt(index - this.starts[i]!);
  }

  /** The index of the chunk holding `offset`. */
  private chunkAt(offset: number): number {
    const starts = this.starts;
    let lo = 0;
    let hi = starts.length - 1;
    while (lo < hi) {
      const mid = (lo + hi + 1) >> 1;
      if (starts[mid]! <= offset) lo = mid;
      else hi = mid - 1;
    }
    return lo;
  }
}

const emptyAdd = new AddBuffer(); // never appended to; the first edit starts a fresh buffer

let nextSerial = 0;

/** One replacement: `removedLength` code units at `start` became `insertedLength` new ones. */
export interface DocumentChange {
  readonly start: number;
  readonly removedLength: number;
  readonly insertedLength: number;
}

/**
 * The document buffer: an immutable piece table. Every edit returns a new `TextDocument` that
 * shares the underlying text with its predecessor, so an undo stack of snapshots costs an
 * array of piece records per step, never a copy of the text.
 *
 * Offsets are UTF-16 code units. Lines end at `\n`; a `\r` before it is kept as line content,
 * so CRLF files round-trip byte for byte.
 */
export class TextDocument {
  static readonly empty = new TextDocument('', emptyAdd, [], 0);

  readonly length: number;
  private readonly original: string;
  private readonly add: AddBuffer;
  private readonly pieces: readonly Piece[];
  private lineStarts: number[] | undefined;
  private pieceOffsetsCache: number[] | undefined;
  private textCache: string | undefined;
  /** A per-process serial, so a child can name its parent without holding it alive. */
  private readonly serial = nextSerial++;
  /** The edit that made this document from the snapshot with serial `parent`, if it was made by one. */
  private readonly origin: { readonly parent: number; readonly change: DocumentChange } | undefined;

  private constructor(
    original: string,
    add: AddBuffer,
    pieces: readonly Piece[],
    length: number,
    lineStarts?: number[],
    origin?: { readonly parent: number; readonly change: DocumentChange },
  ) {
    this.original = original;
    this.add = add;
    this.pieces = pieces;
    this.length = length;
    this.lineStarts = lineStarts;
    this.origin = origin;
  }

  /**
   * The single replacement that turns `base` into this document, when that is known: an empty change
   * when they are the same snapshot, the edit when this document was made from `base` by one
   * `insert`, `delete` or `replace`, and `null` otherwise. Incremental consumers use it to find where
   * the untouched tail begins without comparing text.
   */
  changeSince(base: TextDocument): DocumentChange | null {
    if (base === this) return { start: 0, removedLength: 0, insertedLength: 0 };
    return this.origin?.parent === base.serial ? this.origin.change : null;
  }

  /** Build a document from a string. The string becomes the shared original buffer. */
  static fromString(text: string | null | undefined): TextDocument {
    if (!text) return TextDocument.empty;
    return new TextDocument(text, emptyAdd, [{ fromAdd: false, start: 0, length: text.length }], text.length);
  }

  /** The number of pieces in the table. Tests use it to check that edits coalesce. */
  get pieceCount(): number {
    return this.pieces.length;
  }

  /** The whole text. Cached, because a snapshot never changes. */
  getText(): string;
  /** The substring `[start, start + length)`. */
  getText(start: number, length: number): string;
  getText(start?: number, length?: number): string {
    if (start === undefined || length === undefined) {
      if (this.textCache === undefined) {
        let out = '';
        for (const p of this.pieces) out += this.slice(p, p.start, p.length);
        this.textCache = out;
      }
      return this.textCache;
    }
    if (length === 0) return '';
    if (start < 0 || length < 0 || start + length > this.length) {
      throw new RangeError(
        `Range [${start}, ${start + length}) is outside the document (0..${this.length}).`,
      );
    }
    if (this.textCache !== undefined) return this.textCache.slice(start, start + length);
    const offsets = this.pieceOffsets();
    let i = this.pieceIndexAt(start);
    let remaining = length;
    let posInPiece = start - offsets[i]!;
    let out = '';
    for (; i < this.pieces.length && remaining > 0; i++) {
      const p = this.pieces[i]!;
      const take = Math.min(p.length - posInPiece, remaining);
      out += this.slice(p, p.start + posInPiece, take);
      remaining -= take;
      posInPiece = 0;
    }
    return out;
  }

  /** The UTF-16 code unit at `index`. */
  charAt(index: number): string {
    if (index < 0 || index >= this.length) throw new RangeError(`Offset ${index} is outside the document.`);
    const i = this.pieceIndexAt(index);
    const p = this.pieces[i]!;
    const pos = p.start + (index - this.pieceOffsets()[i]!);
    return p.fromAdd ? this.add.charAt(pos) : this.original.charAt(pos);
  }

  /** Insert `text` at `offset`, returning a new document. */
  insert(offset: number, text: string): TextDocument {
    if (offset < 0 || offset > this.length) throw new RangeError(`Offset ${offset} is outside the document.`);
    if (!text) return this;
    const add = this.insertBuffer();
    const piece: Piece = { fromAdd: true, start: add.append(text), length: text.length };
    return new TextDocument(
      this.original,
      add,
      this.spliceAt(offset, 0, [piece]),
      this.length + text.length,
      this.patchedLineStarts(offset, 0, text),
      this.originOf(offset, 0, text.length),
    );
  }

  /** Delete `length` code units at `offset`. */
  delete(offset: number, length: number): TextDocument {
    if (offset < 0 || length < 0 || offset + length > this.length) {
      throw new RangeError(`Range [${offset}, ${offset + length}) is outside the document.`);
    }
    if (length === 0) return this;
    return new TextDocument(
      this.original,
      this.add,
      this.spliceAt(offset, length, []),
      this.length - length,
      this.patchedLineStarts(offset, length, ''),
      this.originOf(offset, length, 0),
    );
  }

  /** Delete `length` at `offset`, then insert `text` there. */
  replace(offset: number, length: number, text: string): TextDocument {
    if (length === 0) return this.insert(offset, text);
    if (!text) return this.delete(offset, length);
    if (offset < 0 || offset + length > this.length) {
      throw new RangeError(`Range [${offset}, ${offset + length}) is outside the document.`);
    }
    const add = this.insertBuffer();
    const piece: Piece = { fromAdd: true, start: add.append(text), length: text.length };
    return new TextDocument(
      this.original,
      add,
      this.spliceAt(offset, length, [piece]),
      this.length - length + text.length,
      this.patchedLineStarts(offset, length, text),
      this.originOf(offset, length, text.length),
    );
  }

  // ----- Lines -----

  /** The number of lines. An empty document has one empty line. */
  get lineCount(): number {
    return this.starts().length;
  }

  /** The offset of the first character of `line` (0-based). */
  getLineStart(line: number): number {
    const starts = this.starts();
    if (line < 0 || line >= starts.length) throw new RangeError(`Line ${line} is outside the document.`);
    return starts[line]!;
  }

  /** The offset just past the last character of `line`, not counting its `\n`. */
  getLineEnd(line: number): number {
    this.getLineStart(line);
    const starts = this.starts();
    return line + 1 < starts.length ? starts[line + 1]! - 1 : this.length;
  }

  /** The text of `line` without its `\n`. A trailing `\r` is kept. */
  getLineText(line: number): string {
    const start = this.getLineStart(line);
    return this.getText(start, this.getLineEnd(line) - start);
  }

  /** Map an offset to a 0-based line and column. */
  getPosition(offset: number): LinePosition {
    if (offset < 0 || offset > this.length) throw new RangeError(`Offset ${offset} is outside the document.`);
    const starts = this.starts();
    let lo = 0;
    let hi = starts.length - 1;
    while (lo < hi) {
      const mid = (lo + hi + 1) >> 1;
      if (starts[mid]! <= offset) lo = mid;
      else hi = mid - 1;
    }
    return { line: lo, column: offset - starts[lo]! };
  }

  /** Map a 0-based line and column to an offset, clamped to the line. */
  getOffset(line: number, column: number): number {
    const start = this.getLineStart(line);
    const end = this.getLineEnd(line);
    return Math.min(end, Math.max(start, start + Math.max(0, column)));
  }

  // ----- Internals -----

  private originOf(
    start: number,
    removedLength: number,
    insertedLength: number,
  ): { readonly parent: number; readonly change: DocumentChange } {
    return { parent: this.serial, change: { start, removedLength, insertedLength } };
  }

  private insertBuffer(): AddBuffer {
    return this.add.length > 0 ? this.add : new AddBuffer();
  }

  private slice(p: Piece, from: number, take: number): string {
    return p.fromAdd ? this.add.slice(from, take) : this.original.slice(from, from + take);
  }

  /** Replace the pieces covering `[offset, offset + deleteLength)` with `insert`, splitting at the edges. */
  private spliceAt(offset: number, deleteLength: number, insert: readonly Piece[]): Piece[] {
    const head: Piece[] = [];
    const tail: Piece[] = [];
    const deleteEnd = offset + deleteLength;
    let pos = 0;
    for (const p of this.pieces) {
      const pieceStart = pos;
      const pieceEnd = pos + p.length;
      pos = pieceEnd;
      if (pieceStart < offset) {
        head.push({
          fromAdd: p.fromAdd,
          start: p.start,
          length: Math.min(p.length, offset - pieceStart),
        });
      }
      if (pieceEnd > deleteEnd) {
        const skip = Math.max(0, deleteEnd - pieceStart);
        tail.push({ fromAdd: p.fromAdd, start: p.start + skip, length: p.length - skip });
      }
    }
    const result: Piece[] = [];
    const push = (p: Piece): void => {
      if (p.length === 0) return;
      // Merge with the previous piece when both come from the same buffer and sit side by side,
      // so consecutive insertions collapse into one piece.
      const last = result[result.length - 1];
      if (last && last.fromAdd === p.fromAdd && last.start + last.length === p.start) {
        result[result.length - 1] = { ...last, length: last.length + p.length };
        return;
      }
      result.push(p);
    };
    head.forEach(push);
    insert.forEach(push);
    tail.forEach(push);
    return result;
  }

  /** The start offset of each piece; the last entry is the document length. */
  private pieceOffsets(): number[] {
    if (!this.pieceOffsetsCache) {
      const offsets = new Array<number>(this.pieces.length + 1);
      offsets[0] = 0;
      for (let i = 0; i < this.pieces.length; i++) offsets[i + 1] = offsets[i]! + this.pieces[i]!.length;
      this.pieceOffsetsCache = offsets;
    }
    return this.pieceOffsetsCache;
  }

  /** The index of the piece holding `offset`. Needs `0 <= offset < length`. */
  private pieceIndexAt(offset: number): number {
    const offsets = this.pieceOffsets();
    let lo = 0;
    let hi = this.pieces.length - 1;
    while (lo < hi) {
      const mid = (lo + hi + 1) >> 1;
      if (offsets[mid]! <= offset) lo = mid;
      else hi = mid - 1;
    }
    return lo;
  }

  private starts(): number[] {
    if (!this.lineStarts) {
      const starts = [0];
      const text = this.getText();
      for (let i = text.indexOf('\n'); i !== -1; i = text.indexOf('\n', i + 1)) starts.push(i + 1);
      this.lineStarts = starts;
    }
    return this.lineStarts;
  }

  /**
   * The new document's line starts after replacing `[offset, offset + deleteLength)` with
   * `insertText`, derived from this document's without rescanning. Returns `undefined` when
   * this document hasn't computed its own yet; the new one then computes them lazily.
   */
  private patchedLineStarts(offset: number, deleteLength: number, insertText: string): number[] | undefined {
    const old = this.lineStarts;
    if (!old) return undefined;
    const delta = insertText.length - deleteLength;
    let keep = 0;
    while (keep < old.length && old[keep]! <= offset) keep++;
    // A start at exactly offset + deleteLength is dropped: its '\n' was the last deleted character.
    let tailFrom = keep;
    const deleteEnd = offset + deleteLength;
    while (tailFrom < old.length && old[tailFrom]! <= deleteEnd) tailFrom++;
    const result = old.slice(0, keep);
    for (let i = insertText.indexOf('\n'); i !== -1; i = insertText.indexOf('\n', i + 1)) {
      result.push(offset + i + 1);
    }
    for (let j = tailFrom; j < old.length; j++) result.push(old[j]! + delta);
    return result;
  }
}
